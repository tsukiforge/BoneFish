using System.Runtime.CompilerServices;
using System.Windows;
using System.Xml.Linq;

namespace Bloxstrap
{
    public class JsonManager<T> where T : class, new()
    {
        public T OriginalProp { get; set; } = new();

        public T Prop { get; set; } = new();

        /// <summary>
        /// The file hash when last retrieved from disk
        /// </summary>
        public string? LastFileHash { get; private set; }

        public bool Loaded { get; set; } = false;

        public virtual string ClassName => typeof(T).Name;
        
        public virtual string ProfilesLocation => Path.Combine(Paths.Base, $"Profiles.json");

        public virtual string FileLocation => Path.Combine(Paths.Base, $"{ClassName}.json");

        public virtual string LOG_IDENT_CLASS => $"JsonManager<{ClassName}>";

        public virtual void Load(bool alertFailure = true)
        {
            
            string LOG_IDENT = $"{LOG_IDENT_CLASS}::Load";

            App.Logger.WriteLine(LOG_IDENT, $"Loading from {FileLocation}...");

            try
            {
                string contents = File.ReadAllText(FileLocation);

                T? settings = JsonSerializer.Deserialize<T>(contents);

                if (settings is null)
                    throw new ArgumentNullException("Deserialization returned null");

                Prop = settings;
                Loaded = true;
                LastFileHash = MD5Hash.FromString(contents);

                App.Logger.WriteLine(LOG_IDENT, "Loaded successfully!");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Failed to load!");
                App.Logger.WriteException(LOG_IDENT, ex);

                if (alertFailure)
                {
                    string message = "";

                    if (ClassName == nameof(Settings))
                        message = Strings.JsonManager_SettingsLoadFailed;
                    else if (ClassName == nameof(FastFlagManager))
                        message = Strings.JsonManager_FastFlagsLoadFailed;

                    if (!String.IsNullOrEmpty(message))
                        Frontend.ShowMessageBox($"{message}\n\n{ex.Message}", System.Windows.MessageBoxImage.Warning);

                try
                {
                    // Create a backup of loaded file
                    File.Copy(FileLocation, FileLocation + ".bak", true);

                    // ── Cleanup .bak menumpuk (FIX v7.3.1) ─────────────────────
                    // Hapus .bak lebih lama dari 2 versi terakhir untuk mencegah
                    // pembengkakan folder BoneFish (5 file @ ~20MB = ~100MB per user).
                    CleanupOldBackups(Path.GetDirectoryName(FileLocation)!);
                }
                catch (Exception copyEx)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Failed to create backup file: {FileLocation}.bak");
                    App.Logger.WriteException(LOG_IDENT, copyEx);
                }
                }

                Save();
            }
        }

        public virtual void Save()
        {
            string LOG_IDENT = $"{LOG_IDENT_CLASS}::Save";
            
            App.Logger.WriteLine(LOG_IDENT, $"Saving to {FileLocation}...");

            Directory.CreateDirectory(Path.GetDirectoryName(FileLocation)!);

            try
            {
                string contents = JsonSerializer.Serialize(Prop, new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(FileLocation, contents);

                LastFileHash = MD5Hash.FromString(contents);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                App.Logger.WriteLine(LOG_IDENT, "Failed to save");
                App.Logger.WriteException(LOG_IDENT, ex);

                string errorMessage = string.Format(Resources.Strings.Bootstrapper_JsonManagerSaveFailed, ClassName, ex.Message);
                Frontend.ShowMessageBox(errorMessage, System.Windows.MessageBoxImage.Warning);

                return;
            }

            App.Logger.WriteLine(LOG_IDENT, "Save complete!");
        }

        /// <summary>
        /// Is the file on disk different to the one deserialised during this session?
        /// </summary>
        public bool HasFileOnDiskChanged()
        {
            return LastFileHash != MD5Hash.FromFile(FileLocation);
        }

        // ── .bak Retention Policy (FIX v7.3.1) ───────────────────────────────
        // Simpan HANYA 2 backup terakhir per directory. Hapus .bak lebih lama
        // untuk mencegah pembengkakan folder BoneFish (5 file @ ~20MB = ~100MB).
        // Dipanggil dari Load() setelah backup baru dibuat, DAN saat startup retroaktif.
        private const int MaxBackupVersions = 2;

        /// <summary>
        /// Hapus .bak lebih lama dari MaxBackupVersions terakhir berdasarkan
        /// waktu modifikasi. Dipanggil retroaktif saat startup dan setiap
        /// backup baru dibuat.
        /// </summary>
        internal static void CleanupOldBackups(string directory)
        {
            try
            {
                if (!Directory.Exists(directory))
                    return;

                // Kumpulkan semua file .bak di directory ini
                var bakFiles = Directory.GetFiles(directory, "*.bak")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTime)
                    .ToList();

                if (bakFiles.Count <= MaxBackupVersions)
                    return;

                // Hapus yang lebih lama dari MaxBackupVersions terakhir
                foreach (var old in bakFiles.Skip(MaxBackupVersions))
                {
                    try
                    {
                        old.Delete();
                        App.Logger.WriteLine("JsonManager", $"Old backup cleaned: {old.Name} (retention={MaxBackupVersions})");
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteException("JsonManager", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("JsonManager", ex);
            }
        }

        /// <summary>
        /// Retroaktif cleanup: jalankan di semua direktori yang mungkin berisi .bak
        /// saat startup. Panggil dari Bootstrapper/Program setelah Load().
        /// </summary>
        public static void CleanupAllBackupsOnStartup()
        {
            try
            {
                string baseDir = Paths.Base;
                if (Directory.Exists(baseDir))
                    CleanupOldBackups(baseDir);

                // Versi Roblox juga bisa punya .bak
                string versionsDir = Path.Combine(Paths.LocalAppData, "Roblox", "Versions");
                if (Directory.Exists(versionsDir))
                {
                    foreach (string versionDir in Directory.GetDirectories(versionsDir))
                    {
                        string clientSettings = Path.Combine(versionDir, "ClientSettings");
                        if (Directory.Exists(clientSettings))
                            CleanupOldBackups(clientSettings);
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("JsonManager", ex);
            }
        }
    }
}
