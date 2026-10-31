using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using StreamMesh.Core.Database;

namespace StreamMesh.Core.Plugins
{
    public enum PluginSettingType
    {
        Text,
        Password,
        Number,
        Boolean,
        Select
    }

    public class PluginSettingItem : INotifyPropertyChanged
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string Description { get; set; } = "";
        public PluginSettingType Type { get; set; } = PluginSettingType.Text;
        public string DefaultValue { get; set; } = "";
        public List<string> Options { get; set; } = new();

        private string _value = "";
        public string Value
        {
            get => _value;
            set
            {
                if (_value != value)
                {
                    _value = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BoolValue));
                }
            }
        }

        public bool BoolValue
        {
            get => string.Equals(_value, "true", StringComparison.OrdinalIgnoreCase) || _value == "1";
            set
            {
                Value = value ? "true" : "false";
                OnPropertyChanged();
            }
        }

        public bool IsBoolean => Type == PluginSettingType.Boolean;
        public bool IsSelect => Type == PluginSettingType.Select;
        public bool IsText => Type == PluginSettingType.Text || Type == PluginSettingType.Password || Type == PluginSettingType.Number;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public interface IPlugin : INotifyPropertyChanged
    {
        string Id { get; }
        string Name { get; }
        string Description { get; }
        string Category { get; }
        string Author { get; }
        string Version { get; }
        string Icon { get; }
        bool IsEnabled { get; set; }
        string Status { get; }
        string StatusMessage { get; }
        string StatusText { get; }
        string StatusBadgeColor { get; }
        DateTime? LastExecutionTime { get; }
        long ExecutionCount { get; }

        ObservableCollection<PluginSettingItem> Settings { get; }

        Task InitializeAsync();
        Task ShutdownAsync();

        void LoadSettings(DatabaseEngine db);
        void SaveSettings(DatabaseEngine db);
        void ResetSettings(DatabaseEngine db);

        Task<string> ExecuteActionAsync(string actionName, Dictionary<string, object>? parameters = null);
        Dictionary<string, string> GetStatistics();
    }
}
