using System.Windows;
using Bloxstrap.Sandbox.Interfaces;
using Bloxstrap.Sandbox.Models;

namespace Bloxstrap.Sandbox
{
    /// <summary>
    /// Detects experiments that did not finish correctly (crash, restart, power loss) and offers
    /// the user a safe way back: restore the previous configuration, continue, or ignore.
    /// The current configuration is verified against the snapshot before anything is claimed.
    /// </summary>
    public static class ExperimentRecoveryService
    {
        private const string LOG_IDENT = "OptimizationSandbox::Recovery";

        /// <summary>
        /// Find the experiment that was active when the process stopped, excluding drafts and
        /// already-acknowledged interruptions. Pure logic, used by tests.
        /// </summary>
        public static SandboxExperiment? FindUnfinishedExperiment(ExperimentManager manager)
        {
            SandboxExperiment? active = manager.ActiveExperiment;

            if (active is null || !active.IsUnfinished)
                return null;

            return active;
        }

        /// <summary>
        /// Verify whether the current configuration still matches the experiment's changes.
        /// A mismatch means the experiment is not (or no longer) applied and restoring is the
        /// safe recommendation.
        /// </summary>
        public static async Task<bool> IsExperimentCurrentlyAppliedAsync(
            OptimizationSandboxService service,
            SandboxExperiment experiment,
            CancellationToken cancellationToken = default)
        {
            if (experiment.Changes.Count == 0)
                return false;

            var flags = await Task.Run(() => service.FastFlags.GetAll(), cancellationToken);

            foreach (var change in experiment.Changes)
            {
                bool exists = flags.TryGetValue(change.FlagName, out string? actual);

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

        /// <summary>
        /// Prompt the user about an unfinished experiment. Must be called on the UI thread.
        /// Yes → restore the previous configuration (async). No → continue the experiment.
        /// Cancel → ignore, but remember the acknowledgment so the prompt does not nag every startup.
        /// </summary>
        public static async void PromptIfNeeded()
        {
            var service = App.Sandbox;
            var experiment = FindUnfinishedExperiment(service.Manager);

            if (experiment is null)
                return;

            if (experiment.RecoveryAcknowledged)
                return;

            App.Logger.WriteLine(LOG_IDENT, $"SandboxRecoveryDetected: {experiment.DisplayName} in state {experiment.State}");

            bool currentlyApplied = false;
            try
            {
                currentlyApplied = await IsExperimentCurrentlyAppliedAsync(service, experiment);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not verify current configuration: {ex.Message}");
            }

            string stateDescription = experiment.State == SandboxExperimentState.SnapshotCreated
                ? "The experiment was backed up but never applied — your current configuration is unchanged."
                : currentlyApplied
                    ? "The experiment's changes are still applied to your Roblox configuration."
                    : "The configuration does not match the experiment's changes (possibly partially applied).";

            var choice = Frontend.ShowMessageBox(
                $"⚠ Unfinished Optimization Experiment\n\n" +
                $"{experiment.DisplayName} (base: {experiment.BaseProfile}) did not finish correctly.\n" +
                $"{stateDescription}\n\n" +
                $"Yes   → Restore the previous configuration\n" +
                $"No    → Continue the experiment\n" +
                $"Cancel → Ignore (do not ask again)",
                MessageBoxImage.Warning,
                MessageBoxButton.YesNoCancel);

            switch (choice)
            {
                case MessageBoxResult.Yes:
                    await RestoreAndReportAsync(experiment);
                    break;

                case MessageBoxResult.No:
                    App.Logger.WriteLine(LOG_IDENT, $"{experiment.DisplayName}: user chose to continue");
                    break;

                default:
                    service.Manager.AcknowledgeRecovery(experiment);
                    App.Logger.WriteLine(LOG_IDENT, $"{experiment.DisplayName}: recovery ignored by user");
                    break;
            }
        }

        private static async Task RestoreAndReportAsync(SandboxExperiment experiment)
        {
            var service = App.Sandbox;

            try
            {
                bool restored = await Task.Run(() => RestoreSynchronously(experiment));

                await App.Current.Dispatcher.InvokeAsync(() =>
                {
                    Frontend.ShowMessageBox(
                        restored
                            ? "✓ Configuration restored — your original FastFlags are back."
                            : "Configuration was restored but verification failed. Check the logs and the Sandbox page.",
                        restored ? MessageBoxImage.Information : MessageBoxImage.Warning);
                });
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Recovery restore failed: {ex.Message}");

                await App.Current.Dispatcher.InvokeAsync(() =>
                {
                    Frontend.ShowMessageBox(
                        $"Recovery restore FAILED:\n{ex.Message}\n\nThe Sandbox page shows the experiment state — do not apply new changes until this is resolved.",
                        MessageBoxImage.Error);
                });
            }
        }

        /// <summary>Runs on a background thread: restore via snapshot; report success only when verified.</summary>
        private static bool RestoreSynchronously(SandboxExperiment experiment)
        {
            var service = App.Sandbox;

            if (!ExperimentManager.IsTransitionAllowed(experiment.State, SandboxExperimentState.RollingBack))
                throw new SandboxException($"Cannot restore experiment in state {experiment.State}");

            var snapshot = service.Snapshots.LoadAsync(experiment.SnapshotId ?? "").GetAwaiter().GetResult();

            if (snapshot is null)
                throw new SandboxException("Snapshot not found — refusing to restore blindly");

            service.Manager.TransitionTo(experiment, SandboxExperimentState.RollingBack);
            service.Snapshots.RestoreAsync(snapshot, experiment.Changes.Select(c => c.FlagName)).GetAwaiter().GetResult();

            // RestoreAsync already verifies; reaching here means the verification passed.
            service.Manager.TransitionTo(experiment, SandboxExperimentState.RolledBack);
            return true;
        }
    }
}
