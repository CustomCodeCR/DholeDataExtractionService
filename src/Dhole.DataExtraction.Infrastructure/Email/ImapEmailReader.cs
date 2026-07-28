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
            25 * 1024 * 1024
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

            try
            {
                var raw = await client.FetchRawByUidAsync(uid, cancellationToken);
                var externalId = $"imap:{account.EmailAddress}:{uid}";
                var parsed = SimpleMimeParser.ParseRawMessage(raw, externalId, uid);
                messages.Add(parsed);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "No fue posible leer el correo UID {Uid} de {EmailAddress}.",
                    uid,
                    account.EmailAddress
                );
            }
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            await client.ExecuteTaggedAsync("LOGOUT", cancellationToken, throwOnNoOrBad: false);
        }

        return messages;
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

            try
            {
                var tag = NextTag();
                await WriteLineAsync($"{tag} UID FETCH {uid} (BODY.PEEK[])", timeout.Token);

                byte[]? literal = null;
                while (true)
                {
                    var line = await ReadLineAsync(timeout.Token);
                    var literalSize = TryGetLiteralSize(line);

                    if (literalSize.HasValue)
                    {
                        if (literalSize.Value > _maxMessageBytes)
                        {
                            throw new InvalidOperationException(
                                $"El correo UID {uid} pesa {literalSize.Value} bytes y supera el máximo permitido de {_maxMessageBytes} bytes."
                            );
                        }

                        literal = await ReadExactAsync(literalSize.Value, timeout.Token);
                        continue;
                    }

                    if (!line.StartsWith(tag, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!line.Contains(" OK", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"FETCH IMAP falló para UID {uid}: {line}");
                    }

                    return literal ?? Array.Empty<byte>();
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"La descarga del correo UID {uid} excedió {_fetchTimeout.TotalSeconds:0} segundos."
                );
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stream.DisposeAsync();
            _tcpClient.Dispose();
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

        private static string GetCommandName(string command)
        {
            var separator = command.IndexOf(' ');
            return separator > 0 ? command[..separator] : command;
        }

        private static int? TryGetLiteralSize(string line)
        {
            var match = Regex.Match(line, "\\{(?<size>\\d+)\\}$");
            return match.Success && int.TryParse(match.Groups["size"].Value, out var size)
                ? size
                : null;
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
