using Bloxstrap.Sandbox.Models;

namespace Bloxstrap.Sandbox
{
    public enum SandboxDiffType
    {
        Added,
        Changed,
        Removed,
        Unchanged
    }

    public class SandboxDiffEntry
    {
        public SandboxDiffType Type { get; set; }
        public string FlagName { get; set; } = "";
        public string? CurrentValue { get; set; }
        public string? NewValue { get; set; }

        /// <summary>Compact description like "false → true".</summary>
        public string Description =>
            Type == SandboxDiffType.Removed
                ? $"− removed ({CurrentValue})"
                : Type == SandboxDiffType.Added
                    ? $"− absent → {NewValue}"
                    : $"{CurrentValue} → {NewValue}";

        /// <summary>UI marker: + added, ~ changed, − removed, (space) unchanged.</summary>
        public string Marker => Type switch
        {
            SandboxDiffType.Added => "+",
            SandboxDiffType.Changed => "~",
            SandboxDiffType.Removed => "−",
            _ => " "
        };

        /// <summary>UI color hint for the marker.</summary>
        public string MarkerBrush => Type switch
        {
            SandboxDiffType.Added => "#4CAF50",
            SandboxDiffType.Changed => "#FF9800",
            SandboxDiffType.Removed => "#F44336",
            _ => "#808080"
        };
    }

    /// <summary>
    /// Pure, side-effect free diff between a base configuration and a set of experiment changes.
    /// Used both for the "what will change" preview and for verification after writing.
    /// </summary>
    public static class ConfigurationDiffService
    {
        /// <summary>
        /// Compute the diff of applying <paramref name="changes"/> on top of <paramref name="baseFlags"/>.
        /// Unchanged entries are included so the UI can show that a flag is left alone.
        /// </summary>
        public static List<SandboxDiffEntry> ComputeDiff(
            IReadOnlyDictionary<string, string> baseFlags,
            IEnumerable<SandboxChange> changes)
        {
            var entries = new List<SandboxDiffEntry>();

            foreach (var change in changes)
            {
                if (string.IsNullOrWhiteSpace(change.FlagName))
                    continue;

                baseFlags.TryGetValue(change.FlagName, out string? current);

                if (change.NewValue is null)
                {
                    // Removing a flag that does not exist is a no-op; do not surface it as a change.
                    if (current is null)
                        continue;

                    entries.Add(new SandboxDiffEntry
                    {
                        Type = SandboxDiffType.Removed,
                        FlagName = change.FlagName,
                        CurrentValue = current,
                        NewValue = null
                    });
                }
                else if (current is null)
                {
                    entries.Add(new SandboxDiffEntry
                    {
                        Type = SandboxDiffType.Added,
                        FlagName = change.FlagName,
                        CurrentValue = null,
                        NewValue = change.NewValue
                    });
                }
                else if (string.Equals(current, change.NewValue, StringComparison.Ordinal))
                {
                    entries.Add(new SandboxDiffEntry
                    {
                        Type = SandboxDiffType.Unchanged,
                        FlagName = change.FlagName,
                        CurrentValue = current,
                        NewValue = current
                    });
                }
                else
                {
                    entries.Add(new SandboxDiffEntry
                    {
                        Type = SandboxDiffType.Changed,
                        FlagName = change.FlagName,
                        CurrentValue = current,
                        NewValue = change.NewValue
                    });
                }
            }

            return entries;
        }

        public static int CountActualChanges(IEnumerable<SandboxDiffEntry> diff) =>
            diff.Count(x => x.Type is SandboxDiffType.Added or SandboxDiffType.Changed or SandboxDiffType.Removed);
    }

    /// <summary>
    /// Validates sandbox changes before anything is written. Rejects invalid keys, invalid values,
    /// malformed configuration and anything that could be interpreted as a path.
    /// </summary>
    public static class SandboxChangeValidator
    {
        // Roblox FastFlag names are alphanumeric identifiers. Rejecting dots, slashes and
        // whitespace also makes path traversal impossible by construction.
        private static readonly Regex FlagNameRegex = new(
            "^[A-Za-z][A-Za-z0-9_]{0,127}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private const int MaxValueLength = 1024;

        public static bool IsFlagNameValid(string? name) =>
            !string.IsNullOrWhiteSpace(name) && FlagNameRegex.IsMatch(name);

        public static bool IsValueValid(string? value)
        {
            if (value is null)
                return true; // null = remove flag, always valid

            if (value.Length == 0 || value.Length > MaxValueLength)
                return false;

            foreach (char c in value)
            {
                if (char.IsControl(c))
                    return false;
            }

            // Only primitive values are accepted: bool, integer, floating point, plain string.
            // Raw JSON objects/arrays/embedded quotes are rejected to prevent malformed configuration.
            if (value.TrimStart().StartsWith('{') || value.TrimStart().StartsWith('['))
                return false;

            if (value.Contains('"', StringComparison.Ordinal) || value.Contains('\'', StringComparison.Ordinal))
                return false;

            if (bool.TryParse(value, out _))
                return true;
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                return true;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                return true;
            if (value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal))
                return false;

            return true;
        }

        public static bool IsChangeValid(SandboxChange change) =>
            IsFlagNameValid(change.FlagName) && IsValueValid(change.NewValue);

        public static string? GetFirstInvalidChangeMessage(SandboxChange change)
        {
            if (!IsFlagNameValid(change.FlagName))
                return $"Invalid flag name '{change.FlagName}'. Names may only contain letters, digits and underscores.";

            if (!IsValueValid(change.NewValue))
                return $"Invalid value for '{change.FlagName}'. Only plain bool/int/decimal/string values are allowed.";

            return null;
        }
    }
}
