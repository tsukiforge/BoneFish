using Bloxstrap.Sandbox.Interfaces;
using Bloxstrap.Sandbox.Models;

namespace Bloxstrap.Sandbox
{
    /// <summary>
    /// Small filesystem helpers for the sandbox. All writes are atomic (temp file + move) so a
    /// crash mid-write can never leave a truncated journal or snapshot.
    /// </summary>
    public static class SandboxStorage
    {
        public static string GetRoot() => Path.Combine(Paths.Base, "Sandbox");

        public static string GetJournalPath(string? root = null) =>
            Path.Combine(root ?? GetRoot(), "journal.json");

        public static string GetSnapshotPath(string snapshotId, string? root = null) =>
            Path.Combine(root ?? GetRoot(), "Snapshots", $"{snapshotId}.json");

        public static void WriteAllTextAtomic(string path, string content)
        {
            string directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);

            string tempPath = Path.Combine(directory, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(tempPath, content);

            File.Move(tempPath, path, overwrite: true);
        }

        public static string ReadAllText(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
    }

    /// <summary>
    /// Creates, persists, restores and verifies versioned snapshots of the configuration state
    /// that an experiment is about to modify. Restore is deterministic: it returns every touched
    /// flag to the exact value recorded in the snapshot, and verifies afterwards.
    /// </summary>
    public class SnapshotService
    {
        private const string LOG_IDENT = "OptimizationSandbox::SnapshotService";

        private readonly IFastFlagStore _store;
        private readonly string? _storageRoot;

        public SnapshotService(IFastFlagStore store, string? storageRoot = null)
        {
            _store = store;
            _storageRoot = storageRoot;
        }

        /// <summary>
        /// Capture the current values of every flag the experiment touches, plus the raw file content.
        /// Throws <see cref="SandboxException"/> when the snapshot cannot be persisted.
        /// </summary>
        public async Task<SandboxSnapshot> CreateAsync(
            SandboxExperiment experiment,
            string baseProfile,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string snapshotId = $"snapshot_{experiment.Id}";
            var snapshot = new SandboxSnapshot
            {
                Id = snapshotId,
                Version = SandboxSnapshot.CurrentFormatVersion,
                CreatedAt = DateTime.UtcNow,
                BaseProfile = baseProfile,
                TargetApplication = "Roblox",
                RelativeFilePath = "ClientSettings\\ClientAppSettings.json"
            };

            // Only the flags actually touched by the experiment are recorded — never the whole disk.
            var touched = experiment.Changes.Where(c => !string.IsNullOrWhiteSpace(c.FlagName)).Select(c => c.FlagName).ToList();

            Dictionary<string, string> allFlags = new();
            string? rawFile = null;

            await Task.Run(() =>
            {
                allFlags = _store.GetAll();
                rawFile = _store.ReadRawFileContent();
            }, cancellationToken);

            foreach (var name in touched)
            {
                if (allFlags.TryGetValue(name, out string? value))
                    snapshot.OriginalValues[name] = value;
            }

            snapshot.OriginalFileContent = rawFile ?? "{}";
            snapshot.OriginalFileHash = MD5Hash.FromString(snapshot.OriginalFileContent);
            snapshot.ConfigurationHash = ComputeConfigurationHash(snapshot.OriginalValues);

            string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            string path = SandboxStorage.GetSnapshotPath(snapshotId, _storageRoot);

            await Task.Run(() =>
            {
                try
                {
                    SandboxStorage.WriteAllTextAtomic(path, json);
                }
                catch (Exception ex)
                {
                    throw new SandboxException("Failed to persist snapshot", ex);
                }
            }, cancellationToken);

            App.Logger.WriteLine(LOG_IDENT, $"Snapshot '{snapshotId}' created ({touched.Count} flag(s), version {snapshot.Version})");
            return snapshot;
        }

