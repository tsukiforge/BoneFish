namespace Bloxstrap
{
    static class Paths
    {
        // note that these are directories that aren't tethered to the basedirectory
        // so these can safely be called before initialization
        public static string Temp => Path.Combine(Path.GetTempPath(), App.ProjectName);
        public static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        public static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        public static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        public static string WindowsStartMenu => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
        public static string System => Environment.GetFolderPath(Environment.SpecialFolder.System);

        public static string Process => Environment.ProcessPath!;

        public static string TempUpdates => Path.Combine(Temp, "Updates");
        public static string TempLogs => Path.Combine(Temp, "Logs");

        public static string Base { get; private set; } = "";
        public static string Downloads { get; private set; } = "";

        public static string SavedFlagProfiles { get; private set; } = "";
        public static string Logs { get; private set; } = "";
        public static string Integrations { get; private set; } = "";
        public static string Versions { get; private set; } = "";
        public static string Modifications { get; private set; } = "";
        public static string Roblox { get; private set; } = "";
        public static string CustomThemes { get; private set; } = "";

        // cleaner paths
        public static string RobloxLogs { get; private set; } = "";
        public static string RobloxCache { get; private set; } = "";

        public static string Application { get; private set; } = "";

        public static string WallpapersDir => Path.Combine(Base, "Resources", "Wallpapers");

        // Folder untuk custom background images milik user — fallback jika CustomBackgroundPath kosong
        public static string CustomImagesDir => Path.Combine(Base, "images", "img");

        /// <summary>
        /// Scan folder CustomImagesDir (images/img/) dan return semua file gambar yang ditemukan
        /// </summary>
        public static string[] GetCustomUserImages()
        {
            try
            {
                if (!Directory.Exists(CustomImagesDir))
                {
                    Directory.CreateDirectory(CustomImagesDir);
                    return Array.Empty<string>();
                }

                return Directory.GetFiles(CustomImagesDir, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        public static string ResolveWallpapersDir()
        {
            // Coba beberapa lokasi yang mungkin
            string[] possiblePaths = new[]
            {
                Path.Combine(Base, "Resources", "Wallpapers"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Wallpapers"),
                Path.Combine(Path.GetDirectoryName(Paths.Process)!, "Resources", "Wallpapers")
            };

            foreach (var path in possiblePaths)
            {
                if (Directory.Exists(path))
                    return path;
            }

            // Fallback ke lokasi Base
            return Path.Combine(Base, "Resources", "Wallpapers");
        }

        public static string CustomFont => Path.Combine(Modifications, "content\\fonts\\CustomFont.ttf");
        public static string CustomBackground => Path.Combine(Modifications, "content\\textures\\ui\\Shell\\CustomBackground.png");
        public static string CustomLoadingScreen => Path.Combine(Modifications, "content\\textures\\loading\\CustomLoading.png");

        public static bool Initialized => !String.IsNullOrEmpty(Base);

        public static void Initialize(string baseDirectory)
        {
            Base = baseDirectory;
            Downloads = Path.Combine(Base, "Downloads");
            Logs = Path.Combine(Base, "Logs");
            Integrations = Path.Combine(Base, "Integrations");
            Versions = Path.Combine(Base, "Versions");
            Modifications = Path.Combine(Base, "Modifications");
            CustomThemes = Path.Combine(Base, "CustomThemes");
            Roblox = Path.Combine(LocalAppData, "Roblox"); // that was base before?

            RobloxLogs = Path.Combine(Roblox, "logs");
            RobloxCache = Path.Combine(Path.GetTempPath(), "Roblox");

            Application = Path.Combine(Base, $"{App.ProjectName}.exe");
        }
    }
}
