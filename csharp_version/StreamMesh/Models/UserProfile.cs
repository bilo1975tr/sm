using System;
using System.Collections.Generic;

namespace StreamMesh.Models
{
    public class UserProfile
    {
        public string Email { get; set; }
        public string PasswordHash { get; set; } // Hashed locally
        public string Country { get; set; } = "Türkiye";
        public List<string> Languages { get; set; } = new List<string> { "Türkçe" }; // Up to 3 languages
        public string AppLanguage { get; set; } = "Türkçe";
        public bool IsPremium { get; set; }
        public DateTime PremiumExpiry { get; set; }
        public DateTime LastLoginTime { get; set; }
    }
}
