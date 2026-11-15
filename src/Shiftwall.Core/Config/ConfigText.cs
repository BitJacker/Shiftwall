using System.Text;

namespace Shiftwall.Core.Config;

public enum ConfigTextEncoding
{
    /// <summary>Valid UTF-8 (FortiOS writes UTF-8).</summary>
    Utf8,

    /// <summary>Not valid UTF-8: decoded byte-for-byte as ISO-8859-1 so that it can be written back unchanged.</summary>
    Latin1Fallback,
}

/// <summary>
/// Converts between the bytes of a configuration file and text, guaranteeing that
/// <c>Encode(Decode(bytes)) == bytes</c> for any input.
/// </summary>
public sealed record ConfigText(string Text, ConfigTextEncoding Encoding, bool HasBom, bool LooksBinary)
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static ConfigText Decode(ReadOnlySpan<byte> bytes)
    {
        var bom = bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]);
        var body = bom ? bytes[3..] : bytes;
        var binary = LooksLikeBinary(body);
        try
        {
            return new ConfigText(StrictUtf8.GetString(body), ConfigTextEncoding.Utf8, bom, binary);
        }
        catch (DecoderFallbackException)
        {
            return new ConfigText(System.Text.Encoding.Latin1.GetString(body), ConfigTextEncoding.Latin1Fallback, bom, binary);
        }
    }

    /// <summary>Encodes <paramref name="text"/> with the same encoding and BOM as the original file.</summary>
    public byte[] Encode(string text)
    {
        var enc = Encoding == ConfigTextEncoding.Utf8 ? (System.Text.Encoding)StrictUtf8 : System.Text.Encoding.Latin1;
        var body = enc.GetBytes(text);
        if (!HasBom) return body;
        var result = new byte[body.Length + 3];
        result[0] = 0xEF;
        result[1] = 0xBB;
        result[2] = 0xBF;
        body.CopyTo(result, 3);
        return result;
    }

    /// <summary>
    /// A FortiOS text backup contains no NUL bytes and almost no control characters. Encrypted backups and other
    /// binary files do. Only the first 64 KiB are inspected.
    /// </summary>
    private static bool LooksLikeBinary(ReadOnlySpan<byte> bytes)
    {
        var sample = bytes[..Math.Min(bytes.Length, 64 * 1024)];
        if (sample.IsEmpty) return false;
        var control = 0;
        foreach (var b in sample)
        {
            if (b == 0) return true;
            if (b < 0x20 && b is not (0x09 or 0x0A or 0x0D)) control++;
        }

        return control * 100 > sample.Length; // more than 1 %
    }
}
