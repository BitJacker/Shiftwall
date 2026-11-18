namespace Shiftwall.Core.Config;

/// <summary>Raised when an input file is larger than the limit for its kind.</summary>
public sealed class InputTooLargeException(string path, long size, long limit)
    : IOException($"Input file is {size} bytes; the limit is {limit} bytes.")
{
    public string FilePath { get; } = path;

    public long Size { get; } = size;

    public long Limit { get; } = limit;
}

/// <summary>Reads input files with a size limit, so a wrong file (an image, a disk dump) cannot exhaust memory.</summary>
public static class InputFile
{
    /// <summary>
    /// Configuration backups: 256 MiB. Real backups are a few MB; the parser was tested with 280,000 lines (~10 MB).
    /// </summary>
    public const long MaxConfigBytes = 256L * 1024 * 1024;

    /// <summary>CSV imports, mapping templates and anonymization maps.</summary>
    public const long MaxSmallFileBytes = 64L * 1024 * 1024;

    public static byte[] ReadAllBytes(string path, long limit = MaxConfigBytes)
    {
        Check(path, limit);
        return File.ReadAllBytes(path);
    }

    public static async Task<byte[]> ReadAllBytesAsync(string path, long limit = MaxConfigBytes, CancellationToken ct = default)
    {
        Check(path, limit);
        return await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
    }

    public static string ReadAllText(string path, long limit = MaxSmallFileBytes)
    {
        Check(path, limit);
        return File.ReadAllText(path);
    }

    public static async Task<string> ReadAllTextAsync(string path, long limit = MaxSmallFileBytes, CancellationToken ct = default)
    {
        Check(path, limit);
        return await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
    }

    private static void Check(string path, long limit)
    {
        var size = new FileInfo(path).Length;
        if (size > limit) throw new InputTooLargeException(path, size, limit);
    }
}
