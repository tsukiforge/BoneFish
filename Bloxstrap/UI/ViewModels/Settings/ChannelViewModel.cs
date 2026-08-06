using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Bloxstrap.RobloxInterfaces;
using Bloxstrap.Integrations;

namespace Bloxstrap.UI.ViewModels.Settings
{
    public class ChannelViewModel : NotifyPropertyChangedViewModel
    {
        public event EventHandler<string>? RequestNotificationEvent;
        public event EventHandler<string>? RequestUpdateAvailableEvent;
        public event EventHandler? RequestHideUpdateAvailableEvent;

        public bool IsRobloxInstallationMissing => String.IsNullOrEmpty(App.RobloxState.Prop.Player.VersionGuid) && String.IsNullOrEmpty(App.RobloxState.Prop.Studio.VersionGuid);
        
        public ChannelViewModel()
        {
            Task.Run(() => LoadChannelDeployInfo(App.Settings.Prop.Channel));
        }
        
        private bool ValidateDomain(string domain)
        {
            const string domainPattern = @"^([a-zA-Z0-9.-]+)\.([a-zA-Z0-9]+)$";

            return Regex.IsMatch(domain, domainPattern);
        }

        private async Task LoadChannelDeployInfo(string channel)
        {
            ShowLoadingError = false;
            OnPropertyChanged(nameof(ShowLoadingError));

            ChannelInfoLoadingText = Strings.Menu_Channel_Switcher_Fetching;
            OnPropertyChanged(nameof(ChannelInfoLoadingText));

            ChannelDeployInfo = null;
            OnPropertyChanged(nameof(ChannelDeployInfo));

            try
            {
                bool isPrivate = await Deployment.IsChannelPrivate(channel);
                if (App.Cookies.Loaded && isPrivate && string.IsNullOrEmpty(Deployment.ChannelToken))
                {
                    UserChannel? userChannel = await Deployment.GetUserChannel("WindowsPlayer");

                    if (userChannel?.Token is not null)
                        Deployment.ChannelToken = userChannel.Token;
                }

                ClientVersion info = await Deployment.GetInfo(channel, true, true);

                ShowChannelWarning = info.IsBehindDefaultChannel;
                OnPropertyChanged(nameof(ShowChannelWarning));

                ChannelDeployInfo = new DeployInfo
                {
                    Version = info.Version,
                    VersionGuid = isPrivate ? "version-private" : info.VersionGuid, // we dont want to return the hash of private channels for obvious reason
                    Timestamp = info.Timestamp?.ToLocalTime().ToString() ?? "?"
                };

                App.State.Prop.IgnoreOutdatedChannel = true;

                OnPropertyChanged(nameof(ChannelDeployInfo));
            }
            catch (InvalidChannelException ex)
            {
                ShowLoadingError = true;
                OnPropertyChanged(nameof(ShowLoadingError));

                // channels that dont exist also throw HttpStatusCode.Unauthorized
                if (ex.StatusCode == HttpStatusCode.Unauthorized)
                    ChannelInfoLoadingText = Strings.Menu_Channel_Switcher_Unauthorized;
                else
                    ChannelInfoLoadingText = $"An http error has occured ({ex.StatusCode})"; // i dont think we need strings for errors

                OnPropertyChanged(nameof(ChannelInfoLoadingText));
            }
        }


        public bool ShowLoadingError { get; set; } = false;
        public bool ShowChannelWarning { get; set; } = false;

        public DeployInfo? ChannelDeployInfo { get; private set; } = null;
        public string ChannelInfoLoadingText { get; private set; } = null!;

        public string ViewChannel
        {
            get => App.Settings.Prop.Channel;
            set
            {
                value = value.Trim();
                Task.Run(() => LoadChannelDeployInfo(value));
                
                if (value.ToLower() == "live" || value.ToLower() == "zlive") // we are replacing those to prevent any issues
                {
                    App.Settings.Prop.Channel = Deployment.DefaultChannel;
                } else {
                    App.Settings.Prop.Channel = value;
                }
            }
        }

