using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StreamMesh.Core.Media.Ace;
using StreamMesh.Models;

namespace StreamMesh.Core.Media
{
    /// <summary>
    /// Facade coordinating AceStream operations with 100% backward compatibility.
    /// Modular implementations reside in StreamMesh.Core.Media.Ace namespace.
    /// </summary>
    public class AceEngine
    {
        public static AceEngine Instance { get; } = new AceEngine();

        // Process Management
        public static string GetEngineExecutablePath() => AceProcessManager.GetEngineExecutablePath();
        public static bool IsAnyAceProcessRunning() => AceProcessManager.IsAnyAceProcessRunning();
        public static int GetAceProcessCount() => AceProcessManager.GetAceProcessCount();
        public Task<bool> IsEngineHttpAliveAsync() => AceProcessManager.IsEngineHttpAliveAsync();
        public Task<bool> IsEngineRunningAsync() => AceProcessManager.IsEngineRunningAsync();
        public Task<bool> StartEngineIfNotRunningAsync() => AceProcessManager.StartEngineIfNotRunningAsync();
        public Task<bool> StartEngineAsync() => AceProcessManager.StartEngineIfNotRunningAsync();
        public Task<bool> StartAceEngineAsync() => AceProcessManager.StartEngineIfNotRunningAsync();
        public void ShutdownEngine() => AceProcessManager.ShutdownEngine();

        // ID / Hash Extraction
        public bool IsAceStreamUrl(string? url) => AceIdExtractor.IsAceStreamUrl(url);
        public bool IsAceStream(string? url) => AceIdExtractor.IsAceStreamUrl(url);
        public string ExtractPid(string url) => AceIdExtractor.ExtractPid(url);
        public string ExtractAceId(string url) => AceIdExtractor.ExtractAceId(url);
        public string ExtractHash(string url) => AceIdExtractor.ExtractAceId(url);
        public static string Find40CharContentId(JToken? token) => AceIdExtractor.Find40CharContentId(token);

        // Stream & Token Resolution
        public Task<string> ResolveChannelIdToInfohashAsync(string channelId) =>
            AceStreamTokenResolver.ResolveChannelIdToInfohashAsync(channelId);
        public Task<List<string>> GetHttpUrlsWithTokenAsync(string aceUrl) =>
            AceStreamTokenResolver.GetHttpUrlsWithTokenAsync(aceUrl);
        public Task<string?> GetApiAccessTokenAsync() => AceStreamTokenResolver.GetApiAccessTokenAsync();
        public Task<bool> WaitForStreamReadyAsync(string httpUrl, int maxWaitSeconds = 6) =>
            AceStreamTokenResolver.WaitForStreamReadyAsync(httpUrl, maxWaitSeconds);
        public Task<int> GetStreamPeersAsync(string streamUrl) => AceStreamTokenResolver.GetStreamPeersAsync(streamUrl);
        public Task StopAllStreamsAsync() => AceStreamTokenResolver.StopAllStreamsAsync();

        // Search Engine
        public Task<List<SearchResultItem>> SearchAceStreamP2pAsync(string query) =>
            AceSearchEngine.SearchAceStreamP2pAsync(query);
        public Task<List<SearchResultItem>> SearchAceStreamWebAsync(string query, string domain = "all") =>
            AceSearchEngine.SearchAceStreamWebAsync(query, domain);
        public Task<List<SearchResultItem>> SearchAceStreamsAsync(string query) =>
            AceSearchEngine.SearchAceStreamsAsync(query);
    }
}
