using System.Security.Cryptography;
using System.Text;

namespace SqlAccess.Api.Services;

public interface IEncryptionService
{
    /// <summary>Encrypts plaintext. Returns a self-describing, versioned token.</summary>
    string? Encrypt(string? plaintext);

    /// <summary>Decrypts a token produced by Encrypt. Legacy plaintext (no marker) is returned as-is.</summary>
    string? Decrypt(string? value);

    /// <summary>True if the value is already an encrypted token.</summary>
    bool IsEncrypted(string? value);
}

/// <summary>
/// Authenticated encryption for secrets at rest using AES-256-GCM.
/// Token format:  enc::v1::base64( nonce[12] | tag[16] | ciphertext )
///
/// The 256-bit key comes from configuration key "Encryption:Key" (base64, 32 bytes)
/// which MUST be supplied via user-secrets or an environment variable — never committed.
/// </summary>
public sealed class EncryptionService : IEncryptionService
{
    private const string Marker = "enc::v1::";
    private const int NonceSize = 12; // 96-bit nonce (AES-GCM standard)
    private const int TagSize = 16;   // 128-bit auth tag

    private readonly byte[] _key;

    public EncryptionService(IConfiguration config)
    {
        var b64 = config["Encryption:Key"];
        if (string.IsNullOrWhiteSpace(b64))
            throw new InvalidOperationException(
                "Encryption:Key is not configured. Generate one and store it in user-secrets or an env var. " +
                "See README for `dotnet user-secrets set`.");

        try
        {
            _key = Convert.FromBase64String(b64);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Encryption:Key must be a base64 string.");
        }

        if (_key.Length != 32)
            throw new InvalidOperationException(
                $"Encryption:Key must decode to 32 bytes (256-bit); got {_key.Length}.");
    }

    public bool IsEncrypted(string? value) =>
        value is not null && value.StartsWith(Marker, StringComparison.Ordinal);

    public string? Encrypt(string? plaintext)
    {
        if (plaintext is null) return null;
        if (plaintext.Length == 0) return string.Empty;

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        var combined = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, combined, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, combined, NonceSize + TagSize, cipher.Length);

        return Marker + Convert.ToBase64String(combined);
    }

    public string? Decrypt(string? value)
    {
        if (value is null) return null;
        if (value.Length == 0) return string.Empty;

        // Legacy / externally-inserted plaintext: return unchanged so old rows still work.
        if (!IsEncrypted(value)) return value;

        var combined = Convert.FromBase64String(value[Marker.Length..]);
        if (combined.Length < NonceSize + TagSize)
            throw new CryptographicException("Encrypted payload is malformed.");

        var nonce = combined.AsSpan(0, NonceSize);
        var tag = combined.AsSpan(NonceSize, TagSize);
        var cipher = combined.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }
}
