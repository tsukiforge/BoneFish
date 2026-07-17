using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// Service untuk membersihkan cache Roblox secara otomatis.
    /// Hapus file cache yang sudah tua (melebihi maxAgeDays) ATAU
    /// jika total ukuran folder melebihi maxSizeBytes (hapus file terlama dulu).
    ///
    /// AMAN: Tidak menghapus file yang sedang dipakai oleh proses Roblox aktif.
    /// </summary>
    public static class CacheCleanerService
    {
        private const string LOG_IDENT = "CacheCleanerService";

        /// <summary>
        /// Bersihkan cache Roblox: hapus file lebih tua dari maxAgeDays ATAU
        /// jika total ukuran > maxSizeBytes (hapus file terlama sampai di bawah limit).
        /// Otomatis skip jika proses Roblox sedang berjalan.
        /// </summary>
        /// <param name="maxAgeDays">File lebih tua dari ini (hari) akan dihapus</param>
        /// <param name="maxSizeBytes">Jika total ukuran melebihi ini (bytes), hapus file terlama</param>
        /// <returns>Jumlah file yang dihapus</returns>
        public static async Task<int> CleanupOldCache(int maxAgeDays = 14, long maxSizeBytes = 500L * 1024 * 1024)
        {
            const string METHOD_IDENT = "CacheCleanerService::CleanupOldCache";

            // ★ AMAN: Cek dulu apakah Roblox sedang berjalan — jangan cleanup kalo lagi main
            if (IsRobloxRunning())
            {
                App.Logger.WriteLine(METHOD_IDENT, "Roblox is running — skipping cache cleanup to avoid locked files.");
                return 0;
            }

            return await Task.Run(() =>
            {
                try
                {
                    string cacheDir = Paths.RobloxCache;

                    if (string.IsNullOrEmpty(cacheDir) || !Directory.Exists(cacheDir))
                    {
                        App.Logger.WriteLine(METHOD_IDENT, $"Cache directory not found: {cacheDir ?? "(null)"}");
                        return 0;
                    }

                    // Kumpulkan semua file dengan info
                    var files = Directory.GetFiles(cacheDir, "*.*", SearchOption.AllDirectories)
                        .Select(f =>
                        {
                            try
                            {
                                var fi = new FileInfo(f);
                                return new { Path = f, Length = fi.Length, LastWrite = fi.LastWriteTime };
                            }
                            catch { return null; }
                        })
                        .Where(f => f != null)
                        .OrderBy(f => f!.LastWrite) // Sort by oldest first
                        .ToList();

                    if (files.Count == 0)
                    {
                        App.Logger.WriteLine(METHOD_IDENT, "No files found in cache directory.");
                        return 0;
                    }

                    long totalSize = files.Sum(f => f!.Length);
                    DateTime cutoffDate = DateTime.Now.AddDays(-maxAgeDays);

                    int deletedCount = 0;
                    long freedBytes = 0;

                    // Fase 1: Hapus file yang lebih tua dari maxAgeDays
                    var oldFiles = files.Where(f => f!.LastWrite < cutoffDate).Select(f => f!).ToList();

                    foreach (var file in oldFiles)
                    {
                        try
                        {
                            File.Delete(file.Path);
                            deletedCount++;
                            freedBytes += file.Length;
                            totalSize -= file.Length;
                        }
                        catch (Exception ex)
                        {
                            App.Logger.WriteLine(METHOD_IDENT, $"Could not delete old file {file.Path}: {ex.Message}");
                        }
                    }

                    // Fase 2: Jika masih melebihi maxSizeBytes, hapus file terlama
                    if (totalSize > maxSizeBytes)
                    {
                        var remainingFiles = files
                            .Where(f => !oldFiles.Any(o => o.Path == f!.Path))
                            .Select(f => f!)
                            .OrderBy(f => f.LastWrite)
                            .ToList();

                        foreach (var file in remainingFiles)
                        {
                            if (totalSize <= maxSizeBytes)
                                break;

                            try
                            {
                                File.Delete(file.Path);
                                deletedCount++;
                                freedBytes += file.Length;
                                totalSize -= file.Length;
                            }
                            catch (Exception ex)
                            {
                                App.Logger.WriteLine(METHOD_IDENT, $"Could not delete size-limit file {file.Path}: {ex.Message}");
                            }
                        }
                    }

                    App.Logger.WriteLine(METHOD_IDENT,
                        $"Cache cleanup complete: {deletedCount} file(s) deleted, " +
                        $"{(freedBytes / 1024.0 / 1024.0):F2} MB freed, " +
                        $"remaining cache size: {(totalSize / 1024.0 / 1024.0):F2} MB");

                    return deletedCount;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(METHOD_IDENT, $"Cache cleanup failed: {ex.Message}");
                    return 0;
                }
            });
        }

        /// <summary>
        /// Cek apakah proses Roblox sedang berjalan (agar tidak tabrakan file lock).
        /// </summary>
        private static bool IsRobloxRunning()
        {
            try
            {
                return Process.GetProcessesByName("RobloxPlayerBeta").Any()
                    || Process.GetProcessesByName("RobloxStudioBeta").Any();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Dapatkan total ukuran folder cache Roblox saat ini.
        /// </summary>
        public static long GetCacheSize()
        {
            try
            {
                string cacheDir = Paths.RobloxCache;
                if (!Directory.Exists(cacheDir))
                    return 0;

                return Directory.GetFiles(cacheDir, "*.*", SearchOption.AllDirectories)
                    .Sum(f =>
                    {
                        try { return new FileInfo(f).Length; }
                        catch { return 0L; }
                    });
            }
            catch
            {
                return 0;
            }
        }
    }
}
