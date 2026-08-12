using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Bloxstrap.Sandbox;
using Bloxstrap.Sandbox.Interfaces;
using Bloxstrap.Sandbox.Models;
using Bloxstrap.UI.Elements.Dialogs;

namespace Bloxstrap.UI.ViewModels.Settings
{
    /// <summary>Visual state of one step in the Configure → Snapshot → Apply → Test → Result indicator.</summary>
    public enum SandboxStepState
    {
        Pending,
        Active,
        Done
    }

    /// <summary>One entry of the workflow step indicator. Rebuilt from the experiment state on every refresh.</summary>
    public class SandboxWorkflowStep
    {
        public string Label { get; init; } = "";
        public SandboxStepState StepState { get; init; }
        public bool IsLast { get; init; }

        public string Marker => StepState switch
        {
            SandboxStepState.Done => "✓",
            SandboxStepState.Active => "●",
            _ => "○"
        };
    }

    /// <summary>Editable row in the experiment's change list.</summary>
    public class SandboxChangeRow : NotifyPropertyChangedViewModel
    {
        private string _flagName = "";
        private string? _newValue;
        private string? _currentValue;
        private string? _validationError;

        /// <summary>Raised when the user edits the flag name or new value so the diff preview can refresh live.</summary>
        public event Action? RowChanged;

        public string FlagName
        {
            get => _flagName;
            set { _flagName = value; OnPropertyChanged(nameof(FlagName)); RowChanged?.Invoke(); }
        }

        public string? NewValue
        {
            get => _newValue;
            set { _newValue = value; OnPropertyChanged(nameof(NewValue)); RowChanged?.Invoke(); }
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

        // ── Workflow steps ────────────────────────────────────────────────────────────

        public ObservableCollection<SandboxWorkflowStep> WorkflowSteps { get; } = new();

        public bool StepsVisible => _working is not null;

        // ── Change editor ─────────────────────────────────────────────────────────────

        public ObservableCollection<SandboxChangeRow> Changes { get; } = new();

        public ObservableCollection<SandboxDiffEntry> DiffPreview { get; } = new();

        public bool CanEditChanges => _working?.State == SandboxExperimentState.Draft;

        /// <summary>True while editing when at least one real (non no-op) change is configured.</summary>
        public bool HasActualChanges =>
            _working?.State == SandboxExperimentState.Draft && DiffPreview.Any(e => e.Type != SandboxDiffType.Unchanged);

        public bool NoChangesPlaceholderVisible => DiffPreview.Count == 0;

        public ICommand AddChangeCommand => new RelayCommand(AddChange);

        public ICommand RemoveChangeCommand => new RelayCommand<SandboxChangeRow>(RemoveChange);

        public ICommand NewExperimentCommand => new RelayCommand(NewExperiment);

        private void AddChange()
        {
            if (!CanEditChanges || _working is null) return;

            var knownFlags = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in _service.FastFlags.GetAll())
                knownFlags.Add(pair.Key);
            foreach (var preset in FastFlagManager.PresetFlags.Values)
                knownFlags.Add(preset);
            foreach (var existing in _working.Changes)
                knownFlags.Add(existing.FlagName);

            var dialog = new AddSandboxChangeDialog(
                knownFlags: knownFlags,
                currentValues: _service.FastFlags.GetAll(),
                existingChangeNames: _working.Changes.Select(c => c.FlagName).ToHashSet(StringComparer.Ordinal));

            dialog.ShowDialog();

            if (dialog.Result != MessageBoxResult.OK)
                return;

            var change = new SandboxChange { FlagName = dialog.FlagName, NewValue = dialog.NewValue };

            try
            {
                _service.UpsertChange(_working, change);
                Notify($"✓ {change.FlagName} added to the experiment.");
            }
            catch (SandboxException ex)
            {
                Notify($"✗ {ex.Message}");
            }

            ReloadState();
        }

        private void RemoveChange(SandboxChangeRow? row)
        {
            if (row is null || !CanEditChanges) return;
            Changes.Remove(row);
            RefreshDiffPreview();
        }

