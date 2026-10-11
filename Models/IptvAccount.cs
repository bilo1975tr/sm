using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StreamMesh.Models
{
    public class IptvAccount : INotifyPropertyChanged
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        private string _name = "Yeni IPTV Hesabı";
        public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }

        private string _serverUrl = "";
        public string ServerUrl { get => _serverUrl; set { _serverUrl = value; OnPropertyChanged(); } }

        private string _username = "";
        public string Username { get => _username; set { _username = value; OnPropertyChanged(); } }

        private string _password = "";
        public string Password { get => _password; set { _password = value; OnPropertyChanged(); } }

        private string _status = "Bilinmiyor";
        public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }

        private DateTime _expiryDate;
        public DateTime ExpiryDate { get => _expiryDate; set { _expiryDate = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
