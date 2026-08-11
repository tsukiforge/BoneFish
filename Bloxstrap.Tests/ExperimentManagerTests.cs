using Bloxstrap.Sandbox;
using Bloxstrap.Sandbox.Interfaces;
using Bloxstrap.Sandbox.Models;
using Xunit;

namespace Bloxstrap.Tests;

public class ExperimentManagerTests
{
    [Fact]
    public void CreateExperiment_Returns_Draft_With_Incrementing_Id()
    {
        using var h = new TestSandboxHarness();

        var first = h.Service.Manager.CreateExperiment("Balanced", new[] { SandboxTestHelpers.Set("FFlagA", "true") });
        var second = h.Service.Manager.CreateExperiment("UltraLow", Array.Empty<SandboxChange>());

        Assert.Equal("001", first.Id);
        Assert.Equal("002", second.Id);
        Assert.Equal(SandboxExperimentState.Draft, first.State);
        Assert.Equal("Balanced", first.BaseProfile);
        Assert.Single(first.Changes);
    }

    [Fact]
    public void Valid_Transitions_Are_Accepted()
    {
        using var h = new TestSandboxHarness();
        var exp = h.CreateExperiment();

        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.Preparing);
        Assert.Equal(SandboxExperimentState.Preparing, exp.State);

        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.SnapshotCreated);
        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.Applying);
        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.ReadyForTesting);
        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.Testing);
        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.Completed);
        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.Committed);

        Assert.Equal(SandboxExperimentState.Committed, exp.State);
        Assert.True(exp.State.IsTerminal());
    }

    [Fact]
    public void Invalid_Transitions_Are_Rejected()
    {
        using var h = new TestSandboxHarness();
        var exp = h.CreateExperiment();

        // Draft → Completed is not allowed.
        Assert.Throws<SandboxException>(() => h.Service.Manager.TransitionTo(exp, SandboxExperimentState.Completed));
        // Draft → Testing is not allowed either (must go through the full pipeline).
        Assert.Throws<SandboxException>(() => h.Service.Manager.TransitionTo(exp, SandboxExperimentState.Testing));
    }

    [Fact]
    public void Rollback_Transitions_Follow_The_State_Machine()
    {
        using var h = new TestSandboxHarness();

        foreach (var state in new[]
        {
            SandboxExperimentState.ReadyForTesting,
            SandboxExperimentState.Testing,
            SandboxExperimentState.Completed,
            SandboxExperimentState.Failed
        })
        {
            var exp = h.CreateExperiment();
            exp.State = state; // simulate reaching the state

            Assert.True(ExperimentManager.IsTransitionAllowed(state, SandboxExperimentState.RollingBack));
        }

        // Draft / SnapshotCreated cannot roll back directly (they cancel instead).
        Assert.False(ExperimentManager.IsTransitionAllowed(SandboxExperimentState.Draft, SandboxExperimentState.RollingBack));
        Assert.False(ExperimentManager.IsTransitionAllowed(SandboxExperimentState.SnapshotCreated, SandboxExperimentState.RollingBack));
    }

    [Fact]
    public void Cancellation_Is_Allowed_From_Safe_States()
    {
        using var h = new TestSandboxHarness();

        foreach (var state in new[]
        {
            SandboxExperimentState.Draft,
            SandboxExperimentState.SnapshotCreated,
            SandboxExperimentState.Failed
        })
        {
            Assert.True(ExperimentManager.IsTransitionAllowed(state, SandboxExperimentState.Cancelled));
        }

        Assert.False(ExperimentManager.IsTransitionAllowed(SandboxExperimentState.Testing, SandboxExperimentState.Cancelled));
        Assert.False(ExperimentManager.IsTransitionAllowed(SandboxExperimentState.Completed, SandboxExperimentState.Cancelled));
    }

    [Fact]
    public void Active_Experiment_Is_Tracked_And_Cleared()
    {
        using var h = new TestSandboxHarness();
        var exp = h.CreateExperiment();

        Assert.Null(h.Service.Manager.ActiveExperiment);

        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.Preparing);
        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.SnapshotCreated);
        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.Applying);
        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.ReadyForTesting);

        Assert.Same(exp, h.Service.Manager.ActiveExperiment);

        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.RollingBack);
        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.RolledBack);

        Assert.Null(h.Service.Manager.ActiveExperiment);
    }

    [Fact]
    public void Journal_Persists_Across_Manager_Instances()
    {
        using var h = new TestSandboxHarness();
        var exp = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"));
        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.Preparing);
        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.SnapshotCreated);
        exp.SnapshotId = "snapshot_001";
        h.Service.Manager.Persist();

        // Simulate a restart: a fresh manager reads the same journal file.
        var reloaded = new ExperimentManager(h.StorageRoot);

        var found = reloaded.Find("001");
        Assert.NotNull(found);
        Assert.Equal(SandboxExperimentState.SnapshotCreated, found!.State);
        Assert.Equal("snapshot_001", found.SnapshotId);
        Assert.Equal("001", reloaded.ActiveExperiment?.Id);
        Assert.True(found.IsUnfinished);
    }

    [Fact]
    public void Update_Changes_Only_Allowed_On_Drafts()
    {
        using var h = new TestSandboxHarness();
        var exp = h.CreateExperiment(SandboxTestHelpers.Set("FFlagA", "true"));

        Assert.True(h.Service.Manager.TryUpdateChanges(exp, new[] { SandboxTestHelpers.Set("FFlagB", "false") }));
        Assert.Equal("FFlagB", exp.Changes.Single().FlagName);

        h.Service.Manager.TransitionTo(exp, SandboxExperimentState.Preparing);
        Assert.False(h.Service.Manager.TryUpdateChanges(exp, new[] { SandboxTestHelpers.Set("FFlagC", "true") }));
    }
}