        private void NewExperiment()
        {
            if (_working is { IsUnfinished: true })
            {
                Notify("Finish or roll back the current experiment before starting a new one.");
                return;
            }

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
            Notify("New experiment created. Add a FastFlag to begin.");
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
            : _working.State.ToFriendlyName();

        public string SnapshotStatusText => _working is null
            ? "—"
            : _working.State switch
            {
                SandboxExperimentState.Draft or SandboxExperimentState.Preparing => "Not created",
                SandboxExperimentState.RolledBack => "✓ Backup restored",
                SandboxExperimentState.Cancelled => "—",
                _ when _working.SnapshotId is not null => "✓ Backup created",
                _ => "Not created"
            };

        /// <summary>Honest checklist of what actually happened (driven by snapshot/apply markers on the experiment).</summary>
        public string StatusChecklistText
        {
            get
            {
                if (_working is null) return "";

                var lines = new List<string>();
                if (_working.SnapshotId is not null)
                    lines.Add("✓ Backup created");

                if (_working.AppliedAt is not null)
                    lines.Add("✓ Configuration applied");

                switch (_working.State)
                {
                    case SandboxExperimentState.SnapshotCreated:
                        lines.Add("• Backup verified — apply the changes when ready.");
                        break;
                    case SandboxExperimentState.ReadyForTesting:
                        lines.Add("• Launch Roblox and play — changes take effect on the next Roblox start.");
                        break;
                    case SandboxExperimentState.Testing:
                        lines.Add("• Testing in progress — measure, then record a result (or roll back).");
                        break;
                    case SandboxExperimentState.Completed:
                        lines.Add("✓ Testing finished — keep the changes or restore the previous configuration.");
                        break;
                }

                return string.Join("\n", lines);
            }
        }

        public string RobloxStatusText => _service.IsRobloxRunning
            ? "🟡 Roblox is running"
            : "🟢 Roblox is not running";

        public bool IsRobloxRunning => _service.IsRobloxRunning;

        public bool IsRobloxNotRunning => !IsRobloxRunning;

        public string ResultText => _working is null || _working.Result is null
            ? "No result recorded yet"
            : $"Result: {_working.Result!.Value.ToFriendlyLabel()}";

        public string MeasurementText => _working?.Measurement is null
            ? "No measurements recorded"
            : $"Before: {(FormatFps(_working.Measurement.Before))}   After: {FormatFps(_working.Measurement.After)}";

        public string BeforeFpsText => FormatFpsMetric(_working?.Measurement?.Before);

        public string AfterFpsText => FormatFpsMetric(_working?.Measurement?.After);

        public string BeforeP1LowText => FormatP1Low(_working?.Measurement?.Before);

        public string AfterP1LowText => FormatP1Low(_working?.Measurement?.After);

        public string BeforeRamText => FormatRam(_working?.Measurement?.Before);

        public string AfterRamText => FormatRam(_working?.Measurement?.After);

        public string BeforeCpuText => FormatCpu(_working?.Measurement?.Before);

        public string AfterCpuText => FormatCpu(_working?.Measurement?.After);

        // No GPU telemetry source exists in BoneFish — this stays honest "N/A" instead of
        // inventing a number. Kept as a property so a future source can be plugged in.
        public string BeforeGpuText => "N/A";

        public string AfterGpuText => "N/A";

        private static string FormatFpsMetric(SandboxFpsSample? sample) =>
            sample is { Reliable: true } ? $"{sample.MedianFps:F1} FPS ({sample.SampleCount} samples)" : "N/A";

        private static string FormatP1Low(SandboxFpsSample? sample) =>
            sample is { Reliable: true, P1LowFps: > 0 } ? $"{sample.P1LowFps:F1} FPS" : "N/A";

        private static string FormatRam(SandboxFpsSample? sample) =>
            sample is { ProcessMetricsSampled: true } ? $"{sample.AverageRamMB:F0} MB" : "N/A";

        private static string FormatCpu(SandboxFpsSample? sample) =>
            sample is { ProcessMetricsSampled: true } ? $"{sample.AverageCpuPercent:F0}%" : "N/A";

        private static string FormatFps(SandboxFpsSample? sample) =>
            sample is null ? "not measured" : $"{sample.MedianFps:F1} FPS ({sample.SampleCount} samples)";

        public string ErrorText => _working?.LastError is null ? "" : $"⚠ {_working.LastError}";

        public bool HasError => !string.IsNullOrEmpty(ErrorText);

        /// <summary>Shown in the diff card when inline rows contained the same flag twice (merged, last wins).</summary>
        public string DuplicateWarningText { get; private set; } = "";

        /// <summary>Short contextual hint under the action buttons explaining the current step.</summary>
        public string ActionHintText => _working?.State switch
        {
            SandboxExperimentState.Draft => "Create a backup of the current Roblox configuration before applying these changes.",
            SandboxExperimentState.SnapshotCreated => "Backup verified — apply the changes when you are ready.",
            SandboxExperimentState.ReadyForTesting => "Play Roblox with the changes, then measure and record a result.",
            SandboxExperimentState.Testing => "Play Roblox, then measure and record a result — or roll back.",
            SandboxExperimentState.Completed => "Keep the changes or restore the previous configuration.",
            _ => ""
        };

        // ── Recovery banner ───────────────────────────────────────────────────────────

        // Only states that genuinely need attention show the banner. States the user is
        // intentionally working through (backup created, applied, testing) are communicated
        // by the step indicator and status card instead — the banner must not look like an
        // error during normal use.
        public bool RecoveryBannerVisible => _working?.State is SandboxExperimentState.Failed
            or SandboxExperimentState.RollingBack;

        public string RecoveryBannerText => _working?.IsUnfinished == true && RecoveryBannerVisible
            ? $"⚠ Unfinished experiment {_working.DisplayName} (status: {_working.FriendlyStateName}). Your previous configuration can be restored with Rollback, or continue the experiment."
            : "";

        /// <summary>"Roblox is not running — the experiment can be applied now" is only true once a backup exists.</summary>
        public bool RobloxNotRunningBannerVisible => IsRobloxNotRunning && _working?.State is SandboxExperimentState.SnapshotCreated
            or SandboxExperimentState.ReadyForTesting
            or SandboxExperimentState.Testing
            or SandboxExperimentState.Completed;

        // ── Command visibility ────────────────────────────────────────────────────────

        public bool CanPrepare => HasActualChanges;
        public bool CanApply => _working?.State == SandboxExperimentState.SnapshotCreated;
        public bool CanStartTest => _working?.State == SandboxExperimentState.ReadyForTesting;
        public bool CanMeasure => _working?.State == SandboxExperimentState.Testing;
        public bool CanRecordResult => _working?.State == SandboxExperimentState.Testing;
        public bool CanRollback => _working?.State is SandboxExperimentState.SnapshotCreated
            or SandboxExperimentState.ReadyForTesting
            or SandboxExperimentState.Testing
            or SandboxExperimentState.Completed
            or SandboxExperimentState.Failed;
        public bool CanCancel => _working?.State is SandboxExperimentState.Draft
            or SandboxExperimentState.SnapshotCreated
            or SandboxExperimentState.ReadyForTesting
            or SandboxExperimentState.Testing
            or SandboxExperimentState.Failed;
        public bool CanCommit => _working?.State == SandboxExperimentState.Completed;
        public bool IsExperimentFinished => _working?.State is SandboxExperimentState.Committed
            or SandboxExperimentState.RolledBack
            or SandboxExperimentState.Cancelled;

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

                return $"{e.DisplayName}\n" +
                       $"  Base: {e.BaseProfile}\n" +
                       $"  Changes: {e.Changes.Count}\n" +
                       $"  Status: {e.FriendlyStateName}\n" +
                       (e.Result is null ? "" : $"  Result: {e.ResultLabel}\n") +
                       (e.Measurement is null ? "" : $"  {MeasurementTextFor(e)}\n") +
                       $"  Created: {e.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}\n" +
                       $"  Changes:\n{changes}" +
                       (string.IsNullOrEmpty(e.LastError) ? "" : $"\n  Error: {e.LastError}");
            }
        }

