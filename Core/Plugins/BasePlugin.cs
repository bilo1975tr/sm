using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using StreamMesh.Core.Database;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Plugins
{
    public abstract class BasePlugin : IPlugin
    {
        public abstract string Id { get; }
        public abstract string Name { get; }
        public virtual string Description => "StreamMesh modülü.";
        public virtual string Category => "Genel";
        public virtual string Author => "StreamMesh Ekibi";
        public virtual string Version => "1.0.0";
        public virtual string Icon => "🧩";

        private bool _isEnabled = true;
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusBadgeColor));
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        private string _status = "Hazır";
        public string Status
        {
            get => _status;
            protected set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusBadgeColor));
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        private string _statusMessage = "";
        public string StatusMessage
        {
            get => _statusMessage;
            protected set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        public string StatusText => !IsEnabled ? "Devre Dışı" : Status;

        public string StatusBadgeColor
        {
            get
            {
                if (!IsEnabled) return "#475569"; // Slate gray
                return Status switch
                {
                    "Aktif" or "Çalışıyor" or "Hazır" => "#059669", // Emerald green
                    "İşleniyor" or "Senkronize Ediliyor" => "#0284c7", // Sky blue
                    "Uyarı" => "#d97706", // Amber
                    "Hata" => "#dc2626", // Red
                    _ => "#0284c7"
                };
            }
        }

        private DateTime? _lastExecutionTime;
        public DateTime? LastExecutionTime
        {
            get => _lastExecutionTime;
            protected set
            {
                if (_lastExecutionTime != value)
                {
                    _lastExecutionTime = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LastExecutionFormatted));
                }
            }
        }

        public string LastExecutionFormatted =>
            LastExecutionTime.HasValue ? LastExecutionTime.Value.ToString("HH:mm:ss (dd.MM)") : "Henüz çalışmadı";

        private long _executionCount;
        public long ExecutionCount
        {
            get => _executionCount;
            protected set
            {
                if (_executionCount != value)
                {
                    _executionCount = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<PluginSettingItem> Settings { get; } = new();

        protected BasePlugin()
        {
            // Initialize settings schema in subclass constructor
        }

        protected void AddSetting(string key, string label, string defaultValue, PluginSettingType type, string description = "", List<string>? options = null)
        {
            var item = new PluginSettingItem
            {
                Key = key,
                Label = label,
                DefaultValue = defaultValue,
                Value = defaultValue,
                Type = type,
                Description = description,
                Options = options ?? new List<string>()
            };
            Settings.Add(item);
        }

        public string GetString(string key, string fallback = "")
        {
            var s = Settings.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
            return s != null ? s.Value : fallback;
        }

        public int GetInt(string key, int fallback = 0)
        {
            var val = GetString(key, fallback.ToString());
            return int.TryParse(val, out int res) ? res : fallback;
        }

        public bool GetBool(string key, bool fallback = false)
        {
            var val = GetString(key, fallback ? "true" : "false");
            return string.Equals(val, "true", StringComparison.OrdinalIgnoreCase) || val == "1";
        }

        public void SetSettingValue(string key, string value)
        {
            var s = Settings.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
            if (s != null)
            {
                s.Value = value;
            }
        }

        public virtual void LoadSettings(DatabaseEngine db)
        {
            try
            {
                // Persistent enabled state
                string enabledStr = db.GetSetting($"Plugin_{Id}_Enabled", "true");
                IsEnabled = string.Equals(enabledStr, "true", StringComparison.OrdinalIgnoreCase) || enabledStr == "1";

                foreach (var setting in Settings)
                {
                    string dbVal = db.GetSetting($"Plugin_{Id}_{setting.Key}", setting.DefaultValue);
                    setting.Value = dbVal;
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"[{Name}] Error loading settings from database", ex);
            }
        }

        public virtual void SaveSettings(DatabaseEngine db)
        {
            try
            {
                db.SetSetting($"Plugin_{Id}_Enabled", IsEnabled ? "true" : "false");

                foreach (var setting in Settings)
                {
                    db.SetSetting($"Plugin_{Id}_{setting.Key}", setting.Value ?? "");
                }

                LogService.LogInfo($"[{Name}] Settings saved successfully.");
                SetStatus(IsEnabled ? "Aktif" : "Devre Dışı", "Ayarlar kaydedildi ve güncellendi.");
            }
            catch (Exception ex)
            {
                LogService.LogError($"[{Name}] Error saving settings to database", ex);
                SetStatus("Hata", $"Ayarlar kaydedilemedi: {ex.Message}");
            }
        }

        public virtual void ResetSettings(DatabaseEngine db)
        {
            try
            {
                foreach (var setting in Settings)
                {
                    setting.Value = setting.DefaultValue;
                    db.SetSetting($"Plugin_{Id}_{setting.Key}", setting.DefaultValue);
                }
                SetStatus(IsEnabled ? "Aktif" : "Devre Dışı", "Varsayılan ayarlara dönüldü.");
            }
            catch (Exception ex)
            {
                LogService.LogError($"[{Name}] Error resetting settings", ex);
            }
        }

        public virtual Task InitializeAsync()
        {
            SetStatus("Aktif", "Eklenti başarıyla başlatıldı.");
            return Task.CompletedTask;
        }

        public virtual Task ShutdownAsync()
        {
            SetStatus("Durduruldu", "Eklenti kapatıldı.");
            return Task.CompletedTask;
        }

        public virtual Task<string> ExecuteActionAsync(string actionName, Dictionary<string, object>? parameters = null)
        {
            RecordExecution();
            return Task.FromResult($"'{actionName}' işlemi '{Name}' eklentisi tarafından başarıyla tamamlandı.");
        }

        public virtual Dictionary<string, string> GetStatistics()
        {
            return new Dictionary<string, string>
            {
                { "Durum", StatusText },
                { "Sürüm", Version },
                { "Çalışma Sayısı", ExecutionCount.ToString("N0") },
                { "Son Çalışma", LastExecutionFormatted }
            };
        }

        protected void RecordExecution()
        {
            ExecutionCount++;
            LastExecutionTime = DateTime.Now;
        }

        protected void SetStatus(string status, string message = "")
        {
            Status = status;
            StatusMessage = message;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
