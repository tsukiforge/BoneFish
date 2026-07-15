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

        public enum BackgroundType
        {
            Default = 0,
            Cool = 1,
            Quality = 2,
            Extra = 3
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

                    string wallpaperPath = Paths.ResolveWallpapersDir();

                    string imagePath = type switch
                    {
                        BackgroundType.Default => Path.Combine(wallpaperPath, "wallpapers.jpg"),
                        BackgroundType.Cool => Path.Combine(wallpaperPath, "wallpapersC.jpg"),
                        BackgroundType.Quality => Path.Combine(wallpaperPath, "wallpapersQ.jpg"),
                        BackgroundType.Extra => Path.Combine(wallpaperPath, "wallpapersE.jpg"),
                        _ => Path.Combine(wallpaperPath, "wallpapers.jpg")
                    };

                    if (!File.Exists(imagePath))
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Background image not found: {imagePath}");
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
        /// Get random background ASYNC
        /// </summary>
        public static async Task<BitmapImage?> GetRandomBackgroundAsync()
        {
            try
            {
                var backgroundTypes = Enum.GetValues(typeof(BackgroundType)).Cast<BackgroundType>().ToList();
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
        /// Get specific background ASYNC
        /// </summary>
        public static Task<BitmapImage?> GetBackgroundAsync(BackgroundType type)
        {
            return GetBackgroundImageAsync(type);
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
                        string wallpaperPath = Paths.ResolveWallpapersDir();
                        string imagePath = type switch
                        {
                            BackgroundType.Default => Path.Combine(wallpaperPath, "wallpapers.jpg"),
                            BackgroundType.Cool => Path.Combine(wallpaperPath, "wallpapersC.jpg"),
                            BackgroundType.Quality => Path.Combine(wallpaperPath, "wallpapersQ.jpg"),
                            BackgroundType.Extra => Path.Combine(wallpaperPath, "wallpapersE.jpg"),
                            _ => Path.Combine(wallpaperPath, "wallpapers.jpg")
                        };

                        if (!File.Exists(imagePath))
                        {
                            App.Logger.WriteLine(LOG_IDENT, $"Missing background image: {type} at {imagePath}");
                            allExist = false;
                        }
                    }

                    if (allExist)
                        App.Logger.WriteLine(LOG_IDENT, "All background images validated successfully");

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
        }
    }
}