        private static string MeasurementTextFor(SandboxExperiment e) =>
            $"  Before: {FormatFps(e.Measurement!.Before)}   After: {FormatFps(e.Measurement.After)}\n" +
            $"  RAM: {FormatRam(e.Measurement.Before)} → {FormatRam(e.Measurement.After)}   " +
            $"CPU: {FormatCpu(e.Measurement.Before)} → {FormatCpu(e.Measurement.After)}";

        // ── Commands ──────────────────────────────────────────────────────────────────

        public ICommand PrepareCommand => new AsyncRelayCommand(PrepareExperimentAsync);

        public ICommand ApplyCommand => new AsyncRelayCommand(ApplyAsync);

        public ICommand StartTestCommand => new RelayCommand(StartTest);

        public ICommand MeasureBaselineCommand => new AsyncRelayCommand(() => MeasureAsync(baseline: true));

        public ICommand MeasureNowCommand => new AsyncRelayCommand(() => MeasureAsync(baseline: false));

        public ICommand RecordResultCommand => new RelayCommand(RecordResult);

        public ICommand RollbackCommand => new AsyncRelayCommand(RollbackAsync);

        public ICommand CommitCommand => new AsyncRelayCommand(CommitAsync);

        public ICommand CancelCommand => new AsyncRelayCommand(CancelAsync);

