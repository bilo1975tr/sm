using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using StreamMesh.Models;

namespace StreamMesh.Services
{
    public class LocalizationManager : INotifyPropertyChanged
    {
        private static LocalizationManager _instance;
        public static LocalizationManager Instance => _instance ??= new LocalizationManager();

        public event PropertyChangedEventHandler PropertyChanged;

        private string _currentLanguage = "Türkçe";
        public string CurrentLanguage 
        {
            get => _currentLanguage;
            set 
            {
                if (_currentLanguage != value)
                {
                    _currentLanguage = value;
                    OnPropertyChanged();
                    UpdateLanguage(value);
                }
            }
        }

        private Dictionary<string, string> _currentDictionary = new Dictionary<string, string>();

        public string this[string key]
        {
            get
            {
                if (_currentDictionary.TryGetValue(key, out string val))
                    return val;
                return key;
            }
        }

        private LocalizationManager()
        {
            LoadTranslations("Türkçe");
        }

        public void LoadTranslations(string languageName)
        {
            _currentDictionary.Clear();
            
            // Default Turkish
            // -- Login --
            _currentDictionary["Title"] = "StreamMesh - Giriş / Kayıt";
            _currentDictionary["StreamMeshNetwork"] = "StreamMesh Sunucusuna Kaydol";
            _currentDictionary["LoginDesc"] = "Hesabınız yoksa otomatik oluşturulacaktır (Bulut tabanlı)";
            _currentDictionary["EmailOrUser"] = "E-Posta veya Kullanıcı Adı";
            _currentDictionary["Password"] = "Şifre";
            _currentDictionary["Country"] = "Bulunduğunuz Ülke";
            _currentDictionary["KnownLangs"] = "Diğer Bildiğiniz Diller";
            _currentDictionary["AppLang"] = "Uygulama Dili";
            _currentDictionary["GuestLogin"] = "Misafir Girişi";
            _currentDictionary["LoginReg"] = "Giriş / Kayıt";
            // -- Menu --
            _currentDictionary["NavLibrary"] = "Kütüphane";
            _currentDictionary["NavPlayer"] = "Oynatıcı";
            _currentDictionary["NavStats"] = "İstatistikler";
            _currentDictionary["NavSettings"] = "Ayarlar & Sunucu";
            // -- Home --
            _currentDictionary["Home_MyLibrary"] = "Kütüphanem (Güncel)";
            _currentDictionary["Home_AdSpace"] = "Reklam / Sponsor Alanı";
            _currentDictionary["Home_All"] = "Tümü";
            _currentDictionary["Home_Favorites"] = "Favoriler ⭐";
            _currentDictionary["Home_TV"] = "TV";
            _currentDictionary["Home_Movies"] = "Film";
            _currentDictionary["Home_Series"] = "Dizi";
            _currentDictionary["Home_Radio"] = "Radyo";
            _currentDictionary["Home_Search"] = "🔍 Kanal ara...";
            _currentDictionary["Home_Total"] = "Toplam: {0} İçerik";
            _currentDictionary["Home_Page"] = "Sayfa {0} / {1}";
            _currentDictionary["Home_Prev"] = "< Önceki";
            _currentDictionary["Home_Next"] = "Sonraki >";
            // -- Settings --
            _currentDictionary["Set_Title"] = "Ayarlar";
            _currentDictionary["Set_TabResource"] = "Kaynaklar / Yayın";
            _currentDictionary["Set_TabEpg"] = "Kaynaklar / EPG";
            _currentDictionary["Set_TabApp"] = "Uygulama";
            _currentDictionary["Set_AppLang"] = "Uygulama Dili";
            _currentDictionary["Set_AppLangDesc"] = "Uygulamanın arayüz dilini buradan değiştirebilirsiniz.";
            _currentDictionary["Set_LangSelect"] = "Dil Seçimi";
            _currentDictionary["Set_TabPlayer"] = "Oynatıcı Ayarları";
            _currentDictionary["Set_TabNetwork"] = "Sistem / Ağ Ayarları";
            
            _currentDictionary["Set_AddStream"] = "Yeni Oynatma Listesi Kaynağı Ekle";
            _currentDictionary["Set_LinkType"] = "M3U / DPL / Link: ";
            _currentDictionary["Set_Browse"] = "Gözat";
            _currentDictionary["Set_Load"] = "Yükle";
            _currentDictionary["Set_AddedStreams"] = "Ekli Oynatma Listeleri";
            _currentDictionary["Set_Reload"] = "🔄 Yeniden Yükle";
            _currentDictionary["Set_EditSrc"] = "Kaynağı Düzenle (Diller vs)";
            _currentDictionary["Set_DelSrc"] = "Kaynağı Sil";
            _currentDictionary["Set_DelSelectedSrc"] = "Seçili Kaynağı Sil";
            
            _currentDictionary["Set_StreamCheck"] = "Kanal/Yayın Doğrulama";
            _currentDictionary["Set_StreamCheckDesc"] = "IPTV yayınlarının aktif olup olmadığını kontrol edebilirsiniz. Çalışmayanlar silinir.";
            _currentDictionary["Set_CheckAll"] = "Tüm Kanalları Kontrol Et";
            _currentDictionary["Set_CheckUnverified"] = "Onaysız Yayınları Kontrol Et";
            
            _currentDictionary["Set_DataMgmt"] = "Veri Yönetimi";
            _currentDictionary["Set_OptLib"] = "✨ Kütüphaneyi Optimize Et";
            _currentDictionary["Set_ResetChannels"] = "🚨 Tüm Kanalları Sıfırla";
            
            _currentDictionary["Set_AddEpg"] = "Yeni EPG Kaynağı Ekle";
            _currentDictionary["Set_EpgUrl"] = "EPG URL veya Dosya: ";
            _currentDictionary["Set_AddedEpgs"] = "Ekli EPG Listeleri";
            _currentDictionary["Set_DelSelectedEpg"] = "Seçili EPG'yi Sil";
            _currentDictionary["Set_ResetEpgs"] = "🚨 Tüm EPG Verisini Sıfırla";
            
            _currentDictionary["Set_PlayerSetDesc"] = "Oynatıcı Ayarları (VLC & AceStream)";
            _currentDictionary["Set_StartEngine"] = "Motoru Başlat";
            _currentDictionary["Set_NetLocal"] = "Ağ & Yerel Sunucu M3U";
            _currentDictionary["Set_NetDesc"] = "Smart TV veya TiviMate gibi uygulamalar için bu adresi kullanabilirsiniz.";
            _currentDictionary["Set_StartServer"] = "Sunucuyu Başlat";
            _currentDictionary["Status_Off"] = "Durum: Kapalı";
            _currentDictionary["Status_Checking"] = "Kontrol ediliyor...";
            
            // -- Player --
            _currentDictionary["Player_Loading"] = "Kanal Yükleniyor...";
            _currentDictionary["Player_NoCategory"] = "Kategori Yok";
            _currentDictionary["Player_EpgWait"] = "EPG Bilgisi Bekleniyor...";
            _currentDictionary["Player_NextEpgWait"] = "Sonraki: 00:00 Program Yok";
            
            // -- Stats --
            _currentDictionary["Stats_Title"] = "Bulut Senkronizasyonu ve İstatistikler";
            _currentDictionary["Stats_DB"] = "📂 Veritabanı";
            _currentDictionary["Stats_TotChan"] = "Toplam Kanal: {0}";
            _currentDictionary["Stats_CloudSync"] = "☁️ Bulut Senkronizasyon";
            _currentDictionary["Stats_GitRecv"] = "GitHub'dan Gelen: {0}";
            _currentDictionary["Stats_GitSync"] = "Son Okuma: {0}";
            _currentDictionary["Stats_FbPush"] = "Firebase Havuza Gönderilen: {0}";
            _currentDictionary["Stats_SysConsole"] = "🖥 Sistem ve Ağ Konsolu";
            _currentDictionary["Stats_Waiting"] = "Bekleniyor";

            if (languageName == "Almanca")
            {
                _currentDictionary["Title"] = "StreamMesh - Anmelden / Registrieren";
                _currentDictionary["StreamMeshNetwork"] = "StreamMesh Cloud Beitreten";
                _currentDictionary["LoginDesc"] = "Wenn Sie kein Konto haben, wird es automatisch erstellt (Cloud-basiert)";
                _currentDictionary["EmailOrUser"] = "E-Mail oder Benutzername";
                _currentDictionary["Password"] = "Passwort";
                _currentDictionary["Country"] = "Ihr Land";
                _currentDictionary["KnownLangs"] = "Andere Sprachen, die Sie kennen";
                _currentDictionary["AppLang"] = "App-Sprache";
                _currentDictionary["GuestLogin"] = "Als Gast anmelden";
                _currentDictionary["LoginReg"] = "Anmelden / Registrieren";
                _currentDictionary["NavLibrary"] = "Bibliothek";
                _currentDictionary["NavPlayer"] = "Spieler";
                _currentDictionary["NavStats"] = "Statistiken";
                _currentDictionary["NavSettings"] = "Einstellungen";
                
                _currentDictionary["Home_MyLibrary"] = "Meine Bibliothek (Aktuell)";
                _currentDictionary["Home_AdSpace"] = "Reklame / Sponsoren";
                _currentDictionary["Home_All"] = "Alle";
                _currentDictionary["Home_Favorites"] = "Favoriten ⭐";
                _currentDictionary["Home_TV"] = "TV";
                _currentDictionary["Home_Movies"] = "Filme";
                _currentDictionary["Home_Series"] = "Serien";
                _currentDictionary["Home_Radio"] = "Radio";
                _currentDictionary["Home_Search"] = "🔍 Kanal Suchen...";
                _currentDictionary["Home_Total"] = "Gesamt: {0} Inhalt";
                _currentDictionary["Home_Page"] = "Seite {0} / {1}";
                _currentDictionary["Home_Prev"] = "< Zurück";
                _currentDictionary["Home_Next"] = "Weiter >";
                
                _currentDictionary["Set_Title"] = "Einstellungen";
                _currentDictionary["Set_TabResource"] = "Quellen / Streams";
                _currentDictionary["Set_TabEpg"] = "Quellen / EPG";
                _currentDictionary["Set_TabApp"] = "App";
                _currentDictionary["Set_AppLang"] = "App-Sprache";
                _currentDictionary["Set_AppLangDesc"] = "Hier können Sie die Oberflächensprache der Anwendung ändern.";
                _currentDictionary["Set_LangSelect"] = "Sprachauswahl";
                _currentDictionary["Set_TabPlayer"] = "Spieler Einst.";
                _currentDictionary["Set_TabNetwork"] = "System / Netzwerk";
                
                _currentDictionary["Set_AddStream"] = "Neue Wiedergabeliste Hinzufügen";
                _currentDictionary["Set_LinkType"] = "M3U / DPL / Link: ";
                _currentDictionary["Set_Browse"] = "Durchsuchen";
                _currentDictionary["Set_Load"] = "Laden";
                _currentDictionary["Set_AddedStreams"] = "Hinzugefügte Listen";
                _currentDictionary["Set_Reload"] = "🔄 Neu laden";
                _currentDictionary["Set_EditSrc"] = "Quelle bearbeiten";
                _currentDictionary["Set_DelSrc"] = "Quelle löschen";
                _currentDictionary["Set_DelSelectedSrc"] = "Ausgewählte Quelle löschen";
                
                _currentDictionary["Set_StreamCheck"] = "Stream Überprüfung";
                _currentDictionary["Set_StreamCheckDesc"] = "Prüft, ob IPTV-Streams aktiv sind. Nicht funktionierende werden gelöscht.";
                _currentDictionary["Set_CheckAll"] = "Alle Kanäle Prüfen";
                _currentDictionary["Set_CheckUnverified"] = "Ungeprüfte Kanäle Prüfen";
                
                _currentDictionary["Set_DataMgmt"] = "Datenverwaltung";
                _currentDictionary["Set_OptLib"] = "✨ Bibliothek Optimieren";
                _currentDictionary["Set_ResetChannels"] = "🚨 Alle Kanäle Zurücksetzen";
                
                _currentDictionary["Set_AddEpg"] = "Neue EPG-Quelle";
                _currentDictionary["Set_EpgUrl"] = "EPG URL oder Datei: ";
                _currentDictionary["Set_AddedEpgs"] = "Hinzugefügte EPG-Listen";
                _currentDictionary["Set_DelSelectedEpg"] = "EPG löschen";
                _currentDictionary["Set_ResetEpgs"] = "🚨 Alle EPG Daten Zurücksetzen";
                
                _currentDictionary["Set_PlayerSetDesc"] = "Spieler Einstellungen (VLC & AceStream)";
                _currentDictionary["Set_StartEngine"] = "Motor Starten";
                _currentDictionary["Set_NetLocal"] = "Lokaler Server M3U";
                _currentDictionary["Set_NetDesc"] = "Verwenden Sie diese Adresse für Smart-TVs.";
                _currentDictionary["Set_StartServer"] = "Server Starten";
                _currentDictionary["Status_Off"] = "Status: Aus";
                _currentDictionary["Status_Checking"] = "Überprüfen...";
                
                _currentDictionary["Player_Loading"] = "Kanal Lädt...";
                _currentDictionary["Player_NoCategory"] = "Keine Kategorie";
                _currentDictionary["Player_EpgWait"] = "EPG-Informationen Warten...";
                _currentDictionary["Player_NextEpgWait"] = "Nächste: 00:00 Kein Programm";
                
                _currentDictionary["Stats_Title"] = "Cloud-Synchronisation und Statistiken";
                _currentDictionary["Stats_DB"] = "📂 Datenbank";
                _currentDictionary["Stats_TotChan"] = "Gesamtzahl Kanäle: {0}";
                _currentDictionary["Stats_CloudSync"] = "☁️ Cloud-Synchronisation";
                _currentDictionary["Stats_GitRecv"] = "Von GitHub empfangen: {0}";
                _currentDictionary["Stats_GitSync"] = "Letzter Abruf: {0}";
                _currentDictionary["Stats_FbPush"] = "Gesendet an Firebase: {0}";
                _currentDictionary["Stats_SysConsole"] = "🖥 System- und Netzwerkkonsole";
                _currentDictionary["Stats_Waiting"] = "Warten";
            }
            else if (languageName == "İngilizce")
            {
                _currentDictionary["Title"] = "StreamMesh - Login / Register";
                _currentDictionary["StreamMeshNetwork"] = "Join StreamMesh Cloud";
                _currentDictionary["LoginDesc"] = "If you don't have an account, it will be auto-created (Cloud based)";
                _currentDictionary["EmailOrUser"] = "Email or Username";
                _currentDictionary["Password"] = "Password";
                _currentDictionary["Country"] = "Your Country";
                _currentDictionary["KnownLangs"] = "Other Languages You Know";
                _currentDictionary["AppLang"] = "App Language";
                _currentDictionary["GuestLogin"] = "Guest Login";
                _currentDictionary["LoginReg"] = "Login / Register";
                _currentDictionary["NavLibrary"] = "Library";
                _currentDictionary["NavPlayer"] = "Player";
                _currentDictionary["NavStats"] = "Statistics";
                _currentDictionary["NavSettings"] = "Settings & Server";
                
                _currentDictionary["Home_MyLibrary"] = "My Library (Live)";
                _currentDictionary["Home_AdSpace"] = "Ad / Sponsor Space";
                _currentDictionary["Home_All"] = "All";
                _currentDictionary["Home_Favorites"] = "Favorites ⭐";
                _currentDictionary["Home_TV"] = "TV";
                _currentDictionary["Home_Movies"] = "Movies";
                _currentDictionary["Home_Series"] = "Series";
                _currentDictionary["Home_Radio"] = "Radio";
                _currentDictionary["Home_Search"] = "🔍 Search channel...";
                _currentDictionary["Home_Total"] = "Total: {0} Content";
                _currentDictionary["Home_Page"] = "Page {0} / {1}";
                _currentDictionary["Home_Prev"] = "< Prev";
                _currentDictionary["Home_Next"] = "Next >";
                
                _currentDictionary["Set_Title"] = "Settings";
                _currentDictionary["Set_TabResource"] = "Sources / Stream";
                _currentDictionary["Set_TabEpg"] = "Sources / EPG";
                _currentDictionary["Set_TabApp"] = "App";
                _currentDictionary["Set_AppLang"] = "App Language";
                _currentDictionary["Set_AppLangDesc"] = "You can change the application interface language here.";
                _currentDictionary["Set_LangSelect"] = "Language Selection";
                _currentDictionary["Set_TabPlayer"] = "Player Settings";
                _currentDictionary["Set_TabNetwork"] = "System / Network";
                
                _currentDictionary["Set_AddStream"] = "Add New Playlist Source";
                _currentDictionary["Set_LinkType"] = "M3U / DPL / Link: ";
                _currentDictionary["Set_Browse"] = "Browse";
                _currentDictionary["Set_Load"] = "Load";
                _currentDictionary["Set_AddedStreams"] = "Added Playlists";
                _currentDictionary["Set_Reload"] = "🔄 Reload";
                _currentDictionary["Set_EditSrc"] = "Edit Source";
                _currentDictionary["Set_DelSrc"] = "Delete Source";
                _currentDictionary["Set_DelSelectedSrc"] = "Delete Selected Source";
                
                _currentDictionary["Set_StreamCheck"] = "Stream Validation";
                _currentDictionary["Set_StreamCheckDesc"] = "Check if IPTV streams are active. Dead streams will be deleted.";
                _currentDictionary["Set_CheckAll"] = "Check All Channels";
                _currentDictionary["Set_CheckUnverified"] = "Check Unverified Channels";
                
                _currentDictionary["Set_DataMgmt"] = "Data Management";
                _currentDictionary["Set_OptLib"] = "✨ Optimize Library";
                _currentDictionary["Set_ResetChannels"] = "🚨 Reset All Channels";
                
                _currentDictionary["Set_AddEpg"] = "Add New EPG Source";
                _currentDictionary["Set_EpgUrl"] = "EPG URL or File: ";
                _currentDictionary["Set_AddedEpgs"] = "Added EPG Lists";
                _currentDictionary["Set_DelSelectedEpg"] = "Delete Selected EPG";
                _currentDictionary["Set_ResetEpgs"] = "🚨 Reset All EPG Data";
                
                _currentDictionary["Set_PlayerSetDesc"] = "Player Settings (VLC & AceStream)";
                _currentDictionary["Set_StartEngine"] = "Start Engine";
                _currentDictionary["Set_NetLocal"] = "Network & Local Server M3U";
                _currentDictionary["Set_NetDesc"] = "You can use this address for apps like Smart TV.";
                _currentDictionary["Set_StartServer"] = "Start Server";
                _currentDictionary["Status_Off"] = "Status: Off";
                _currentDictionary["Status_Checking"] = "Checking...";
                
                _currentDictionary["Player_Loading"] = "Channel Loading...";
                _currentDictionary["Player_NoCategory"] = "No Category";
                _currentDictionary["Player_EpgWait"] = "Awaiting EPG Info...";
                _currentDictionary["Player_NextEpgWait"] = "Next: 00:00 No Program";
                
                _currentDictionary["Stats_Title"] = "Cloud Sync and Statistics";
                _currentDictionary["Stats_DB"] = "📂 Database";
                _currentDictionary["Stats_TotChan"] = "Total Channels: {0}";
                _currentDictionary["Stats_CloudSync"] = "☁️ Cloud Synchronization";
                _currentDictionary["Stats_GitRecv"] = "Received from GitHub: {0}";
                _currentDictionary["Stats_GitSync"] = "Last Sync: {0}";
                _currentDictionary["Stats_FbPush"] = "Pushed to Firebase: {0}";
                _currentDictionary["Stats_SysConsole"] = "🖥 System & Network Console";
                _currentDictionary["Stats_Waiting"] = "Waiting";
            }

            OnPropertyChanged(System.Windows.Data.Binding.IndexerName);
        }

