using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StreamMesh.Core.Media.Iptv;
using StreamMesh.Models;

namespace StreamMesh.Core.Media
{
    /// <summary>
    /// Facade providing unified access to IPTV account parsing, verification, purging, and ingestion.
    /// Modular implementations reside in StreamMesh.Core.Media.Iptv.
    /// </summary>
    public static class IptvAccountHelper
    {
        public static string ExtractHostAndPort(string? url) => IptvUrlParser.ExtractHostAndPort(url);

        public static bool IsIgnoredHost(string host) => IptvHostFilter.IsIgnoredHost(host);

        public static bool IsFailedAccount(IptvAccountInfo account) => IptvHostFilter.IsFailedAccount(account);

        public static void MarkAccountFailed(IptvAccountInfo account, bool markHostFailed = false) =>
            IptvHostFilter.MarkAccountFailed(account, markHostFailed);

        public static Task<bool> FastCheckHostIsIptvAsync(IptvAccountInfo account, int timeoutMs = 1500) =>
            IptvAccountVerifier.FastCheckHostIsIptvAsync(account, timeoutMs);

        public static Task<bool> FastVerifyAccountAsync(IptvAccountInfo account, int timeoutMs = 1500) =>
            IptvAccountVerifier.FastVerifyAccountAsync(account, timeoutMs);

        public static IptvAccountInfo? ParseAccountFromUrl(string url) => IptvUrlParser.ParseAccountFromUrl(url);

        public static bool UrlBelongsToAccount(string url, IptvAccountInfo account) =>
            IptvAccountPurger.UrlBelongsToAccount(url, account);

        public static bool ChannelBelongsToAccount(Channel? channel, IptvAccountInfo? account) =>
            IptvAccountPurger.ChannelBelongsToAccount(channel, account);

        public static Task<PurgeResult> PurgeAccountFromDatabaseAsync(IptvAccountInfo account, List<Channel>? sourceChannels = null) =>
            IptvAccountPurger.PurgeAccountFromDatabaseAsync(account, sourceChannels);

        public static Task<(int totalChannels, int totalUrls, int exclusiveChannels)> CountAccountOccurrencesAsync(IptvAccountInfo account) =>
            IptvAccountPurger.CountAccountOccurrencesAsync(account);

        public static string GenerateFullM3uUrl(IptvAccountInfo account, string? outputFormat = null) =>
            IptvAccountImporter.GenerateFullM3uUrl(account, outputFormat);

        public static bool AddOrRefreshProviderEpg(IptvAccountInfo account) =>
            IptvAccountImporter.AddOrRefreshProviderEpg(account);

        public static Task<(bool success, int importedCount, string message)> FetchAndImportFullAccountAsync(
            IptvAccountInfo account, 
            Action<string, double>? progress = null) =>
            IptvAccountImporter.FetchAndImportFullAccountAsync(account, progress);
    }
}
