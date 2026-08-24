using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Common.Interfaces;
using Bloxstrap.GameSession;

namespace Bloxstrap.UI.ViewModels.Settings
{
    public sealed class GameSessionApplication : INotifyPropertyChanged
    {
        private readonly Action<GameSessionApplication, bool> _approvalChanged;
        private bool _suspendDuringGame;

        public event PropertyChangedEventHandler? PropertyChanged;

        public GameSessionRule Rule { get; }
        public string Name { get; }
        public string Path { get; }
        public string Status { get; set; } = "";

        public bool SuspendDuringGame
        {
            get => _suspendDuringGame;
            set
            {
                if (_suspendDuringGame == value)
                    return;

                _suspendDuringGame = value;
                Rule.SuspendDuringGame = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SuspendDuringGame)));
                _approvalChanged(this, value);
            }
        }

        public GameSessionApplication(GameSessionRule rule, string name, string path, Action<GameSessionApplication, bool> approvalChanged)
        {
            Rule = rule;
            Name = name;
            Path = path;
            _suspendDuringGame = rule.SuspendDuringGame;
            _approvalChanged = approvalChanged;
        }
    }

    public sealed class SuspendedApplicationViewModel
    {
        public string Name { get; init; } = "";
        public string Status { get; init; } = "";
    }

    public class GameSessionViewModel : NotifyPropertyChangedViewModel, INavigationAware
    {
        private readonly DispatcherTimer _refreshTimer;
        private bool _hasDetectorNotice;
        private string _detectorNotice = "";
        private string _suspensionStatus = "";
        private bool _hasActiveSession;
        private string _activeSessionText = "";
        private string _lastSessionText = "";
        private int _automaticCandidateCount;
        private bool _isBusy;

        public ObservableCollection<GameSessionApplication> Applications { get; } = new();
        public ObservableCollection<SuspendedApplicationViewModel> ActiveSuspendedApplications { get; } = new();

        public ICommand ScanCommand { get; }
        public ICommand RestoreCommand { get; }

        // Master toggle — default OFF (opt-in). Saat false, seluruh fitur di-skip
        // (tidak ada BeginSessionAsync di bootstrapper) dan halaman ini digreyed-out.
        public bool GameSessionEnabled
        {
            get => App.Settings.Prop.GameSessionEnabled;
            set
            {
                if (App.Settings.Prop.GameSessionEnabled == value)
                    return;

                App.Settings.Prop.GameSessionEnabled = value;
                OnPropertyChanged(nameof(GameSessionEnabled));
                try { App.Settings.Save(); } catch { }
            }
        }

        public bool AutoSelectSafeApps
        {
            get => App.Settings.Prop.GameSessionAutoSelectSafeApps;
            set
            {
                if (App.Settings.Prop.GameSessionAutoSelectSafeApps == value)
                    return;

                App.Settings.Prop.GameSessionAutoSelectSafeApps = value;
                OnPropertyChanged(nameof(AutoSelectSafeApps));
                try { App.Settings.Save(); } catch { }
                _ = ScanAsync();
            }
        }

        public bool HasDetectorNotice
        {
            get => _hasDetectorNotice;
            private set { _hasDetectorNotice = value; OnPropertyChanged(nameof(HasDetectorNotice)); }
        }

        public string DetectorNotice
        {
            get => _detectorNotice;
            private set { _detectorNotice = value; OnPropertyChanged(nameof(DetectorNotice)); }
        }

        public string SuspensionStatus
        {
            get => _suspensionStatus;
            private set { _suspensionStatus = value; OnPropertyChanged(nameof(SuspensionStatus)); }
        }

        public bool HasActiveSession
        {
            get => _hasActiveSession;
            private set { _hasActiveSession = value; OnPropertyChanged(nameof(HasActiveSession)); }
        }

        public string ActiveSessionText
        {
            get => _activeSessionText;
            private set { _activeSessionText = value; OnPropertyChanged(nameof(ActiveSessionText)); }
        }

        public string LastSessionText
        {
            get => _lastSessionText;
            private set { _lastSessionText = value; OnPropertyChanged(nameof(LastSessionText)); }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set { _isBusy = value; OnPropertyChanged(nameof(IsBusy)); }
        }

        public bool HasApplications => Applications.Count > 0;
        public bool HasNoApplications => Applications.Count == 0;

        public int AutomaticCandidateCount
        {
            get => _automaticCandidateCount;
            private set { _automaticCandidateCount = value; OnPropertyChanged(nameof(AutomaticCandidateCount)); }
        }

        public GameSessionViewModel()
        {
            ScanCommand = new AsyncRelayCommand(ScanAsync);
            RestoreCommand = new AsyncRelayCommand(RestoreNowAsync);
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _refreshTimer.Tick += (_, _) => RefreshSessionState();
        }

        public void OnNavigatedTo()
        {
            _refreshTimer.Start();
            _ = ScanAsync();
        }

        public void OnNavigatedFrom()
        {
            _refreshTimer.Stop();
        }

        private async Task ScanAsync()
        {
            if (IsBusy)
                return;

            IsBusy = true;
            try
            {
                await App.GameSession.Detector.RefreshAsync();
                IReadOnlyList<ProcessSnapshot> liveProcesses = await Task.Run(App.GameSession.ScanForUi);
                MergeRules(liveProcesses);
                UpdateDetectorNotice();
                RefreshSessionState();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("GameSessionViewModel::Scan", ex.Message);
                DetectorNotice = ex.Message;
                HasDetectorNotice = true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Tombol "Pulihkan Sekarang": restore record sesi aktif, lalu rescue scan
        /// seluruh sistem untuk proses beku yang TIDAK tercatat (kasus record hilang).
        /// </summary>
        private async Task RestoreNowAsync()
        {
            if (IsBusy)
                return;

            IsBusy = true;
            try
            {
                int restoredFromRecord = 0;
                if (App.GameSession.Store.ReadActive() is { } session)
                    restoredFromRecord = App.GameSession.EndSession().TotalSuspended;

                IReadOnlyList<RescuedProcess> rescued = await Task.Run(() => App.GameSession.RescueSuspendedProcesses());

                RefreshSessionState();

                string message;
                if (restoredFromRecord > 0 && rescued.Count > 0)
                {
                    message = App.GameSession.Store.ReadHistory().LastOrDefault() is { } last
                        ? $"{App.GameSession.FormatSummary(last)}\n{String.Format(Strings.GameSession_RestoreNow_Rescued, rescued.Count)}"
                        : String.Format(Strings.GameSession_RestoreNow_Rescued, rescued.Count);
                }
                else if (restoredFromRecord > 0)
                {
                    message = App.GameSession.Store.ReadHistory().LastOrDefault() is { } last
                        ? App.GameSession.FormatSummary(last)
                        : Strings.GameSession_RestoreNow_Done;
                }
                else if (rescued.Count > 0)
                {
                    message = String.Format(Strings.GameSession_RestoreNow_Rescued, rescued.Count);
                }
                else
                {
                    message = Strings.GameSession_RestoreNow_None;
                }

                Frontend.ShowMessageBox(message, MessageBoxImage.Information, MessageBoxButton.OK);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("GameSessionViewModel::RestoreNow", ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void MergeRules(IReadOnlyList<ProcessSnapshot> liveProcesses)
        {
            var rules = App.Settings.Prop.GameSessionRules;
            bool changed = false;

            foreach (ProcessSnapshot process in liveProcesses)
            {
                if (ProcessClassifier.IsCritical(process, App.GameSession.Detector, Environment.ProcessId, 0))
                    continue;

                if (FindRule(rules, process) is null)
                {
                    rules.Add(new GameSessionRule
                    {
                        ProcessName = process.ProcessName,
                        ExecutablePath = process.ExecutablePath,
                        SuspendDuringGame = ShouldAutoSelect(process),
                        AutoSelectionDisabled = false
                    });
                    changed = true;
                }
                else if (App.Settings.Prop.GameSessionAutoSelectSafeApps
                    && App.GameSession.Detector.State == SecurityDetectionState.Ok)
                {
                    GameSessionRule rule = FindRule(rules, process)!;
                    var autoCandidate = new GameSessionRule
                    {
                        ProcessName = process.ProcessName,
                        ExecutablePath = process.ExecutablePath,
                        SuspendDuringGame = true
                    };

                    if (!rule.AutoSelectionDisabled
                        && ProcessClassifier.Classify(process, App.GameSession.Detector, Environment.ProcessId, 0, autoCandidate) == ProcessClassification.Safe
                        && !rule.SuspendDuringGame)
                    {
                        rule.SuspendDuringGame = true;
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                try { App.Settings.Save(); } catch { }
            }

            Applications.Clear();
            foreach (GameSessionRule rule in rules
                .Where(rule => !ProcessClassifier.IsAlwaysProtected(
                    new ProcessSnapshot
                    {
                        ProcessId = -1,
                        SessionId = Environment.ProcessId == 0 ? -1 : Process.GetCurrentProcess().SessionId,
                        ProcessName = rule.ProcessName,
                        ExecutablePath = rule.ExecutablePath
                    },
                    App.GameSession.Detector,
                    Environment.ProcessId,
                    0))
                .OrderByDescending(rule => rule.SuspendDuringGame)
                .ThenBy(rule => rule.ProcessName, StringComparer.OrdinalIgnoreCase))
            {
                Applications.Add(new GameSessionApplication(
                    rule,
                    rule.ProcessName,
                    rule.ExecutablePath ?? "Path unavailable",
                    ApprovalChanged));
            }

            OnPropertyChanged(nameof(HasApplications));
            OnPropertyChanged(nameof(HasNoApplications));
            AutomaticCandidateCount = liveProcesses.Count(process =>
                ProcessClassifier.IsAutomaticCandidate(process, App.GameSession.Detector, Environment.ProcessId, 0));
        }

        private void ApprovalChanged(GameSessionApplication application, bool approved)
        {
            if (!approved)
                application.Rule.AutoSelectionDisabled = true;
            else if (approved)
                application.Rule.AutoSelectionDisabled = false;

            try { App.Settings.Save(); } catch { }
            UpdateDetectorNotice();
        }

        private bool ShouldAutoSelect(ProcessSnapshot process)
        {
            if (!App.Settings.Prop.GameSessionAutoSelectSafeApps
                || App.GameSession.Detector.State != SecurityDetectionState.Ok)
            {
                return false;
            }

            var candidate = new GameSessionRule
            {
                ProcessName = process.ProcessName,
                ExecutablePath = process.ExecutablePath,
                SuspendDuringGame = true
            };

            return ProcessClassifier.Classify(process, App.GameSession.Detector, Environment.ProcessId, 0, candidate)
                == ProcessClassification.Safe;
        }

        private void UpdateDetectorNotice()
        {
            SecurityDetectionState state = App.GameSession.Detector.State;
            HasDetectorNotice = state != SecurityDetectionState.Ok;
            DetectorNotice = state switch
            {
                SecurityDetectionState.Unavailable => Strings.GameSession_DetectorUnavailable,
                SecurityDetectionState.Degraded => Strings.GameSession_DetectorDegraded,
                _ => ""
            };
            SuspensionStatus = state == SecurityDetectionState.Ok
                ? ""
                : Strings.GameSession_ZeroSuspended;
        }

        private void RefreshSessionState()
        {
            try
            {
                GameSessionRecord? session = App.GameSession.Store.ReadActive();
                HasActiveSession = session is not null && session.SuspendedProcesses.Count > 0;
                ActiveSuspendedApplications.Clear();

                if (session is not null)
                {
                    ActiveSessionText = String.Format(Strings.GameSession_SessionActive, session.SuspendedProcesses.Count);
                    foreach (SuspendedProcessRecord process in session.SuspendedProcesses)
                    {
                        string status = process.PartiallySuspended
                            ? String.Format(Strings.GameSession_PartialSuspended, process.SuspendedThreadCount, process.TotalThreadCount)
                            : Strings.GameSession_SuspendedLabel;

                        ActiveSuspendedApplications.Add(new SuspendedApplicationViewModel
                        {
                            Name = process.ProcessName,
                            Status = status
                        });
                    }
                }
                else
                {
                    ActiveSessionText = "";
                }

                SessionSummary? last = App.GameSession.Store.ReadHistory().LastOrDefault();
                LastSessionText = last is null
                    ? Strings.GameSession_NoHistory
                    : FormatSummary(last);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("GameSessionViewModel::Refresh", ex.Message);
            }
        }

        private static GameSessionRule? FindRule(IEnumerable<GameSessionRule> rules, ProcessSnapshot process)
        {
            return rules.FirstOrDefault(rule =>
                (!String.IsNullOrWhiteSpace(process.ExecutablePath)
                    && String.Equals(rule.ExecutablePath, process.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                || (String.IsNullOrWhiteSpace(rule.ExecutablePath)
                    && String.Equals(rule.ProcessName, process.ProcessName, StringComparison.OrdinalIgnoreCase)));
        }

        private static string FormatSummary(SessionSummary summary)
        {
            if (summary.TotalSuspended == 0)
                return Strings.GameSession_NoHistory;

            string restoredNames = String.Join(", ", summary.Results.Where(result => result.Succeeded).Select(result => result.ProcessName));
            string failures = String.Join(" ", summary.Results
                .Where(result => !result.Succeeded)
                .Select(result => result.Status == RestoreStatus.NotFound
                    ? String.Format(Strings.GameSession_RestoredFailedEntry, result.ProcessName)
                    : String.Format(Strings.GameSession_RestoredFailedGeneric, result.ProcessName, result.Message)));

            if (summary.RestoredCount == summary.TotalSuspended)
                return String.Format(Strings.GameSession_RestoredAll, summary.RestoredCount, restoredNames);

            return String.Format(Strings.GameSession_RestoredPartial, summary.RestoredCount, summary.TotalSuspended, failures);
        }
    }
}
