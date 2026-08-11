using System.Windows;
using Bloxstrap.UI.ViewModels.Settings;

namespace Bloxstrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Interaction logic for SandboxPage.xaml
    /// </summary>
    public partial class SandboxPage
    {
        private bool _initialLoad = false;

        private SandboxViewModel _viewModel = null!;

        public SandboxPage()
        {
            SetupViewModel();
            InitializeComponent();
        }

        private void SetupViewModel()
        {
            _viewModel = new SandboxViewModel();

            _viewModel.RequestNotificationEvent += (_, message) => ShowSandboxNotification(message);

            DataContext = _viewModel;
        }

        private void ShowSandboxNotification(string message)
        {
            SandboxSnackbar.Message = message;
            SandboxSnackbar.Appearance = message.StartsWith("✗")
                ? Wpf.Ui.Common.ControlAppearance.Danger
                : Wpf.Ui.Common.ControlAppearance.Success;
            SandboxSnackbar.Visibility = Visibility.Visible;
            SandboxSnackbar.Show();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_initialLoad)
            {
                _initialLoad = true;
                return;
            }

            SetupViewModel();
        }
    }
}
