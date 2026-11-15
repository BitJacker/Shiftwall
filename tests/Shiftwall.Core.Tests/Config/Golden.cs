using System.Text;

namespace Shiftwall.Core.Tests.Config;

/// <summary>Access to the synthetic fixtures in Golden/Parser (see the README there).</summary>
internal static class Golden
{
    public static string Dir => Path.Combine(AppContext.BaseDirectory, "Golden", "Parser");

    public static byte[] Bytes(string file) => File.ReadAllBytes(Path.Combine(Dir, file));

    public static string Text(string file) => Encoding.UTF8.GetString(Bytes(file));
}
