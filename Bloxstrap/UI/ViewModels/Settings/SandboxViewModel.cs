using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Bloxstrap.Sandbox;
using Bloxstrap.Sandbox.Interfaces;
using Bloxstrap.Sandbox.Models;

namespace Bloxstrap.UI.ViewModels.Settings
{
    /// <summary>Editable row in the experiment's change list.</summary>
    public class SandboxChangeRow : NotifyPropertyChangedViewModel
    {
        private string _flagName = "";
        private string? _newValue;
        private string? _currentValue;
        private string? _validationError;

        public string FlagName
        {
            get => _flagName;
            set { _flagName = value; OnPropertyChanged(nameof(FlagName)); }
        }

        public string? NewValue
        {
            get => _newValue;
            set { _newValue = value; OnPropertyChanged(nameof(NewValue)); }
        }

        public string? CurrentValue
        {
            get => _currentValue;
            set { _currentValue = value; OnPropertyChanged(nameof(CurrentValue)); }
        }

        public string? ValidationError
        {
            get => _validationError;
            set { _validationError = value; OnPropertyChanged(nameof(ValidationError)); }
        }

        public SandboxChange ToChange() => new() { FlagName = FlagName.Trim(), NewValue = string.IsNullOrWhiteSpace(NewValue) ? null : NewValue.Trim() };
    }

    public class SandboxViewModel : NotifyPropertyChangedViewModel
    {
        private readonly OptimizationSandboxService _service = App.Sandbox;
        private SandboxExperiment? _working;
        private SandboxExperiment? _selectedHistory;
        private bool _isBusy;
        private string _busyText = "";

        public static readonly TimeSpan MeasurementDuration = TimeSpan.FromSeconds(30);

        public SandboxViewModel()
        {
            ReloadState();
        }

        public event EventHandler<string>? RequestNotificationEvent;

        private void Notify(string message) => RequestNotificationEvent?.Invoke(this, message);

        // ── Base profile ──────────────────────────────────────────────────────────────

        public IReadOnlyList<string> BaseProfiles { get; } = new[]
        {
            "None", "Balanced", "UltraLow", "Stable", "AutoOptimize", "ExtremePerformance"
        };

        public string SelectedBaseProfile
        {
            get => _working?.BaseProfile ?? "Balanced";
            set
            {
                if (_working is null) return;
                _working.BaseProfile = value;
                _service.Manager.Persist();
                OnPropertyChanged(nameof(SelectedBaseProfile));
            }
        }

        // ── Change editor ─────────────────────────────────────────────────────────────

        public ObservableCollection<SandboxChangeRow> Changes { get; } = new();

        public ObservableCollection<SandboxDiffEntry> DiffPreview { get; } = new();

        public bool CanEditChanges => _working?.State == SandboxExperimentState.Draft;

        public ICommand AddChangeCommand => new RelayCommand(AddChange);

        public ICommand RemoveChangeCommand => new RelayCommand<SandboxChangeRow>(RemoveChange);

        public ICommand NewExperimentCommand => new RelayCommand(NewExperiment);

        private void AddChange()
        {
            if (!CanEditChanges) return;
            Changes.Add(new SandboxChangeRow { CurrentValue = "" });
            RefreshDiffPreview();
        }

        private void RemoveChange(SandboxChangeRow? row)
        {
            if (row is null || !CanEditChanges) return;
            Changes.Remove(row);
            RefreshDiffPreview();
        }

        private void NewExperiment()
        {
            if (_working is { State: SandboxExperimentState.Draft })
            {
                // Reuse the existing draft id instead of spamming history.
                _working.Changes.Clear();
                _working.BaseProfile = "Balanced";
                _working.LastError = null;
                _service.Manager.Persist();
            }
            else
            {
                _working = _service.Manager.CreateExperiment("Balanced", Array.Empty<SandboxChange>());
            }

            ReloadState();
            Notify("New experiment created. Add FastFlag changes below.");
        }

