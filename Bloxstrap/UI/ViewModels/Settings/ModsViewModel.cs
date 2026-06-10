using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Microsoft.Win32;

using Windows.Win32;
using Windows.Win32.UI.Shell;
using Windows.Win32.Foundation;

using CommunityToolkit.Mvvm.Input;

using Bloxstrap.Models.SettingTasks;
using Bloxstrap.AppData;
using Bloxstrap.Integrations;

namespace Bloxstrap.UI.ViewModels.Settings
{
    public class ModsViewModel : NotifyPropertyChangedViewModel
    {
        private ImageSource? _backgroundPreview;
        private ImageSource? _loadingScreenPreview;
        private string _wallpaperStatus = string.Empty;

        public ImageSource? BackgroundPreview
        {
            get => _backgroundPreview;
            set
            {
                _backgroundPreview = value;
                OnPropertyChanged(nameof(BackgroundPreview));
                OnPropertyChanged(nameof(BackgroundPreviewVisibility));
            }
        }

        public ImageSource? LoadingScreenPreview
        {
            get => _loadingScreenPreview;
            set
            {
                _loadingScreenPreview = value;
                OnPropertyChanged(nameof(LoadingScreenPreview));
                OnPropertyChanged(nameof(LoadingScreenPreviewVisibility));
            }
        }

        public string WallpaperStatus
        {
            get => _wallpaperStatus;
            set
            {
                if (_wallpaperStatus != value)
                {
                    _wallpaperStatus = value;
                    OnPropertyChanged(nameof(WallpaperStatus));
                }
            }
        }

        public bool EnableWallpaperLauncher
        {
            get => App.Settings.Prop.EnableWallpaperLauncher;
            set
            {
                App.Settings.Prop.EnableWallpaperLauncher = value;
                OnPropertyChanged(nameof(EnableWallpaperLauncher));
            }
        }

        public Visibility BackgroundPreviewVisibility => BackgroundPreview != null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility LoadingScreenPreviewVisibility => LoadingScreenPreview != null ? Visibility.Visible : Visibility.Collapsed;

        private void OpenModsFolder() => Process.Start("explorer.exe", Paths.Modifications);

        private readonly Dictionary<string, byte[]> FontHeaders = new()
        {
            { "ttf", new byte[4] { 0x00, 0x01, 0x00, 0x00 } },
            { "otf", new byte[4] { 0x4F, 0x54, 0x54, 0x4F } },
            { "ttc", new byte[4] { 0x74, 0x74, 0x63, 0x66 } } 
        };

        private void ManageCustomFont()
        {
            if (!String.IsNullOrEmpty(TextFontTask.NewState))
            {
                TextFontTask.NewState = "";
            }
            else
            {
                var dialog = new OpenFileDialog
                {
                    Filter = $"{Strings.Menu_FontFiles}|*.ttf;*.otf;*.ttc"
                };

                if (dialog.ShowDialog() != true)
                    return;

                string type = dialog.FileName.Substring(dialog.FileName.Length-3, 3).ToLowerInvariant();

                if (!FontHeaders.ContainsKey(type) 
                    || !FontHeaders.Any(x => File.ReadAllBytes(dialog.FileName).Take(4).SequenceEqual(x.Value)))
                {
                    Frontend.ShowMessageBox(Strings.Menu_Mods_Misc_CustomFont_Invalid, MessageBoxImage.Error);
                    return;
                }

                TextFontTask.NewState = dialog.FileName;
            }

            OnPropertyChanged(nameof(ChooseCustomFontVisibility));
            OnPropertyChanged(nameof(DeleteCustomFontVisibility));
        }

        public ICommand OpenModsFolderCommand => new RelayCommand(OpenModsFolder);

        public Visibility ChooseCustomFontVisibility => !String.IsNullOrEmpty(TextFontTask.NewState) ? Visibility.Collapsed : Visibility.Visible;

        public Visibility DeleteCustomFontVisibility => !String.IsNullOrEmpty(TextFontTask.NewState) ? Visibility.Visible : Visibility.Collapsed;

