using Bloxstrap.Sandbox;
using Bloxstrap.Sandbox.Interfaces;
using Bloxstrap.Sandbox.Models;
using Xunit;

namespace Bloxstrap.Tests;

public class OptimizationSandboxServiceTests
{
    [Fact]
    public async Task Apply_Valid_Changes_Snapshot_Then_Write_Then_Verify()
    {
        using var h = new TestSandboxHarness();
        h.Store.SetValue("FFlagA", "false");

        var exp = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"), SandboxTestHelpers.Set("FFlagB", "120"));

        bool ok = await h.Service.ApplyAsync(exp, confirmedRestart: false);

        Assert.True(ok);
        Assert.Equal(SandboxExperimentState.ReadyForTesting, exp.State);
        Assert.Equal("true", h.Store.GetValue("FFlagA"));
        Assert.Equal("120", h.Store.GetValue("FFlagB"));
        Assert.NotNull(exp.SnapshotId);
        Assert.True(File.Exists(Path.Combine(h.StorageRoot, "Snapshots", $"{exp.SnapshotId}.json")));
    }

    [Fact]
    public async Task Apply_Rejects_Invalid_Flag_Names_And_Values()
    {
        using var h = new TestSandboxHarness();

        var badName = h.CreateExperiment(SandboxTestHelpers.Set("../evil", "true"));
        await Assert.ThrowsAsync<SandboxException>(() => h.Service.ApplyAsync(badName, false));
        Assert.Equal(SandboxExperimentState.Draft, badName.State); // nothing happened

        var badValue = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "{\"json\":true}"));
        await Assert.ThrowsAsync<SandboxException>(() => h.Service.ApplyAsync(badValue, false));
        Assert.Equal(SandboxExperimentState.Draft, badValue.State);
    }

    [Fact]
    public async Task Apply_Empty_Changes_Is_Rejected()
    {
        using var h = new TestSandboxHarness();
        var exp = h.CreateExperiment();

        await Assert.ThrowsAsync<SandboxException>(() => h.Service.ApplyAsync(exp, false));
    }

    [Fact]
    public async Task Apply_While_Roblox_Running_Requires_Confirmation()
    {
        using var h = new TestSandboxHarness();
        var service = new OptimizationSandboxService(
            h.Store,
            presetWriter: name => h.PresetWrites.Add(name),
            robloxRunningCheck: () => true,
            storageRoot: h.StorageRoot);

        var exp = service.Manager.CreateExperiment("Balanced", new[] { SandboxTestHelpers.Set("FFlagA", "true") });

        await Assert.ThrowsAsync<SandboxException>(() => service.ApplyAsync(exp, confirmedRestart: false));
        Assert.Equal(SandboxExperimentState.Draft, exp.State);

        // Explicit confirmation allows the apply (the UI shows the restart warning).
        Assert.True(await service.ApplyAsync(exp, confirmedRestart: true));
        Assert.Equal(SandboxExperimentState.ReadyForTesting, exp.State);
    }

    [Fact]
    public async Task Apply_Save_Failure_Rolls_Back_Immediately()
    {
        using var h = new TestSandboxHarness();
        h.Store.SetValue("FFlagA", "false");

        var exp = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"));

        // Fail only the first write; the automatic rollback then succeeds.
        bool saveFailed = false;
        h.Store.OnSaveFailure = () =>
        {
            if (saveFailed)
                return;
            saveFailed = true;
            throw new InvalidOperationException("simulated save failure");
        };

        // The store failure propagates as-is (the service rethrows the original after rolling back).
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.ApplyAsync(exp, false));
        Assert.True(saveFailed);
        Assert.Equal(SandboxExperimentState.RolledBack, exp.State); // auto-rollback succeeded
        Assert.Equal("false", h.Store.GetValue("FFlagA"));
    }

    [Fact]
    public async Task Rollback_Restores_Snapshot_And_Verifies()
    {
        using var h = new TestSandboxHarness();
        h.Store.SetValue("FFlagA", "false");
        h.Store.SetValue("FFlagB", "60");

        var exp = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"), SandboxTestHelpers.Set("FFlagB", "120"));
        await h.Service.ApplyAsync(exp, false);
        Assert.Equal(SandboxExperimentState.ReadyForTesting, exp.State);

        await h.Service.RollbackAsync(exp);

        Assert.Equal(SandboxExperimentState.RolledBack, exp.State);
        Assert.Equal("false", h.Store.GetValue("FFlagA"));
        Assert.Equal("60", h.Store.GetValue("FFlagB"));
    }

    [Fact]
    public async Task Rollback_Verification_Failure_Marks_Failed_And_Throws()
    {
        using var h = new TestSandboxHarness();
        h.Store.SetValue("FFlagA", "false");

        var exp = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"));
        await h.Service.ApplyAsync(exp, false);

        // Tamper with the snapshot on disk so the restore cannot be verified as "original".
        string path = Path.Combine(h.StorageRoot, "Snapshots", $"{exp.SnapshotId}.json");
        string json = await File.ReadAllTextAsync(path);
        json = json.Replace("\"false\"", "\"EVIL\"", StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, json);

        await Assert.ThrowsAsync<SandboxException>(() => h.Service.RollbackAsync(exp));

        Assert.Equal(SandboxExperimentState.Failed, exp.State);
        Assert.NotNull(exp.LastError);
    }

    [Fact]
    public async Task Cancel_From_Draft_Needs_No_Restore()
    {
        using var h = new TestSandboxHarness();
        var exp = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"));

        await h.Service.CancelAsync(exp);

        Assert.Equal(SandboxExperimentState.Cancelled, exp.State);
        Assert.Null(h.Store.GetValue("FFlagA")); // never written
    }

    [Fact]
    public async Task Cancel_Applied_Experiment_Restores_Configuration()
    {
        using var h = new TestSandboxHarness();
        h.Store.SetValue("FFlagA", "false");

        var exp = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"));
        await h.Service.ApplyAsync(exp, false);

        await h.Service.CancelAsync(exp);

        Assert.Equal(SandboxExperimentState.Cancelled, exp.State);
        Assert.Equal("false", h.Store.GetValue("FFlagA"));
    }

    [Fact]
    public async Task Commit_Keeps_Changes_And_Writes_Preset()
    {
        using var h = new TestSandboxHarness();
        h.Store.SetValue("FFlagA", "false");

        var exp = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"));
        await h.Service.ApplyAsync(exp, false);
        h.Service.StartTesting(exp);
        exp.Measurement = new SandboxMeasurement(); // no samples collected
        h.Service.RecordResult(exp); // → InsufficientData, still Completed

        Assert.Equal(SandboxExperimentState.Completed, exp.State);

        bool committed = await h.Service.CommitAsync(exp);

        Assert.True(committed);
        Assert.Equal(SandboxExperimentState.Committed, exp.State);
        Assert.Equal("true", h.Store.GetValue("FFlagA"));
        Assert.Contains("Balanced", h.PresetWrites); // base profile adopted for the manual-preset guard
    }

    [Fact]
    public async Task Commit_Is_Only_Allowed_From_Completed()
    {
        using var h = new TestSandboxHarness();
        var exp = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"));

        await Assert.ThrowsAsync<SandboxException>(() => h.Service.CommitAsync(exp));
        Assert.Equal(SandboxExperimentState.Draft, exp.State);
    }

    [Fact]
    public async Task Result_Recording_With_No_Measurements_Is_Insufficient_Data()
    {
        using var h = new TestSandboxHarness();
        var exp = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"));
        await h.Service.ApplyAsync(exp, false);
        h.Service.StartTesting(exp);
        exp.Measurement = new SandboxMeasurement(); // no samples collected
        h.Service.RecordResult(exp);

        Assert.Equal(SandboxExperimentState.Completed, exp.State);
        Assert.Equal(SandboxTestResult.InsufficientData, exp.Result);
        Assert.Equal("Insufficient data", exp.ResultLabel);
    }

    [Fact]
    public void Recovery_Detection_Finds_Interrupted_Experiment()
    {
        using var h = new TestSandboxHarness();
        var exp = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"));

        // Simulate an interrupted apply: journal says Testing but no terminal state was reached.
        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.Preparing);
        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.SnapshotCreated);
        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.Applying);
        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.ReadyForTesting);
        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.Testing);
        exp.SnapshotId = "snapshot_001";

        var unfinished = ExperimentRecoveryService.FindUnfinishedExperiment(h.Service.Manager);

        Assert.NotNull(unfinished);
        Assert.Same(exp, unfinished);
    }

    [Fact]
    public async Task Recovery_Detection_Returns_Null_For_Finished_Experiments()
    {
        using var h = new TestSandboxHarness();
        var exp = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"));
        await h.Service.CancelAsync(exp);

        Assert.Null(ExperimentRecoveryService.FindUnfinishedExperiment(h.Service.Manager));
    }

    [Fact]
    public async Task Recovery_IsExperimentCurrentlyApplied_Detects_Mismatch()
    {
        using var h = new TestSandboxHarness();
        h.Store.SetValue("FFlagA", "false");

        var exp = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"));
        await h.Service.ApplyAsync(exp, false);

        Assert.True(await ExperimentRecoveryService.IsExperimentCurrentlyAppliedAsync(h.Service, exp));

        // Simulate the configuration being partially modified by something else.
        h.Store.SetValue("FFlagA", "CORRUPTED");
        Assert.False(await ExperimentRecoveryService.IsExperimentCurrentlyAppliedAsync(h.Service, exp));
    }

    [Fact]
    public async Task Recovery_Restore_Returns_Configuration_To_Snapshot()
    {
        using var h = new TestSandboxHarness();
        h.Store.SetValue("FFlagA", "false");

        var exp = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"));
        await h.Service.ApplyAsync(exp, false);

        // Simulate restart: reload journal fresh.
        var reloaded = new ExperimentManager(h.StorageRoot);
        var found = reloaded.Find(exp.Id)!;
        found.SnapshotId = exp.SnapshotId;

        // Restore through the same primitives the recovery flow uses.
        var snapshot = await h.Service.Snapshots.LoadAsync(found.SnapshotId!, CancellationToken.None)
            ?? throw new InvalidOperationException("snapshot missing");
        await h.Service.Snapshots.RestoreAsync(snapshot, found.Changes.Select(c => c.FlagName), CancellationToken.None);

        Assert.Equal("false", h.Store.GetValue("FFlagA"));
    }

    // ── Staged workflow (Prepare → Apply) ────────────────────────────────────────────

    [Fact]
    public async Task Prepare_Creates_And_Verifies_Snapshot_Without_Applying()
    {
        using var h = new TestSandboxHarness();
        h.Store.SetValue("FFlagA", "false");

        var exp = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"));

        var snapshot = await h.Service.PrepareAsync(exp);

        Assert.Equal(SandboxExperimentState.SnapshotCreated, exp.State);
        Assert.NotNull(exp.SnapshotId);
        Assert.Equal("false", snapshot.OriginalValues["FFlagA"]);
        Assert.Equal("false", h.Store.GetValue("FFlagA")); // nothing written yet
        Assert.True(File.Exists(Path.Combine(h.StorageRoot, "Snapshots", $"{snapshot.Id}.json")));
    }

    [Fact]
    public async Task Prepare_Empty_Changes_Is_Rejected()
    {
        using var h = new TestSandboxHarness();
        var exp = h.CreateExperiment();

        await Assert.ThrowsAsync<SandboxException>(() => h.Service.PrepareAsync(exp));
        Assert.Equal(SandboxExperimentState.Draft, exp.State);
    }

    [Fact]
    public async Task Apply_From_SnapshotCreated_Writes_And_Verifies()
    {
        using var h = new TestSandboxHarness();
        h.Store.SetValue("FFlagA", "false");

        var exp = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"));
        await h.Service.PrepareAsync(exp);
        Assert.Equal(SandboxExperimentState.SnapshotCreated, exp.State);

        bool ok = await h.Service.ApplyAsync(exp, confirmedRestart: false);

        Assert.True(ok);
        Assert.Equal(SandboxExperimentState.ReadyForTesting, exp.State);
        Assert.Equal("true", h.Store.GetValue("FFlagA"));
    }

    [Fact]
    public async Task Apply_Blocked_Without_Snapshot()
    {
        using var h = new TestSandboxHarness();
        var exp = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"));

        // A state that requires a snapshot, but none exists.
        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.Preparing);
        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.SnapshotCreated);

        await Assert.ThrowsAsync<SandboxException>(() => h.Service.ApplyAsync(exp, false));
        Assert.Equal(SandboxExperimentState.SnapshotCreated, exp.State);
    }

    [Fact]
    public async Task Apply_Blocked_From_NonApplicable_States()
    {
        using var h = new TestSandboxHarness();
        var exp = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"));
        await h.Service.ApplyAsync(exp, false);
        h.Service.StartTesting(exp);

        await Assert.ThrowsAsync<SandboxException>(() => h.Service.ApplyAsync(exp, false));
    }

    [Fact]
    public async Task Apply_Rejects_Duplicate_Flags()
    {
        using var h = new TestSandboxHarness();
        var exp = h.CreateExperiment(
            SandboxTestHelpers.Set("FFlagA", "true"),
            SandboxTestHelpers.Set("FFlagA", "120"));

        await Assert.ThrowsAsync<SandboxException>(() => h.Service.ApplyAsync(exp, false));
        Assert.Equal(SandboxExperimentState.Draft, exp.State);
    }

    [Fact]
    public async Task Rollback_From_SnapshotCreated_Is_Allowed()
    {
        using var h = new TestSandboxHarness();
        h.Store.SetValue("FFlagA", "false");

        var exp = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"));
        await h.Service.PrepareAsync(exp);

        await h.Service.RollbackAsync(exp);

        Assert.Equal(SandboxExperimentState.RolledBack, exp.State);
        Assert.Equal("false", h.Store.GetValue("FFlagA"));
    }

    [Fact]
    public async Task Recovery_Can_Restore_From_SnapshotCreated()
    {
        using var h = new TestSandboxHarness();
        var exp = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"));
        await h.Service.PrepareAsync(exp);

        // The recovery path needs SnapshotCreated → RollingBack to be legal.
        Assert.True(ExperimentManager.IsTransitionAllowed(SandboxExperimentState.SnapshotCreated, SandboxExperimentState.RollingBack));
    }

    // ── Upsert semantics (duplicates / no-ops) ───────────────────────────────────────

    [Fact]
    public void UpsertChange_Replaces_Duplicate_Entry()
    {
        using var h = new TestSandboxHarness();
        h.Store.SetValue("FFlagA", "false");
        var exp = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"));

        h.Service.UpsertChange(exp, SandboxTestHelpers.Set("FFlagA", "120"));

        var change = Assert.Single(exp.Changes);
        Assert.Equal("FFlagA", change.FlagName);
        Assert.Equal("120", change.NewValue);
    }

    [Fact]
    public void UpsertChange_NoOp_Value_Removes_Entry()
    {
        using var h = new TestSandboxHarness();
        h.Store.SetValue("FFlagA", "false");
        var exp = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"));

        // Same value as current → no actual change → entry disappears.
        h.Service.UpsertChange(exp, SandboxTestHelpers.Set("FFlagA", "false"));

        Assert.Empty(exp.Changes);
    }

    [Fact]
    public void UpsertChange_Remove_Of_Nonexistent_Flag_Is_NoOp()
    {
        using var h = new TestSandboxHarness();
        var exp = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"));

        h.Service.UpsertChange(exp, SandboxTestHelpers.Remove("FFlagA"));

        Assert.Empty(exp.Changes);
    }

    [Fact]
    public void UpsertChange_Invalid_Flag_Is_Rejected()
    {
        using var h = new TestSandboxHarness();
        var exp = h.CreateExperiment();

        Assert.Throws<SandboxException>(() => h.Service.UpsertChange(exp, SandboxTestHelpers.Set("bad name", "true")));
        Assert.Empty(exp.Changes);
    }

    [Fact]
    public async Task UpsertChange_Is_Only_Allowed_On_Draft()
    {
        using var h = new TestSandboxHarness();
        var exp = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"));
        await h.Service.PrepareAsync(exp);

        Assert.Throws<SandboxException>(() => h.Service.UpsertChange(exp, SandboxTestHelpers.Set("FFlagB", "true")));
        Assert.Single(exp.Changes); // unchanged
    }

    // ── Workflow step indicator mapping ──────────────────────────────────────────────

    [Fact]
    public void Workflow_Step_Index_Maps_States()
    {
        using var h = new TestSandboxHarness();
        var exp = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"));
        Assert.Equal(0, exp.GetWorkflowStepIndex()); // Configure

        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.Preparing);
        Assert.Equal(1, exp.GetWorkflowStepIndex()); // Snapshot

        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.SnapshotCreated);
        Assert.Equal(1, exp.GetWorkflowStepIndex()); // Snapshot

        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.Applying);
        Assert.Equal(2, exp.GetWorkflowStepIndex()); // Apply

        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.ReadyForTesting);
        Assert.Equal(3, exp.GetWorkflowStepIndex()); // Test

        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.Testing);
        Assert.Equal(3, exp.GetWorkflowStepIndex()); // Test

        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.Completed);
        Assert.Equal(4, exp.GetWorkflowStepIndex()); // Result

        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.Committed);
        Assert.Equal(4, exp.GetWorkflowStepIndex()); // Result (finished)
    }

    [Fact]
    public void Workflow_Step_Index_Handles_Interrupted_States()
    {
        using var h = new TestSandboxHarness();
        var exp = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"));

        // Failed before any snapshot → still on the Snapshot step.
        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.Preparing);
        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.Failed);
        Assert.Equal(1, exp.GetWorkflowStepIndex());

        // Failed after apply started → on the Apply step.
        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.Preparing);
        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.SnapshotCreated);
        exp.SnapshotId = "snapshot_x";
        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.Applying);
        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.Failed);
        Assert.Equal(2, exp.GetWorkflowStepIndex());
    }

    [Fact]
    public void Friendly_State_Names_Are_User_Facing()
    {
        Assert.Equal("Draft", SandboxExperimentState.Draft.ToFriendlyName());
        Assert.Equal("Ready", SandboxExperimentState.SnapshotCreated.ToFriendlyName());
        Assert.Equal("Ready for Testing", SandboxExperimentState.ReadyForTesting.ToFriendlyName());
        Assert.Equal("Rolled Back", SandboxExperimentState.RolledBack.ToFriendlyName());
        Assert.Equal("Committed", SandboxExperimentState.Committed.ToFriendlyName());
    }

    [Fact]
    public void Friendly_Result_Labels_Never_Claim_Success_On_Weak_Data()
    {
        Assert.Equal("🟢 Potential Improvement", SandboxTestResult.Improved.ToFriendlyLabel());
        Assert.Equal("🟡 Similar", SandboxTestResult.Similar.ToFriendlyLabel());
        Assert.Equal("🔴 Degraded", SandboxTestResult.Degraded.ToFriendlyLabel());
        Assert.Equal("🟡 Inconclusive", SandboxTestResult.Inconclusive.ToFriendlyLabel());
        Assert.Equal("⚪ Not Enough Data", SandboxTestResult.InsufficientData.ToFriendlyLabel());
    }
}