        // ── Status ────────────────────────────────────────────────────────────────────

        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(nameof(IsBusy)); }
        }

        public string BusyText
        {
            get => _busyText;
            set { _busyText = value; OnPropertyChanged(nameof(BusyText)); }
        }

        public SandboxExperiment? WorkingExperiment => _working;

        public string ExperimentTitle => _working?.DisplayName ?? "No experiment";

        public string StateText => _working is null
            ? "Idle"
            : _working.State.ToString();

        public string SnapshotStatusText => _working is null
            ? "—"
            : _working.State is SandboxExperimentState.Draft or SandboxExperimentState.Preparing
                ? "Not created"
                : "✓ Ready";

        public string RobloxStatusText => _service.IsRobloxRunning ? "● Roblox running" : "○ Roblox not running";

        public bool IsRobloxRunning => _service.IsRobloxRunning;

        public string ResultText => _working is null || _working.Result is null
            ? "No result recorded"
            : $"Result: {_working.ResultLabel}";

        public string MeasurementText => _working?.Measurement is null
            ? "No measurements recorded"
            : $"Before: {(FormatFps(_working.Measurement.Before))}   After: {FormatFps(_working.Measurement.After)}";

        private static string FormatFps(SandboxFpsSample? sample) =>
            sample is null ? "not measured" : $"{sample.MedianFps:F1} FPS ({sample.SampleCount} samples)";

        public string ErrorText => _working?.LastError is null ? "" : $"⚠ {_working.LastError}";

        public bool HasError => !string.IsNullOrEmpty(ErrorText);

        // ── Recovery banner ───────────────────────────────────────────────────────────

        public bool RecoveryBannerVisible => _working?.IsUnfinished == true;

        public string RecoveryBannerText => _working?.IsUnfinished == true
            ? $"⚠ Unfinished experiment {_working.DisplayName} (state: {_working.State}). Choose an action below or use the recovery prompt at the next start."
            : "";

        // ── Command visibility ────────────────────────────────────────────────────────

        public bool CanApply => _working?.State == SandboxExperimentState.Draft && Changes.Count > 0;
        public bool CanStartTest => _working?.State == SandboxExperimentState.ReadyForTesting;
        public bool CanMeasure => _working?.State == SandboxExperimentState.Testing;
        public bool CanRecordResult => _working?.State == SandboxExperimentState.Testing;
        public bool CanRollback => _working?.State is SandboxExperimentState.ReadyForTesting
            or SandboxExperimentState.Testing
            or SandboxExperimentState.Completed
            or SandboxExperimentState.Failed;
        public bool CanCancel => _working?.State is SandboxExperimentState.Draft
            or SandboxExperimentState.SnapshotCreated
            or SandboxExperimentState.ReadyForTesting
            or SandboxExperimentState.Testing
            or SandboxExperimentState.Failed;
        public bool CanCommit => _working?.State == SandboxExperimentState.Completed;

        // ── History ───────────────────────────────────────────────────────────────────

        public ObservableCollection<SandboxExperiment> History { get; } = new();

        public SandboxExperiment? SelectedHistory
        {
            get => _selectedHistory;
            set { _selectedHistory = value; OnPropertyChanged(nameof(SelectedHistory)); OnPropertyChanged(nameof(HistoryDetailText)); }
        }

        public string HistoryDetailText
        {
            get
            {
                if (_selectedHistory is null)
                    return "Select an experiment to inspect its changes, result and status.";

                var e = _selectedHistory;
                string changes = e.Changes.Count == 0
                    ? "  (no changes)"
                    : string.Join("\n", e.Changes.Select(c => $"  {c}"));

                return $"{e.DisplayName}   created {e.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}\n" +
                       $"  Status: {e.State}" +
                       (string.IsNullOrEmpty(e.BaseProfile) ? "" : $"   Base: {e.BaseProfile}") +
                       (string.IsNullOrEmpty(e.SnapshotId) ? "" : $"   Snapshot: {e.SnapshotId}") +
                       (e.Result is null ? "" : $"\n  Result: {e.ResultLabel}") +
                       (e.Measurement is null ? "" : $"\n  {MeasurementTextFor(e)}") +
                       $"\n  Changes:\n{changes}" +
                       (string.IsNullOrEmpty(e.LastError) ? "" : $"\n  Error: {e.LastError}");
            }
        }

        private static string MeasurementTextFor(SandboxExperiment e) =>
            $"  Before: {FormatFps(e.Measurement!.Before)}   After: {FormatFps(e.Measurement.After)}";

        // ── Commands ──────────────────────────────────────────────────────────────────

        public ICommand ApplyCommand => new AsyncRelayCommand(ApplyAsync);

        public ICommand StartTestCommand => new RelayCommand(StartTest);

        public ICommand MeasureBaselineCommand => new AsyncRelayCommand(() => MeasureAsync(baseline: true));

        public ICommand MeasureNowCommand => new AsyncRelayCommand(() => MeasureAsync(baseline: false));

        public ICommand RecordResultCommand => new RelayCommand(RecordResult);

        public ICommand RollbackCommand => new AsyncRelayCommand(RollbackAsync);

        public ICommand CommitCommand => new AsyncRelayCommand(CommitAsync);

        public ICommand CancelCommand => new AsyncRelayCommand(CancelAsync);

        private async Task ApplyAsync()
        {
            var exp = _working;
            if (exp is null) return;

            bool confirmedRestart = true;

            if (_service.IsRobloxRunning)
            {
                var result = Frontend.ShowMessageBox(
                    "Roblox is currently running.\n\n" +
                    "This experiment requires Roblox to restart before the changes take effect.\n" +
                    "BoneFish will NOT close or restart Roblox for you.\n\n" +
                    "Continue applying while Roblox is running?",
                    MessageBoxImage.Question, MessageBoxButton.YesNo);

                if (result != MessageBoxResult.Yes)
                    return;

                confirmedRestart = true;
            }

            IsBusy = true;
            BusyText = "Creating snapshot and applying changes...";
            try
            {
                bool ok = await _service.ApplyAsync(exp, confirmedRestart);
                if (ok)
                    Notify($"✓ {exp.DisplayName} applied and verified. Configuration is ready for testing.");
            }
            catch (SandboxException ex)
            {
                Notify($"✗ {ex.Message}");
            }
            catch (Exception ex)
            {
                Notify($"✗ Unexpected error: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
                BusyText = "";
                ReloadState();
            }
        }

        private void StartTest()
        {
            var exp = _working;
            if (exp is null) return;

            _service.StartTesting(exp);

            Notify("Testing started. Launch/join a Roblox game, then use \"Measure baseline\" (optional) and \"Measure now\".");
            ReloadState();
        }

        private async Task MeasureAsync(bool baseline)
        {
            var exp = _working;
            if (exp is null) return;

            IsBusy = true;
            BusyText = baseline
                ? "Measuring the PREVIOUS configuration for 30s — playing now is fine..."
                : "Measuring the experiment configuration for 30s...";

            try
            {
                var sample = baseline
                    ? await _service.MeasureBaselineAsync(exp, MeasurementDuration)
                    : await _service.MeasureNowAsync(exp, MeasurementDuration);

                Notify(sample.Reliable
                    ? $"✓ Measured {sample.MedianFps:F1} FPS (median, {sample.SampleCount} samples)."
                    : "Measurement could not be verified (ETW telemetry needs administrator rights or not enough data). Result will be inconclusive.");
            }
            catch (SandboxException ex)
            {
                Notify($"✗ {ex.Message}");
            }
            finally
            {
                IsBusy = false;
                BusyText = "";
                ReloadState();
            }
        }

        private void RecordResult()
        {
            var exp = _working;
            if (exp is null) return;

            try
            {
                _service.RecordResult(exp);
                Notify($"✓ Testing complete. Result: {exp.ResultLabel}. Commit the changes or roll back.");
            }
            catch (SandboxException ex)
            {
                Notify($"✗ {ex.Message}");
            }
            finally
            {
                ReloadState();
            }
        }

        private async Task RollbackAsync()
        {
            var exp = _working;
            if (exp is null) return;

            IsBusy = true;
            BusyText = "Restoring the snapshot...";
            try
            {
                await _service.RollbackAsync(exp);
                Notify("✓ Configuration restored to the snapshot state (verified).");
            }
            catch (SandboxException ex)
            {
                Notify($"✗ Rollback failed: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
                BusyText = "";
                ReloadState();
            }
        }

        private async Task CommitAsync()
        {
            var exp = _working;
            if (exp is null) return;

            IsBusy = true;
            BusyText = "Committing changes to the active profile...";
            try
            {
                await _service.CommitAsync(exp);
                Notify($"✓ {exp.DisplayName} committed to the active profile. Changes are persistent.");
            }
            catch (SandboxException ex)
            {
                Notify($"✗ Commit failed: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
                BusyText = "";
                ReloadState();
            }
        }

        private async Task CancelAsync()
        {
            var exp = _working;
            if (exp is null) return;

            bool needsRestore = exp.State is SandboxExperimentState.ReadyForTesting
                or SandboxExperimentState.Testing
                or SandboxExperimentState.Completed
                or SandboxExperimentState.Failed;

            if (needsRestore)
            {
                var result = Frontend.ShowMessageBox(
                    "Cancelling this experiment will restore your previous configuration.\n\nContinue?",
                    MessageBoxImage.Question, MessageBoxButton.YesNo);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            IsBusy = true;
            BusyText = "Cancelling...";
            try
            {
                await _service.CancelAsync(exp);
                Notify("Experiment cancelled.");
            }
            catch (SandboxException ex)
            {
                Notify($"✗ Cancel failed: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
                BusyText = "";
                ReloadState();
            }
        }

        // ── State refresh ─────────────────────────────────────────────────────────────

        public void ReloadState()
        {
            _working = _service.Manager.ActiveExperiment
                ?? _service.Manager.History.FirstOrDefault(e => e.State == SandboxExperimentState.Draft)
                ?? _working;

            Changes.Clear();
            if (_working is not null)
            {
                foreach (var change in _working.Changes)
                {
                    Changes.Add(new SandboxChangeRow
                    {
                        FlagName = change.FlagName,
                        NewValue = change.NewValue,
                        CurrentValue = _service.FastFlags.GetValue(change.FlagName)
                    });
                }
            }

            RefreshDiffPreview();

            History.Clear();
            foreach (var experiment in _service.Manager.History)
                History.Add(experiment);

            if (_selectedHistory is not null && !History.Contains(_selectedHistory))
                _selectedHistory = null;

            OnPropertyChanged(nameof(WorkingExperiment));
            OnPropertyChanged(nameof(ExperimentTitle));
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(SnapshotStatusText));
            OnPropertyChanged(nameof(RobloxStatusText));
            OnPropertyChanged(nameof(IsRobloxRunning));
            OnPropertyChanged(nameof(ResultText));
            OnPropertyChanged(nameof(MeasurementText));
            OnPropertyChanged(nameof(ErrorText));
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(RecoveryBannerVisible));
            OnPropertyChanged(nameof(RecoveryBannerText));
            OnPropertyChanged(nameof(CanEditChanges));
            OnPropertyChanged(nameof(CanApply));
            OnPropertyChanged(nameof(CanStartTest));
            OnPropertyChanged(nameof(CanMeasure));
            OnPropertyChanged(nameof(CanRecordResult));
            OnPropertyChanged(nameof(CanRollback));
            OnPropertyChanged(nameof(CanCancel));
            OnPropertyChanged(nameof(CanCommit));
            OnPropertyChanged(nameof(SelectedBaseProfile));
            OnPropertyChanged(nameof(HistoryDetailText));
        }

        private void RefreshDiffPreview()
        {
            DiffPreview.Clear();

            var baseFlags = new Dictionary<string, string>();
            foreach (var change in Changes)
            {
                if (string.IsNullOrWhiteSpace(change.FlagName))
                    continue;

                if (!baseFlags.ContainsKey(change.FlagName))
                {
                    string? current = _service.FastFlags.GetValue(change.FlagName.Trim());
                    baseFlags[change.FlagName.Trim()] = current ?? "";
                }
            }

            // Flags currently on disk that the user has not touched stay out of the diff.
            foreach (var entry in ConfigurationDiffService.ComputeDiff(baseFlags, Changes.Select(c => c.ToChange())))
                DiffPreview.Add(entry);

            OnPropertyChanged(nameof(DiffPreview));
        }
    }
}