        private async Task PrepareExperimentAsync()
        {
            var exp = _working;
            if (exp is null || !CanPrepare) return;

            IsBusy = true;
            BusyText = "Creating a backup of the current Roblox configuration...";
            try
            {
                await _service.PrepareAsync(exp);
                Notify("✓ Backup created and verified. You can now apply the experiment.");
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

        private async Task ApplyAsync()
        {
            var exp = _working;
            if (exp is null) return;

            int changeCount = ConfigurationDiffService.CountActualChanges(DiffPreview);

            var confirm = Frontend.ShowMessageBox(
                $"You are about to apply {changeCount} configuration change(s).\n\n" +
                "Your previous configuration has been backed up and can be restored at any time.\n\n" +
                "Continue?",
                MessageBoxImage.Question, MessageBoxButton.YesNo);

            if (confirm != MessageBoxResult.Yes)
                return;

            if (_service.IsRobloxRunning)
            {
                var result = Frontend.ShowMessageBox(
                    "🟡 Roblox is currently running.\n\n" +
                    "These changes will take effect the next time Roblox starts.\n" +
                    "BoneFish will NOT close or restart Roblox for you.\n\n" +
                    "Continue applying while Roblox is running?",
                    MessageBoxImage.Question, MessageBoxButton.YesNo);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            IsBusy = true;
            BusyText = "Applying the backed-up changes and verifying...";
            try
            {
                bool ok = await _service.ApplyAsync(exp, confirmedRestart: true);
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
            BusyText = "Restoring the previous configuration...";
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
            BusyText = "Keeping the changes and saving them to the active profile...";
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
                    var row = new SandboxChangeRow
                    {
                        FlagName = change.FlagName,
                        NewValue = change.NewValue,
                        CurrentValue = _service.FastFlags.GetValue(change.FlagName)
                    };
                    row.RowChanged += RefreshDiffPreview;
                    Changes.Add(row);
                }
            }

            RefreshWorkflowSteps();
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
            OnPropertyChanged(nameof(StatusChecklistText));
            OnPropertyChanged(nameof(RobloxStatusText));
            OnPropertyChanged(nameof(IsRobloxRunning));
            OnPropertyChanged(nameof(IsRobloxNotRunning));
            OnPropertyChanged(nameof(ResultText));
            OnPropertyChanged(nameof(MeasurementText));
            OnPropertyChanged(nameof(BeforeFpsText));
            OnPropertyChanged(nameof(AfterFpsText));
            OnPropertyChanged(nameof(BeforeP1LowText));
            OnPropertyChanged(nameof(AfterP1LowText));
            OnPropertyChanged(nameof(BeforeRamText));
            OnPropertyChanged(nameof(AfterRamText));
            OnPropertyChanged(nameof(BeforeCpuText));
            OnPropertyChanged(nameof(AfterCpuText));
            OnPropertyChanged(nameof(BeforeGpuText));
            OnPropertyChanged(nameof(AfterGpuText));
            OnPropertyChanged(nameof(ErrorText));
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(RecoveryBannerVisible));
            OnPropertyChanged(nameof(RecoveryBannerText));
            OnPropertyChanged(nameof(RobloxNotRunningBannerVisible));
            OnPropertyChanged(nameof(CanEditChanges));
            OnPropertyChanged(nameof(HasActualChanges));
            OnPropertyChanged(nameof(CanPrepare));
            OnPropertyChanged(nameof(CanApply));
            OnPropertyChanged(nameof(CanStartTest));
            OnPropertyChanged(nameof(CanMeasure));
            OnPropertyChanged(nameof(CanRecordResult));
            OnPropertyChanged(nameof(CanRollback));
            OnPropertyChanged(nameof(CanCancel));
            OnPropertyChanged(nameof(CanCommit));
            OnPropertyChanged(nameof(IsExperimentFinished));
            OnPropertyChanged(nameof(ActionHintText));
            OnPropertyChanged(nameof(SelectedBaseProfile));
            OnPropertyChanged(nameof(HistoryDetailText));
            OnPropertyChanged(nameof(StepsVisible));
            OnPropertyChanged(nameof(WorkflowSteps));
            OnPropertyChanged(nameof(NoChangesPlaceholderVisible));
        }

        private void RefreshWorkflowSteps()
        {
            WorkflowSteps.Clear();

            if (_working is null)
                return;

            int active = _working.GetWorkflowStepIndex();
            bool allDone = _working.State.IsTerminal();
            string[] labels = { "Configure", "Snapshot", "Apply", "Test", "Result" };

            for (int i = 0; i < labels.Length; i++)
            {
                WorkflowSteps.Add(new SandboxWorkflowStep
                {
                    Label = labels[i],
                    IsLast = i == labels.Length - 1,
                    StepState = allDone || i < active ? SandboxStepState.Done : i == active ? SandboxStepState.Active : SandboxStepState.Pending
                });
            }

            OnPropertyChanged(nameof(WorkflowSteps));
        }

        private void RefreshDiffPreview()
        {
            DiffPreview.Clear();

            var baseFlags = new Dictionary<string, string>();

            // Merge duplicate rows for the same flag (last value wins) so the diff never shows
            // the same flag twice — consistent with the upsert semantics used by the dialog.
            var deduped = new Dictionary<string, SandboxChange>(StringComparer.Ordinal);
            bool hadDuplicates = false;

            foreach (var row in Changes)
            {
                var change = row.ToChange();
                if (string.IsNullOrWhiteSpace(change.FlagName))
                    continue;

                if (deduped.ContainsKey(change.FlagName))
                    hadDuplicates = true;
                deduped[change.FlagName] = change;

                if (!baseFlags.ContainsKey(change.FlagName))
                {
                    string? current = _service.FastFlags.GetValue(change.FlagName);
                    baseFlags[change.FlagName] = current ?? "";
                }
            }

            // Flags currently on disk that the user has not touched stay out of the diff.
            foreach (var entry in ConfigurationDiffService.ComputeDiff(baseFlags, deduped.Values))
                DiffPreview.Add(entry);

            DuplicateWarningText = hadDuplicates
                ? "Duplicate entries were merged (last value wins). Review the diff before preparing."
                : "";

            OnPropertyChanged(nameof(DiffPreview));
            OnPropertyChanged(nameof(NoChangesPlaceholderVisible));
            OnPropertyChanged(nameof(HasActualChanges));
            OnPropertyChanged(nameof(CanPrepare));
            OnPropertyChanged(nameof(DuplicateWarningText));
        }
    }
}
