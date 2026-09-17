using Bloxstrap.Integrations;
using Bloxstrap.UI.Elements.About;
using Bloxstrap.UI.Elements.ContextMenu;

namespace Bloxstrap.UI
{
    public class NotifyIconWrapper : IDisposable
    {
        // lol who needs properly structured mvvm and xaml when you have the absolute catastrophe that this is

        private bool _disposing = false;

        private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
        
        private readonly MenuContainer _menuContainer;
        
        private readonly Watcher _watcher;

        private System.Drawing.Icon? _updateIcon;
        private string? _updateReleaseUrl;

        private ActivityWatcher? _activityWatcher => _watcher.ActivityWatcher;

        EventHandler? _alertClickHandler;

        public NotifyIconWrapper(Watcher watcher)
        {
            App.Logger.WriteLine("NotifyIconWrapper::NotifyIconWrapper", "Initializing notification area icon");

            _watcher = watcher;

            _notifyIcon = new(new System.ComponentModel.Container())
            {
                Icon = Properties.Resources.IconBoneFish,
                Text = "BoneFish",
                Visible = true
            };

            _notifyIcon.MouseClick += MouseClickEventHandler;

            _ = CheckForUpdatesAsync();

            if (_activityWatcher is not null && App.Settings.Prop.ShowServerDetails)
                _activityWatcher.ShowNotif += ShowNotif;

            _menuContainer = new(_watcher);
            _menuContainer.Show();
        }

        #region Context menu
        public void MouseClickEventHandler(object? sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left && _updateReleaseUrl is not null)
            {
                Utilities.ShellExecute(_updateReleaseUrl);
                return;
            }

            if (e.Button != System.Windows.Forms.MouseButtons.Right)
                return;

            _menuContainer.Activate();
            _menuContainer.ContextMenu.IsOpen = true;
        }
        #endregion

        private async Task CheckForUpdatesAsync()
        {
            const string LOG_IDENT = "NotifyIconWrapper::CheckForUpdates";

            if (!App.Settings.Prop.CheckForUpdates)
            {
                App.Logger.WriteLine(LOG_IDENT, "Update check skipped because CheckForUpdates is disabled");
                return;
            }

            try
            {
                GithubRelease? primaryRelease = await App.GetLatestRelease(App.ProjectRepository);
                GithubRelease? secondaryRelease = await App.GetLatestRelease(App.SecondaryProjectRepository);

                App.Logger.WriteLine(
                    LOG_IDENT,
                    $"Local version: {App.Version}; " +
                    $"{App.ProjectRepository}: {primaryRelease?.TagName ?? "unavailable"}; " +
                    $"{App.SecondaryProjectRepository}: {secondaryRelease?.TagName ?? "unavailable"}");

                var releases = new[]
                {
                    (Repository: App.ProjectRepository, Release: primaryRelease),
                    (Repository: App.SecondaryProjectRepository, Release: secondaryRelease)
                }
                .Where(item => item.Release is not null)
                .Select(item => (item.Repository, Release: item.Release!))
                .Where(item => Version.TryParse(item.Release.TagName.TrimStart('v'), out _))
                .OrderByDescending(item => Utilities.GetVersionFromString(item.Release.TagName))
                .ToList();

                if (releases.Count == 0)
                {
                    App.Logger.WriteLine(LOG_IDENT, "No valid release found from either repository");
                    return;
                }

                var latest = releases[0];
                VersionComparison comparison = Utilities.CompareVersions(App.Version, latest.Release.TagName);
                if (comparison != VersionComparison.LessThan)
                {
                    App.Logger.WriteLine(
                        LOG_IDENT,
                        $"No update needed: local {App.Version} is {comparison.ToString().ToLowerInvariant()} " +
                        $"than or equal to release {latest.Release.TagName} from {latest.Repository}");
                    return;
                }

                _updateReleaseUrl = $"https://github.com/{latest.Repository}/releases/tag/{latest.Release.TagName}";
                _updateIcon = CreateUpdateIcon();
                _notifyIcon.Icon = _updateIcon;
                _notifyIcon.Text = $"BoneFish - Update {latest.Release.TagName} tersedia";

                App.Logger.WriteLine(
                    LOG_IDENT,
                    $"Update tersedia: {latest.Release.TagName} dari {latest.Repository}. " +
                    "Indicator tray aktif; popup otomatis tidak ditampilkan.");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Update check failed (non-fatal)");
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        private static System.Drawing.Icon CreateUpdateIcon()
        {
            using var bitmap = Properties.Resources.IconBoneFish.ToBitmap();
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.Red))
            using (var outline = new System.Drawing.Pen(System.Drawing.Color.White, 1.5f))
            {
                float diameter = Math.Max(5, bitmap.Width / 3f);
                float x = bitmap.Width - diameter - 1;
                float y = 1;
                graphics.FillEllipse(brush, x, y, diameter, diameter);
                graphics.DrawEllipse(outline, x, y, diameter, diameter);
            }

            IntPtr handle = bitmap.GetHicon();
            using var icon = System.Drawing.Icon.FromHandle(handle);
            return (System.Drawing.Icon)icon.Clone();
        }

