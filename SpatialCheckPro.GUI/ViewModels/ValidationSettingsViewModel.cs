using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SpatialCheckPro.GUI.ViewModels
{
    public class ValidationSettingsViewModel : INotifyPropertyChanged
    {
        private bool _enableHighPerformanceMode;

        public bool EnableHighPerformanceMode
        {
            get => _enableHighPerformanceMode;
            set
            {
                _enableHighPerformanceMode = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
