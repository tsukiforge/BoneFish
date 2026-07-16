using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using Bloxstrap.Integrations;

namespace Bloxstrap.UI.ViewModels.Settings
{
    public class ExperimentalViewModel : NotifyPropertyChangedViewModel
    {
        // ── Wallpaper Properties ────────────────────────────────────────────────
        private BitmapImage? _backgroundImage;
        public BitmapImage? BackgroundImage
        {
            get => _backgroundImage;
            set { _backgroundImage = value; OnPropertyChanged(nameof(BackgroundImage)); }
        }

        private bool _isBackgroundLoading;
        public bool IsBackgroundLoading
        {
            get => _isBackgroundLoading;
            set { _isBackgroundLoading = value; OnPropertyChanged(nameof(IsBackgroundLoading)); }
        }

        public bool BackgroundRandomMode
        {
            get => App.Settings.Prop.BackgroundRandomMode;
            set
            {
                App.Settings.Prop.BackgroundRandomMode = value;
                OnPropertyChanged(nameof(BackgroundRandomMode));
                try { App.Settings.Save(); } catch { }

                // Kalo random mode diaktifkan, langsung ganti random
                if (value && EnableWallpaperLauncher)
                    _ = LoadRandomBackgroundAsync();
            }
        }

        public bool EnableWallpaperLauncher
        {
            get => App.Settings.Prop.EnableWallpaperLauncher;
            set
            {
                App.Settings.Prop.EnableWallpaperLauncher = value;
                OnPropertyChanged(nameof(EnableWallpaperLauncher));
                try { App.Settings.Save(); } catch { }

                if (value)
                {
                    // Load sesuai mode: random atau saved
                    if (App.Settings.Prop.BackgroundRandomMode || string.IsNullOrEmpty(App.Settings.Prop.SelectedBackgroundType))
                        _ = LoadRandomBackgroundAsync();
                    else
                        _ = LoadSavedBackgroundAsync();
                }
                else
                    BackgroundImage = null;
            }
        }

        public async Task LoadRandomBackgroundAsync()
        {
            if (!EnableWallpaperLauncher)
                return;

            IsBackgroundLoading = true;
            try
            {
                BackgroundImage = await AppBackgroundService.GetRandomBackgroundAsync();
                
                // Save ke Settings biar kalo random mode mati, background tetap
                App.Settings.Prop.SelectedBackgroundType = "Random";
                try { App.Settings.Save(); } catch { }
            }
            finally
            {
                IsBackgroundLoading = false;
            }
        }

        public async Task LoadSavedBackgroundAsync()
        {
            if (!EnableWallpaperLauncher)
                return;

            string savedType = App.Settings.Prop.SelectedBackgroundType;
            if (string.IsNullOrEmpty(savedType) || savedType == "Random")
            {
                // Belum pernah milih atau mode random → random
                await LoadRandomBackgroundAsync();
                return;
            }

            IsBackgroundLoading = true;
            try
            {
                if (App.Settings.Prop.BackgroundRandomMode)
                {
                    // Random mode aktif: load random aja
                    await LoadRandomBackgroundAsync();
                }
                else if (savedType == "Custom")
                {
                    BackgroundImage = await AppBackgroundService.GetCustomBackgroundAsync();
                }
                else if (Enum.TryParse<AppBackgroundService.BackgroundType>(savedType, out var parsedType))
                {
                    BackgroundImage = await AppBackgroundService.GetBackgroundAsync(parsedType);
                }
                else
                {
                    await LoadRandomBackgroundAsync();
                }
            }
            finally
            {
                IsBackgroundLoading = false;
            }
        }

        public async Task SelectBackground(AppBackgroundService.BackgroundType type)
        {
            IsBackgroundLoading = true;
            try
            {
                BackgroundImage = await AppBackgroundService.GetBackgroundAsync(type);
                
                // Persistence: simpan pilihan user
                App.Settings.Prop.SelectedBackgroundType = type.ToString();
                App.Settings.Prop.BackgroundRandomMode = false;
                try { App.Settings.Save(); } catch { }
            }
            finally
            {
                IsBackgroundLoading = false;
            }
        }

