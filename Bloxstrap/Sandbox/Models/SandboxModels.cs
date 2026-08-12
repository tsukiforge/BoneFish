using System.Text.Json.Serialization;

namespace Bloxstrap.Sandbox.Models
{
    /// <summary>
    /// Lifecycle states of an optimization experiment.
    ///
    /// Terminal states: <see cref="Committed"/>, <see cref="RolledBack"/>, <see cref="Cancelled"/>.
    /// Recovery-relevant states (config may differ from snapshot): SnapshotCreated, Applying,
    /// ReadyForTesting, Testing, Completed, Failed, RollingBack.
    /// </summary>
    public enum SandboxExperimentState
    {
        /// <summary>Being edited, nothing applied yet.</summary>
        Draft,

        /// <summary>Apply in progress (before the snapshot is persisted).</summary>
        Preparing,

        /// <summary>Snapshot created, configuration not modified yet.</summary>
        SnapshotCreated,

        /// <summary>Configuration is being written.</summary>
        Applying,

        /// <summary>Changes applied and verified; waiting for the user to test.</summary>
        ReadyForTesting,

        /// <summary>User is testing the experiment in Roblox.</summary>
        Testing,

        /// <summary>Testing finished, result recorded; awaiting commit or rollback.</summary>
        Completed,

        /// <summary>Rollback is in progress.</summary>
        RollingBack,

        /// <summary>Changes were integrated into the active profile.</summary>
        Committed,

        /// <summary>Original configuration restored and verified.</summary>
        RolledBack,

        /// <summary>User cancelled; configuration restored if it had been modified.</summary>
        Cancelled,

        /// <summary>An operation failed. Recovery is still possible via the snapshot.</summary>
        Failed
    }

    public static class SandboxExperimentStateExtensions
    {
        /// <summary>
        /// True when the experiment's changes may still be applied to the live configuration,
        /// i.e. the process was interrupted before reaching a terminal state.
        /// </summary>
        public static bool IsUnfinished(this SandboxExperimentState state) =>
            state is SandboxExperimentState.SnapshotCreated
                or SandboxExperimentState.Applying
                or SandboxExperimentState.ReadyForTesting
                or SandboxExperimentState.Testing
                or SandboxExperimentState.Completed
                or SandboxExperimentState.Failed
                or SandboxExperimentState.RollingBack;

        /// <summary>True when the experiment reached a final, user-confirmed state.</summary>
        public static bool IsTerminal(this SandboxExperimentState state) =>
            state is SandboxExperimentState.Committed
                or SandboxExperimentState.RolledBack
                or SandboxExperimentState.Cancelled;

        /// <summary>
        /// Maps the experiment lifecycle onto the 5-step user workflow shown on the Sandbox page:
        /// 0 Configure → 1 Snapshot → 2 Apply → 3 Test → 4 Result. Purely derived from real
        /// backend state so the UI can never claim progress the state machine did not make.
        /// </summary>
        public static int GetWorkflowStepIndex(this SandboxExperiment experiment)
        {
            return experiment.State switch
            {
                SandboxExperimentState.Draft => 0,
                SandboxExperimentState.Preparing => 1,
                SandboxExperimentState.SnapshotCreated => 1,
                SandboxExperimentState.Applying => 2,
                SandboxExperimentState.ReadyForTesting => 3,
                SandboxExperimentState.Testing => 3,
                SandboxExperimentState.Completed => 4,
                SandboxExperimentState.Committed or SandboxExperimentState.RolledBack or SandboxExperimentState.Cancelled => 4,
                // Interrupted/error states sit on the milestone that was actually reached:
                // the backup exists → Apply step; otherwise the Snapshot step.
                SandboxExperimentState.RollingBack or SandboxExperimentState.Failed =>
                    experiment.CompletedAt is not null ? 4
                    : experiment.SnapshotId is not null ? 2
                    : 1,
                _ => 0
            };
        }

        /// <summary>User-facing status names used by history and status chips.</summary>
        public static string ToFriendlyName(this SandboxExperimentState state) => state switch
        {
            SandboxExperimentState.Draft => "Draft",
            SandboxExperimentState.Preparing => "Preparing",
            SandboxExperimentState.SnapshotCreated => "Ready",
            SandboxExperimentState.Applying => "Applying",
            SandboxExperimentState.ReadyForTesting => "Ready for Testing",
            SandboxExperimentState.Testing => "Testing",
            SandboxExperimentState.Completed => "Completed",
            SandboxExperimentState.RollingBack => "Rolling Back",
            SandboxExperimentState.Committed => "Committed",
            SandboxExperimentState.RolledBack => "Rolled Back",
            SandboxExperimentState.Cancelled => "Cancelled",
            SandboxExperimentState.Failed => "Failed",
            _ => state.ToString()
        };
    }

    /// <summary>
    /// Friendly, user-facing result labels. The sandbox deliberately avoids claiming success:
    /// small FPS deltas classify as Similar/Inconclusive and missing telemetry as "Not Enough Data".
    /// </summary>
    public static class SandboxTestResultExtensions
    {
        public static string ToFriendlyLabel(this SandboxTestResult result) => result switch
        {
            SandboxTestResult.Improved => "🟢 Potential Improvement",
            SandboxTestResult.Similar => "🟡 Similar",
            SandboxTestResult.Degraded => "🔴 Degraded",
            SandboxTestResult.Inconclusive => "🟡 Inconclusive",
            _ => "⚪ Not Enough Data"
        };
    }

