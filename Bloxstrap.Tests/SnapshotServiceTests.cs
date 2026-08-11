using Bloxstrap.Sandbox;
using Bloxstrap.Sandbox.Interfaces;
using Bloxstrap.Sandbox.Models;
using Xunit;

namespace Bloxstrap.Tests;

public class SnapshotServiceTests
{
    private static SnapshotService CreateService(string? root = null) => new(new FakeFastFlagStore(), root);

    [Fact]
    public async Task CreateSnapshot_Records_Touched_Flags_And_File()
    {
        using var h = new TestSandboxHarness();
        h.Store.SetValue("FFlagA", "false");
        h.Store.SetValue("FFlagB", "60");
        h.Store.RawContent = "{\"FFlagA\":\"false\",\"FFlagB\":\"60\"}";

        var experiment = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"), SandboxTestHelpers.Set("FFlagC", "new"));
        var snapshot = await h.Service.Snapshots.CreateAsync(experiment, "Balanced");

        Assert.NotNull(snapshot);
        Assert.Equal(SandboxSnapshot.CurrentFormatVersion, snapshot.Version);
        Assert.Equal("false", snapshot.OriginalValues["FFlagA"]);
        // FFlagB exists in the base but is NOT touched by the experiment — it must not be recorded.
        Assert.False(snapshot.OriginalValues.ContainsKey("FFlagB"));
        // FFlagC did not exist before — it must NOT appear as an original value.
        Assert.False(snapshot.OriginalValues.ContainsKey("FFlagC"));
        Assert.Equal("{\"FFlagA\":\"false\",\"FFlagB\":\"60\"}", snapshot.OriginalFileContent);
        Assert.False(string.IsNullOrEmpty(snapshot.ConfigurationHash));

        // Persisted to disk under the storage root.
        Assert.True(File.Exists(Path.Combine(h.StorageRoot, "Snapshots", $"{snapshot.Id}.json")));
    }

    [Fact]
    public async Task RestoreSnapshot_Returns_Original_Values()
    {
        using var h = new TestSandboxHarness();
        h.Store.SetValue("FFlagA", "false");
        h.Store.SetValue("FFlagB", "60");

        var experiment = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"), SandboxTestHelpers.Set("FFlagB", "120"));
        var snapshot = await h.Service.Snapshots.CreateAsync(experiment, "Balanced");

        // Apply the experiment (write to the store).
        h.Store.SetValue("FFlagA", "true");
        h.Store.SetValue("FFlagB", "120");

        await h.Service.Snapshots.RestoreAsync(snapshot, experiment.Changes.Select(c => c.FlagName));

        Assert.Equal("false", h.Store.GetValue("FFlagA"));
        Assert.Equal("60", h.Store.GetValue("FFlagB"));
        Assert.True(await h.Service.Snapshots.VerifyRestoredAsync(snapshot, experiment.Changes.Select(c => c.FlagName)));
    }

    [Fact]
    public async Task RestoreSnapshot_Removes_Flags_That_Did_Not_Exist_Before()
    {
        using var h = new TestSandboxHarness();
        h.Store.SetValue("FFlagA", "false");

        var experiment = h.CreateExperiment(SandboxTestHelpers.Set("FFlagNew", "true"));
        var snapshot = await h.Service.Snapshots.CreateAsync(experiment, "Balanced");

        h.Store.SetValue("FFlagNew", "true"); // experiment applied

        await h.Service.Snapshots.RestoreAsync(snapshot, experiment.Changes.Select(c => c.FlagName));

        Assert.Null(h.Store.GetValue("FFlagNew"));
        Assert.True(await h.Service.Snapshots.VerifyRestoredAsync(snapshot, experiment.Changes.Select(c => c.FlagName)));
    }

    [Fact]
    public async Task LoadSnapshot_Corrupted_File_Throws()
    {
        using var h = new TestSandboxHarness();
        var experiment = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"));
        var snapshot = await h.Service.Snapshots.CreateAsync(experiment, "Balanced");

        // Corrupt the snapshot file on disk.
        string path = Path.Combine(h.StorageRoot, "Snapshots", $"{snapshot.Id}.json");
        await File.WriteAllTextAsync(path, "{ not valid json");

        var ex = await Assert.ThrowsAsync<SandboxException>(() => h.Service.Snapshots.LoadAsync(snapshot.Id));
        Assert.Contains("corrupted", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadSnapshot_Tampered_Content_Fails_Integrity_Check()
    {
        using var h = new TestSandboxHarness();
        h.Store.SetValue("FFlagA", "false");

        var experiment = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"));
        var snapshot = await h.Service.Snapshots.CreateAsync(experiment, "Balanced");

        // Tamper with an original value but keep valid JSON — the internal hash must catch it.
        string path = Path.Combine(h.StorageRoot, "Snapshots", $"{snapshot.Id}.json");
        string json = await File.ReadAllTextAsync(path);
        json = json.Replace("\"false\"", "\"EVIL\"", StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, json);

        var ex = await Assert.ThrowsAsync<SandboxException>(() => h.Service.Snapshots.LoadAsync(snapshot.Id));
        Assert.Contains("integrity", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadSnapshot_Unsupported_Version_Is_Rejected()
    {
        using var h = new TestSandboxHarness();
        var experiment = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"));
        var snapshot = await h.Service.Snapshots.CreateAsync(experiment, "Balanced");

        string path = Path.Combine(h.StorageRoot, "Snapshots", $"{snapshot.Id}.json");
        string json = await File.ReadAllTextAsync(path);
        json = json.Replace($"\"Version\": {SandboxSnapshot.CurrentFormatVersion}", "\"Version\": 999", StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, json);

        var ex = await Assert.ThrowsAsync<SandboxException>(() => h.Service.Snapshots.LoadAsync(snapshot.Id));
        Assert.Contains("unsupported format version", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Restore_Verification_Fails_When_Config_Does_Not_Match_Snapshot()
    {
        using var h = new TestSandboxHarness();
        h.Store.SetValue("FFlagA", "false");

        var experiment = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"));
        var snapshot = await h.Service.Snapshots.CreateAsync(experiment, "Balanced");

        // Something else modified the flag after the snapshot.
        h.Store.SetValue("FFlagA", "SOMETHING ELSE");

        Assert.False(await h.Service.Snapshots.VerifyRestoredAsync(snapshot, experiment.Changes.Select(c => c.FlagName)));
    }
}
