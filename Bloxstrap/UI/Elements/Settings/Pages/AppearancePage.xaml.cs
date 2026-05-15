using Bloxstrap.UI.ViewModels.Settings;

using System.Windows.Controls;

namespace Bloxstrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Interaction logic for AppearancePage.xaml
    /// </summary>
    public partial class AppearancePage
    {
        public AppearancePage()
        {
            var viewModel = new AppearanceViewModel(this);
            
            // Subscribe ke event untuk update icon di title bar
            viewModel.IconChangedEvent += (sender, e) => UpdateMainWindowIcon();
            
            DataContext = viewModel;
            InitializeComponent();
        }

        private void UpdateMainWindowIcon()
        {
            var mainWindow = System.Windows.Window.GetWindow(this) as MainWindow;
            if (mainWindow != null)
            {
                mainWindow.RootTitleBar.Icon = App.Settings.Prop.BootstrapperIcon.GetImageSource();
            }
        }

        public void CustomThemeSelection(object sender, SelectionChangedEventArgs e)
        {
            AppearanceViewModel viewModel = (AppearanceViewModel)DataContext;

            viewModel.SelectedCustomTheme = (string)((ListBox)sender).SelectedItem;
            viewModel.SelectedCustomThemeName = viewModel.SelectedCustomTheme;

            viewModel.OnPropertyChanged(nameof(viewModel.SelectedCustomTheme));
            viewModel.OnPropertyChanged(nameof(viewModel.SelectedCustomThemeName));
        }
    }
}