using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace Bloxstrap.UI.ViewModels.Settings
{
    public class ExperimentalViewModel : NotifyPropertyChangedViewModel
    {
        public bool EnableSystemTrayOnClose
        {
            get => App.Settings.Prop.EnableSystemTrayOnClose;
            set => App.Settings.Prop.EnableSystemTrayOnClose = value;
        }
    }
}
