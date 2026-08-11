using Bloxstrap.Sandbox.Interfaces;
using Bloxstrap.Sandbox.Models;

namespace Bloxstrap.Sandbox
{
    /// <summary>
    /// Orchestrates optimization experiments end to end:
    ///
    /// Apply:     validate → check Roblox state → create snapshot → write → verify → ReadyForTesting
    /// Test:      measure baseline (temporary rollback) / measure experiment / record result
    /// Rollback:  load snapshot → restore → verify → mark rolled back (deterministic)
    /// Commit:    persist changes into the active BoneFish profile
    ///
    /// The service never kills Roblox, never touches anything outside the FastFlag configuration,
    /// and never claims success without verification.
    /// </summary>
    public class OptimizationSandboxService
    {
        private const string LOG_IDENT = "OptimizationSandbox";

        private readonly IFastFlagStore _store;
        private readonly Action<string>? _presetWriter;
        private readonly Func<bool>? _robloxRunningCheck;

        public ExperimentManager Manager { get; }
        public SnapshotService Snapshots { get; }
        public PerformanceMeasurementService Performance { get; } = new();

        /// <summary>The flag store the sandbox operates on (App.FastFlags in production).</summary>
        public IFastFlagStore FastFlags => _store;

        public OptimizationSandboxService(
            IFastFlagStore store,
            Action<string>? presetWriter = null,
            Func<bool>? robloxRunningCheck = null,
            string? storageRoot = null)
        {
            _store = store;
            _presetWriter = presetWriter;
            _robloxRunningCheck = robloxRunningCheck;
            Manager = new ExperimentManager(storageRoot);
            Snapshots = new SnapshotService(store, storageRoot);
        }

        public bool IsRobloxRunning => _robloxRunningCheck?.Invoke() ?? DefaultRobloxRunningCheck();

        private static bool DefaultRobloxRunningCheck()
        {
            try
            {
                return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(App.RobloxPlayerAppName)).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        // ── Apply ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Safely apply an experiment: validate, check Roblox state, snapshot, write, verify.
        /// On any failure the configuration is rolled back immediately — partial changes are never left behind.
        /// </summary>
        /// <param name="confirmedRestart">
        /// Must be true when Roblox is running; the UI is responsible for informing the user that
        /// the experiment requires a Roblox restart and asking for explicit confirmation.
        /// </param>
        public async Task<bool> ApplyAsync(
            SandboxExperiment experiment,
            bool confirmedRestart,
            CancellationToken cancellationToken = default)
        {
            if (experiment.State != SandboxExperimentState.Draft)
                throw new SandboxException($"Cannot apply experiment in state {experiment.State}");

            ValidateChanges(experiment);

            if (IsRobloxRunning && !confirmedRestart)
                throw new SandboxException("Roblox is running. This experiment requires Roblox to restart — apply only after explicit confirmation.");

            App.Logger.WriteLine(LOG_IDENT, $"{experiment.DisplayName}: apply started");

            Manager.TransitionTo(experiment, SandboxExperimentState.Preparing);

            try
            {
                var snapshot = await Snapshots.CreateAsync(experiment, experiment.BaseProfile, cancellationToken);
                experiment.SnapshotId = snapshot.Id;
                Manager.TransitionTo(experiment, SandboxExperimentState.SnapshotCreated);
                App.Logger.WriteLine(LOG_IDENT, $"SandboxSnapshotCreated: {snapshot.Id}");

                Manager.TransitionTo(experiment, SandboxExperimentState.Applying);
                App.Logger.WriteLine(LOG_IDENT, $"SandboxApplyStarted: {experiment.DisplayName}");

                await WriteChangesAsync(experiment.Changes, cancellationToken);

                if (!await VerifyChangesAsync(experiment.Changes, cancellationToken))
                    throw new SandboxException("Configuration verification failed after writing");

                Manager.TransitionTo(experiment, SandboxExperimentState.ReadyForTesting);
                App.Logger.WriteLine(LOG_IDENT, $"SandboxApplyCompleted: {experiment.DisplayName}");
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"SandboxApplyFailed: {experiment.DisplayName} — {ex.Message}");

                // Never leave partial configuration changes behind.
                try
                {
                    await RollbackAsync(experiment, cancellationToken);
                    App.Logger.WriteLine(LOG_IDENT, $"SandboxRollbackCompleted (auto after failed apply): {experiment.DisplayName}");
                }
                catch (Exception rollbackEx)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Automatic rollback after failed apply also failed: {rollbackEx.Message}");
                    Manager.MarkFailed(experiment, rollbackEx.Message);
                }

                throw;
            }
        }

        private void ValidateChanges(SandboxExperiment experiment)
        {
            if (experiment.Changes.Count == 0)
                throw new SandboxException("The experiment has no changes to apply.");

            foreach (var change in experiment.Changes)
            {
                string? error = SandboxChangeValidator.GetFirstInvalidChangeMessage(change);
                if (error is not null)
                    throw new SandboxException(error);
            }
        }

