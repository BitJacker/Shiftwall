using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Shiftwall.Features.Toolbox.Anonymize;

/// <summary>
/// Encrypts the anonymization map with a password chosen by the user, so it can be opened on another PC.
/// AES-256-GCM; key derived with PBKDF2-HMAC-SHA256, 600,000 iterations (OWASP Password Storage Cheat Sheet,
/// https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html, checked 2026-11-15), random
/// 16-byte salt and 12-byte nonce per file. The header is authenticated as associated data.
/// </summary>
/// <remarks>
/// File layout: "SWMAP" | version (1) | iterations (int32 LE) | salt (16) | nonce (12) | tag (16) | ciphertext.
/// </remarks>
public static class MapProtector
{
    public const int Iterations = 600_000;
    public const int MinPasswordLength = 12;
    private const byte Version = 1;
    private static readonly byte[] Magic = "SWMAP"u8.ToArray();
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly int HeaderSize = Magic.Length + 1 + 4 + SaltSize + NonceSize;

    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, string password, int iterations = Iterations)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (password.Length < MinPasswordLength) throw new ArgumentException($"Password must be at least {MinPasswordLength} characters.", nameof(password));
        if (iterations < 100_000) throw new ArgumentOutOfRangeException(nameof(iterations));

        var output = new byte[HeaderSize + TagSize + plaintext.Length];
        var span = output.AsSpan();
        Magic.CopyTo(span);
        span[Magic.Length] = Version;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(Magic.Length + 1, 4), iterations);
        var salt = span.Slice(Magic.Length + 5, SaltSize);
        var nonce = span.Slice(Magic.Length + 5 + SaltSize, NonceSize);
        RandomNumberGenerator.Fill(salt);
        RandomNumberGenerator.Fill(nonce);

        var key = DeriveKey(password, salt, iterations);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext, span[(HeaderSize + TagSize)..], span.Slice(HeaderSize, TagSize), span[..HeaderSize]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        return output;
    }

    /// <summary>Decrypts a map file. Throws <see cref="CryptographicException"/> on a wrong password or a damaged file.</summary>
    public static byte[] Decrypt(ReadOnlySpan<byte> file, string password)
    {
        if (file.Length < HeaderSize + TagSize || !file[..Magic.Length].SequenceEqual(Magic) || file[Magic.Length] != Version)
        {
            throw new InvalidDataException("Not a Shiftwall anonymization map.");
        }

        var iterations = BinaryPrimitives.ReadInt32LittleEndian(file.Slice(Magic.Length + 1, 4));
        if (iterations is < 100_000 or > 10_000_000) throw new InvalidDataException("Invalid key derivation parameters.");
        var salt = file.Slice(Magic.Length + 5, SaltSize);
        var nonce = file.Slice(Magic.Length + 5 + SaltSize, NonceSize);
        var tag = file.Slice(HeaderSize, TagSize);
        var ciphertext = file[(HeaderSize + TagSize)..];
        var plaintext = new byte[ciphertext.Length];
        var key = DeriveKey(password, salt, iterations);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, file[..HeaderSize]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        return plaintext;
    }

    private static byte[] DeriveKey(string password, ReadOnlySpan<byte> salt, int iterations)
    {
        var pwd = Encoding.UTF8.GetBytes(password);
        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(pwd, salt, iterations, HashAlgorithmName.SHA256, 32);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pwd);
        }
    }
}
