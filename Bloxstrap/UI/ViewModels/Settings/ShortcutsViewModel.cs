using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Bloxstrap.Integrations;

namespace Bloxstrap.UI.ViewModels.Settings
{
    public class ShortcutsViewModel : NotifyPropertyChangedViewModel
    {
        public bool IsStudioOptionVisible => App.IsStudioVisible;

        public ShortcutTask DesktopIconTask { get; } = new("Desktop", Paths.Desktop, $"{App.ProjectName}.lnk");

        public ShortcutTask StartMenuIconTask { get; } = new("StartMenu", Paths.WindowsStartMenu, $"{App.ProjectName}.lnk");

        public ShortcutTask PlayerIconTask { get; } = new("RobloxPlayer", Paths.Desktop, $"{Strings.LaunchMenu_LaunchRoblox}.lnk", "-player");

        public ShortcutTask StudioIconTask { get; } = new("RobloxStudio", Paths.Desktop, $"{Strings.LaunchMenu_LaunchRobloxStudio}.lnk", "-studio");

        public ShortcutTask SettingsIconTask { get; } = new("Settings", Paths.Desktop, $"{Strings.Menu_Title}.lnk", "-settings");

        public ExtractIconsTask ExtractIconsTask { get; } = new();

        // ── Repair Shortcuts (Fitur A) ──────────────────────────────────────────────
        public ICommand RepairShortcutsCommand { get; }

        /// <summary>
        /// Event untuk memberitahu UI menampilkan notifikasi hasil repair.
        /// </summary>
        public event EventHandler<string>? RequestNotificationEvent;

        private void Notify(string message) => RequestNotificationEvent?.Invoke(this, message);

        private void OnRepairShortcuts()
        {
            try
            {
                int repaired = Bloxstrap.Installer.RepairShortcuts();
                if (repaired > 0)
                    Notify($"✅ {repaired} shortcut(s) berhasil diperbaiki.");
                else
                    Notify("✅ Semua shortcut sudah OK — tidak perlu perbaikan.");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("ShortcutsViewModel", $"Repair shortcuts error: {ex.Message}");
                Notify($"❌ Gagal memperbaiki shortcut: {ex.Message}");
            }
        }

        public ShortcutsViewModel()
        {
            RepairShortcutsCommand = new RelayCommand(OnRepairShortcuts);
        }
    }
}