        private async Task WriteChangesAsync(IReadOnlyList<SandboxChange> changes, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                foreach (var change in changes)
                {
                    if (string.IsNullOrWhiteSpace(change.FlagName))
                        continue;

                    _store.SetValue(change.FlagName, change.NewValue);
                }

                _store.Save();
            }, cancellationToken);
        }

        private async Task<bool> VerifyChangesAsync(IReadOnlyList<SandboxChange> changes, CancellationToken cancellationToken)
        {
            Dictionary<string, string> current = new();
            await Task.Run(() => current = _store.GetAll(), cancellationToken);

            foreach (var change in changes)
            {
                bool exists = current.TryGetValue(change.FlagName, out string? actual);

                if (change.NewValue is null)
                {
                    if (exists)
                        return false;
                }
                else if (!exists || !string.Equals(actual, change.NewValue, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        // ── Testing ───────────────────────────────────────────────────────────────────

        /// <summary>Enter the Testing state. Safe to call from ReadyForTesting.</summary>
        public void StartTesting(SandboxExperiment experiment)
        {
            if (experiment.State != SandboxExperimentState.ReadyForTesting)
                throw new SandboxException($"Cannot start testing from state {experiment.State}");

            Manager.TransitionTo(experiment, SandboxExperimentState.Testing);
            App.Logger.WriteLine(LOG_IDENT, $"SandboxTestStarted: {experiment.DisplayName}");
        }

        /// <summary>
        /// Measure the PRE-experiment configuration by temporarily restoring the snapshot,
        /// sampling, and then re-applying the experiment changes. The baseline snapshot is
        /// not re-created — the original snapshot remains the single source of truth.
        /// </summary>
        public async Task<SandboxFpsSample> MeasureBaselineAsync(
            SandboxExperiment experiment,
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            if (experiment.State != SandboxExperimentState.Testing)
                throw new SandboxException($"Baseline measurement requires the Testing state (current: {experiment.State})");

            if (experiment.SnapshotId is null)
                throw new SandboxException("No snapshot available for baseline measurement");

            var snapshot = await Snapshots.LoadAsync(experiment.SnapshotId, cancellationToken)
                ?? throw new SandboxException("Snapshot not found for baseline measurement");

            SandboxFpsSample sample;

            await Snapshots.RestoreAsync(snapshot, experiment.Changes.Select(c => c.FlagName), cancellationToken);

            try
            {
                sample = await Performance.MeasureAsync(duration, null, cancellationToken);
            }
            finally
            {
                // Put the experiment back into effect regardless of measurement outcome.
                await WriteChangesAsync(experiment.Changes, cancellationToken);

                if (!await VerifyChangesAsync(experiment.Changes, cancellationToken))
                {
                    var rollbackError = new SandboxException("Failed to re-apply experiment changes after baseline measurement");
                    Manager.MarkFailed(experiment, rollbackError.Message);
                    throw rollbackError;
                }
            }

            experiment.Measurement ??= new SandboxMeasurement();
            experiment.Measurement.Before = sample;
            Manager.Persist();
            return sample;
        }

        /// <summary>Measure the experiment configuration while Testing.</summary>
        public async Task<SandboxFpsSample> MeasureNowAsync(
            SandboxExperiment experiment,
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            if (experiment.State != SandboxExperimentState.Testing)
                throw new SandboxException($"Measurement requires the Testing state (current: {experiment.State})");

            var sample = await Performance.MeasureAsync(duration, null, cancellationToken);

            experiment.Measurement ??= new SandboxMeasurement();
            experiment.Measurement.After = sample;
            Manager.Persist();
            return sample;
        }

        /// <summary>Classify the recorded measurement and move the experiment to Completed.</summary>
        public void RecordResult(SandboxExperiment experiment)
        {
            if (experiment.State != SandboxExperimentState.Testing)
                throw new SandboxException($"Cannot record a result from state {experiment.State}");

            if (experiment.Measurement is null)
            {
                Manager.MarkFailed(experiment, "No measurement recorded before finishing testing");
                throw new SandboxException("No measurement recorded");
            }

            var result = PerformanceMeasurementService.Classify(
                experiment.Measurement.Before,
                experiment.Measurement.After);

            experiment.Result = result;
            experiment.ResultLabel = PerformanceMeasurementService.ResultToLabel(result);

            Manager.TransitionTo(experiment, SandboxExperimentState.Completed);
            App.Logger.WriteLine(LOG_IDENT, $"SandboxTestCompleted: {experiment.DisplayName} — result {experiment.ResultLabel}");
        }

        // ── Rollback ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Deterministic rollback: load snapshot → restore original values → verify → mark rolled back.
        /// Throws (and marks the experiment Failed) when verification fails — rollback is never
        /// claimed to have succeeded without proof.
        /// </summary>
        public async Task RollbackAsync(SandboxExperiment experiment, CancellationToken cancellationToken = default)
        {
            if (!ExperimentManager.IsTransitionAllowed(experiment.State, SandboxExperimentState.RollingBack))
                throw new SandboxException($"Cannot roll back experiment in state {experiment.State}");

            if (experiment.SnapshotId is null)
                throw new SandboxException("No snapshot available for rollback");

            Manager.TransitionTo(experiment, SandboxExperimentState.RollingBack);
            App.Logger.WriteLine(LOG_IDENT, $"SandboxRollbackStarted: {experiment.DisplayName}");

            try
            {
                var snapshot = await Snapshots.LoadAsync(experiment.SnapshotId, cancellationToken)
                    ?? throw new SandboxException("Snapshot not found for rollback");

                await Snapshots.RestoreAsync(snapshot, experiment.Changes.Select(c => c.FlagName), cancellationToken);

                Manager.TransitionTo(experiment, SandboxExperimentState.RolledBack);
                App.Logger.WriteLine(LOG_IDENT, $"SandboxRollbackCompleted: {experiment.DisplayName}");
            }
            catch (Exception ex)
            {
                Manager.MarkFailed(experiment, ex.Message);
                App.Logger.WriteLine(LOG_IDENT, $"SandboxRollbackFailed: {experiment.DisplayName} — {ex.Message}");
                throw;
            }
        }

        // ── Cancel ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Cancel from any safe state. If the configuration was modified, it is rolled back first
        /// (restore + verify), then the experiment is marked Cancelled.
        /// </summary>
        public async Task CancelAsync(SandboxExperiment experiment, CancellationToken cancellationToken = default)
        {
            switch (experiment.State)
            {
                case SandboxExperimentState.Draft:
                    Manager.TransitionTo(experiment, SandboxExperimentState.Cancelled);
                    break;

                case SandboxExperimentState.SnapshotCreated:
                    // Nothing was written yet — cancelling is safe without a restore.
                    Manager.TransitionTo(experiment, SandboxExperimentState.Cancelled);
                    break;

                case SandboxExperimentState.Failed when experiment.SnapshotId is null:
                    Manager.TransitionTo(experiment, SandboxExperimentState.Cancelled);
                    break;

                default:
                    if (!ExperimentManager.IsTransitionAllowed(experiment.State, SandboxExperimentState.RollingBack))
                        throw new SandboxException($"Cannot cancel experiment in state {experiment.State}");

                    if (experiment.SnapshotId is null)
                        throw new SandboxException("No snapshot available to restore before cancelling");

                    Manager.TransitionTo(experiment, SandboxExperimentState.RollingBack);

                    try
                    {
                        var snapshot = await Snapshots.LoadAsync(experiment.SnapshotId, cancellationToken)
                            ?? throw new SandboxException("Snapshot not found for cancellation restore");

                        await Snapshots.RestoreAsync(snapshot, experiment.Changes.Select(c => c.FlagName), cancellationToken);

                        Manager.TransitionTo(experiment, SandboxExperimentState.Cancelled);
                    }
                    catch (Exception ex)
                    {
                        Manager.MarkFailed(experiment, ex.Message);
                        throw;
                    }
                    break;
            }

            App.Logger.WriteLine(LOG_IDENT, $"SandboxExperimentCancelled: {experiment.DisplayName}");
        }

        // ── Commit ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Commit a Completed experiment: ensure the changes are persisted in the FastFlag
        /// configuration and integrate them into the active BoneFish profile without duplicating
        /// the entire base configuration. The base profile name is preserved (or assigned from the
        /// experiment's base) so the existing manual-preset guard keeps auto-optimization from
        /// overwriting the committed flags on the next launch.
        /// </summary>
        public async Task<bool> CommitAsync(SandboxExperiment experiment, CancellationToken cancellationToken = default)
        {
            if (experiment.State != SandboxExperimentState.Completed)
                throw new SandboxException($"Cannot commit experiment in state {experiment.State}");

            App.Logger.WriteLine(LOG_IDENT, $"SandboxCommitStarted: {experiment.DisplayName}");

            // Ensure the changes are still in place, re-writing them if something touched them.
            if (!await VerifyChangesAsync(experiment.Changes, cancellationToken))
            {
                await WriteChangesAsync(experiment.Changes, cancellationToken);

                if (!await VerifyChangesAsync(experiment.Changes, cancellationToken))
                    throw new SandboxException("Commit verification failed");
            }

            if (_presetWriter is not null)
            {
                string currentPreset = App.Settings.Prop.SelectedPerformancePreset ?? "None";

                string targetPreset = currentPreset switch
                {
                    "UltraLow" or "Balanced" or "Stable" or "ExtremePerformance" => currentPreset,
                    _ when experiment.BaseProfile is "UltraLow" or "Balanced" or "Stable" or "ExtremePerformance" => experiment.BaseProfile,
                    _ => "Balanced"
                };

                if (targetPreset != currentPreset)
                {
                    _presetWriter(targetPreset);
                    App.Logger.WriteLine(LOG_IDENT, $"Commit: preset '{currentPreset}' → '{targetPreset}' ({experiment.DisplayName})");
                }
            }

            Manager.TransitionTo(experiment, SandboxExperimentState.Committed);
            App.Logger.WriteLine(LOG_IDENT, $"SandboxCommitCompleted: {experiment.DisplayName}");
            return true;
        }
    }
}
