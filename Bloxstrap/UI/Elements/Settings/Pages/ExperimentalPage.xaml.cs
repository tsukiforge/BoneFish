using System.Windows;
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

        /// <summary>
        /// ★ FIX 4: Responsive layout — stacking vertikal kalo lebar < 700px.
        /// RightPanel pindah dari Grid.Column=2 ke Grid.Row=1 (di bawah LeftPanel).
        /// </summary>
        private void MainGrid_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
        {
            if (sender is Panel panel)
            {
                bool narrow = e.NewSize.Width < 700;
                Grid.SetColumn(LeftPanel, 0);
                Grid.SetRow(LeftPanel, 0);

                if (narrow)
                {
                    // Stack vertikal: kanan di bawah kiri
                    Grid.SetColumn(RightPanel, 0);
                    Grid.SetRow(RightPanel, 1);
                }
                else
                {
                    // Side by side: kanan di kolom 2
                    Grid.SetColumn(RightPanel, 2);
                    Grid.SetRow(RightPanel, 0);
                }
            }
        }
    }
}
