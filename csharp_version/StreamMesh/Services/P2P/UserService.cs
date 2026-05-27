using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using StreamMesh.Models;

namespace StreamMesh.Services.P2P
{
    public static class UserService
    {
        private static readonly string UserFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "users.dat");
        public static UserProfile CurrentUser { get; private set; }

        public static bool AutoLogin()
        {
            if (File.Exists(UserFilePath))
            {
                try
                {
                    byte[] cipher = File.ReadAllBytes(UserFilePath);
                    string json = EncryptionService.Decrypt(cipher);
                    if (!string.IsNullOrEmpty(json))
                    {
                        var user = JsonConvert.DeserializeObject<UserProfile>(json);
                        if (user != null)
                        {
                            if ((DateTime.UtcNow - user.LastLoginTime).TotalDays > 90)
                            {
                                File.Delete(UserFilePath);
                                return false; // 90 days policy
                            }
                            user.LastLoginTime = DateTime.UtcNow;
                            CurrentUser = user;
                            SaveUser();
                            LocalizationManager.Instance.CurrentLanguage = CurrentUser.AppLanguage ?? "Türkçe";
                            return true;
                        }
                    }
                }
                catch { }
            }
            return false;
        }

        public static void RegisterOrLogin(string email, string password, string country, string l1, string l2, string appLang)
        {
            if (File.Exists(UserFilePath))
            {
                byte[] cipher = File.ReadAllBytes(UserFilePath);
                string json = EncryptionService.Decrypt(cipher);
                if (!string.IsNullOrEmpty(json))
                {
                    var user = JsonConvert.DeserializeObject<UserProfile>(json);
                    if (user != null && user.Email == email && user.PasswordHash == HashPassword(password))
                    {
                        user.LastLoginTime = DateTime.UtcNow;
                        user.Country = country;
                        
                        var langs = new System.Collections.Generic.List<string> { country };
                        if (!string.IsNullOrEmpty(l1)) langs.Add(l1);
                        if (!string.IsNullOrEmpty(l2)) langs.Add(l2);
                        user.Languages = langs.Distinct().ToList();
                        
                        user.AppLanguage = appLang;
                        CurrentUser = user;
                        SaveUser();
                        LocalizationManager.Instance.CurrentLanguage = appLang;
                        return; // Logged in
                    }
                }
            }
            
            var langs = new System.Collections.Generic.List<string> { country };
            if (!string.IsNullOrEmpty(l1)) langs.Add(l1);
            if (!string.IsNullOrEmpty(l2)) langs.Add(l2);

            // New register or overwrite
            CurrentUser = new UserProfile
            {
                Email = email,
                PasswordHash = HashPassword(password),
                Country = country,
                Languages = langs.Distinct().ToList(),
                AppLanguage = appLang,
                IsPremium = false,
                LastLoginTime = DateTime.UtcNow
            };
            SaveUser();
            LocalizationManager.Instance.CurrentLanguage = appLang;
        }

        public static void GuestLogin(string appLang = "Türkçe")
        {
            CurrentUser = new UserProfile
            {
                Email = "Misafir",
                PasswordHash = "",
                Country = "Türkiye",
                Languages = new System.Collections.Generic.List<string> { "İngilizce" },
                AppLanguage = appLang,
                IsPremium = false,
                LastLoginTime = DateTime.UtcNow
            };
            SaveUser(); // So auto-login works next time for the guest as well
            LocalizationManager.Instance.CurrentLanguage = appLang;
        }

        public static UserProfile GetProfile()
        {
            return CurrentUser;
        }

        public static void SaveProfile(UserProfile profile)
        {
            CurrentUser = profile;
            SaveUser();
        }

        private static void SaveUser()
        {
            if (CurrentUser == null) return;
            string json = JsonConvert.SerializeObject(CurrentUser);
            byte[] cipher = EncryptionService.Encrypt(json);
            File.WriteAllBytes(UserFilePath, cipher);
        }

        private static string HashPassword(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                var builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }

        public static void EnforceInactivePolicy()
        {
            if (CurrentUser != null && (DateTime.UtcNow - CurrentUser.LastLoginTime).TotalDays > 90)
            {
                // > 3 months inactive, delete profile.
                if (File.Exists(UserFilePath)) File.Delete(UserFilePath);
                CurrentUser = null;
            }
        }
    }
}
