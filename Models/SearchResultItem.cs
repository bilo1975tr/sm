using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StreamMesh.Models
{
    /// <summary>
    /// Represents a search result item across external P2P and IPTV repository queries.
    /// </summary>
    public class SearchResultItem : INotifyPropertyChanged
    {
        private string _name = "";
        private string _url = "";
        private string _source = "";
        private string _category = "Canlı TV";
        private string _peersOrDetails = "";
        private string _logoUrl = "";
        private string _groupTitle = "";
        private bool _isAdded = false;
        private string _addButtonText = "➕ Kütüphaneye Ekle";

        public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
        public string Url { get => _url; set { _url = value; OnPropertyChanged(); } }
        public string Source { get => _source; set { _source = value; OnPropertyChanged(); } }
        public string Category { get => _category; set { _category = value; OnPropertyChanged(); } }
        public string PeersOrDetails { get => _peersOrDetails; set { _peersOrDetails = value; OnPropertyChanged(); } }
        public string LogoUrl { get => _logoUrl; set { _logoUrl = value; OnPropertyChanged(); } }
        public string GroupTitle { get => _groupTitle; set { _groupTitle = value; OnPropertyChanged(); } }
        public bool IsAdded { get => _isAdded; set { _isAdded = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanAdd)); } }
        public bool CanAdd => !_isAdded;
        public string AddButtonText { get => _addButtonText; set { _addButtonText = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }
}
