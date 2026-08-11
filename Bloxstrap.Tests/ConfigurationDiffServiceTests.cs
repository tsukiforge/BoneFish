using Bloxstrap.Sandbox;
using Bloxstrap.Sandbox.Models;
using Xunit;

namespace Bloxstrap.Tests;

public class ConfigurationDiffServiceTests
{
    private static readonly Dictionary<string, string> Base = new()
    {
        ["FFlagA"] = "false",
        ["FIntB"] = "60",
        ["DFStringC"] = "Default"
    };

    [Fact]
    public void Added_Value_Is_Reported_As_Added()
    {
        var diff = ConfigurationDiffService.ComputeDiff(Base, new[] { SandboxTestHelpers.Set("FFlagNew", "true") });

        var entry = Assert.Single(diff);
        Assert.Equal(SandboxDiffType.Added, entry.Type);
        Assert.Null(entry.CurrentValue);
        Assert.Equal("true", entry.NewValue);
    }

    [Fact]
    public void Changed_Value_Is_Reported_As_Changed()
    {
        var diff = ConfigurationDiffService.ComputeDiff(Base, new[] { SandboxTestHelpers.Set("FIntB", "120") });

        var entry = Assert.Single(diff);
        Assert.Equal(SandboxDiffType.Changed, entry.Type);
        Assert.Equal("60", entry.CurrentValue);
        Assert.Equal("120", entry.NewValue);
        Assert.Equal("60 → 120", entry.Description);
    }

    [Fact]
    public void Removed_Value_Is_Reported_As_Removed()
    {
        var diff = ConfigurationDiffService.ComputeDiff(Base, new[] { SandboxTestHelpers.Remove("FFlagA") });

        var entry = Assert.Single(diff);
        Assert.Equal(SandboxDiffType.Removed, entry.Type);
        Assert.Equal("false", entry.CurrentValue);
        Assert.Null(entry.NewValue);
    }

    [Fact]
    public void Unchanged_Value_Is_Reported_As_Unchanged()
    {
        var diff = ConfigurationDiffService.ComputeDiff(Base, new[] { SandboxTestHelpers.Set("FFlagA", "false") });

        var entry = Assert.Single(diff);
        Assert.Equal(SandboxDiffType.Unchanged, entry.Type);
    }

    [Fact]
    public void Removing_NonExistent_Flag_Is_A_NoOp()
    {
        var diff = ConfigurationDiffService.ComputeDiff(Base, new[] { SandboxTestHelpers.Remove("FFlagDoesNotExist") });

        Assert.Empty(diff);
    }

    [Fact]
    public void CountActualChanges_Ignores_Unchanged()
    {
        var diff = ConfigurationDiffService.ComputeDiff(Base, new[]
        {
            SandboxTestHelpers.Set("FFlagA", "true"),   // changed
            SandboxTestHelpers.Set("FFlagB", "true"),   // added
            SandboxTestHelpers.Remove("DFStringC"),     // removed
            SandboxTestHelpers.Set("FIntB", "60")       // unchanged
        });

        Assert.Equal(3, ConfigurationDiffService.CountActualChanges(diff));
    }

    // ── Validation ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("FFlagGood", true)]
    [InlineData("DFIntCSGLevelOfDetailSwitchingDistance", true)]
    [InlineData("A", true)]
    [InlineData("../../etc/passwd", false)]
    [InlineData("ClientSettings\\ClientAppSettings.json", false)]
    [InlineData("flag with space", false)]
    [InlineData("1startsWithDigit", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void FlagName_Validation(string? name, bool expected) =>
        Assert.Equal(expected, SandboxChangeValidator.IsFlagNameValid(name));

    [Theory]
    [InlineData("true", true)]
    [InlineData("120", true)]
    [InlineData("12.5", true)]
    [InlineData("some plain string", true)]
    [InlineData("{\"json\":\"object\"}", false)]
    [InlineData("[\"array\"]", false)]
    [InlineData("line\nbreak", false)]
    [InlineData("", false)]
    [InlineData(null, true)] // removal is always valid
    public void Value_Validation(string? value, bool expected) =>
        Assert.Equal(expected, SandboxChangeValidator.IsValueValid(value));

    [Fact]
    public void Invalid_Change_Produces_Message()
    {
        var change = new SandboxChange { FlagName = "bad/path", NewValue = "x" };
        Assert.NotNull(SandboxChangeValidator.GetFirstInvalidChangeMessage(change));
    }

    [Fact]
    public void Valid_Change_Produces_No_Message()
    {
        var change = new SandboxChange { FlagName = "FFlagValid", NewValue = "true" };
        Assert.Null(SandboxChangeValidator.GetFirstInvalidChangeMessage(change));
    }
}
