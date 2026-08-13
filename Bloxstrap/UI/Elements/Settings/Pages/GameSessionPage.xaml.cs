using Bloxstrap.UI.ViewModels.Settings;
using System.Windows.Input;

namespace Bloxstrap.UI.Elements.Settings.Pages
{
    public partial class GameSessionPage
    {
        public GameSessionPage()
        {
            DataContext = new GameSessionViewModel();
            InitializeComponent();
        }

        private void Page_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (PageScrollViewer.ScrollableHeight <= 0)
                return;

            double nextOffset = PageScrollViewer.VerticalOffset - e.Delta;
            PageScrollViewer.ScrollToVerticalOffset(Math.Clamp(nextOffset, 0, PageScrollViewer.ScrollableHeight));
            e.Handled = true;
        }
    }
}
