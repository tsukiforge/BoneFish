using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;

namespace Bloxstrap.Integrations
{
    /// <summary>
    /// Service untuk mengubah background UI aplikasi BoneFish
    /// Mendukung 3 pilihan wallpaper sebagai background dari folder Bloxstrap\Resources\Wallpapers\
    /// Wallpaper berubah random setiap kali aplikasi dibuka
    ///
    /// ★ FIX LOADING: Sekarang loading pakai MemoryStream + background thread
    ///   Jadi UI thread gak nge-block pas baca file dari disk! ★
    /// </summary>
    public class AppBackgroundService
    {
        private const string LOG_IDENT = "AppBackgroundService";

        // Cache biar gak loading ulang tiap kali ganti halaman
        private static readonly Dictionary<BackgroundType, BitmapImage?> _imageCache = new();

        // Cache khusus custom background (pisah dari enum biar gak pake magic number)
        private static BitmapImage? _customCache;

        public enum BackgroundType
        {
            Default = 0,
            Cool = 1,
            Quality = 2,
            Extra = 3,
            Custom = 4
        }

        /// <summary>
        /// Load wallpaper ASYNC — gak block UI! Pake MemoryStream biar file handle cepet di-release.
        /// </summary>
        public static Task<BitmapImage?> GetBackgroundImageAsync(BackgroundType type)
        {
            return Task.Run(() =>
            {
                try
                {
                    // Cek cache dulu
                    if (_imageCache.TryGetValue(type, out var cached) && cached != null)
                        return cached;

                    string? imagePath = GetImagePath(type);

                    if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Background image not found: {imagePath ?? "(path empty/null)"}");
                        return null;
                    }

                    // ★ FIX LOADING: Baca byte ke memory dulu, baru bikin BitmapImage
                    // Dengan cara ini file handle cepet di-release dan UI gak nge-block
                    byte[] imageBytes = File.ReadAllBytes(imagePath);

                    var bitmap = new BitmapImage();
                    using (var ms = new MemoryStream(imageBytes))
                    {
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = ms;
                        bitmap.EndInit();
                    }
                    bitmap.Freeze();

                    // Simpan ke cache
                    _imageCache[type] = bitmap;

                    return bitmap;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Error loading background image: {ex.Message}");
                    return null;
                }
            });
        }

        /// <summary>
        /// Get random background ASYNC — skip Custom karena itu pilihan user.
        /// </summary>
        public static async Task<BitmapImage?> GetRandomBackgroundAsync()
        {
            try
            {
                var backgroundTypes = Enum.GetValues(typeof(BackgroundType))
                    .Cast<BackgroundType>()
                    .Where(t => t != BackgroundType.Custom)
                    .ToList();

                var randomType = backgroundTypes[Random.Shared.Next(backgroundTypes.Count)];

                App.Logger.WriteLine(LOG_IDENT, $"Selected random background: {randomType}");
                return await GetBackgroundImageAsync(randomType);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error getting random background: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get custom background ASYNC dari path yang disimpan di Settings.
        /// Fallback ke Default kalo file gak ada / rusak.
        /// </summary>
        public static async Task<BitmapImage?> GetCustomBackgroundAsync()
        {
            try
            {
                string? customPath = App.Settings.Prop.CustomBackgroundPath;

                if (string.IsNullOrEmpty(customPath) || !File.Exists(customPath))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Custom background path invalid or not found: {customPath}. Falling back to Default.");
                    return await GetBackgroundImageAsync(BackgroundType.Default);
                }

                // Cek cache custom dulu
                if (_customCache != null)
                    return _customCache;

                byte[] imageBytes = await Task.Run(() => File.ReadAllBytes(customPath));

                var bitmap = new BitmapImage();
                using (var ms = new MemoryStream(imageBytes))
                {
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                }
                bitmap.Freeze();

                _customCache = bitmap;
                return bitmap;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Error loading custom background: {ex.Message}. Falling back to Default.");
                return await GetBackgroundImageAsync(BackgroundType.Default);
            }
        }

        /// <summary>
        /// Get specific background ASYNC
        /// </summary>
        public static Task<BitmapImage?> GetBackgroundAsync(BackgroundType type)
        {
            return type == BackgroundType.Custom
                ? GetCustomBackgroundAsync()
                : GetBackgroundImageAsync(type);
        }

        /// <summary>
        /// Resolve image path berdasarkan BackgroundType
        /// </summary>
        private static string? GetImagePath(BackgroundType type)
        {
            if (type == BackgroundType.Custom)
                return App.Settings.Prop.CustomBackgroundPath;

            string wallpaperPath = Paths.ResolveWallpapersDir();

            return type switch
            {
                BackgroundType.Default => Path.Combine(wallpaperPath, "wallpapers.jpg"),
                BackgroundType.Cool => Path.Combine(wallpaperPath, "wallpapersC.jpg"),
                BackgroundType.Quality => Path.Combine(wallpaperPath, "wallpapersQ.jpg"),
                BackgroundType.Extra => Path.Combine(wallpaperPath, "wallpapersE.jpg"),
                _ => Path.Combine(wallpaperPath, "wallpapers.jpg")
            };
        }

        /// <summary>
        /// Validasi file background — jalan di background thread
        /// </summary>
        public static Task<bool> ValidateBackgroundFilesAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    bool allExist = true;

                    foreach (BackgroundType type in Enum.GetValues(typeof(BackgroundType)))
                    {
                        if (type == BackgroundType.Custom)
                            continue; // custom path divalidasi pas dipake, bukan di sini

                        string? imagePath = GetImagePath(type);

                        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                        {
                            App.Logger.WriteLine(LOG_IDENT, $"Missing background image: {type} at {imagePath}");
                            allExist = false;
                        }
                    }

                    if (allExist)
                        App.Logger.WriteLine(LOG_IDENT, "All built-in background images validated successfully");

                    return allExist;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Error validating background files: {ex.Message}");
                    return false;
                }
            });
        }

        /// <summary>
        /// Hapus cache — panggil kalo user ganti wallpaper
        /// </summary>
        public static void ClearCache()
        {
            _imageCache.Clear();
            _customCache = null;
        }
    }
}
