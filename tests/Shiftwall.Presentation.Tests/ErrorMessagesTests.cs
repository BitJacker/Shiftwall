using System.Globalization;
using System.Security.Cryptography;
using Shiftwall.Core.Config;
using Shiftwall.Presentation.Localization;
using Shiftwall.Presentation.Services;

namespace Shiftwall.Presentation.Tests;

public sealed class ErrorMessagesTests
{
    public static TheoryData<Exception, string> Cases() => new()
    {
        { new FileNotFoundException("secret config line"), "Error.FileNotFound" },
        { new UnauthorizedAccessException("x"), "Error.AccessDenied" },
        { new IOException("x"), "Error.FileInUse" },
        { new OutOfMemoryException(), "Error.OutOfMemory" },
        { new CryptographicException("x"), "Error.Crypto" },
        { new AggregateException(new UnauthorizedAccessException()), "Error.AccessDenied" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void KnownErrors_GetActionableText(Exception ex, string key)
    {
        Loc.Instance.Culture = CultureInfo.GetCultureInfo("en");
        Assert.Equal(Loc.T(key), ErrorMessages.Friendly(ex, "C:\\logs"));
    }

    [Fact]
    public void TooLarge_NamesFileAndLimit_NeverTheExceptionMessage()
    {
        Loc.Instance.Culture = CultureInfo.GetCultureInfo("it-IT");
        var text = ErrorMessages.Friendly(new InputTooLargeException(@"C:\x\photo.jpg", 300L * 1024 * 1024, InputFile.MaxConfigBytes), "C:\\logs");
        Assert.Contains("photo.jpg", text);
        Assert.Contains("300", text);
        Assert.Contains("256", text);
    }

    [Fact]
    public void Unexpected_PointsToTheLog_WithoutDetails()
    {
        Loc.Instance.Culture = CultureInfo.GetCultureInfo("en");
        var text = ErrorMessages.Friendly(new InvalidOperationException("set password ENC abc"), "C:\\logs");
        Assert.Contains("C:\\logs", text);
        Assert.DoesNotContain("ENC", text);
    }
}