        public bool UpdateCheckingEnabled
        {
            get => App.Settings.Prop.CheckForUpdates;
            set => App.Settings.Prop.CheckForUpdates = value;
        }

        public ICommand CheckForUpdatesCommand => new RelayCommand(async () => await CheckForUpdates());

        public ICommand OpenReleasePageCommand => new RelayCommand(() => Utilities.ShellExecute(App.ProjectDownloadLink));

        public bool StaticDirectory
        {
            get => App.Settings.Prop.StaticDirectory;
            set => App.Settings.Prop.StaticDirectory = value;
        }

        public string RobloxDomain
        {
            get => App.Settings.Prop.RobloxDomain;
            set
            {
                if (ValidateDomain(value))
                    App.Settings.Prop.RobloxDomain = value;
                else
                    Frontend.ShowMessageBox("uh oh\nyit appeaws y-you'we entewed inwawid domain\npweasee dont mess with this as y-y-you cweawwy h-hawe nyo idea what youwe **wags my tail** doing!1~", System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxButton.OK);
            }
        }

        public IReadOnlyDictionary<string, ChannelChangeMode> ChannelChangeModes => new Dictionary<string, ChannelChangeMode>
        {
            { Strings.Menu_Channel_ChangeAction_Automatic, ChannelChangeMode.Automatic },
            { Strings.Menu_Channel_ChangeAction_Prompt, ChannelChangeMode.Prompt },
            { Strings.Menu_Channel_ChangeAction_Ignore, ChannelChangeMode.Ignore },
        };

        public string SelectedChannelChangeMode
        {
            get => ChannelChangeModes.FirstOrDefault(x => x.Value == App.Settings.Prop.ChannelChangeMode).Key;
            set => App.Settings.Prop.ChannelChangeMode = ChannelChangeModes[value];
        }

        public bool ForceRobloxReinstallation
        {
            get => App.State.Prop.ForceReinstall || IsRobloxInstallationMissing;
            set => App.State.Prop.ForceReinstall = value;
        }

        private async Task CheckForUpdates()
        {
            try
            {
                var releaseInfo = await App.GetLatestRelease();

                if (releaseInfo is null)
                {
                    Notify("Tidak bisa memeriksa pembaruan saat ini.");
                    RequestHideUpdateAvailableEvent?.Invoke(this, EventArgs.Empty);
                    return;
                }

                var versionComparison = Utilities.CompareVersions(App.Version, releaseInfo.TagName);

                if (App.IsProductionBuild && (versionComparison == VersionComparison.Equal || versionComparison == VersionComparison.GreaterThan))
                {
                    Notify("Fishstrap sudah versi terbaru.");
                    RequestHideUpdateAvailableEvent?.Invoke(this, EventArgs.Empty);
                    return;
                }

                RequestUpdateAvailableEvent?.Invoke(this, releaseInfo.TagName);
                Notify($"Pembaruan tersedia: {releaseInfo.TagName}. Klik tombol di bawah untuk membuka halaman rilis.");
            }
            catch
            {
                Notify("Gagal memeriksa pembaruan. Periksa koneksi internet dan coba lagi.");
                RequestHideUpdateAvailableEvent?.Invoke(this, EventArgs.Empty);
            }
        }

        private void Notify(string message) => RequestNotificationEvent?.Invoke(this, message);

        // ── FITUR 1: Clean Roblox Player & Studio ────────────────────────
        // ★ FIX freeze: delete folder Roblox (GB-an) sekarang dijalankan di
        // background thread (lihat RobloxCleanupService.CleanupAsync). Dialog
        // konfirmasi tetap di UI thread; hanya operasi disk yang dipindah.

        public ICommand CleanRobloxPlayerCommand => new AsyncRelayCommand(() => RobloxCleanupService.CleanPlayerAsync());

        public ICommand CleanRobloxStudioCommand => new AsyncRelayCommand(() => RobloxCleanupService.CleanStudioAsync());
    }
}
