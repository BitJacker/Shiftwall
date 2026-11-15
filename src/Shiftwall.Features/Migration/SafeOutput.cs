namespace Shiftwall.Features.Migration;

/// <summary>Raised when a write would overwrite a source configuration or an existing file.</summary>
public sealed class UnsafeOutputException(string message) : IOException(message);

/// <summary>Writes outputs without ever overwriting an original configuration.</summary>
public static class SafeOutput
{
    /// <summary>
    /// Writes <paramref name="bytes"/> to <paramref name="path"/>. Refuses any path that resolves to one of
    /// <paramref name="protectedPaths"/> (the originals), and refuses existing files unless
    /// <paramref name="allowOverwrite"/> is true (never for originals).
    /// </summary>
    public static void Write(string path, byte[] bytes, IEnumerable<string> protectedPaths, bool allowOverwrite = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(bytes);
        var full = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (protectedPaths.Any(p => string.Equals(Path.GetFullPath(p), full, comparison)))
        {
            throw new UnsafeOutputException("The output would overwrite an original configuration. Choose another file name.");
        }

        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // Write to a temporary file in the same folder, then move: never leaves a half-written output.
        var temp = Path.Combine(dir ?? ".", "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp");
        try
        {
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes);
                fs.Flush(flushToDisk: true);
            }

            File.Move(temp, full, overwrite: allowOverwrite);
        }
        catch (IOException) when (File.Exists(full) && !allowOverwrite)
        {
            throw new UnsafeOutputException("A file with this name already exists. Choose another file name.");
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <summary>Suggested output name: <c>branch.conf</c> → <c>branch-FG-70G.conf</c>, never equal to the source.</summary>
    public static string SuggestName(string sourcePath, string? targetLabel, string? directory = null)
    {
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var ext = Path.GetExtension(sourcePath);
        if (string.IsNullOrEmpty(ext)) ext = ".conf";
        var label = string.Concat((targetLabel ?? "migrated").Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var dir = directory ?? Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? ".";
        var candidate = Path.Combine(dir, $"{name}-{label}{ext}");
        for (var i = 2; File.Exists(candidate) || string.Equals(Path.GetFullPath(candidate), Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase); i++)
        {
            candidate = Path.Combine(dir, $"{name}-{label}-{i}{ext}");
        }

        return candidate;
    }
}
