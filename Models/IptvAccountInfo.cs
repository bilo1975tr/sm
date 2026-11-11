using System;

namespace StreamMesh.Models
{
    /// <summary>
    /// Represents parsed IPTV provider account credentials and server endpoint.
    /// </summary>
    public class IptvAccountInfo
    {
        public string HostWithPort { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string SignatureKey => $"{HostWithPort}/{Username}/{Password}".ToLowerInvariant();
        public string DisplaySummary => $"{Username} @ {HostWithPort}";

        public static IptvAccountInfo FromAccount(IptvAccount? acc)
        {
            if (acc == null) return new IptvAccountInfo();
            return new IptvAccountInfo
            {
                HostWithPort = acc.ServerUrl ?? "",
                Username = acc.Username ?? "",
                Password = acc.Password ?? ""
            };
        }
    }
}
