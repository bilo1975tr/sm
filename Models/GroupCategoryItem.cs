using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StreamMesh.Models
{
    public class GroupCategoryItem : INotifyPropertyChanged
    {
        private string _name = "";
        private int _count = 0;
        private bool _isSelected = false;

        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); } }
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

        public string DisplayName => Count > 0 ? $"{Name} ({Count})" : Name;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