        /// <summary>
        /// Load a snapshot from disk, validating version and internal integrity.
        /// Throws when the data is missing or corrupted so callers never restore blindly.
        /// </summary>
        public async Task<SandboxSnapshot?> LoadAsync(string snapshotId, CancellationToken cancellationToken = default)
        {
            string path = SandboxStorage.GetSnapshotPath(snapshotId, _storageRoot);

            if (!File.Exists(path))
                return null;

            string json;
            SandboxSnapshot? snapshot = null;

            await Task.Run(() =>
            {
                json = SandboxStorage.ReadAllText(path);

                try
                {
                    snapshot = JsonSerializer.Deserialize<SandboxSnapshot>(json);
                }
                catch (Exception ex)
                {
                    throw new SandboxException($"Snapshot '{snapshotId}' is corrupted and cannot be parsed", ex);
                }
            }, cancellationToken);

            if (snapshot is null)
                throw new SandboxException($"Snapshot '{snapshotId}' is corrupted (empty content)");

            if (!snapshot.IsSupportedVersion)
                throw new SandboxException($"Snapshot '{snapshotId}' uses unsupported format version {snapshot.Version}");

            if (!IsInternallyConsistent(snapshot))
                throw new SandboxException($"Snapshot '{snapshotId}' failed integrity validation");

            return snapshot;
        }

        /// <summary>Deterministic hash over the snapshot's original flag values (sorted by key).</summary>
        public static string ComputeConfigurationHash(IReadOnlyDictionary<string, string> values)
        {
            var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in values)
                sorted[pair.Key] = pair.Value;

            string canonical = JsonSerializer.Serialize(sorted, new JsonSerializerOptions { WriteIndented = true });
            return MD5Hash.FromString(canonical);
        }

        private static bool IsInternallyConsistent(SandboxSnapshot snapshot)
        {
            if (string.IsNullOrWhiteSpace(snapshot.Id))
                return false;

            if (MD5Hash.FromString(snapshot.OriginalFileContent) != snapshot.OriginalFileHash)
                return false;

            if (ComputeConfigurationHash(snapshot.OriginalValues) != snapshot.ConfigurationHash)
                return false;

            return true;
        }

        /// <summary>
        /// Restore every touched flag to its pre-experiment value and verify the result.
        /// Flags that did not exist before are removed. Throws if verification fails — the caller
        /// must never claim success in that case.
        /// </summary>
        public async Task RestoreAsync(
            SandboxSnapshot snapshot,
            IEnumerable<string> touchedFlagNames,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var touched = touchedFlagNames.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal).ToList();

            await Task.Run(() =>
            {
                foreach (var name in touched)
                {
                    if (snapshot.OriginalValues.TryGetValue(name, out string? original))
                        _store.SetValue(name, original);
                    else
                        _store.SetValue(name, null);
                }

                _store.Save();
            }, cancellationToken);

            if (!await VerifyRestoredAsync(snapshot, touched, cancellationToken))
                throw new SandboxException("Rollback verification failed: configuration does not match the snapshot");

            App.Logger.WriteLine(LOG_IDENT, $"Snapshot '{snapshot.Id}' restored and verified ({touched.Count} flag(s))");
        }

        /// <summary>
        /// Verify that the current configuration matches the snapshot for all touched flags.
        /// </summary>
        public async Task<bool> VerifyRestoredAsync(
            SandboxSnapshot snapshot,
            IEnumerable<string> touchedFlagNames,
            CancellationToken cancellationToken = default)
        {
            var touched = touchedFlagNames.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal).ToList();

            Dictionary<string, string> currentFlags = new();
            await Task.Run(() => currentFlags = _store.GetAll(), cancellationToken);

            foreach (var name in touched)
            {
                bool existedBefore = snapshot.OriginalValues.TryGetValue(name, out string? original);
                bool existsNow = currentFlags.TryGetValue(name, out string? current);

                if (existedBefore)
                {
                    if (!existsNow || !string.Equals(original, current, StringComparison.Ordinal))
                        return false;
                }
                else if (existsNow)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