    /// <summary>
    /// One temporary configuration change. A null <see cref="NewValue"/> means "remove this flag".
    /// </summary>
    public class SandboxChange
    {
        public string FlagName { get; set; } = "";
        public string? NewValue { get; set; }

        public SandboxChange Clone() => new() { FlagName = FlagName, NewValue = NewValue };

        public override string ToString() => NewValue is null ? $"− {FlagName}" : $"{FlagName} = {NewValue}";
    }

    /// <summary>
    /// A versioned, persisted snapshot of the configuration state before an experiment was applied.
    /// Only the flags actually touched by the experiment (plus the raw file content) are recorded.
    /// </summary>
    public class SandboxSnapshot
    {
        public const int CurrentFormatVersion = 1;

        public string Id { get; set; } = "";

        /// <summary>Snapshot format version. Future BoneFish versions can migrate or reject incompatible snapshots.</summary>
        public int Version { get; set; } = CurrentFormatVersion;

        public DateTime CreatedAt { get; set; }

        public string BaseProfile { get; set; } = "None";

        public string TargetApplication { get; set; } = "Roblox";

        /// <summary>Relative path (under the BoneFish modifications folder) of the file being snapshotted.</summary>
        public string RelativeFilePath { get; set; } = "ClientSettings\\ClientAppSettings.json";

        /// <summary>Full original content of the touched file.</summary>
        public string OriginalFileContent { get; set; } = "{}";

        /// <summary>MD5 of <see cref="OriginalFileContent"/> at snapshot time.</summary>
        public string OriginalFileHash { get; set; } = "";

        /// <summary>
        /// Values of every flag the experiment touches, as they were before the experiment.
        /// A flag absent from this dictionary did not exist before the experiment.
        /// </summary>
        public Dictionary<string, string> OriginalValues { get; set; } = new();

        /// <summary>MD5 of the canonical serialization of <see cref="OriginalValues"/>.</summary>
        public string ConfigurationHash { get; set; } = "";

        public bool IsSupportedVersion => Version <= CurrentFormatVersion && Version > 0;
    }

    public class SandboxFpsSample
    {
        public DateTime SampledAt { get; set; }

        public double MedianFps { get; set; }

        /// <summary>1% low FPS estimate derived from the same per-second samples (PresentMon-style frame-time percentile).</summary>
        public double P1LowFps { get; set; }

        public int SampleCount { get; set; }

        /// <summary>Average Roblox process working set (MB) over the sampling window. 0 = not sampled.</summary>
        public double AverageRamMB { get; set; }

        /// <summary>Average Roblox process CPU usage (% of all logical processors). 0 = not sampled.</summary>
        public double AverageCpuPercent { get; set; }

        /// <summary>
        /// True when at least one RAM/CPU sample was recorded. Used to distinguish a genuinely
        /// sampled ~0% CPU reading from a metric that was never measured — display is gated on
        /// this flag, not on the value being non-zero.
        /// </summary>
        public bool ProcessMetricsSampled { get; set; }

        /// <summary>False when telemetry was unavailable (e.g. ETW needs admin) or samples were too few.</summary>
        public bool Reliable { get; set; }
    }

    public class SandboxMeasurement
    {
        public SandboxFpsSample? Before { get; set; }

        public SandboxFpsSample? After { get; set; }
    }

    public enum SandboxTestResult
    {
        Improved,
        Similar,
        Degraded,
        Inconclusive,
        InsufficientData
    }

    /// <summary>
    /// An optimization experiment: a temporary set of FastFlag changes layered on top of a base profile.
    /// </summary>
    public class SandboxExperiment
    {
        public string Id { get; set; } = "";

        public DateTime CreatedAt { get; set; }

        public string BaseProfile { get; set; } = "None";

        public List<SandboxChange> Changes { get; set; } = new();

        public SandboxExperimentState State { get; set; } = SandboxExperimentState.Draft;

        public string? SnapshotId { get; set; }

        public DateTime? AppliedAt { get; set; }

        public DateTime? CompletedAt { get; set; }

        public SandboxMeasurement? Measurement { get; set; }

        public SandboxTestResult? Result { get; set; }

        /// <summary>Human-readable result label, e.g. "Improved", "Insufficient data".</summary>
        public string? ResultLabel { get; set; }

        /// <summary>Set when the user chose "Ignore" on the recovery prompt so we do not nag every startup.</summary>
        public bool RecoveryAcknowledged { get; set; }

        public string? LastError { get; set; }

        public string DisplayName => $"Experiment #{Id}";

        [JsonIgnore]
        public bool IsUnfinished => State.IsUnfinished();

        /// <summary>User-facing status name (e.g. "Rolled Back") used by history and banners.</summary>
        [JsonIgnore]
        public string FriendlyStateName => State.ToFriendlyName();
    }

    /// <summary>
    /// Persistent journal for the Optimization Sandbox. Written atomically on every change so
    /// interrupted experiments can be detected and recovered after a crash or restart.
    /// </summary>
    public class SandboxJournal
    {
        public int NextExperimentNumber { get; set; } = 1;

        /// <summary>Id of the experiment currently applied to the configuration, if any.</summary>
        public string? ActiveExperimentId { get; set; }

        public List<SandboxExperiment> Experiments { get; set; } = new();
    }
}
