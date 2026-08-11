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

        public int SampleCount { get; set; }

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
