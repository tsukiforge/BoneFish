using Bloxstrap.Sandbox.Interfaces;
using Bloxstrap.Sandbox.Models;

namespace Bloxstrap.Sandbox
{
    /// <summary>
    /// Owns experiment lifecycle and persistence. Enforces the experiment state machine —
    /// every transition is validated against an explicit transition table — and keeps the
    /// journal on disk (written atomically) so interrupted experiments survive restarts.
    /// </summary>
    public class ExperimentManager
    {
        private const string LOG_IDENT = "OptimizationSandbox::ExperimentManager";

        private readonly string? _storageRoot;
        private readonly object _lock = new();
        private SandboxJournal _journal = new();

        public ExperimentManager(string? storageRoot = null)
        {
            _storageRoot = storageRoot;
            LoadJournal();
        }

        // ── State machine ──────────────────────────────────────────────────────────────

        private static readonly Dictionary<SandboxExperimentState, HashSet<SandboxExperimentState>> AllowedTransitions =
            new()
            {
                [SandboxExperimentState.Draft] = new HashSet<SandboxExperimentState>
                {
                    SandboxExperimentState.Preparing,
                    SandboxExperimentState.Cancelled
                },
                [SandboxExperimentState.Preparing] = new HashSet<SandboxExperimentState>
                {
                    SandboxExperimentState.SnapshotCreated,
                    SandboxExperimentState.Failed
                },
                [SandboxExperimentState.SnapshotCreated] = new HashSet<SandboxExperimentState>
                {
                    SandboxExperimentState.Applying,
                    SandboxExperimentState.Cancelled, // nothing written yet — safe to cancel
                    SandboxExperimentState.Failed
                },
                [SandboxExperimentState.Applying] = new HashSet<SandboxExperimentState>
                {
                    SandboxExperimentState.ReadyForTesting,
                    SandboxExperimentState.RollingBack, // partial write → rollback immediately
                    SandboxExperimentState.Failed
                },
                [SandboxExperimentState.ReadyForTesting] = new HashSet<SandboxExperimentState>
                {
                    SandboxExperimentState.Testing,
                    SandboxExperimentState.RollingBack
                },
                [SandboxExperimentState.Testing] = new HashSet<SandboxExperimentState>
                {
                    SandboxExperimentState.Completed,
                    SandboxExperimentState.RollingBack
                },
                [SandboxExperimentState.Completed] = new HashSet<SandboxExperimentState>
                {
                    SandboxExperimentState.Committed,
                    SandboxExperimentState.RollingBack
                },
                [SandboxExperimentState.RollingBack] = new HashSet<SandboxExperimentState>
                {
                    SandboxExperimentState.RolledBack,
                    SandboxExperimentState.Cancelled,
                    SandboxExperimentState.Failed
                },
                [SandboxExperimentState.Failed] = new HashSet<SandboxExperimentState>
                {
                    SandboxExperimentState.Preparing, // retry apply
                    SandboxExperimentState.RollingBack,
                    SandboxExperimentState.Cancelled
                }
            };

        public static bool IsTransitionAllowed(SandboxExperimentState from, SandboxExperimentState to) =>
            AllowedTransitions.TryGetValue(from, out var targets) && targets.Contains(to);

        /// <summary>Move an experiment to <paramref name="target"/>; throws on invalid transitions.</summary>
        public void TransitionTo(SandboxExperiment experiment, SandboxExperimentState target)
        {
            if (!IsTransitionAllowed(experiment.State, target))
                throw new SandboxException(
                    $"Invalid experiment transition: {experiment.State} → {target} (experiment {experiment.DisplayName})");

            App.Logger.WriteLine(LOG_IDENT, $"{experiment.DisplayName}: {experiment.State} → {target}");

            bool becomesActive = target.IsUnfinished();
            experiment.State = target;

            switch (target)
            {
                case SandboxExperimentState.Applying:
                    experiment.AppliedAt = DateTime.UtcNow;
                    break;
                case SandboxExperimentState.Completed:
                    experiment.CompletedAt = DateTime.UtcNow;
                    break;
                case SandboxExperimentState.RolledBack:
                case SandboxExperimentState.Committed:
                case SandboxExperimentState.Cancelled:
                    _journal.ActiveExperimentId = null;
                    experiment.LastError = null;
                    break;
            }

            if (becomesActive)
                _journal.ActiveExperimentId = experiment.Id;

            SaveJournal();
        }

