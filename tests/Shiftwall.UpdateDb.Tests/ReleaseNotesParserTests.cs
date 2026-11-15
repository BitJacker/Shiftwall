using Shiftwall.UpdateDb;

namespace Shiftwall.UpdateDb.Tests;

public sealed class ReleaseNotesParserTests
{
    [Fact]
    public void SupportedModels_StopAtSpecialBranchSection()
    {
        const string text = """
            Supported models
            FortiOS 7.6.8 supports the following models.
            FortiGate
            FG-30G, FG-40F, FG-60F
            FortiWiFi
            FWF-40F, FWF-60F
            FortiGate VM
            FG-VM64, FG-VM64-KVM
            Special branch supported models
            FG-70G-SPECIAL
            """;
        Assert.Equal(["FG-30G", "FG-40F", "FG-60F", "FWF-40F", "FWF-60F", "FG-VM64", "FG-VM64-KVM"],
            ReleaseNotesFetcher.ExtractSupportedModels(text));
    }

    [Fact]
    public void MissingSection_YieldsEmptyList() =>
        Assert.Empty(ReleaseNotesFetcher.ExtractSupportedModels("Introduction only."));
}
