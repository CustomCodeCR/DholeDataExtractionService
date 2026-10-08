using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dhole.DataExtraction.Infrastructure.GrpcClients;

/// <summary>
/// Bounded, local OCR used by every extraction entry point (manual gRPC and email).
/// No shell invocation or remote OCR provider is used. Missing binaries degrade
/// gracefully to the existing deterministic/AI extraction and are logged.
/// </summary>
public interface IDocumentOcrService
{
    Task<string> RecognizePdfPageAsync(
        byte[] pdfContent,
        int pageNumber,
        CancellationToken cancellationToken = default
    );

    Task<string> RecognizeImageAsync(
        byte[] imageContent,
        string extension,
        CancellationToken cancellationToken = default
    );
}

public sealed class DocumentOcrService(
    IConfiguration configuration,
    ILogger<DocumentOcrService> logger
) : IDocumentOcrService
{
    private const int DefaultMaxFileBytes = 25 * 1024 * 1024;
    private const int DefaultProcessTimeoutSeconds = 40;

    public async Task<string> RecognizePdfPageAsync(
        byte[] pdfContent,
        int pageNumber,
        CancellationToken cancellationToken = default
    )
    {
        if (!Enabled || !Allowed(pdfContent) || pageNumber < 1)
            return string.Empty;

        var directory = CreateWorkingDirectory();
        try
        {
            var source = Path.Combine(directory, "source.pdf");
            var prefix = Path.Combine(directory, "page");
            await File.WriteAllBytesAsync(source, pdfContent, cancellationToken);

            // Limit raster dimensions and DPI to avoid pixel bombs / memory spikes.
            var rasterization = await RunAsync(
                "pdftoppm",
                ["-f", pageNumber.ToString(), "-l", pageNumber.ToString(),
                 "-singlefile", "-scale-to", "1800", "-gray", "-png", source, prefix],
                cancellationToken
            );

            var image = prefix + ".png";
            if (rasterization.ExitCode != 0 || !File.Exists(image))
            {
                logger.LogWarning("PDF OCR rasterization failed on page {Page}: {Error}",
                    pageNumber, Truncate(rasterization.Error));
                return string.Empty;
            }

            return await RecognizeFileAsync(image, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Unable to OCR PDF page {Page}.", pageNumber);
            return string.Empty;
        }
        finally
        {
            DeleteWorkingDirectory(directory);
        }
    }

    public async Task<string> RecognizeImageAsync(
        byte[] imageContent,
        string extension,
        CancellationToken cancellationToken = default
    )
    {
        if (!Enabled || !Allowed(imageContent))
            return string.Empty;

        var normalized = extension.ToLowerInvariant();
        if (normalized is not (".png" or ".jpg" or ".jpeg" or ".tif" or ".tiff" or ".bmp" or ".webp"))
            return string.Empty;

        var directory = CreateWorkingDirectory();
        try
        {
            var source = Path.Combine(directory, "source" + normalized);
            await File.WriteAllBytesAsync(source, imageContent, cancellationToken);
            return await RecognizeFileAsync(source, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Unable to OCR image.");
            return string.Empty;
        }
        finally
        {
            DeleteWorkingDirectory(directory);
        }
    }

    private async Task<string> RecognizeFileAsync(string imagePath, CancellationToken cancellationToken)
    {
        var language = configuration["AI:DocumentOcr:Languages"];
        if (string.IsNullOrWhiteSpace(language))
            language = "spa+eng";

        // PSM 11 searches for sparse text, useful for heterogeneous tariff tables.
        var result = await RunAsync(
            "tesseract", [imagePath, "stdout", "-l", language, "--psm", "11"],
            cancellationToken
        );
        if (result.ExitCode != 0)
        {
            logger.LogWarning("Tesseract OCR returned {ExitCode}: {Error}",
                result.ExitCode, Truncate(result.Error));
            return string.Empty;
        }

        var output = result.Output.Trim();
        return output.Length <= 30_000 ? output : output[..30_000];
    }

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken
    )
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(ReadPositiveInt(
            configuration["AI:DocumentOcr:ProcessTimeoutSeconds"],
            DefaultProcessTimeoutSeconds
        )));

        try
        {
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, await outputTask, await errorTask);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            logger.LogWarning("OCR process {Executable} exceeded the configured timeout.", executable);
            return (-1, string.Empty, "timeout");
        }
        catch (Win32Exception exception)
        {
            logger.LogWarning(exception, "OCR executable {Executable} is not installed.", executable);
            return (-1, string.Empty, "executable unavailable");
        }
        finally
        {
            // HasExited throws if Process.Start failed (for example binary missing).
            TryKill(process);
        }
    }

    private bool Enabled => !bool.TryParse(
        configuration["AI:DocumentOcr:Enabled"], out var enabled
    ) || enabled;

    private bool Allowed(byte[] fileContent)
    {
        var maximum = ReadPositiveInt(
            configuration["AI:DocumentOcr:MaximumFileBytes"],
            DefaultMaxFileBytes
        );
        if (fileContent.Length > maximum || fileContent.Length == 0)
        {
            logger.LogWarning("OCR skipped because document size {Bytes} exceeds limit {Limit}.",
                fileContent.Length, maximum);
            return false;
        }
        return true;
    }

    private static string CreateWorkingDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "dhole-document-ocr", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteWorkingDirectory(string directory)
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        catch (IOException) { /* cleanup will be handled by container temporary storage */ }
        catch (UnauthorizedAccessException) { /* no impact on the extracted result */ }
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }

    private static int ReadPositiveInt(string? text, int fallback) =>
        int.TryParse(text, out var result) && result > 0 ? result : fallback;

    private static string Truncate(string value) =>
        value.Length > 500 ? value[..500] : value;
}