        /// <summary>True when the experiment's changes are currently applied to the live configuration.</summary>
        public static bool IsAppliedState(SandboxExperimentState state) =>
            state is SandboxExperimentState.ReadyForTesting
                or SandboxExperimentState.Testing
                or SandboxExperimentState.Completed;

        // ── Journal persistence ────────────────────────────────────────────────────────

        private void LoadJournal()
        {
            string path = SandboxStorage.GetJournalPath(_storageRoot);

            try
            {
                if (!File.Exists(path))
                    return;

                string json = SandboxStorage.ReadAllText(path);
                var journal = JsonSerializer.Deserialize<SandboxJournal>(json);

                if (journal is not null)
                    _journal = journal;
            }
            catch (Exception ex)
            {
                // Never crash startup over a damaged journal; keep defaults and log.
                App.Logger.WriteLine(LOG_IDENT, "Failed to load sandbox journal; starting fresh");
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        private void SaveJournal()
        {
            string path = SandboxStorage.GetJournalPath(_storageRoot);

            try
            {
                string json = JsonSerializer.Serialize(_journal, new JsonSerializerOptions { WriteIndented = true });
                SandboxStorage.WriteAllTextAtomic(path, json);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Failed to save sandbox journal");
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        // ── Experiment CRUD ────────────────────────────────────────────────────────────

        public SandboxExperiment CreateExperiment(string baseProfile, IEnumerable<SandboxChange> changes)
        {
            lock (_lock)
            {
                var experiment = new SandboxExperiment
                {
                    Id = _journal.NextExperimentNumber.ToString("D3"),
                    CreatedAt = DateTime.UtcNow,
                    BaseProfile = baseProfile,
                    Changes = changes.Select(c => c.Clone()).ToList(),
                    State = SandboxExperimentState.Draft
                };

                _journal.NextExperimentNumber++;
                _journal.Experiments.Add(experiment);
                SaveJournal();

                App.Logger.WriteLine(LOG_IDENT, $"Experiment #{experiment.Id} created (base: {baseProfile}, {experiment.Changes.Count} change(s))");
                return experiment;
            }
        }

        public SandboxExperiment? Find(string? experimentId) =>
            _journal.Experiments.FirstOrDefault(e => e.Id == experimentId);

        public SandboxExperiment? ActiveExperiment =>
            _journal.ActiveExperimentId is null ? null : Find(_journal.ActiveExperimentId);

        public IReadOnlyList<SandboxExperiment> History =>
            _journal.Experiments.OrderByDescending(e => e.CreatedAt).ToList();

        /// <summary>Update an experiment's changes while it is still a draft.</summary>
        public bool TryUpdateChanges(SandboxExperiment experiment, IEnumerable<SandboxChange> changes)
        {
            if (experiment.State != SandboxExperimentState.Draft)
                return false;

            experiment.Changes = changes.Select(c => c.Clone()).ToList();
            SaveJournal();
            return true;
        }

        /// <summary>Set a recovery acknowledgment so the startup prompt does not nag repeatedly.</summary>
        public void AcknowledgeRecovery(SandboxExperiment experiment)
        {
            experiment.RecoveryAcknowledged = true;
            SaveJournal();
        }

        public void MarkFailed(SandboxExperiment experiment, string error)
        {
            experiment.LastError = error;

            if (IsTransitionAllowed(experiment.State, SandboxExperimentState.Failed))
            {
                TransitionTo(experiment, SandboxExperimentState.Failed);
            }
            else
            {
                App.Logger.WriteLine(LOG_IDENT, $"{experiment.DisplayName}: marking failed without state transition ({experiment.State}) — error: {error}");
                SaveJournal();
            }
        }

        /// <summary>Persist non-state changes (measurements, notes) to the journal.</summary>
        public void Persist() => SaveJournal();
    }
}