        public ICommand ManageCustomFontCommand => new RelayCommand(ManageCustomFont);

        public ModsViewModel()
        {
            // Restore custom background from settings if it exists
            if (!String.IsNullOrEmpty(App.Settings.Prop.CustomBackgroundPath) && File.Exists(App.Settings.Prop.CustomBackgroundPath))
            {
                BackgroundTask.NewState = App.Settings.Prop.CustomBackgroundPath;
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(App.Settings.Prop.CustomBackgroundPath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 400;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    
                    BackgroundPreview = bitmap;
                }
                catch { }
            }

            SelectWallpaper1Command = new AsyncRelayCommand(SelectWallpaper1);
            SelectWallpaper2Command = new AsyncRelayCommand(SelectWallpaper2);
            SelectWallpaper3Command = new AsyncRelayCommand(SelectWallpaper3);
            SelectWallpaper4Command = new AsyncRelayCommand(SelectWallpaper4);

            WallpaperStatus = App.Settings.Prop.EnableWallpaperLauncher
                ? "✓ Background acak aktif setiap aplikasi dibuka."
                : "Background acak tidak aktif.";
        }

        public IAsyncRelayCommand SelectWallpaper1Command { get; }
        public IAsyncRelayCommand SelectWallpaper2Command { get; }
        public IAsyncRelayCommand SelectWallpaper3Command { get; }
        public IAsyncRelayCommand SelectWallpaper4Command { get; }

        private Task SelectWallpaper1() => ApplyWallpaper(AppBackgroundService.BackgroundType.Default);
        private Task SelectWallpaper2() => ApplyWallpaper(AppBackgroundService.BackgroundType.Cool);
        private Task SelectWallpaper3() => ApplyWallpaper(AppBackgroundService.BackgroundType.Quality);
        private Task SelectWallpaper4() => ApplyWallpaper(AppBackgroundService.BackgroundType.Extra);

        private async Task ApplyWallpaper(AppBackgroundService.BackgroundType type)
        {
            await Task.Run(() =>
            {
                try
                {
                    var img = AppBackgroundService.GetBackground(type);
                    WallpaperStatus = img != null
                        ? $"✓ Background diubah ke: {type}"
                        : $"✗ Gagal memuat background: {type}";
                }
                catch (Exception ex)
                {
                    WallpaperStatus = $"✗ Error: {ex.Message}";
                }
            });
        }

        private void ManageCustomBackground()
        {
            if (!String.IsNullOrEmpty(BackgroundTask.NewState))
            {
                BackgroundTask.NewState = "";
                BackgroundPreview = null;
                App.Settings.Prop.CustomBackgroundPath = "";
            }
            else
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp"
                };

                if (dialog.ShowDialog() != true)
                    return;

                try
                {
                    using var img = System.Drawing.Image.FromFile(dialog.FileName);
                    BackgroundTask.NewState = dialog.FileName;
                    App.Settings.Prop.CustomBackgroundPath = dialog.FileName;
                    
                    // Load preview image
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(dialog.FileName);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 400; // Resize untuk preview
                    bitmap.EndInit();
                    bitmap.Freeze();
                    
                    BackgroundPreview = bitmap;
                }
                catch
                {
                    Frontend.ShowMessageBox(Strings.Menu_Mods_Misc_CustomBackground_Invalid, MessageBoxImage.Error);
                    return;
                }
            }

