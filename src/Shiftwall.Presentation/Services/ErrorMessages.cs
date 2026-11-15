using System.Security.Cryptography;
using Shiftwall.Core.Config;
using Shiftwall.Presentation.Localization;

namespace Shiftwall.Presentation.Services;

/// <summary>
/// User-facing text for an exception: what happened and what to do. Never a stack trace, never exception messages
/// (which may quote file content), never configuration content.
/// </summary>
public static class ErrorMessages
{
    public static string Friendly(Exception ex, string logDirectory) => ex switch
    {
        InputTooLargeException big => Loc.F("Error.FileTooLarge", Path.GetFileName(big.FilePath), big.Size / (1024 * 1024), big.Limit / (1024 * 1024)),
        FileNotFoundException or DirectoryNotFoundException => Loc.T("Error.FileNotFound"),
        UnauthorizedAccessException => Loc.T("Error.AccessDenied"),
        PathTooLongException => Loc.T("Error.PathTooLong"),
        IOException => Loc.T("Error.FileInUse"),
        OutOfMemoryException or InsufficientMemoryException => Loc.T("Error.OutOfMemory"),
        CryptographicException => Loc.T("Error.Crypto"),
        AggregateException { InnerExceptions.Count: 1 } agg => Friendly(agg.InnerExceptions[0], logDirectory),
        _ => Loc.F("Error.Unexpected", logDirectory),
    };
}
