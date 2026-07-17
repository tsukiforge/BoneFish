using System;
using System.Windows;
using Bloxstrap.UI.ViewModels.Settings;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;

namespace Bloxstrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Interaction logic for ShortcutsPage.xaml
    /// </summary>
    public partial class ShortcutsPage
    {
        private ShortcutsViewModel _viewModel = null!;

        public ShortcutsPage()
        {
            _viewModel = new ShortcutsViewModel();
            DataContext = _viewModel;
            InitializeComponent();

            _viewModel.RequestNotificationEvent += (_, message) => ShowNotification(message);
        }

        private void ShowNotification(string message)
        {
            try
            {
                RepairSnackbar.Title = "Perbaiki Shortcut";
                RepairSnackbar.Message = message;
                RepairSnackbar.Appearance = message.StartsWith("✅") ? ControlAppearance.Success : ControlAppearance.Danger;
                RepairSnackbar.Visibility = Visibility.Visible;
                RepairSnackbar.Show();
            }
            catch { }
        }
    }
}
