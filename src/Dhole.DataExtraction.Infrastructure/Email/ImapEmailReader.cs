using System.Globalization;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using System.Text;
using System.Text.RegularExpressions;
using Dhole.DataExtraction.Application.Abstractions.Emails;
using Dhole.DataExtraction.Domain.Emails.Entities;
using Microsoft.Extensions.Logging;

namespace Dhole.DataExtraction.Infrastructure.Email;

public sealed partial class ImapEmailReader(ILogger<ImapEmailReader> logger) : IEmailReader
{
    public async Task<IReadOnlyCollection<EmailMessageReadModel>> ReadNewMessagesAsync(
        EmailIngestionAccount account,
        string passwordOrAppPassword,
        int maxMessages,
        CancellationToken cancellationToken = default
    )
    {
        var take = maxMessages <= 0 ? 25 : maxMessages;
        await using var client = await ImapConnection.ConnectAsync(account.Host, account.Port, account.UseSsl, cancellationToken);

        await client.ReadGreetingAsync(cancellationToken);
        await client.ExecuteTaggedAsync($"LOGIN {Quote(account.Username)} {Quote(passwordOrAppPassword)}", cancellationToken);
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
            try
            {
                var raw = await client.FetchRawByUidAsync(uid, cancellationToken);
                var externalId = $"imap:{account.EmailAddress}:{uid}";
                var parsed = SimpleMimeParser.ParseRawMessage(raw, externalId, uid);
                messages.Add(parsed);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "No fue posible leer el correo UID {Uid} de {EmailAddress}.", uid, account.EmailAddress);
            }
        }

        await client.ExecuteTaggedAsync("LOGOUT", cancellationToken, throwOnNoOrBad: false);
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

    [GeneratedRegex("\\*\\s+SEARCH\\s+(?<uids>[0-9 ]*)", RegexOptions.IgnoreCase)]
    private static partial Regex SearchResponseRegex();

    private sealed class ImapConnection : IAsyncDisposable
    {
        private readonly TcpClient _tcpClient;
        private readonly Stream _stream;
        private int _tagSequence;

        private ImapConnection(TcpClient tcpClient, Stream stream)
        {
            _tcpClient = tcpClient;
            _stream = stream;
        }

        public static async Task<ImapConnection> ConnectAsync(string host, int port, bool useSsl, CancellationToken cancellationToken)
        {
            var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(host, port, cancellationToken);
            Stream stream = tcpClient.GetStream();

            if (useSsl)
            {
                var ssl = new SslStream(stream, leaveInnerStreamOpen: false, ValidateServerCertificate);
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = host,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                }, cancellationToken);

                stream = ssl;
            }

            return new ImapConnection(tcpClient, stream);
        }

        public async Task ReadGreetingAsync(CancellationToken cancellationToken)
        {
            _ = await ReadLineAsync(cancellationToken);
        }

        public async Task<string> ExecuteTaggedAsync(
            string command,
            CancellationToken cancellationToken,
            bool throwOnNoOrBad = true
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
                    throw new InvalidOperationException($"Comando IMAP falló: {command}. Respuesta: {line}");
                }

                return response.ToString();
            }
        }

        public async Task<byte[]> FetchRawByUidAsync(long uid, CancellationToken cancellationToken)
        {
            var tag = NextTag();
            await WriteLineAsync($"{tag} UID FETCH {uid} (BODY.PEEK[])", cancellationToken);

            byte[]? literal = null;
            while (true)
            {
                var line = await ReadLineAsync(cancellationToken);
                var literalSize = TryGetLiteralSize(line);

                if (literalSize.HasValue)
                {
                    literal = await ReadExactAsync(literalSize.Value, cancellationToken);
                    continue;
                }

                if (line.StartsWith(tag, StringComparison.Ordinal))
                {
                    if (!line.Contains(" OK", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"FETCH IMAP falló para UID {uid}: {line}");
                    }

                    return literal ?? Array.Empty<byte>();
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stream.DisposeAsync();
            _tcpClient.Dispose();
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
                var read = await _stream.ReadAsync(buffer.AsMemory(offset, size - offset), cancellationToken);
                if (read == 0)
                {
                    throw new IOException("El servidor IMAP cerró la conexión leyendo el mensaje.");
                }

                offset += read;
            }

            return buffer;
        }

        private static int? TryGetLiteralSize(string line)
        {
            var match = Regex.Match(line, "\\{(?<size>\\d+)\\}$");
            return match.Success && int.TryParse(match.Groups["size"].Value, out var size) ? size : null;
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
