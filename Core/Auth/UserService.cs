using System;
using StreamMesh.Models;

namespace StreamMesh.Core.Auth
{
    public static class UserService
    {
        public static UserProfile CurrentUser { get; private set; } = new UserProfile 
        { 
            Email = "user@streammesh.local", 
            IsPremium = true,
            LastLoginTime = DateTime.Now 
        };
    }
}
