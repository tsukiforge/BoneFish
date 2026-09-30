using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

using Bloxstrap.Integrations;
using Bloxstrap.UI.ViewModels.ContextMenu;

namespace Bloxstrap.UI.Elements.ContextMenu
{
    /// <summary>
    /// Interaction logic for ServerInformation.xaml
    /// </summary>
    public partial class ServerInformation
    {
        // FIX (audit tray): terima ActivityWatcher langsung agar window bisa menampilkan
        // data game eksternal (diluncurkan di luar BoneFish) — bukan hanya watcher
        // internal _watcher.ActivityWatcher.
        public ServerInformation(ActivityWatcher activityWatcher)
        {
            DataContext = new ServerInformationViewModel(activityWatcher);
            InitializeComponent();
        }
    }
}
