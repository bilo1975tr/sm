using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using StreamMesh.Core.Network;
using StreamMesh.Core.Utils;

namespace StreamMesh.UI.ViewModels
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        private string _version = "";
        public string Version { get => _version; set { _version = value; OnPropertyChanged(); } }

        private string _updateMessage = "";
        public string UpdateMessage { get => _updateMessage; set { _updateMessage = value; OnPropertyChanged(); } }

        private bool _isUpdateAvailable;
        public bool IsUpdateAvailable { get => _isUpdateAvailable; set { _isUpdateAvailable = value; OnPropertyChanged(); } }

        public MainWindowViewModel()
        {
            Version = UpdateService.GetCurrentVersion();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
