using System.Text;
using SqlAccess.Api.Cache.Networking;

namespace SqlAccess.Tests;

/// <summary>
/// Tests the RESP wire protocol: <see cref="RespValue"/> serialization and
/// <see cref="RespConnection"/> command parsing (both the array-of-bulk-strings and inline forms).
/// </summary>
public sealed class RespProtocolTests
{
    private static string Wire(RespValue v) => Encoding.UTF8.GetString(v.ToBytes());

    [Fact]
    public void SimpleString_encodes_with_plus_prefix()
        => Assert.Equal("+OK\r\n", Wire(RespValue.Simple("OK")));

    [Fact]
    public void Error_encodes_with_minus_prefix()
        => Assert.Equal("-ERR bad\r\n", Wire(RespValue.Error("ERR bad")));

    [Fact]
    public void Integer_encodes_with_colon_prefix()
        => Assert.Equal(":42\r\n", Wire(RespValue.Integer(42)));

    [Fact]
    public void Bulk_string_encodes_length_then_payload()
        => Assert.Equal("$5\r\nhello\r\n", Wire(RespValue.Bulk("hello")));

    [Fact]
    public void Nil_encodes_as_negative_one_bulk()
        => Assert.Equal("$-1\r\n", Wire(RespValue.Nil));

    [Fact]
    public void Bulk_string_uses_utf8_byte_length_not_char_length()
    {
        // "é" is 2 bytes in UTF-8 — the length prefix must be the byte count.
        Assert.Equal("$2\r\né\r\n", Wire(RespValue.Bulk("é")));
    }

    [Fact]
    public void Array_encodes_count_then_items()
    {
        var arr = RespValue.Array([RespValue.Integer(1), RespValue.Bulk("x")]);
        Assert.Equal("*2\r\n:1\r\n$1\r\nx\r\n", Wire(arr));
    }

    [Fact]
    public void Empty_array_encodes_as_star_zero()
        => Assert.Equal("*0\r\n", Wire(RespValue.Array([])));

    [Fact]
    public async Task ReadCommand_parses_resp_array_form()
    {
        var input = "*3\r\n$3\r\nSET\r\n$1\r\nk\r\n$5\r\nhello\r\n";
        var conn = new RespConnection(new MemoryStream(Encoding.UTF8.GetBytes(input)));

        var args = await conn.ReadCommandAsync(CancellationToken.None);

        Assert.NotNull(args);
        Assert.Equal(["SET", "k", "hello"], args);
    }

    [Fact]
    public async Task ReadCommand_parses_inline_form()
    {
        var conn = new RespConnection(new MemoryStream(Encoding.UTF8.GetBytes("SET k hello\r\n")));

        var args = await conn.ReadCommandAsync(CancellationToken.None);

        Assert.NotNull(args);
        Assert.Equal(["SET", "k", "hello"], args);
    }

    [Fact]
    public async Task ReadCommand_inline_collapses_repeated_whitespace()
    {
        var conn = new RespConnection(new MemoryStream(Encoding.UTF8.GetBytes("GET    k\r\n")));

        var args = await conn.ReadCommandAsync(CancellationToken.None);

        Assert.NotNull(args);
        Assert.Equal(["GET", "k"], args);
    }

    [Fact]
    public async Task ReadCommand_returns_null_on_disconnect()
    {
        var conn = new RespConnection(new MemoryStream(Array.Empty<byte>()));

        Assert.Null(await conn.ReadCommandAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReadCommand_preserves_binary_payload_with_embedded_spaces()
    {
        // A bulk value containing spaces must survive intact (that's why real clients use the array form).
        var input = "*3\r\n$3\r\nSET\r\n$1\r\nk\r\n$11\r\nhello world\r\n";
        var conn = new RespConnection(new MemoryStream(Encoding.UTF8.GetBytes(input)));

        var args = await conn.ReadCommandAsync(CancellationToken.None);

        Assert.NotNull(args);
        Assert.Equal(["SET", "k", "hello world"], args);
    }

    [Fact]
    public async Task ReadCommand_invalid_multibulk_length_throws()
    {
        var conn = new RespConnection(new MemoryStream(Encoding.UTF8.GetBytes("*abc\r\n")));

        await Assert.ThrowsAsync<FormatException>(() => conn.ReadCommandAsync(CancellationToken.None));
    }
}
