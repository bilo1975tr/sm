using System;
using System.Collections.Generic;
using System.Threading;
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
        public int GetSlotHttpPort(int slotIndex) => AceProcessManager.GetSlotHttpPort(slotIndex);
        public Task<bool> IsEngineHttpAliveAsync() => AceProcessManager.IsEngineHttpAliveAsync();
        public Task<bool> IsEngineRunningAsync() => AceProcessManager.IsEngineRunningAsync();
        public Task<bool> StartEngineIfNotRunningAsync() => AceProcessManager.StartEngineIfNotRunningAsync();
        public Task<bool> StartEngineAsync() => AceProcessManager.StartEngineIfNotRunningAsync();
        public Task<bool> StartAceEngineAsync() => AceProcessManager.StartEngineIfNotRunningAsync();
        public Task<int> StartSlotEngineAsync(int slotIndex, CancellationToken token = default) => AceProcessManager.StartSlotEngineAsync(slotIndex, token);
        public Task StopSlotEngineAsync(int slotIndex) => AceProcessManager.StopSlotEngineAsync(slotIndex);
        public Task StopAllAuxiliaryEnginesAsync() => AceProcessManager.StopAllAuxiliaryEnginesAsync();
        public void ShutdownEngine() => AceProcessManager.ShutdownEngine();

        // ID / Hash Extraction
        public bool IsAceStreamUrl(string? url) => AceIdExtractor.IsAceStreamUrl(url);
        public bool IsLocalAceHttpUrl(string? url) => AceIdExtractor.IsLocalAceHttpUrl(url);
        public string ExtractPid(string url) => AceIdExtractor.ExtractPid(url);
        public string ExtractAceId(string url) => AceIdExtractor.ExtractAceId(url);
        public string ExtractHash(string url) => AceIdExtractor.ExtractAceId(url);
        public static string Find40CharContentId(JToken? token) => AceIdExtractor.Find40CharContentId(token);

        // Stream & Token Resolution
        public Task<string> ResolveChannelIdToInfohashAsync(string channelId, int httpPort = AceProcessManager.BaseHttpPort) =>
            AceStreamTokenResolver.ResolveChannelIdToInfohashAsync(channelId, httpPort);
        public Task<List<string>> GetHttpUrlsWithTokenAsync(string aceUrl) =>
            AceStreamTokenResolver.GetHttpUrlsWithTokenAsync(aceUrl);
        public Task<string?> GetApiAccessTokenAsync() => AceStreamTokenResolver.GetApiAccessTokenAsync();
        public Task<bool> WaitForStreamReadyAsync(string httpUrl, int maxWaitSeconds = 6) =>
            AceStreamTokenResolver.WaitForStreamReadyAsync(httpUrl, maxWaitSeconds);
        public Task<int> GetStreamPeersAsync(string streamUrl) => AceStreamTokenResolver.GetStreamPeersAsync(streamUrl);
        public Task StopAllStreamsOnPortAsync(int httpPort) => AceStreamTokenResolver.StopAllStreamsOnPortAsync(httpPort);
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
