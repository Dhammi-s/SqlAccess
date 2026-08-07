using System.Text;

namespace SqlAccess.Api.Cache.Networking;

/// <summary>
/// Reads commands from and writes replies to a single client stream. Accepts both the RESP
/// array-of-bulk-strings format (used by redis-cli and real clients) and plain inline commands
/// (space-separated, convenient for telnet/nc testing).
/// </summary>
public sealed class RespConnection
{
    private readonly Stream _stream;
    private readonly byte[] _buffer = new byte[16 * 1024];
    private int _pos;
    private int _len;

    public RespConnection(Stream stream) => _stream = stream;

    /// <summary>Reads the next command as an argument array; returns <c>null</c> when the client disconnects.</summary>
    public async Task<string[]?> ReadCommandAsync(CancellationToken ct)
    {
        var line = await ReadLineAsync(ct);
        if (line is null) return null;                       // disconnected
        if (line.Length == 0) return [];                     // blank line — ignore

        if (line[0] == '*')                                  // RESP array
        {
            if (!int.TryParse(line.AsSpan(1), out var count) || count < 0)
                throw new FormatException("Protocol error: invalid multibulk length");

            var args = new string[count];
            for (var i = 0; i < count; i++)
            {
                var header = await ReadLineAsync(ct) ?? throw new EndOfStreamException();
                if (header.Length == 0 || header[0] != '$')
                    throw new FormatException("Protocol error: expected '$'");
                if (!int.TryParse(header.AsSpan(1), out var bulkLen) || bulkLen < 0)
                    throw new FormatException("Protocol error: invalid bulk length");

                var data = await ReadExactAsync(bulkLen, ct);
                await ReadExactAsync(2, ct);                 // trailing CRLF
                args[i] = Encoding.UTF8.GetString(data);
            }
            return args;
        }

        // Inline command
        return line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Writes a reply and flushes.</summary>
    public async Task WriteAsync(RespValue value, CancellationToken ct)
    {
        var bytes = value.ToBytes();
        await _stream.WriteAsync(bytes, ct);
        await _stream.FlushAsync(ct);
    }

    private async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        var sb = new StringBuilder(64);
        while (true)
        {
            var b = await ReadByteAsync(ct);
            if (b < 0) return sb.Length == 0 ? null : sb.ToString();
            if (b == '\r')
            {
                var next = await ReadByteAsync(ct);
                if (next == '\n') return sb.ToString();
                if (next < 0) return sb.ToString();
                sb.Append('\r').Append((char)next);
            }
            else if (b == '\n') return sb.ToString();
            else sb.Append((char)b);
        }
    }

    private async Task<byte[]> ReadExactAsync(int count, CancellationToken ct)
    {
        var result = new byte[count];
        var read = 0;
        while (read < count)
        {
            if (_pos >= _len && !await FillAsync(ct)) throw new EndOfStreamException();
            var take = Math.Min(count - read, _len - _pos);
            System.Buffer.BlockCopy(_buffer, _pos, result, read, take);
            _pos += take;
            read += take;
        }
        return result;
    }

    private async Task<int> ReadByteAsync(CancellationToken ct)
    {
        if (_pos >= _len && !await FillAsync(ct)) return -1;
        return _buffer[_pos++];
    }

    private async Task<bool> FillAsync(CancellationToken ct)
    {
        _len = await _stream.ReadAsync(_buffer, ct);
        _pos = 0;
        return _len > 0;
    }
}
