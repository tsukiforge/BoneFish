using System.Windows.Controls;
using Bloxstrap.UI.ViewModels.Settings;

namespace Bloxstrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Interaction logic for ExperimentalPage.xaml
    /// </summary>
    public partial class ExperimentalPage
    {
        public ExperimentalPage()
        {
            DataContext = new ExperimentalViewModel();
            InitializeComponent();
        }
    }
}
