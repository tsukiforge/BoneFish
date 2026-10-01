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

            if (_activityWatcher is not null && App.Settings.Prop.ShowServerDetails)
                _activityWatcher.ShowNotif += ShowNotif;

            _menuContainer = new(_watcher);
            _menuContainer.Show();

            // FIX (audit tray): game eksternal mengganti ActivityWatcher aktif secara
            // dinamis — tampilkan notifikasi lokasi server dari watcher mana pun yang
            // sedang aktif (jika ShowServerDetails aktif).
            _watcher.ActiveGameWatcherChanged += ActiveGameWatcherChangedHandler;
        }

        private void ActiveGameWatcherChangedHandler(object? sender, Watcher.ActiveGameWatcherChangedEventArgs e)
        {
            if (e.Watcher is null)
                return; // teardown ditangani TeardownExternalActivityWatcher

            if (e.IsExternal && App.Settings.Prop.ShowServerDetails)
            {
                // Guard dobel-subscribe: watcher eksternal juga disubscribe via
                // SetupExternalActivityWatcher dari Watcher.AttachExternalGame.
                e.Watcher.ShowNotif -= ShowNotif;
                e.Watcher.ShowNotif += ShowNotif;

                // Game eksternal bisa sudah in-game saat watcher menempel — tampilkan
                // notifikasi lokasi server segera, tidak menunggu event join berikutnya.
                if (e.Watcher.InGame)
                    ShowNotif(e.Watcher, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Saat game eksternal menempel, jalankan ShowNotif untuk watcher-nya agar
        /// detail server game eksternal ikut muncul (dipanggil dari Watcher).
        /// </summary>
        public void SetupExternalActivityWatcher(ActivityWatcher watcher)
        {
            if (App.Settings.Prop.ShowServerDetails)
            {
                watcher.ShowNotif -= ShowNotif;
                watcher.ShowNotif += ShowNotif;

                if (watcher.InGame)
                    ShowNotif(watcher, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Lepaskan wiring game eksternal dan tutup window server yang masih menampilkan
        /// datanya. endSession=true → game eksternal benar-benar berakhir (proses mati /
        /// user exit); false → watcher eksternal dilepas sesaat (takeover watcher baru).
        /// </summary>
        public void TeardownExternalActivityWatcher(bool endSession)
        {
            // Hapus langganan ShowNotif milik watcher eksternal (handler terpasang di
            // SetupExternalActivityWatcher / ActiveGameWatcherChangedHandler).
            if (_watcher.ExternalActivityWatcher is { } external)
            {
                try { external.ShowNotif -= ShowNotif; } catch { }
            }

            if (endSession)
                _menuContainer.Dispatcher.Invoke(_menuContainer.CloseServerDependentWindows);
        }

        #region Context menu
        public void MouseClickEventHandler(object? sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button != System.Windows.Forms.MouseButtons.Right)
                return;

            _menuContainer.Activate();
            _menuContainer.ContextMenu.IsOpen = true;
        }
        #endregion

        #region Activity handlers
        public async void ShowNotif(object? sender, EventArgs e)
        {
            // FIX (audit tray #1): pakai watcher yang memicu event — kalau sender-nya
            // watcher game eksternal, data yang ditampilkan harus milik game eksternal,
            // bukan watcher internal. Fallback ke watcher aktif dari Watcher.
            ActivityWatcher? watcher = sender as ActivityWatcher
                ?? _watcher.FindActiveGameWatcher()
                ?? _activityWatcher;

            if (watcher is null)
                return;

            string title = watcher.Data.ServerType switch
            {
                ServerType.Public => Strings.ContextMenu_ServerInformation_Notification_Title_Public,
                ServerType.Private => Strings.ContextMenu_ServerInformation_Notification_Title_Private,
                ServerType.Reserved => Strings.ContextMenu_ServerInformation_Notification_Title_Reserved,
                _ => ""
            };

            string? serverLocation = await watcher.Data.QueryServerLocation();
            string? serverUptime;

            DateTime? serverTime = watcher.Data.StartTime;
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
            UpdateTrayStatus();

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

        /// <summary>
        /// FIX (audit tray #4): tooltip tray mencerminkan status Game Session aktif
        /// (jumlah aplikasi yang ditahan), bukan sekadar nama aplikasi.
        /// </summary>
        private void UpdateTrayStatus()
        {
            try
            {
                int suspended = App.GameSession.Store.ReadActive()?.SuspendedProcesses.Count ?? 0;
                _notifyIcon.Text = suspended > 0
                    ? $"BoneFish — Game Session aktif ({suspended} aplikasi ditahan)"
                    : "BoneFish";
            }
            catch
            {
                // tooltip bersifat kosmetik — jangan biarkan gagal memengaruhi alert
            }
        }

        public void Dispose()
        {
            if (_disposing)
                return;

            _disposing = true;

            App.Logger.WriteLine("NotifyIconWrapper::Dispose", "Disposing NotifyIcon");

            _menuContainer.Dispatcher.Invoke(_menuContainer.Close);
            _notifyIcon.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
