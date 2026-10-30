using System.Diagnostics;
using System.Text;
using Dhole.DataExtraction.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dhole.DataExtraction.Infrastructure.Markdown;

/// <summary>
/// Converts untrusted email bodies and attachments to Markdown using the local
/// Microsoft MarkItDown Python package. Never opens a user-supplied path or URL:
/// document bytes are sent through standard input to convert_stream().
/// </summary>
public sealed class MarkItDownConverter(
    IConfiguration configuration,
    ILogger<MarkItDownConverter> logger
)
{
    private const int DefaultMaxInputBytes = 25 * 1024 * 1024;
    private const int DefaultMaxOutputCharacters = 2_000_000;

    private static readonly HashSet<string> SupportedExtensions = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ".html", ".htm", ".txt", ".csv", ".tsv", ".json", ".xml",
        ".eml", ".msg", ".pdf", ".xlsx", ".xls", ".xlsm", ".doc", ".docx",
        ".rtf", ".pptx", ".zip", ".png", ".jpg", ".jpeg", ".gif",
        ".webp", ".bmp", ".tif", ".tiff"
    };

    // Keeping the bridge embedded in C# avoids invoking a shell or accepting
    // arbitrary file paths. The library's convert_stream() accepts byte streams.
    private const string PythonBridge = """
import io
import subprocess
import sys
import zipfile
from markitdown import MarkItDown, StreamInfo

extension, mimetype, filename = sys.argv[1:4]
data = sys.stdin.buffer.read()
if extension == ".zip":
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        members = archive.infolist()
        if len(members) > 100 or sum(m.file_size for m in members) > 40 * 1024 * 1024:
            raise ValueError("ZIP exceeds safe member/expanded-size limits")
        if any(m.flag_bits & 1 for m in members):
            raise ValueError("Encrypted ZIP requires manual review")
if extension == ".doc":
    result = subprocess.run(
        ["antiword", "-"], input=data, capture_output=True, timeout=25, check=True
    )
    data, extension, mimetype = result.stdout, ".txt", "text/plain"
elif extension == ".rtf":
    from striprtf.striprtf import rtf_to_text
    data = rtf_to_text(data.decode("latin-1", errors="replace")).encode("utf-8")
    extension, mimetype = ".txt", "text/plain"
result = MarkItDown(enable_plugins=False).convert_stream(
    io.BytesIO(data),
    stream_info=StreamInfo(
        extension=extension, mimetype=mimetype or None, filename=filename
    ),
)
sys.stdout.write(result.markdown or "")
""";

    public static bool SupportsExtension(string? extension) =>
        !string.IsNullOrWhiteSpace(extension)
        && SupportedExtensions.Contains(extension.StartsWith('.') ? extension : $".{extension}");

    public async Task<string?> ConvertEmailBodyAsync(
        string? bodyText,
        string? bodyHtml,
        CancellationToken cancellationToken = default
    )
    {
        // Prefer the actual HTML message so MarkItDown can recover table
        // headers, rows and links before they are flattened as plain text.
        var hasHtml = !string.IsNullOrWhiteSpace(bodyHtml);
        var source = hasHtml
            ? bodyHtml!
            : EmailPricingContentSelector.SelectPreferredBody(bodyText, null);
        if (string.IsNullOrWhiteSpace(source))
            return null;

        var converted = await ConvertAsync(
            Encoding.UTF8.GetBytes(source),
            hasHtml ? "email-body.html" : "email-body.txt",
            hasHtml ? "text/html" : "text/plain",
            hasHtml ? ".html" : ".txt",
            cancellationToken
        );

        if (string.IsNullOrWhiteSpace(converted))
            return null;

        // Quote/forward chains often contain stale tariffs; retain only the
        // newest actual offer, after MarkItDown has structured the content.
        var focused = EmailPricingContentSelector.SelectBestPricingSection(converted);
        return string.IsNullOrWhiteSpace(focused) ? null : focused;
    }

    public async Task<string?> ConvertAsync(
        byte[] content,
        string fileName,
        string? contentType,
        string? extension,
        CancellationToken cancellationToken = default
    )
    {
        if (configuration.GetValue("MarkItDown:Enabled", true) == false
            || content.Length == 0
            || content.Length > configuration.GetValue(
                "MarkItDown:MaxInputBytes", DefaultMaxInputBytes))
            return null;

        var normalizedExtension = string.IsNullOrWhiteSpace(extension)
            ? Path.GetExtension(fileName)
            : extension!.StartsWith('.') ? extension : $".{extension}";
        if (!SupportedExtensions.Contains(normalizedExtension))
            return null;

        var python = configuration["MarkItDown:PythonExecutable"] ?? "python3";
        var timeoutSeconds = Math.Clamp(
            configuration.GetValue("MarkItDown:TimeoutSeconds", 60), 5, 300);
        var outputLimit = Math.Clamp(
            configuration.GetValue("MarkItDown:MaxOutputCharacters", DefaultMaxOutputCharacters),
            1000, 5_000_000);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = python,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            }
        };
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add(PythonBridge);
        process.StartInfo.ArgumentList.Add(normalizedExtension.ToLowerInvariant());
        process.StartInfo.ArgumentList.Add((contentType ?? "").Split(';')[0].Trim());
        // Filename is metadata only, never a file path accepted by the bridge.
        process.StartInfo.ArgumentList.Add(Path.GetFileName(fileName));

        try
        {
            if (!process.Start())
                return null;

            // Drain both pipes while writing to avoid deadlocking on large tables.
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.StandardInput.BaseStream.WriteAsync(content, timeout.Token);
            await process.StandardInput.BaseStream.FlushAsync(timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            var markdown = await stdout;
            var error = await stderr;

            if (process.ExitCode != 0 || markdown.Length > outputLimit)
            {
                logger.LogWarning(
                    "MarkItDown no pudo convertir {Name}: exit={ExitCode}, outputLength={Length}, detalle={Error}",
                    fileName, process.ExitCode, markdown.Length,
                    error.Length > 500 ? error[..500] : error
                );
                return null;
            }

            return string.IsNullOrWhiteSpace(markdown) ? null : markdown.Trim();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("MarkItDown agotó {TimeoutSeconds}s al convertir {Name}; usando el lector anterior.",
                timeoutSeconds, fileName);
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception,
                "MarkItDown no disponible para {Name}; usando el lector anterior.", fileName);
            return null;
        }
        finally
        {
            if (process.StartInfo is not null)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }
    }
}
