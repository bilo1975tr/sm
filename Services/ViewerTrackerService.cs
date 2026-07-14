using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StreamMesh.Models;

namespace StreamMesh.Services
{
    public class ViewerTrackerService
    {
        private static readonly Lazy<ViewerTrackerService> _instance = new Lazy<ViewerTrackerService>(() => new ViewerTrackerService());
        public static ViewerTrackerService Instance => _instance.Value;

        private string _activeChannelId = null;
        private CancellationTokenSource _cts;

        private ViewerTrackerService()
        {
        }

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            Task.Run(() => HeartbeatLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            if (_cts == null) return;
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }

        public void SetActiveChannel(string channelId)
        {
            string oldId = _activeChannelId;
            if (oldId == channelId) return;

            _activeChannelId = channelId;
            StunService.Instance.BroadcastStatus(channelId ?? "");
        }

        public void ClearActiveChannel()
        {
            _activeChannelId = null;
            StunService.Instance.BroadcastStatus("");
        }

        private async Task HeartbeatLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    string activeId = _activeChannelId;
                    StunService.Instance.BroadcastStatus(activeId ?? "");
                }
                catch (Exception ex)
                {
                    LogService.LogError("ViewerTracker P2P heartbeat error", ex);
                }

                try
                {
                    await Task.Delay(15000, token); // 15 seconds is perfect for real-time responsiveness
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        public async Task<Dictionary<string, int>> FetchViewerCountsAsync()
        {
            await Task.Yield();
            try
            {
                return StunService.Instance.GetP2PViewerCounts();
            }
            catch (Exception ex)
            {
                LogService.LogError("FetchViewerCountsAsync failed", ex);
                return new Dictionary<string, int>();
            }
        }
    }
}