            OnPropertyChanged(nameof(ChooseCustomBackgroundVisibility));
            OnPropertyChanged(nameof(DeleteCustomBackgroundVisibility));
        }

        private void ManageCustomLoadingScreen()
        {
            if (!String.IsNullOrEmpty(LoadingScreenTask.NewState))
            {
                LoadingScreenTask.NewState = "";
                LoadingScreenPreview = null;
            }
            else
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp"
                };

                if (dialog.ShowDialog() != true)
                    return;

                try
                {
                    using var img = System.Drawing.Image.FromFile(dialog.FileName);
                    LoadingScreenTask.NewState = dialog.FileName;
                    
                    // Load preview image
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(dialog.FileName);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 400; // Resize untuk preview
                    bitmap.EndInit();
                    bitmap.Freeze();
                    
                    LoadingScreenPreview = bitmap;
                }
                catch
                {
                    Frontend.ShowMessageBox(Strings.Menu_Mods_Misc_CustomLoadingScreen_Invalid, MessageBoxImage.Error);
                    return;
                }
            }

            OnPropertyChanged(nameof(ChooseCustomLoadingScreenVisibility));
            OnPropertyChanged(nameof(DeleteCustomLoadingScreenVisibility));
        }

        public Visibility ChooseCustomBackgroundVisibility => !String.IsNullOrEmpty(BackgroundTask.NewState) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility DeleteCustomBackgroundVisibility => !String.IsNullOrEmpty(BackgroundTask.NewState) ? Visibility.Visible : Visibility.Collapsed;
        public ICommand ManageCustomBackgroundCommand => new RelayCommand(ManageCustomBackground);

        public Visibility ChooseCustomLoadingScreenVisibility => !String.IsNullOrEmpty(LoadingScreenTask.NewState) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility DeleteCustomLoadingScreenVisibility => !String.IsNullOrEmpty(LoadingScreenTask.NewState) ? Visibility.Visible : Visibility.Collapsed;
        public ICommand ManageCustomLoadingScreenCommand => new RelayCommand(ManageCustomLoadingScreen);

        public BackgroundModPresetTask BackgroundTask { get; } = new();
        public LoadingScreenModPresetTask LoadingScreenTask { get; } = new();

        public ICommand OpenCompatSettingsCommand => new RelayCommand(OpenCompatSettings);

        public ModPresetTask OldAvatarBackgroundTask { get; } = new("OldAvatarBackground", @"ExtraContent\places\Mobile.rbxl", "OldAvatarBackground.rbxl");

        public ModPresetTask OldCharacterSoundsTask { get; } = new("OldCharacterSounds", new()
        {
            { @"content\sounds\action_footsteps_plastic.mp3", "Sounds.OldWalk.mp3"  },
            { @"content\sounds\action_jump.mp3",              "Sounds.OldJump.mp3"  },
            { @"content\sounds\action_get_up.mp3",            "Sounds.OldGetUp.mp3" },
            { @"content\sounds\action_falling.mp3",           "Sounds.Empty.mp3"    },
            { @"content\sounds\action_jump_land.mp3",         "Sounds.Empty.mp3"    },
            { @"content\sounds\action_swim.mp3",              "Sounds.Empty.mp3"    },
            { @"content\sounds\impact_water.mp3",             "Sounds.Empty.mp3"    }
        });

        public EmojiModPresetTask EmojiFontTask { get; } = new();

        public EnumModPresetTask<Enums.CursorType> CursorTypeTask { get; } = new("CursorType", new()
        {
            {
                Enums.CursorType.From2006, new()
                {
                    { @"content\textures\Cursors\KeyboardMouse\ArrowCursor.png",    "Cursor.From2006.ArrowCursor.png"    },
                    { @"content\textures\Cursors\KeyboardMouse\ArrowFarCursor.png", "Cursor.From2006.ArrowFarCursor.png" }
                }
            },
            {
                Enums.CursorType.From2013, new()
                {
                    { @"content\textures\Cursors\KeyboardMouse\ArrowCursor.png",    "Cursor.From2013.ArrowCursor.png"    },
                    { @"content\textures\Cursors\KeyboardMouse\ArrowFarCursor.png", "Cursor.From2013.ArrowFarCursor.png" }
                }
            }
        });

        public FontModPresetTask TextFontTask { get; } = new();

        private void OpenCompatSettings()
        {
            string path = new RobloxPlayerData().ExecutablePath;

            if (File.Exists(path))
                PInvoke.SHObjectProperties(HWND.Null, SHOP_TYPE.SHOP_FILEPATH, path, "Compatibility");
            else
                Frontend.ShowMessageBox(Strings.Common_RobloxNotInstalled, MessageBoxImage.Error);

        }
    }
}
