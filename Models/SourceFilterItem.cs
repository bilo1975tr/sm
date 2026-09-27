using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StreamMesh.Models
{
    public class SourceFilterItem : INotifyPropertyChanged
    {
        private string _id = "";
        private string _name = "";
        private string _sourceUrl = "";
        private int _count = 0;
        private bool _isSelected = false;
        private string _icon = "⚡";

        public string Id
        {
            get => _id;
            set { if (_id != value) { _id = value; OnPropertyChanged(); } }
        }

        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); } }
        }

        public string SourceUrl
        {
            get => _sourceUrl;
            set { if (_sourceUrl != value) { _sourceUrl = value; OnPropertyChanged(); } }
        }

        public int Count
        {
            get => _count;
            set { if (_count != value) { _count = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); } }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
        }

        public string Icon
        {
            get => _icon;
            set { if (_icon != value) { _icon = value; OnPropertyChanged(); } }
        }

        public string DisplayName => Count > 0 ? $"{Icon} {Name} ({Count})" : $"{Icon} {Name}";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
