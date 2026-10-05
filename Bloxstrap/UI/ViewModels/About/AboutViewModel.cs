using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace Bloxstrap.UI.ViewModels.About
{
    public class AboutViewModel : NotifyPropertyChangedViewModel
    {
        public string Version => string.Format(Strings.Menu_About_Version, App.Version);

        public BuildMetadataAttribute BuildMetadata => App.BuildMetadata;
            
        public string BuildTimestamp => BuildMetadata.Timestamp.ToFriendlyString();
        public string BuildCommitHashUrl => $"https://github.com/{App.ProjectRepository}/commit/{BuildMetadata.CommitHash}";

        public bool RollbackAvailable => global::Bloxstrap.Installer.IsRollbackAvailable;
        public string RollbackAvailabilityText => RollbackAvailable
            ? "Versi BoneFish sebelumnya tersimpan. Rollback mengganti aplikasi saja; versi lama mungkin tidak mengenali pengaturan yang lebih baru."
            : "Versi sebelumnya akan tersedia setelah BoneFish diperbarui ke versi yang lebih baru.";
        public ICommand RollbackCommand => new RelayCommand(Rollback, () => RollbackAvailable);

        public Visibility BuildInformationVisibility => App.IsProductionBuild ? Visibility.Collapsed : Visibility.Visible;
        public Visibility BuildCommitVisibility => App.IsActionBuild ? Visibility.Visible : Visibility.Collapsed;

        private static void Rollback()
        {
            MessageBoxResult result = Frontend.ShowMessageBox(
                "BoneFish akan ditutup dan kembali ke versi sebelumnya. File Roblox tidak dihapus, tetapi versi lama mungkin mengabaikan atau menulis ulang pengaturan BoneFish yang lebih baru. Lanjutkan?",
                MessageBoxImage.Warning,
                MessageBoxButton.YesNo);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                global::Bloxstrap.Installer.BeginRollback();
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("AboutViewModel::Rollback", ex);
                Frontend.ShowMessageBox(
                    $"Rollback BoneFish gagal dimulai: {ex.Message}",
                    MessageBoxImage.Error);
            }
        }
    }
}
