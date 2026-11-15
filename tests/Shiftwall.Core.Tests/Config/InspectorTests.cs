using System.Text;
using Shiftwall.Core.Config;

namespace Shiftwall.Core.Tests.Config;

public sealed class InspectorTests
{
    [Fact]
    public void CompleteSingleVdomBackup_IsNotPartial()
    {
        var r = ConfigInspector.Inspect(Golden.Bytes("single-vdom.fgtcfg"));
        Assert.Equal(ConfigFileKind.FortiOsText, r.Kind);
        Assert.False(r.IsPartial);
        Assert.False(r.IsMultiVdom);
        Assert.False(r.PrivateDataEncryption);
        Assert.False(r.HasErrors);
        Assert.Equal(4, r.EncryptedValueCount);
        Assert.Contains(r.Diagnostics, d => d.Code == DiagnosticCodes.EncryptedValues && d.Arguments.SequenceEqual(["4"]));
    }

    [Fact]
    public void MultiVdomBackup_IsDetected_WithVdomNames()
    {
        var r = ConfigInspector.Inspect(Golden.Bytes("multi-vdom.fgtcfg"));
        Assert.True(r.IsMultiVdom);
        Assert.Equal(["root", "branch"], r.Vdoms);
        Assert.Equal("1", r.Header!.VdomFlag);
        Assert.False(r.IsPartial);
    }

    [Fact]
    public void PrivateDataEncryption_IsDetected_InGlobalScope()
    {
        var r = ConfigInspector.Inspect(Golden.Bytes("multi-vdom.fgtcfg"));
        Assert.True(r.PrivateDataEncryption);
        Assert.Contains(r.Diagnostics, d => d.Code == DiagnosticCodes.PrivateDataEncryption && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void PrivateDataEncryptionDisabled_IsNotReported()
    {
        var text = "#config-version=FGT60F-7.6.8-FW-build3744-261022:opmode=0:vdom=0:user=admin\nconfig system global\n    set private-data-encryption disable\nend\n";
        Assert.False(ConfigInspector.Inspect(Encoding.UTF8.GetBytes(text)).PrivateDataEncryption);
    }

    [Fact]
    public void MaskedPasswords_AreCounted()
    {
        var r = ConfigInspector.Inspect(Golden.Bytes("masked.fgtcfg"));
        Assert.Equal(2, r.MaskedPasswordCount);
        Assert.Contains(r.Diagnostics, d => d.Code == DiagnosticCodes.PasswordMasked && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void ShowOutput_WithoutHeader_IsPartial()
    {
        var r = ConfigInspector.Inspect(Golden.Bytes("partial-show.fgtcfg"));
        Assert.True(r.IsPartial);
        var partial = Assert.Single(r.Diagnostics, d => d.Code == DiagnosticCodes.PartialConfig);
        Assert.Equal(["no-header", "no-system-global"], partial.Arguments);
        Assert.Contains(r.Diagnostics, d => d.Code == DiagnosticCodes.MissingHeader);
    }

    [Fact]
    public void TruncatedBackup_IsPartial_WithError()
    {
        var r = ConfigInspector.Inspect(Golden.Bytes("truncated.fgtcfg"));
        Assert.True(r.IsPartial);
        Assert.True(r.HasErrors);
    }

    [Fact]
    public void YamlBackup_IsRecognised_AndNotParsed()
    {
        var r = ConfigInspector.Inspect(Golden.Bytes("yaml-backup.fgtcfg"));
        Assert.Equal(ConfigFileKind.Yaml, r.Kind);
        Assert.Null(r.Document);
    }

    [Fact]
    public void BinaryContent_IsReportedAsEncryptedOrBinary_AndNotParsed()
    {
        var bytes = new byte[2048];
        new Random(7).NextBytes(bytes);
        bytes[10] = 0;
        var r = ConfigInspector.Inspect(bytes);
        Assert.Equal(ConfigFileKind.BinaryOrEncrypted, r.Kind);
        Assert.Null(r.Document);
        Assert.Contains(r.Diagnostics, d => d.Code == DiagnosticCodes.NotText && d.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\n")]
    public void EmptyFile_IsReported(string text) =>
        Assert.Equal(ConfigFileKind.Empty, ConfigInspector.Inspect(Encoding.UTF8.GetBytes(text)).Kind);

    [Fact]
    public void Diagnostics_NeverContainSecretValues()
    {
        foreach (var file in Directory.GetFiles(Golden.Dir, "*.fgtcfg"))
        {
            var r = ConfigInspector.Inspect(File.ReadAllBytes(file));
            foreach (var arg in r.Diagnostics.SelectMany(d => d.Arguments))
            {
                Assert.DoesNotContain("U2hpZnR3YWxs", arg, StringComparison.Ordinal);
                Assert.DoesNotContain(ConfigInspector.PasswordMaskToken, arg, StringComparison.Ordinal);
            }
        }
    }
}
