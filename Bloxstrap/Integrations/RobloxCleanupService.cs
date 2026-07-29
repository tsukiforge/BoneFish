using System.Windows;
using Microsoft.Win32;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// Menyediakan fitur "Bersihkan Roblox Player" dan "Bersihkan Roblox Studio"
    /// yang berdiri sendiri (independent dari uninstall BoneFish).
    /// 
    /// AUDIT FINDINGS:
    /// - Paths.Versions/{Base}/Versions memiliki subfolder TERPISAH untuk Player
    ///   (WindowsPlayer) dan Studio (WindowsStudio64) — aman dihapus sendiri-sendiri.
    /// - Dynamic version directory (based on VersionGuid) juga terpisah per produk.
    /// - Registry key Player: roblox, roblox-player
    /// - Registry key Studio: roblox-studio, roblox-studio-auth, Roblox.Place, .rbxl, .rbxlx
    /// - Stock Roblox %localappdata%/Roblox/Versions is SHARED — tidak disentuh di sini.
    /// - Cache dan logs (Paths.RobloxCache, Paths.RobloxLogs) bersifat SHARED — 
    ///   opsional dihapus dengan peringatan.
    /// </summary>
    public static class RobloxCleanupService
    {
        private const string LOG_IDENT = "RobloxCleanupService";

        /// <summary>
        /// Bersihkan semua data Roblox Player. Tidak menyentuh instalasi BoneFish.
        /// </summary>
        public static bool CleanPlayer()
        {
            return Cleanup(cleanPlayer: true, cleanStudio: false);
        }

        /// <summary>
        /// Bersihkan semua data Roblox Studio. Tidak menyentuh instalasi BoneFish.
        /// </summary>
        public static bool CleanStudio()
        {
            return Cleanup(cleanPlayer: false, cleanStudio: true);
        }

        private static bool Cleanup(bool cleanPlayer, bool cleanStudio)
        {
            string productLabel = cleanPlayer ? "Roblox Player" : "Roblox Studio";

            // ── Step 1: Cek & tutup proses terkait ──────────────────────────
            var processes = new List<Process>();

            if (cleanPlayer)
            {
                processes.AddRange(Process.GetProcessesByName(App.RobloxPlayerAppName.Replace(".exe", "")));
                // Crash handler juga cukup penting untuk dimatikan
                try { processes.AddRange(Process.GetProcessesByName("RobloxCrashHandler")); } catch { }
            }

            if (cleanStudio)
            {
                processes.AddRange(Process.GetProcessesByName(App.RobloxStudioAppName.Replace(".exe", "")));
            }

            if (processes.Any(p => !p.HasExited))
            {
                string runningMsg = cleanPlayer && cleanStudio
                    ? "Roblox Player dan/atau Studio sedang berjalan"
                    : $"{productLabel} sedang berjalan";

                var result = Frontend.ShowMessageBox(
                    $"{runningMsg}, tetapi harus ditutup sebelum pembersihan.\n\nApakah Anda ingin menutup {productLabel} sekarang?",
                    MessageBoxImage.Information,
                    MessageBoxButton.OKCancel,
                    MessageBoxResult.OK
                );

                if (result != MessageBoxResult.OK)
                    return false;

                foreach (var process in processes)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                            process.WaitForExit(3000);
                        }
                        process.Close();
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Failed to kill process {process.Id}: {ex.Message}");
                    }
                }
            }

            // ── Step 2: Dialog konfirmasi — destructive action ─────────────
            var whatWillBeDeleted = new List<string>();

            // BoneFish managed versions (terpisah per produk — aman)
            if (cleanPlayer)
            {
                // Static dir
                string playerStatic = Path.Combine(Paths.Versions, "WindowsPlayer");
                if (Directory.Exists(playerStatic))
                    whatWillBeDeleted.Add(playerStatic);

                // Dynamic dir (version GUID)
                if (!string.IsNullOrEmpty(App.RobloxState.Prop.Player.VersionGuid))
                {
                    string playerDynamic = Path.Combine(Paths.Versions, App.RobloxState.Prop.Player.VersionGuid);
                    if (Directory.Exists(playerDynamic))
                        whatWillBeDeleted.Add(playerDynamic);
                }
                whatWillBeDeleted.Add("Registry: roblox, roblox-player");
            }

            if (cleanStudio)
            {
                string studioStatic = Path.Combine(Paths.Versions, "WindowsStudio64");
                if (Directory.Exists(studioStatic))
                    whatWillBeDeleted.Add(studioStatic);

                if (!string.IsNullOrEmpty(App.RobloxState.Prop.Studio.VersionGuid))
                {
                    string studioDynamic = Path.Combine(Paths.Versions, App.RobloxState.Prop.Studio.VersionGuid);
                    if (Directory.Exists(studioDynamic))
                        whatWillBeDeleted.Add(studioDynamic);
                }
                whatWillBeDeleted.Add("Registry: roblox-studio, roblox-studio-auth");
                whatWillBeDeleted.Add("Registry: Roblox.Place, .rbxl, .rbxlx");
            }

            // Shared cache/logs — hapus dengan catatan
            if (cleanPlayer || cleanStudio)
            {
                if (Directory.Exists(Paths.RobloxLogs))
                    whatWillBeDeleted.Add($"Logs ({Paths.RobloxLogs}) - shared antara Player & Studio");

                if (Directory.Exists(Paths.RobloxCache))
                    whatWillBeDeleted.Add($"Cache ({Paths.RobloxCache}) - shared antara Player & Studio");
            }

            string detail = string.Join("\n• ", whatWillBeDeleted);
            string confirmMsg =
                $"⚠️ PERINGATAN: Data yang dihapus TIDAK BISA dikembalikan!\n\n" +
                $"Berikut adalah data {productLabel} yang akan dihapus:\n\n• {detail}\n\n" +
                $"Instalasi BoneFish sendiri TIDAK akan tersentuh.\n\n" +
                $"Apakah Anda yakin ingin melanjutkan?";

            var confirmResult = Frontend.ShowMessageBox(
                confirmMsg,
                MessageBoxImage.Warning,
                MessageBoxButton.YesNo,
                MessageBoxResult.No
            );

            if (confirmResult != MessageBoxResult.Yes)
                return false;

            // ── Step 3: Eksekusi penghapusan ───────────────────────────────
            long totalBytesFreed = 0;

            // Hapus direktori versi
            if (cleanPlayer)
            {
                totalBytesFreed += DeleteDirectorySafe(Path.Combine(Paths.Versions, "WindowsPlayer"));
                if (!string.IsNullOrEmpty(App.RobloxState.Prop.Player.VersionGuid))
                    totalBytesFreed += DeleteDirectorySafe(Path.Combine(Paths.Versions, App.RobloxState.Prop.Player.VersionGuid));

                // Unreg registry Player
                WindowsRegistry.Unregister("roblox");
                WindowsRegistry.Unregister("roblox-player");

                // Reset state Player
                App.RobloxState.Prop.Player.VersionGuid = "";
                App.RobloxState.Prop.Player.Size = 0;
            }

            if (cleanStudio)
            {
                totalBytesFreed += DeleteDirectorySafe(Path.Combine(Paths.Versions, "WindowsStudio64"));
                if (!string.IsNullOrEmpty(App.RobloxState.Prop.Studio.VersionGuid))
                    totalBytesFreed += DeleteDirectorySafe(Path.Combine(Paths.Versions, App.RobloxState.Prop.Studio.VersionGuid));

                // Unreg registry Studio
                WindowsRegistry.Unregister("roblox-studio");
                WindowsRegistry.Unregister("roblox-studio-auth");
                WindowsRegistry.Unregister("Roblox.Place");
                WindowsRegistry.Unregister(".rbxl");
                WindowsRegistry.Unregister(".rbxlx");

                // Reset state Studio
                App.RobloxState.Prop.Studio.VersionGuid = "";
                App.RobloxState.Prop.Studio.Size = 0;
            }

            // Hapus shared logs & cache
            totalBytesFreed += DeleteDirectorySafe(Paths.RobloxLogs);
            totalBytesFreed += DeleteDirectorySafe(Paths.RobloxCache);

            // Simpan state
            App.RobloxState.Save();

            // ── Step 4: Tampilkan hasil ────────────────────────────────────
            string freedSize = FormatBytes(totalBytesFreed);
            Frontend.ShowMessageBox(
                $"{productLabel} berhasil dibersihkan!\n\n" +
                $"Ruang kosong: {freedSize}",
                MessageBoxImage.Information,
                MessageBoxButton.OK
            );

            App.Logger.WriteLine(LOG_IDENT, $"Cleanup {productLabel} selesai. Freed: {freedSize}");
            return true;
        }

        private static long DeleteDirectorySafe(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return 0;

            try
            {
                long size = CalculateDirectorySize(path);
                Directory.Delete(path, true);
                App.Logger.WriteLine(LOG_IDENT, $"Deleted: {path} ({FormatBytes(size)})");
                return size;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to delete {path}: {ex.Message}");
                return 0;
            }
        }

        private static long CalculateDirectorySize(string path)
        {
            try
            {
                return Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                    .Sum(file => new FileInfo(file).Length);
            }
            catch
            {
                return 0;
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }
    }
}
