using System;
using System.Collections.Generic;

namespace StreamMesh.Models
{
    public class UserProfile
    {
        public string Email { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty; // Hashed locally
        public string Country { get; set; } = "Türkiye";
        public List<string> Languages { get; set; } = new List<string> { "Türkçe" }; // Up to 3 languages
        public string AppLanguage { get; set; } = "Türkçe";
        public string ReferralCode { get; set; } = string.Empty;
        public string ReferredBy { get; set; } = string.Empty;
        public bool IsPremium { get; set; }
        public DateTime PremiumExpiry { get; set; }
        public DateTime LastLoginTime { get; set; }
        public bool WeeklyMovieAndChannelUpdateEnabled { get; set; } = true;
        public DateTime LastMovieAndChannelUpdateTime { get; set; } = DateTime.MinValue;
    }
}
