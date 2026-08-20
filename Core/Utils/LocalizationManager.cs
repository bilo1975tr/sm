using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StreamMesh.Core.Utils
{
    public class LocalizationManager : INotifyPropertyChanged
    {
        private static LocalizationManager? _instance;
        public static LocalizationManager Instance => _instance ??= new LocalizationManager();

        public event PropertyChangedEventHandler? PropertyChanged;

        private string _currentLanguage = "tr";
        public string CurrentLanguage
        {
            get => _currentLanguage;
            set
            {
                if (_currentLanguage != value)
                {
                    _currentLanguage = value;
                    LoadTranslations(value);
                    OnPropertyChanged();
                    OnPropertyChanged("Item[]");
                }
            }
        }

        private readonly Dictionary<string, string> _currentDictionary = new Dictionary<string, string>();

        public string this[string key]
        {
            get
            {
                if (_currentDictionary.TryGetValue(key, out string? val))
                    return val;
                return key;
            }
        }

        private LocalizationManager()
        {
            LoadTranslations("tr");
        }

        public void LoadTranslations(string languageCode)
        {
            _currentDictionary.Clear();
            string lang = (languageCode ?? "tr").ToLowerInvariant().Trim();

            // Default Turkish
            _currentDictionary["NavLibrary"] = "Kütüphane";
            _currentDictionary["NavPlayer"] = "Oynatıcı";
            _currentDictionary["NavStats"] = "İstatistikler";
            _currentDictionary["NavSettings"] = "Ayarlar";
            _currentDictionary["GlobalSearch"] = "Global Arama";

            _currentDictionary["Home_MyLibrary"] = "Kütüphanem";
            _currentDictionary["Home_All"] = "Tümü";
            _currentDictionary["Home_Favorites"] = "Favoriler";
            _currentDictionary["Home_TV"] = "TV Kanalları";
            _currentDictionary["Home_Movies"] = "Filmler";
            _currentDictionary["Home_Series"] = "Diziler";
            _currentDictionary["Home_Radio"] = "Radyo";
            _currentDictionary["Home_Total"] = "Toplam: {0} İçerik";

            _currentDictionary["Set_Title"] = "Ayarlar";
            _currentDictionary["Set_TabResource"] = "Yayın Kaynakları";
            _currentDictionary["Set_TabEpg"] = "EPG Rehber";
            _currentDictionary["Set_TabApp"] = "Uygulama & AI";
            _currentDictionary["Set_TabNetwork"] = "Ağ / P2P";
            _currentDictionary["Set_TabAuto"] = "Oto Güncelle";
            _currentDictionary["Ai_Thinking"] = "AI Düşünüyor...";

            if (lang == "en")
            {
                _currentDictionary["NavLibrary"] = "Library";
                _currentDictionary["NavPlayer"] = "Player";
                _currentDictionary["NavStats"] = "Stats";
                _currentDictionary["NavSettings"] = "Settings";
                _currentDictionary["GlobalSearch"] = "Global Search";

                _currentDictionary["Home_MyLibrary"] = "My Library";
                _currentDictionary["Home_All"] = "All";
                _currentDictionary["Home_Favorites"] = "Favorites";
                _currentDictionary["Home_TV"] = "TV Channels";
                _currentDictionary["Home_Movies"] = "Movies";
                _currentDictionary["Home_Series"] = "Series";
                _currentDictionary["Home_Radio"] = "Radio";
                _currentDictionary["Home_Total"] = "Total: {0} Items";

                _currentDictionary["Set_Title"] = "Settings";
                _currentDictionary["Set_TabResource"] = "Stream Sources";
                _currentDictionary["Set_TabEpg"] = "EPG Guide";
                _currentDictionary["Set_TabApp"] = "App & AI";
                _currentDictionary["Set_TabNetwork"] = "Network / P2P";
                _currentDictionary["Set_TabAuto"] = "Auto Update";
                _currentDictionary["Ai_Thinking"] = "AI Thinking...";
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
