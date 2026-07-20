using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

using Wpf.Ui.Controls.Interfaces;
using Wpf.Ui.Mvvm.Contracts;

using Bloxstrap.UI.ViewModels.Settings;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;
using Bloxstrap.UI.Elements.Settings.Pages;
using Bloxstrap.Integrations;

namespace Bloxstrap.UI.Elements.Settings
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : INavigationWindow
    {
        private Models.Persistable.WindowState _state => App.State.Prop.SettingsWindow;

        public MainWindow(bool showAlreadyRunningWarning)
        {
            var viewModel = new MainWindowViewModel();

            viewModel.RequestSaveNoticeEvent += (_, _) => SettingsSavedSnackbar.Show();
            viewModel.RequestCloseWindowEvent += (_, _) => Close();

            DataContext = viewModel;

            InitializeComponent();

            if (showAlreadyRunningWarning)
                ShowAlreadyRunningSnackbar();

            gbs.Opacity = viewModel.GBSEnabled ? 1 : 0.5;
            gbs.IsEnabled = viewModel.GBSEnabled; // binding doesnt work as expected so we are setting it in here instead

            LoadState();

            string? lastPageName = App.State.Prop.LastPage;
            Type? lastPage = lastPageName is null ? null : Type.GetType(lastPageName);

            App.RemoteData.Subscribe((object? sender, EventArgs e) => {
                RemoteDataBase Data = App.RemoteData.Prop;

                AlertBar.Visibility = Data.AlertEnabled ? Visibility.Visible : Visibility.Collapsed;
                AlertBar.Message = Data.AlertContent;
                AlertBar.Severity = Data.AlertSeverity;

                if (Data.KillFlags)
                    fastflags.PageType = typeof(FastFlagsDisabled);
            });

            // ★ GLOBAL WALLPAPER: Subscribe ke event perubahan background
            // Agar wallpaper muncul di semua halaman + navigasi
            // ★ FIX 3: Background selalu aktif — langsung load tanpa cek EnableWallpaperLauncher
            AppBackgroundService.BackgroundChanged += OnGlobalBackgroundChanged;
            LoadSavedGlobalBackground();

            if (lastPage != null)
                SafeNavigate(lastPage);

            RootNavigation.Navigated += OnNavigation!;

            void OnNavigation(object? sender, RoutedNavigationEventArgs e)
            {
                INavigationItem? currentPage = RootNavigation.Current;

                App.State.Prop.LastPage = currentPage?.PageType.FullName!;
            }
        }

        public void LoadState()
        {
            if (_state.Left > SystemParameters.VirtualScreenWidth)
                _state.Left = 0;

            if (_state.Top > SystemParameters.VirtualScreenHeight)
                _state.Top = 0;

            if (_state.Width > 0)
                this.Width = _state.Width;

            if (_state.Height > 0)
                this.Height = _state.Height;

            if (_state.Left > 0 && _state.Top > 0)
            {
                this.WindowStartupLocation = WindowStartupLocation.Manual;
                this.Left = _state.Left;
                this.Top = _state.Top;
            }
        }

        // ── Global Wallpaper Event Handler ─────────────────────────────────────
        private void OnGlobalBackgroundChanged(object? sender, EventArgs e)
        {
            try
            {
                var bg = AppBackgroundService.CurrentBackgroundImage;
                if (bg != null)
                {
                    GlobalWallpaperBrush.ImageSource = bg;
                }
                else
                {
                    GlobalWallpaperBrush.ImageSource = null;
                }
            }
            catch { }
        }

        /// <summary>
        /// Load saved global background saat startup (tanpa menunggu ViewModel).
        /// </summary>
        private async void LoadSavedGlobalBackground()
        {
            try
            {
                string savedType = App.Settings.Prop.SelectedBackgroundType;
                if (string.IsNullOrEmpty(savedType) || savedType == "Random" || App.Settings.Prop.BackgroundRandomMode)
                {
                    var bg = await AppBackgroundService.GetRandomBackgroundAsync();
                    AppBackgroundService.SetGlobalBackground(bg);
                }
                else if (savedType == "Custom")
                {
                    var bg = await AppBackgroundService.GetCustomBackgroundAsync();
                    AppBackgroundService.SetGlobalBackground(bg);
                }
                else if (Enum.TryParse<AppBackgroundService.BackgroundType>(savedType, out var parsedType))
                {
                    var bg = await AppBackgroundService.GetBackgroundAsync(parsedType);
                    AppBackgroundService.SetGlobalBackground(bg);
                }
            }
            catch { }
        }

        private async void SafeNavigate(Type page)
        {
            await Task.Delay(500); // same as below

            if (page == typeof(GlobalSettingsPage) && !App.GlobalSettings.Loaded)
                return; // prevent from navigating onto disabled page

            Navigate(page);
        }

        private async void ShowAlreadyRunningSnackbar()
        {
            await Task.Delay(500); // wait for everything to finish loading
            AlreadyRunningSnackbar.Show();
        }

        #region INavigationWindow methods

        public Frame GetFrame() => RootFrame;

        public INavigation GetNavigation() => RootNavigation;

        public bool Navigate(Type pageType) => RootNavigation.Navigate(pageType);

        public void SetPageService(IPageService pageService) => RootNavigation.PageService = pageService;

        public void ShowWindow() => Show();

        public void CloseWindow() => Close();

        #endregion INavigationWindow methods

        private void WpfUiWindow_Closing(object sender, CancelEventArgs e)
        {
            if (App.FastFlags.Changed || App.PendingSettingTasks.Any())
            {
                var result = Frontend.ShowMessageBox(Strings.Menu_UnsavedChanges, MessageBoxImage.Warning, MessageBoxButton.YesNo);

                if (result != MessageBoxResult.Yes)
                    e.Cancel = true;
            }

            // Save settings sebelum window benar-benar close
            // App.Settings.Save() idempotent — aman dipanggil berkali-kali
            if (!e.Cancel)
            {
                try { App.Settings.Save(); } catch { }
            }

            _state.Width = this.Width;
            _state.Height = this.Height;

            _state.Top = this.Top;
            _state.Left = this.Left;

            App.State.Save();
        }

        private void WpfUiWindow_Closed(object sender, EventArgs e)
        {
            if (App.LaunchSettings.TestModeFlag.Active)
                LaunchHandler.LaunchRoblox(LaunchMode.Player);
            else
                App.SoftTerminate();
        }
    }
}