        // ── ICommand for wallpaper background selector ───────────────────────────
        public ICommand SelectWallpaperDefaultCommand { get; }
        public ICommand SelectWallpaperCoolCommand { get; }
        public ICommand SelectWallpaperQualityCommand { get; }
        public ICommand SelectWallpaperExtraCommand { get; }
        public ICommand BrowseCustomBackgroundCommand { get; }

        private async void OnSelectWallpaperDefault() => await SelectBackground(AppBackgroundService.BackgroundType.Default);
        private async void OnSelectWallpaperCool() => await SelectBackground(AppBackgroundService.BackgroundType.Cool);
        private async void OnSelectWallpaperQuality() => await SelectBackground(AppBackgroundService.BackgroundType.Quality);
        private async void OnSelectWallpaperExtra() => await SelectBackground(AppBackgroundService.BackgroundType.Extra);

        private void OnBrowseCustomBackground()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Pilih gambar background kustom",
                    Filter = "File gambar (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png|Semua file (*.*)|*.*",
                    CheckFileExists = true,
                    Multiselect = false
                };

                if (dialog.ShowDialog() == true)
                {
                    string selectedPath = dialog.FileName;
                    App.Settings.Prop.CustomBackgroundPath = selectedPath;
                    App.Settings.Prop.SelectedBackgroundType = "Custom";
                    App.Settings.Prop.BackgroundRandomMode = false;
                    try { App.Settings.Save(); } catch { }

                    // Clear cache biar load ulang
                    AppBackgroundService.ClearCache();

                    // Load selected custom background
                    _ = SelectBackground(AppBackgroundService.BackgroundType.Custom);
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("ExperimentalViewModel", $"Browse custom background error: {ex.Message}");
            }
        }
        public bool EnableSystemTrayOnClose
        {
            get => App.Settings.Prop.EnableSystemTrayOnClose;
            set
            {
                App.Settings.Prop.EnableSystemTrayOnClose = value;
                OnPropertyChanged(nameof(EnableSystemTrayOnClose));
                try { App.Settings.Save(); } catch { }
            }
        }

        public bool EnableRobloxNotifications
        {
            get => App.Settings.Prop.EnableRobloxNotifications;
            set
            {
                App.Settings.Prop.EnableRobloxNotifications = value;
                OnPropertyChanged(nameof(EnableRobloxNotifications));
                try { App.Settings.Save(); } catch { }
            }
        }

        public bool EnableFriendOnlineNotifications
        {
            get => App.Settings.Prop.EnableFriendOnlineNotifications;
            set
            {
                App.Settings.Prop.EnableFriendOnlineNotifications = value;
                OnPropertyChanged(nameof(EnableFriendOnlineNotifications));
                try { App.Settings.Save(); } catch { }
            }
        }

        public bool EnableNotificationSound
        {
            get => App.Settings.Prop.EnableNotificationSound;
            set
            {
                App.Settings.Prop.EnableNotificationSound = value;
                OnPropertyChanged(nameof(EnableNotificationSound));
                try { App.Settings.Save(); } catch { }
            }
        }

        public bool IsRunningAsAdmin => Utilities.IsRunningAsAdmin();

        public Visibility FpsAdminInfoVisibility =>
            EnableFpsMonitor && !IsRunningAsAdmin ? Visibility.Visible : Visibility.Collapsed;

        public bool EnableFpsMonitor
        {
            get => App.Settings.Prop.EnableFpsMonitor;
            set
            {
                App.Settings.Prop.EnableFpsMonitor = value;
                OnPropertyChanged(nameof(EnableFpsMonitor));
                OnPropertyChanged(nameof(FpsAdminInfoVisibility));
                try { App.Settings.Save(); } catch { }
            }
        }

        public bool OptimizeForLowEnd
        {
            get => App.Settings.Prop.OptimizeForLowEnd;
            set
            {
                App.Settings.Prop.OptimizeForLowEnd = value;
                OnPropertyChanged(nameof(OptimizeForLowEnd));
                try { App.Settings.Save(); } catch { }
            }
        }

        public bool EnableHotkeys
        {
            get => App.Settings.Prop.EnableHotkeys;
            set
            {
                App.Settings.Prop.EnableHotkeys = value;
                OnPropertyChanged(nameof(EnableHotkeys));
                try { App.Settings.Save(); } catch { }
            }
        }

        public bool EnableCrosshair
        {
            get => App.Settings.Prop.EnableCrosshair;
            set
            {
                App.Settings.Prop.EnableCrosshair = value;
                OnPropertyChanged(nameof(EnableCrosshair));
                try { App.Settings.Save(); } catch { }
                
                // Notify CrosshairService to apply/remove overlay
                CrosshairService.Instance?.ApplySettings();
            }
        }

        public string CrosshairStyle
        {
            get => App.Settings.Prop.CrosshairStyle;
            set
            {
                App.Settings.Prop.CrosshairStyle = value;
                OnPropertyChanged(nameof(CrosshairStyle));
                try { App.Settings.Save(); } catch { }
                
                CrosshairService.Instance?.ApplySettings();
            }
        }

        public double CrosshairSize
        {
            get => App.Settings.Prop.CrosshairSize;
            set
            {
                App.Settings.Prop.CrosshairSize = value;
                OnPropertyChanged(nameof(CrosshairSize));
                try { App.Settings.Save(); } catch { }
                
                CrosshairService.Instance?.ApplySettings();
            }
        }

        public double CrosshairOpacity
        {
            get => App.Settings.Prop.CrosshairOpacity;
            set
            {
                App.Settings.Prop.CrosshairOpacity = value;
                OnPropertyChanged(nameof(CrosshairOpacity));
                try { App.Settings.Save(); } catch { }
                
                CrosshairService.Instance?.ApplySettings();
            }
        }

        // Available crosshair styles for the ComboBox
        public string[] CrosshairStyles { get; } = new[] { "Cross", "Dot", "Circle", "CrossDot" };

        public ICommand SelectCrosshairColorCommand { get; }

        private void SelectCrosshairColor(object? param)
        {
            if (param is string color && !string.IsNullOrEmpty(color))
            {
                App.Settings.Prop.CrosshairColor = color;
                try { App.Settings.Save(); } catch { }
                CrosshairService.Instance?.ApplySettings();
            }
        }

        public bool EnableTurboMode
        {
            get => App.Settings.Prop.EnableTurboMode;
            set
            {
                App.Settings.Prop.EnableTurboMode = value;
                OnPropertyChanged(nameof(EnableTurboMode));

                if (value)
                {
                    // Turbo Mode ON: force extreme optimizations
                    App.Settings.Prop.OptimizeForLowEnd = true;
                    App.Settings.Prop.ForceExtremeMode = true;
                    OnPropertyChanged(nameof(OptimizeForLowEnd));

                    AutoOptimizeService.ApplyAggressiveOptimizations(AutoOptimizeService.SystemTier.ExtremePerformance);
                }
                else
                {
                    // Turbo Mode OFF: restore normal settings
                    App.Settings.Prop.OptimizeForLowEnd = false;
                    App.Settings.Prop.ForceExtremeMode = false;
                    OnPropertyChanged(nameof(OptimizeForLowEnd));

                    AutoOptimizeService.RemoveOptimizations();
                }

                try { App.Settings.Save(); } catch { }

                if (App.FastFlags.Changed)
                    App.FastFlags.Save();
            }
        }

        public ExperimentalViewModel()
        {
            SelectCrosshairColorCommand = new RelayCommand<string?>(SelectCrosshairColor);
            SelectWallpaperDefaultCommand = new RelayCommand(OnSelectWallpaperDefault);
            SelectWallpaperCoolCommand = new RelayCommand(OnSelectWallpaperCool);
            SelectWallpaperQualityCommand = new RelayCommand(OnSelectWallpaperQuality);
            SelectWallpaperExtraCommand = new RelayCommand(OnSelectWallpaperExtra);
            BrowseCustomBackgroundCommand = new RelayCommand(OnBrowseCustomBackground);

            // Auto-load wallpaper pas halaman ini di-load
            // Pake persistence: kalo random mode ON atau belom pernah milih → random
            // Kalo ada saved type → load sesuai saved type
            if (App.Settings.Prop.EnableWallpaperLauncher)
            {
                if (App.Settings.Prop.BackgroundRandomMode || string.IsNullOrEmpty(App.Settings.Prop.SelectedBackgroundType))
                    _ = LoadRandomBackgroundAsync();
                else
                    _ = LoadSavedBackgroundAsync();
            }
        }
    }
}
