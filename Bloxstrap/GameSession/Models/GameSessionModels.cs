using System.Text.Json.Serialization;

namespace Bloxstrap.GameSession.Models
{
    public enum ProcessClassification
    {
        Critical,
        Safe,
        Keep
    }

    public enum SecurityDetectionState
    {
        Ok,
        Degraded,
        Unavailable
    }

    public enum RestoreStatus
    {
        Restored,
        NotFound,
        IdentityMismatch,
        ResumeFailed,
        VerificationFailed
    }

    public enum SessionRestoreState
    {
        Active,
        Restoring,
        Restored,
        Pending
    }

    /// <summary>
    /// Persistent per-application approval. New rules are always disabled.
    /// </summary>
    public class GameSessionRule
    {
        public string ProcessName { get; set; } = "";
        public string? ExecutablePath { get; set; }
        public bool SuspendDuringGame { get; set; }
        public bool AutoSelectionDisabled { get; set; }

    }

    public class ProcessSnapshot
    {
        public int ProcessId { get; set; }
        public int SessionId { get; set; } = -1;
        public string ProcessName { get; set; } = "";
        public string? ExecutablePath { get; set; }
        public DateTime? StartTimeUtc { get; set; }
    }

    public class SuspendedProcessRecord
    {
        public int ProcessId { get; set; }
        public int SessionId { get; set; } = -1;
        public string ProcessName { get; set; } = "";
        public string? ExecutablePath { get; set; }
        public DateTime? StartTimeUtc { get; set; }
        public string? AppliedRule { get; set; }
        public List<int> ThreadIds { get; set; } = new();
        public int TotalThreadCount { get; set; }
        public int SuspendedThreadCount { get; set; }
        public int FailedThreadCount { get; set; }
        public bool PartiallySuspended { get; set; }
    }

    public class GameSessionRecord
    {
        public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
        public int CoordinatorProcessId { get; set; }
        public int GameProcessId { get; set; }
        public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
        public SecurityDetectionState DetectorState { get; set; } = SecurityDetectionState.Unavailable;
        public string? DetectorMessage { get; set; }
        public SessionRestoreState RestoreState { get; set; } = SessionRestoreState.Active;
        public List<string> AppliedRules { get; set; } = new();
        public List<SuspendedProcessRecord> SuspendedProcesses { get; set; } = new();
        public bool HandedOffToWatcher { get; set; }
    }

    public class RestoreResult
    {
        public string ProcessName { get; set; } = "";
        public RestoreStatus Status { get; set; }
        public string Message { get; set; } = "";

        [JsonIgnore]
        public bool Succeeded => Status == RestoreStatus.Restored;
    }

    public class SessionSummary
    {
        public string SessionId { get; set; } = "";
        public int GameProcessId { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime EndedAtUtc { get; set; } = DateTime.UtcNow;
        public int TotalSuspended { get; set; }
        public int RestoredCount { get; set; }
        public List<RestoreResult> Results { get; set; } = new();
    }
}
