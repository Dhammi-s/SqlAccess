using System.Text;

namespace SqlAccess.Api.Cache.Networking;

/// <summary>
/// A reply in our Redis-RESP-inspired protocol. Encodes to the classic RESP wire types:
/// <c>+</c> simple string, <c>-</c> error, <c>:</c> integer, <c>$</c> bulk string, <c>*</c> array.
/// </summary>
public abstract record RespValue
{
    /// <summary>Serializes this value to its RESP byte representation.</summary>
    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        WriteTo(ms);
        return ms.ToArray();
    }

    /// <summary>Writes this value's RESP encoding to the stream.</summary>
    public abstract void WriteTo(Stream s);

    protected static void WriteAscii(Stream s, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        s.Write(bytes, 0, bytes.Length);
    }

    // Convenience factories
    public static RespValue Simple(string v) => new RespSimpleString(v);
    public static RespValue Error(string m) => new RespError(m);
    public static RespValue Integer(long n) => new RespInteger(n);
    public static RespValue Bulk(string? v) => new RespBulkString(v);
    public static readonly RespValue Nil = new RespBulkString(null);
    public static RespValue Array(IReadOnlyList<RespValue> items) => new RespArray(items);
}

/// <summary>RESP simple string: <c>+OK\r\n</c>.</summary>
public sealed record RespSimpleString(string Value) : RespValue
{
    public override void WriteTo(Stream s) => WriteAscii(s, "+" + Value + "\r\n");
}

/// <summary>RESP error: <c>-ERR message\r\n</c>.</summary>
public sealed record RespError(string Message) : RespValue
{
    public override void WriteTo(Stream s) => WriteAscii(s, "-" + Message + "\r\n");
}

/// <summary>RESP integer: <c>:123\r\n</c>.</summary>
public sealed record RespInteger(long Value) : RespValue
{
    public override void WriteTo(Stream s) => WriteAscii(s, ":" + Value + "\r\n");
}

/// <summary>RESP bulk string: <c>$5\r\nhello\r\n</c>, or <c>$-1\r\n</c> for nil.</summary>
public sealed record RespBulkString(string? Value) : RespValue
{
    public override void WriteTo(Stream s)
    {
        if (Value is null) { WriteAscii(s, "$-1\r\n"); return; }
        var data = Encoding.UTF8.GetBytes(Value);
        WriteAscii(s, "$" + data.Length + "\r\n");
        s.Write(data, 0, data.Length);
        WriteAscii(s, "\r\n");
    }
}

/// <summary>RESP array: <c>*2\r\n...\r\n...</c>.</summary>
public sealed record RespArray(IReadOnlyList<RespValue> Items) : RespValue
{
    public override void WriteTo(Stream s)
    {
        WriteAscii(s, "*" + Items.Count + "\r\n");
        foreach (var item in Items) item.WriteTo(s);
    }
}
