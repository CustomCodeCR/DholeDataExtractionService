using System.Globalization;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Dhole.DataExtraction.Application.Abstractions.Emails;
using Dhole.DataExtraction.Domain.Emails.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dhole.DataExtraction.Infrastructure.Email;

public sealed partial class ImapEmailReader(
    IConfiguration configuration,
    ILogger<ImapEmailReader> logger
) : IEmailReader
{
    public async Task<IReadOnlyCollection<EmailMessageReadModel>> ReadNewMessagesAsync(
        EmailIngestionAccount account,
        string passwordOrAppPassword,
        int maxMessages,
        CancellationToken cancellationToken = default
    )
    {
        var take = maxMessages <= 0 ? 25 : maxMessages;
        var connectTimeout = TimeSpan.FromSeconds(ReadPositiveInt(
            configuration["EmailIngestion:Imap:ConnectTimeoutSeconds"],
            30
        ));
        var commandTimeout = TimeSpan.FromSeconds(ReadPositiveInt(
            configuration["EmailIngestion:Imap:CommandTimeoutSeconds"],
            60
        ));
        var fetchTimeout = TimeSpan.FromSeconds(ReadPositiveInt(
            configuration["EmailIngestion:Imap:FetchTimeoutSeconds"],
            180
        ));
        var maxMessageBytes = ReadPositiveInt(
            configuration["EmailIngestion:Imap:MaxMessageBytes"],
            64 * 1024 * 1024
        );

        await using var client = await ImapConnection.ConnectAsync(
            account.Host,
            account.Port,
            account.UseSsl,
            connectTimeout,
            commandTimeout,
            fetchTimeout,
            maxMessageBytes,
            cancellationToken
        );

        await client.ReadGreetingAsync(cancellationToken);
        await client.ExecuteTaggedAsync(
            $"LOGIN {Quote(account.Username)} {Quote(passwordOrAppPassword)}",
            cancellationToken
        );
        await client.ExecuteTaggedAsync($"SELECT {Quote(account.FolderName)}", cancellationToken);

        var searchCommand = account.LastProcessedUid.HasValue && account.LastProcessedUid.Value > 0
            ? $"UID SEARCH UID {account.LastProcessedUid.Value + 1}:*"
            : "UID SEARCH UNSEEN";

        var searchResponse = await client.ExecuteTaggedAsync(searchCommand, cancellationToken);
        var uids = ParseUids(searchResponse)
            .Where(uid => !account.LastProcessedUid.HasValue || uid > account.LastProcessedUid.Value)
            .OrderBy(uid => uid)
            .Take(take)
            .ToArray();

        var messages = new List<EmailMessageReadModel>();

        foreach (var uid in uids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var externalId = $"imap:{account.EmailAddress}:{uid}";
            byte[] raw;

            try
            {
                raw = await client.FetchRawByUidAsync(uid, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var detail = GetDeepestExceptionMessage(exception);
                logger.LogError(
                    exception,
                    "No fue posible descargar el correo UID {Uid} de {EmailAddress}. Detalle: {Detail}",
                    uid,
                    account.EmailAddress,
                    detail
                );
                throw new InvalidOperationException(
                    $"No fue posible descargar el correo UID {uid}: {detail}. "
                        + "La sincronización se reintentará sin avanzar el cursor.",
                    exception
                );
            }

            try
            {
                var parsed = SimpleMimeParser.ParseRawMessage(raw, externalId, uid);
                if (HasUsableMimeContent(parsed))
                {
                    messages.Add(parsed);
                }
                else
                {
                    logger.LogWarning(
                        "El correo UID {Uid} de {EmailAddress} no produjo cuerpo ni adjuntos con el parser MIME normal. Se usará lectura tolerante.",
                        uid,
                        account.EmailAddress
                    );
                    messages.Add(
                        SimpleMimeParser.ParseRawMessageFallback(raw, externalId, uid)
                    );
                }
            }
            catch (Exception exception)
            {
                // A malformed MIME envelope must not become a poison message that blocks
                // every newer UID. Preserve the raw EML and expose its textual payload so
                // deterministic extraction or AI can still recover the freight rates.
                logger.LogWarning(
                    exception,
                    "El correo UID {Uid} de {EmailAddress} tiene una estructura MIME inválida. Se usará lectura tolerante y se conservará el correo bruto.",
                    uid,
                    account.EmailAddress
                );

                try
                {
                    messages.Add(
                        SimpleMimeParser.ParseRawMessageFallback(raw, externalId, uid)
                    );
                }
                catch (Exception fallbackException)
                {
                    var detail = GetDeepestExceptionMessage(fallbackException);
                    logger.LogError(
                        fallbackException,
                        "También falló la lectura tolerante del correo UID {Uid} de {EmailAddress}. Detalle: {Detail}",
                        uid,
                        account.EmailAddress,
                        detail
                    );
                    throw new InvalidOperationException(
                        $"No fue posible interpretar el correo UID {uid}: {detail}. "
                            + "La sincronización se reintentará sin avanzar el cursor.",
                        fallbackException
                    );
                }
            }
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await client.ExecuteTaggedAsync(
                    "LOGOUT",
                    cancellationToken,
                    throwOnNoOrBad: false
                );
            }
            catch (Exception exception) when (IsBenignDisconnect(exception))
            {
                // El correo ya fue descargado. Gmail y otros servidores pueden cerrar la
                // conexión antes de confirmar LOGOUT; eso no debe convertir una sincronización
                // válida en un error de la cuenta.
                logger.LogDebug(
                    exception,
                    "El servidor IMAP cerró la sesión durante LOGOUT de {EmailAddress}.",
                    account.EmailAddress
                );
            }
        }

        return messages;
    }

    private static bool HasUsableMimeContent(EmailMessageReadModel message)
    {
        return !string.IsNullOrWhiteSpace(message.BodyText)
            || !string.IsNullOrWhiteSpace(message.BodyHtml)
            || message.Attachments.Count > 0;
    }

    private static string GetDeepestExceptionMessage(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            var message = current.Message?.Trim();
            if (!string.IsNullOrWhiteSpace(message)
                && !messages.Contains(message, StringComparer.OrdinalIgnoreCase))
            {
                messages.Add(message);
            }
        }

        return messages.Count == 0 ? "error desconocido" : messages[^1];
    }

    private static bool IsBenignDisconnect(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is ObjectDisposedException)
            {
                return true;
            }

            if (current is SocketException socketException)
            {
                return socketException.SocketErrorCode is
                    SocketError.TryAgain or
                    SocketError.WouldBlock or
                    SocketError.ConnectionAborted or
                    SocketError.ConnectionReset or
                    SocketError.NotConnected or
                    SocketError.Shutdown;
            }

            if (current is IOException ioException)
            {
                var message = ioException.Message;
                if (
                    message.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("cerró la conexión", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("closed the connection", StringComparison.OrdinalIgnoreCase)
                )
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IReadOnlyCollection<long> ParseUids(string response)
    {
        var match = SearchResponseRegex().Match(response);
        if (!match.Success)
        {
            return Array.Empty<long>();
        }

        return match.Groups["uids"].Value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var uid) ? uid : 0)
            .Where(uid => uid > 0)
            .Distinct()
            .ToArray();
    }

    private static string Quote(string value)
    {
        return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    }

    private static int ReadPositiveInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }

    [GeneratedRegex("\\*\\s+SEARCH\\s+(?<uids>[0-9 ]*)", RegexOptions.IgnoreCase)]
    private static partial Regex SearchResponseRegex();

    private sealed class ImapConnection : IAsyncDisposable
    {
        private readonly TcpClient _tcpClient;
        private readonly Stream _stream;
        private readonly TimeSpan _commandTimeout;
        private readonly TimeSpan _fetchTimeout;
        private readonly int _maxMessageBytes;
        private int _tagSequence;

        private ImapConnection(
            TcpClient tcpClient,
            Stream stream,
            TimeSpan commandTimeout,
            TimeSpan fetchTimeout,
            int maxMessageBytes
        )
        {
            _tcpClient = tcpClient;
            _stream = stream;
            _commandTimeout = commandTimeout;
            _fetchTimeout = fetchTimeout;
            _maxMessageBytes = maxMessageBytes;
        }

        public static async Task<ImapConnection> ConnectAsync(
            string host,
            int port,
            bool useSsl,
            TimeSpan connectTimeout,
            TimeSpan commandTimeout,
            TimeSpan fetchTimeout,
            int maxMessageBytes,
            CancellationToken cancellationToken
        )
        {
            using var timeout = CreateTimeoutToken(cancellationToken, connectTimeout);
            var tcpClient = new TcpClient();

            try
            {
                await tcpClient.ConnectAsync(host, port, timeout.Token);
                Stream stream = tcpClient.GetStream();

                if (useSsl)
                {
                    var ssl = new SslStream(
                        stream,
                        leaveInnerStreamOpen: false,
                        ValidateServerCertificate
                    );

                    await ssl.AuthenticateAsClientAsync(
                        new SslClientAuthenticationOptions
                        {
                            TargetHost = host,
                            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                        },
                        timeout.Token
                    );

                    stream = ssl;
                }

                // El lector IMAP alterna lecturas por línea y literales binarios. Un buffer
                // evita realizar una operación TLS por cada byte sin perder los bytes ya leídos.
                stream = new BufferedStream(stream, 64 * 1024);

                return new ImapConnection(
                    tcpClient,
                    stream,
                    commandTimeout,
                    fetchTimeout,
                    maxMessageBytes
                );
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                tcpClient.Dispose();
                throw new TimeoutException(
                    $"La conexión IMAP a {host}:{port} excedió {connectTimeout.TotalSeconds:0} segundos."
                );
            }
            catch (SocketException exception)
            {
                tcpClient.Dispose();
                throw new IOException(
                    $"No fue posible abrir la conexión IMAP a {host}:{port}. "
                        + $"Error de red: {GetSocketErrorDescription(exception)}",
                    exception
                );
            }
            catch
            {
                tcpClient.Dispose();
                throw;
            }
        }

        public async Task ReadGreetingAsync(CancellationToken cancellationToken)
        {
            using var timeout = CreateTimeoutToken(cancellationToken, _commandTimeout);

            try
            {
                _ = await ReadLineAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"El servidor IMAP no envió el saludo dentro de {_commandTimeout.TotalSeconds:0} segundos."
                );
            }
        }

        public async Task<string> ExecuteTaggedAsync(
            string command,
            CancellationToken cancellationToken,
            bool throwOnNoOrBad = true
        )
        {
            using var timeout = CreateTimeoutToken(cancellationToken, _commandTimeout);

            try
            {
                return await ExecuteTaggedCoreAsync(command, timeout.Token, throwOnNoOrBad);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"El comando IMAP '{GetCommandName(command)}' excedió {_commandTimeout.TotalSeconds:0} segundos."
                );
            }
        }

        public async Task<byte[]> FetchRawByUidAsync(long uid, CancellationToken cancellationToken)
        {
            using var timeout = CreateTimeoutToken(cancellationToken, _fetchTimeout);
            ImapFetchRejectedException? firstRejected = null;

            try
            {
                foreach (var fetchItem in new[] { "BODY.PEEK[]", "RFC822" })
                {
                    try
                    {
                        return await FetchRawByUidCoreAsync(
                            uid,
                            fetchItem,
                            timeout.Token
                        );
                    }
                    catch (ImapFetchRejectedException exception)
                    {
                        firstRejected ??= exception;
                    }
                }

                throw new InvalidOperationException(
                    $"El servidor IMAP rechazó las dos formas de descargar el correo UID {uid}.",
                    firstRejected
                );
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"La descarga del correo UID {uid} excedió {_fetchTimeout.TotalSeconds:0} segundos."
                );
            }
        }

        private async Task<byte[]> FetchRawByUidCoreAsync(
            long uid,
            string fetchItem,
            CancellationToken cancellationToken
        )
        {
            var tag = NextTag();
            await WriteLineAsync(
                $"{tag} UID FETCH {uid} ({fetchItem})",
                cancellationToken
            );

            byte[]? literal = null;
            while (true)
            {
                var line = await ReadLineAsync(cancellationToken);
                var literalSize = TryGetLiteralSize(line);

                if (literalSize.HasValue)
                {
                    if (literalSize.Value > _maxMessageBytes)
                    {
                        throw new InvalidOperationException(
                            $"El correo UID {uid} pesa {literalSize.Value} bytes y supera el máximo permitido de {_maxMessageBytes} bytes."
                        );
                    }

                    literal = await ReadExactAsync(literalSize.Value, cancellationToken);
                    continue;
                }

                if (!line.StartsWith(tag, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!line.Contains(" OK", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ImapFetchRejectedException(
                        $"FETCH {fetchItem} falló para UID {uid}: {line}"
                    );
                }

                if (literal is null || literal.Length == 0)
                {
                    throw new ImapFetchRejectedException(
                        $"FETCH {fetchItem} terminó correctamente, pero el servidor no devolvió contenido para UID {uid}."
                    );
                }

                return literal;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _stream.DisposeAsync();
            }
            catch (IOException)
            {
                // La liberación de una conexión de red ya rota no debe invalidar correos
                // que fueron descargados y procesados correctamente.
            }
            catch (SocketException)
            {
                // Mismo caso para errores EAGAIN/reset durante el cierre del socket.
            }
            catch (ObjectDisposedException)
            {
                // Dispose debe ser idempotente.
            }
            finally
            {
                _tcpClient.Dispose();
            }
        }

        private async Task<string> ExecuteTaggedCoreAsync(
            string command,
            CancellationToken cancellationToken,
            bool throwOnNoOrBad
        )
        {
            var tag = NextTag();
            await WriteLineAsync($"{tag} {command}", cancellationToken);

            var response = new StringBuilder();
            while (true)
            {
                var line = await ReadLineAsync(cancellationToken);
                response.AppendLine(line);

                if (!line.StartsWith(tag, StringComparison.Ordinal))
                {
                    continue;
                }

                if (throwOnNoOrBad && !line.Contains(" OK", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Comando IMAP falló: {GetCommandName(command)}. Respuesta: {line}"
                    );
                }

                return response.ToString();
            }
        }

        private string NextTag() => $"A{Interlocked.Increment(ref _tagSequence):0000}";

        private async Task WriteLineAsync(string value, CancellationToken cancellationToken)
        {
            var bytes = Encoding.ASCII.GetBytes(value + "\r\n");
            await _stream.WriteAsync(bytes.AsMemory(0, bytes.Length), cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }

        private async Task<string> ReadLineAsync(CancellationToken cancellationToken)
        {
            var buffer = new List<byte>(256);
            var one = new byte[1];

            while (true)
            {
                var read = await _stream.ReadAsync(one.AsMemory(0, 1), cancellationToken);
                if (read == 0)
                {
                    throw new IOException("El servidor IMAP cerró la conexión.");
                }

                buffer.Add(one[0]);
                if (one[0] == '\n')
                {
                    break;
                }
            }

            return Encoding.ASCII.GetString(buffer.ToArray()).TrimEnd('\r', '\n');
        }

        private async Task<byte[]> ReadExactAsync(int size, CancellationToken cancellationToken)
        {
            var buffer = new byte[size];
            var offset = 0;

            while (offset < size)
            {
                var read = await _stream.ReadAsync(
                    buffer.AsMemory(offset, size - offset),
                    cancellationToken
                );

                if (read == 0)
                {
                    throw new IOException("El servidor IMAP cerró la conexión leyendo el mensaje.");
                }

                offset += read;
            }

            return buffer;
        }

        private static CancellationTokenSource CreateTimeoutToken(
            CancellationToken cancellationToken,
            TimeSpan timeout
        )
        {
            var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            source.CancelAfter(timeout);
            return source;
        }

        private static string GetSocketErrorDescription(SocketException exception)
        {
            return exception.SocketErrorCode switch
            {
                SocketError.TryAgain or SocketError.WouldBlock =>
                    "recurso de red temporalmente no disponible",
                SocketError.TooManyOpenSockets or SocketError.NoBufferSpaceAvailable =>
                    "se agotaron temporalmente los sockets o buffers de red",
                SocketError.TimedOut => "tiempo de conexión agotado",
                SocketError.NetworkUnreachable => "red no disponible",
                SocketError.HostUnreachable or SocketError.HostDown =>
                    "servidor de correo no disponible",
                SocketError.ConnectionRefused => "conexión rechazada por el servidor",
                SocketError.ConnectionReset or SocketError.ConnectionAborted =>
                    "conexión cerrada por el servidor",
                _ => exception.Message,
            };
        }

        private static string GetCommandName(string command)
        {
            var separator = command.IndexOf(' ');
            return separator > 0 ? command[..separator] : command;
        }

        private static int? TryGetLiteralSize(string line)
        {
            var match = Regex.Match(line, @"~?\{(?<size>\d+)\+?\}$");
            return match.Success && int.TryParse(match.Groups["size"].Value, out var size)
                ? size
                : null;
        }

        private sealed class ImapFetchRejectedException(string message)
            : InvalidOperationException(message)
        {
        }

        private static bool ValidateServerCertificate(
            object sender,
            X509Certificate? certificate,
            X509Chain? chain,
            SslPolicyErrors sslPolicyErrors
        )
        {
            return sslPolicyErrors == SslPolicyErrors.None;
        }
    }
}
