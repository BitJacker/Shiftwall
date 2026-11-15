using Shiftwall.Core.Config;

namespace Shiftwall.Core.Tests.Config;

public sealed class InputFileTests
{
    [Fact]
    public void FilesOverTheLimit_AreRejectedBeforeReading()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, new byte[2048]);
            var ex = Assert.Throws<InputTooLargeException>(() => InputFile.ReadAllBytes(path, limit: 1024));
            Assert.Equal((2048L, 1024L), (ex.Size, ex.Limit));
            Assert.Equal(2048, InputFile.ReadAllBytes(path, limit: 4096).Length);
            Assert.IsAssignableFrom<IOException>(ex);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Limits_AreGenerousForRealBackups()
    {
        Assert.Equal(256L * 1024 * 1024, InputFile.MaxConfigBytes);
        Assert.True(InputFile.MaxSmallFileBytes >= 16L * 1024 * 1024);
    }
}