        private void UpdateLanguage(string language)
        {
            LoadTranslations(language);
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // --- Lists requested by user ---
        
        public static readonly List<string> Top50Languages = new List<string>
        {
            "Türkçe", "İngilizce", "Almanca", "İspanyolca", "Fransızca", 
            "İtalyanca", "Rusça", "Portekizce", "Arapça", "Çince",
            "Japonca", "Korece", "Hintçe", "Bengalce", "Urduca",
            "Endonezce", "Farsça", "Kürtçe", "Azerice", "Kazakça",
            "Özbekçe", "Türkmence", "Felemenkçe", "Lehçe", "Ukraynaca",
            "Romence", "Macarca", "Çekçe", "Yunanca", "İsveççe",
            "Bulgarca", "Sırpça", "Hırvatça", "Slovakça", "Danca",
            "Fince", "Norveççe", "Gürcüce", "Ermenice", "İbranice",
            "Vietnamca", "Tayca", "Malayca", "Svahili", "Seylanca",
            "Amharca", "Afrikanca", "Tamilce", "Telugu", "Marathice"
        };
        
        public static List<string> SystemCultures
        {
            get
            {
                var cultures = System.Globalization.CultureInfo.GetCultures(System.Globalization.CultureTypes.SpecificCultures)
                    .Select(c => c.NativeName)
                    .Distinct()
                    .OrderBy(n => n)
                    .ToList();
                return cultures;
            }
        }

        public static List<string> SystemCulturesWithNone
        {
            get
            {
                var list = new List<string> { "Hiçbiri", "Bilinmiyor" };
                list.AddRange(SystemCultures);
                return list;
            }
        }

        private static List<string> _cachedSystemLanguages;
        public static List<string> SystemLanguages
        {
            get
            {
                if (_cachedSystemLanguages != null) return _cachedSystemLanguages;

                var list = new List<string>
                {
                    "Türkçe", "İngilizce", "Almanca", "Fransızca", "İspanyolca", "İtalyanca", "Rusça", "Portekizce", "Arapça", "Çince",
                    "Arnavutça", "Azerice", "Boşnakça", "Bulgarca", "Hırvatça", "Sırpça", "Makedonca", "Slovence", "Slovakça", "Çekçe",
                    "Lehçe", "Romence", "Yunanca", "Kürtçe", "Gürcüce", "Ermenice", "Felemenkçe", "İsveççe", "Norveççe", "Danca",
                    "Fince", "Ukraynaca", "Macarca", "Estonyaca", "Letonca", "Litvanyaca", "Katalanca", "Baskça", "Galce", "İrlandaca",
                    "Farsça", "Türkmence", "Kazakça", "Özbekçe", "Kırgızca", "Tacikçe", "Moğolca", "Japonca", "Korece", "Vietnamca",
                    "Tayca", "Malayca", "Endonezce", "Filipince", "Hintçe", "Urduca", "Tamilce", "Bengalce", "İbranice", "Amharca", 
                    "Svahili", "Somalice", "Afrikanca", "Balkan Dilleri"
                };

                try
                {
                    var neutralCultures = System.Globalization.CultureInfo.GetCultures(System.Globalization.CultureTypes.NeutralCultures);
                    foreach (var c in neutralCultures)
                    {
                        string displayName = c.DisplayName;
                        if (!string.IsNullOrEmpty(displayName) && !displayName.Contains("Invariant"))
                        {
                            displayName = char.ToUpper(displayName[0], new System.Globalization.CultureInfo("tr-TR")) + 
                                          (displayName.Length > 1 ? displayName.Substring(1) : "");

                            if (!list.Contains(displayName))
                            {
                                list.Add(displayName);
                            }
                        }

                        string nativeName = c.NativeName;
                        if (!string.IsNullOrEmpty(nativeName) && !nativeName.Contains("Invariant"))
                        {
                            nativeName = char.ToUpper(nativeName[0], new System.Globalization.CultureInfo("tr-TR")) + 
                                         (nativeName.Length > 1 ? nativeName.Substring(1) : "");

                            if (!list.Contains(nativeName))
                            {
                                list.Add(nativeName);
                            }
                        }
                    }
                }
                catch { }

                _cachedSystemLanguages = list.Distinct().OrderBy(x => x).ToList();
                return _cachedSystemLanguages;
            }
        }

        public static List<string> SystemLanguagesWithNone
        {
            get
            {
                var list = new List<string> { "Hiçbiri", "Bilinmiyor", "Hepsi", "Tümü" };
                list.AddRange(SystemLanguages);
                return list;
            }
        }

        public static readonly List<string> AllCountries = new List<string>
        {
            "Türkiye", "Almanya", "Amerika Birleşik Devletleri", "Birleşik Krallık", "Fransa", 
            "İtalya", "İspanya", "Hollanda", "Rusya", "Çin", "Japonya", "Güney Kore", 
            "Avustralya", "Brezilya", "Kanada", "Hindistan", "Meksika", "Arjantin", "Güney Afrika",
            "Mısır", "Suudi Arabistan", "İran", "Irak", "Suriye", "Yunanistan", "Bulgaristan",
            "Romanya", "Polonya", "Ukrayna", "İsveç", "Norveç", "Finlandiya", "Danimarka",
            "İsviçre", "Avusturya", "Belçika", "İrlanda", "Portekiz", "Yeni Zelanda",
            "Endonezya", "Malezya", "Filipinler", "Tayland", "Vietnam", "Pakistan", "Bangladeş",
            "Cezayir", "Nijerya", "Kenya", "Fas", "Azerbaycan", "Kazakistan", "Özbekistan",
            "Türkmenistan", "Kırgızistan"
        }; // can be expanded

        public static readonly List<string> KnownLanguagesList = new List<string>
        {
            "Tümü (Tüm Ülkeler)",
            "Türkiye (Türkçe)",
            "Almanya (Almanca)",
            "Amerika Birleşik Devletleri (İngilizce)",
            "İspanya (İspanyolca)",
            "Fransa (Fransızca)",
            "İtalya (İtalyanca)",
            "Rusya (Rusça)",
            "Portekiz (Portekizce)",
            "Çin (Çince)",
            "Japonya (Japonca)"
        };
    }
}
