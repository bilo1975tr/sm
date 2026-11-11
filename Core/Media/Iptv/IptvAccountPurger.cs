using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StreamMesh.Core.Database;
using StreamMesh.Core.Utils;
using StreamMesh.Models;

namespace StreamMesh.Core.Media.Iptv
{
    public class PurgeResult
    {
        public int ChannelsDeletedEntirely { get; set; }
        public int ChannelsModifiedSourcesRemoved { get; set; }
        public int TotalUrlsRemoved { get; set; }
        public int SourcesRemovedFromM3uSources { get; set; }
    }

    /// <summary>
    /// Handles database cleanup, removal of expired provider links, and account ownership detection.
    /// </summary>
    public static class IptvAccountPurger
    {
        public static bool UrlBelongsToAccount(string? url, IptvAccountInfo? account)
        {
            if (string.IsNullOrWhiteSpace(url) || account == null) return false;

            if (!url.Contains(account.HostWithPort, StringComparison.OrdinalIgnoreCase)) return false;
            if (!url.Contains(account.Username, StringComparison.OrdinalIgnoreCase)) return false;

            if (!string.IsNullOrEmpty(account.Password) && url.Contains(account.Password, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var parsed = IptvUrlParser.ParseAccountFromUrl(url);
            if (parsed != null && parsed.SignatureKey == account.SignatureKey)
            {
                return true;
            }

            return false;
        }

        public static bool ChannelBelongsToAccount(Channel? channel, IptvAccountInfo? account)
        {
            if (channel == null || account == null) return false;

            string pUrl = (channel.PlaylistUrl ?? "").Trim();
            if (!string.IsNullOrEmpty(pUrl) && pUrl.Contains(account.HostWithPort, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(account.Username) || pUrl.Contains(account.Username, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            foreach (var u in channel.GetUrlList())
            {
                if (UrlBelongsToAccount(u, account))
                {
                    return true;
                }
            }
            return false;
        }

        public static async Task<PurgeResult> PurgeAccountFromDatabaseAsync(IptvAccountInfo account, List<Channel>? sourceChannels = null)
        {
            var result = new PurgeResult();
            if (account == null) return result;

            var db = new DatabaseEngine();
            var allChannels = sourceChannels ?? await db.GetChannelsByAccountAsync(account.HostWithPort, account.Username);

            var channelsToDelete = new List<string>();
            var channelsToUpdate = new List<Channel>();

            foreach (var ch in allChannels)
            {
                var urls = ch.GetUrlList();
                if (urls.Count == 0) continue;

                var matchingUrls = urls.Where(u => UrlBelongsToAccount(u, account)).ToList();
                if (matchingUrls.Count == 0) continue;

                result.TotalUrlsRemoved += matchingUrls.Count;
                var remainingUrls = urls.Where(u => !UrlBelongsToAccount(u, account)).ToList();

                if (remainingUrls.Count == 0)
                {
                    channelsToDelete.Add(ch.Id);
                    result.ChannelsDeletedEntirely++;
                }
                else
                {
                    ch.Url = string.Join(",", remainingUrls);
                    if (ch.PreferredUrlIndex >= remainingUrls.Count)
                    {
                        ch.PreferredUrlIndex = 0;
                    }
                    channelsToUpdate.Add(ch);
                    result.ChannelsModifiedSourcesRemoved++;
                }
            }

            if (channelsToDelete.Count > 0)
            {
                await db.DeleteChannelsAsync(channelsToDelete);
                LogService.LogInfo($"[IptvAccountPurger] Deleted {channelsToDelete.Count} channels that had only this account.");
            }

            if (channelsToUpdate.Count > 0)
            {
                await db.SaveChannelsBatchAsync(channelsToUpdate, clearFirst: false, notifyUpdated: false);
                LogService.LogInfo($"[IptvAccountPurger] Updated {channelsToUpdate.Count} channels removing dead URLs.");
            }

            try
            {
                var m3uSources = db.GetM3uSources();
                foreach (var src in m3uSources)
                {
                    if (UrlBelongsToAccount(src, account))
                    {
                        db.RemoveM3uSource(src);
                        result.SourcesRemovedFromM3uSources++;
                    }
                }
            }
            catch { }

            try
            {
                var iptvAccs = db.GetAllIptvAccounts();
                foreach (var acc in iptvAccs)
                {
                    if (acc.Username.Equals(account.Username, StringComparison.OrdinalIgnoreCase) &&
                        acc.ServerUrl.Contains(account.HostWithPort, StringComparison.OrdinalIgnoreCase))
                    {
                        db.RemoveIptvAccount(acc.Id);
                    }
                }
            }
            catch { }

            return result;
        }

        public static async Task<(int totalChannels, int totalUrls, int exclusiveChannels)> CountAccountOccurrencesAsync(IptvAccountInfo? account)
        {
            if (account == null) return (0, 0, 0);

            var db = new DatabaseEngine();
            int total = await db.CountChannelsByAccountAsync(account.HostWithPort, account.Username).ConfigureAwait(false);
            return (total, total, total);
        }
    }
}