        #region Activity handlers
        public async void ShowNotif(object? sender, EventArgs e)
        {
            if (_activityWatcher is null)
                return;

            string title = _activityWatcher.Data.ServerType switch
            {
                ServerType.Public => Strings.ContextMenu_ServerInformation_Notification_Title_Public,
                ServerType.Private => Strings.ContextMenu_ServerInformation_Notification_Title_Private,
                ServerType.Reserved => Strings.ContextMenu_ServerInformation_Notification_Title_Reserved,
                _ => ""
            };

            string? serverLocation = await _activityWatcher.Data.QueryServerLocation();
            string? serverUptime;

            DateTime? serverTime = _activityWatcher.Data.StartTime;
            if (serverTime is not null)
            {
                TimeSpan _serverUptime = DateTime.UtcNow - serverTime.Value;

                if (_serverUptime.TotalMinutes == 0)
                    serverUptime = "0 minutes"; // :sob:
                else
                    serverUptime = Time.FormatTimeSpan(_serverUptime);
            }
            else
                serverUptime = Strings.Common_Unknown; // this should never happen

            ShowAlert(
                title,
                String.Format(
                    Strings.ContextMenu_ServerDetails_Notification_Text,
                    serverLocation,
                    serverUptime
                    ),
                10,
                (_, _) => _menuContainer.ShowServerInformationWindow()
            );
        }
        #endregion

        // we may need to create our own handler for this, because this sorta sucks
        public void ShowAlert(string caption, string message, int duration, EventHandler? clickHandler)
        {
            string id = Guid.NewGuid().ToString()[..8];

            string LOG_IDENT = $"NotifyIconWrapper::ShowAlert.{id}";

            App.Logger.WriteLine(LOG_IDENT, $"Showing alert for {duration} seconds (clickHandler={clickHandler is not null})");
            App.Logger.WriteLine(LOG_IDENT, $"{caption}: {message.Replace("\n", "\\n")}");

            _notifyIcon.BalloonTipTitle = caption;
            _notifyIcon.BalloonTipText = message;

            if (_alertClickHandler is not null)
            {
                App.Logger.WriteLine(LOG_IDENT, "Previous alert still present, erasing click handler");
                _notifyIcon.BalloonTipClicked -= _alertClickHandler;
            }

            _alertClickHandler = clickHandler;
            _notifyIcon.BalloonTipClicked += clickHandler;

            _notifyIcon.ShowBalloonTip(duration);

            Task.Run(async () =>
            {
                await Task.Delay(duration * 1000);
             
                _notifyIcon.BalloonTipClicked -= clickHandler;

                App.Logger.WriteLine(LOG_IDENT, "Duration over, erasing current click handler");

                if (_alertClickHandler == clickHandler)
                    _alertClickHandler = null;
                else
                    App.Logger.WriteLine(LOG_IDENT, "Click handler has been overridden by another alert");
            });
        }

        public void Dispose()
        {
            if (_disposing)
                return;

            _disposing = true;

            App.Logger.WriteLine("NotifyIconWrapper::Dispose", "Disposing NotifyIcon");

            _menuContainer.Dispatcher.Invoke(_menuContainer.Close);
            _notifyIcon.Dispose();
            _updateIcon?.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
