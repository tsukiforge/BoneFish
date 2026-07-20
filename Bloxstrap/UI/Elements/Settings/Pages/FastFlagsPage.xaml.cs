using System.Windows;
using System.Windows.Input;

using Bloxstrap.UI.ViewModels.Settings;
using Wpf.Ui.Mvvm.Contracts;

namespace Bloxstrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Interaction logic for FastFlagsPage.xaml
    /// </summary>
    public partial class FastFlagsPage
    {
        private bool _initialLoad = false;

        private FastFlagsViewModel _viewModel = null!;

        public FastFlagsPage()
        {
            SetupViewModel();
            InitializeComponent();
        }

        private void SetupViewModel()
        {
            _viewModel = new FastFlagsViewModel();

            _viewModel.OpenFlagEditorEvent += OpenFlagEditor;
            _viewModel.RequestPageReloadEvent += (_, _) => SetupViewModel();
            _viewModel.RequestNotificationEvent += (_, message) => ShowFastFlagsNotification(message);
            _viewModel.RequestCloseWindowEvent += (_, _) =>
            {
                if (Window.GetWindow(this) is Window win)
                    win.Close();
            };

            DataContext = _viewModel;
        }

        private void ShowFastFlagsNotification(string message)
        {
            FastFlagsSnackbar.Message = message;
            FastFlagsSnackbar.Visibility = Visibility.Visible;
            FastFlagsSnackbar.Show();
        }

        private void OpenFlagEditor(object? sender, EventArgs e)
        {
            if (Window.GetWindow(this) is INavigationWindow window)
            {
               window.Navigate(typeof(FastFlagEditorPage));
            }
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // refresh datacontext on page load to synchronize with editor page
            
            if (!_initialLoad)
            {
                _initialLoad = true;
                return;
            }

            SetupViewModel();
        }

        /// <summary>
        /// ★ FIX 4: Responsive layout — stacking vertikal kalo lebar < 700px.
        /// RightPanel pindah dari Grid.Column=2 ke Grid.Row=1 (di bawah LeftPanel).
        /// </summary>
        private void MainGrid_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.Panel panel)
            {
                bool narrow = e.NewSize.Width < 700;
                System.Windows.Controls.Grid.SetColumn(LeftPanel, 0);
                System.Windows.Controls.Grid.SetRow(LeftPanel, 0);

                if (narrow)
                {
                    // Stack vertikal: kanan di bawah kiri
                    System.Windows.Controls.Grid.SetColumn(RightPanel, 0);
                    System.Windows.Controls.Grid.SetRow(RightPanel, 1);
                }
                else
                {
                    // Side by side: kanan di kolom 2
                    System.Windows.Controls.Grid.SetColumn(RightPanel, 2);
                    System.Windows.Controls.Grid.SetRow(RightPanel, 0);
                }
            }
        }

        private void InfoBar_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Utilities.ShellExecute("https://devforum.roblox.com/t/allowlist-for-local-client-configuration-via-fast-flags/3966569");
        }

        private void ValidateInt32(object sender, TextCompositionEventArgs e) => e.Handled = e.Text != "-" && !Int32.TryParse(e.Text, out int _);
        
        private void ValidateUInt32(object sender, TextCompositionEventArgs e) => e.Handled = !UInt32.TryParse(e.Text, out uint _);
    }
}
