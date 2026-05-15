using System.ComponentModel;
using System.Windows.Media;

namespace Bloxstrap.Models
{
    public class RobloxIconEntry : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public RobloxIcon IconType { get; set; }
        public ImageSource ImageSource => IconType.GetImageSource();

        public void RefreshImageSource() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImageSource)));
    }
}
