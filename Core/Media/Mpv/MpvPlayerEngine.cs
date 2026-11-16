using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using Newtonsoft.Json.Linq;
using StreamMesh.Core.Utils;

namespace StreamMesh.Core.Media.Mpv
{
    public enum MpvPlaybackStatus
    {
        Stopped,
        Opening,
        Playing,
        Paused,
        Ended,
        Failed
    }

    public sealed class MpvTrackInfo
    {
        public int Id { get; set; }
        public string Type { get; set; } = "";
        public string Lang { get; set; } = "";
        public string Title { get; set; } = "";
        public string Codec { get; set; } = "";
        public int Channels { get; set; }
        public long BitRate { get; set; }
        public bool Selected { get; set; }
    }

    /// <summary>
    /// Thread-safe managed wrapper over libmpv with instant live audio track switching,
    /// zero-reconnect runtime DSP filters (acompressor / sharpen), and A/V clock stability.
    /// </summary>
    public sealed class MpvPlayerEngine : IDisposable
    {
        private IntPtr _handle = IntPtr.Zero;
        private Thread? _eventThread;
        private volatile bool _disposed;
        private readonly object _cmdLock = new();

        private double _positionSeconds;
        private double _durationSeconds;
        private int _videoWidth;
        private int _videoHeight;
        private string _videoCodec = "";
        private string _audioCodec = "";
        private int _volume = 100;
        private bool _isMuted;
        private bool _isPaused;
        private MpvPlaybackStatus _status = MpvPlaybackStatus.Stopped;

        public MpvPlaybackStatus Status => _status;
        public long PositionMs => (long)Math.Max(0, _positionSeconds * 1000.0);
        public long DurationMs => (long)Math.Max(0, _durationSeconds * 1000.0);
        public int VideoWidth => _videoWidth;
        public int VideoHeight => _videoHeight;
        public string VideoCodec => _videoCodec;
        public string AudioCodec => _audioCodec;
        public int Volume => _volume;
        public bool IsMuted => _isMuted;
        public bool IsInitialized => _handle != IntPtr.Zero && !_disposed;

        public event Action<MpvPlaybackStatus>? StatusChanged;
        public event Action<bool, string?>? OpenCompleted;
        public event Action? TracksChanged;

        public static bool IsLibraryAvailable() => MpvNative.IsLibraryAvailable();

        public void Initialize(IntPtr windowHandle, bool hwAccel = true, int cacheMs = 3500, string userAgent = "", bool headless = false)
        {
            if (_handle != IntPtr.Zero) return;
            if (MpvNative.EnsureLoaded() == IntPtr.Zero)
                throw new DllNotFoundException("libmpv-2.dll bulunamadı.");

            IntPtr ctx = MpvNative.mpv_create();
            if (ctx == IntPtr.Zero)
                throw new InvalidOperationException("mpv_create başarısız oldu.");

            try
            {
                if (headless || windowHandle == IntPtr.Zero)
                {
                    MpvNative.mpv_set_option_string(ctx, "vo", "null");
                    MpvNative.mpv_set_option_string(ctx, "ao", "null");
                    MpvNative.mpv_set_option_string(ctx, "vid", "1");
                    MpvNative.mpv_set_option_string(ctx, "aid", "1");
                }
                else
                {
                    MpvNative.mpv_set_option_string(ctx, "wid", windowHandle.ToInt64().ToString(CultureInfo.InvariantCulture));
                    MpvNative.mpv_set_option_string(ctx, "vo", "gpu");
                    MpvNative.mpv_set_option_string(ctx, "gpu-api", "d3d11");
                    MpvNative.mpv_set_option_string(ctx, "hwdec", hwAccel ? "auto-safe" : "no");
                }

                MpvNative.mpv_set_option_string(ctx, "idle", "yes");
                MpvNative.mpv_set_option_string(ctx, "keep-open", "no");
                MpvNative.mpv_set_option_string(ctx, "osc", "no");
                MpvNative.mpv_set_option_string(ctx, "osd-level", "0");
                MpvNative.mpv_set_option_string(ctx, "input-default-bindings", "no");
                MpvNative.mpv_set_option_string(ctx, "input-vo-keyboard", "no");
                MpvNative.mpv_set_option_string(ctx, "cursor-autohide", "no");
                MpvNative.mpv_set_option_string(ctx, "ytdl", "no");

                // Broadcast-grade A/V sync & anti-microstutter buffering
                MpvNative.mpv_set_option_string(ctx, "video-sync", "audio");
                MpvNative.mpv_set_option_string(ctx, "audio-buffer", "0.25");
                MpvNative.mpv_set_option_string(ctx, "cache", "yes");
                double cacheSecs = Math.Clamp(cacheMs / 1000.0, 2.5, 15.0);
                MpvNative.mpv_set_option_string(ctx, "cache-secs", cacheSecs.ToString("0.0", CultureInfo.InvariantCulture));
                MpvNative.mpv_set_option_string(ctx, "demuxer-max-bytes", "64MiB");
                MpvNative.mpv_set_option_string(ctx, "demuxer-max-back-bytes", "32MiB");
                MpvNative.mpv_set_option_string(ctx, "demuxer-readahead-secs", "6");
                MpvNative.mpv_set_option_string(ctx, "network-timeout", "15");
                MpvNative.mpv_set_option_string(ctx, "tls-verify", "no");
                MpvNative.mpv_set_option_string(ctx, "hls-bitrate", "max");
                MpvNative.mpv_set_option_string(ctx, "alang", "tr,tur,en,eng,de,deu");

                if (!string.IsNullOrWhiteSpace(userAgent))
                    MpvNative.mpv_set_option_string(ctx, "user-agent", userAgent);

                int initRes = MpvNative.mpv_initialize(ctx);
                if (initRes < 0)
                    throw new InvalidOperationException($"mpv_initialize hatası: {MpvNative.GetErrorString(initRes)}");

                _handle = ctx;

                MpvNative.mpv_observe_property(_handle, 1, "time-pos", MpvFormat.Double);
                MpvNative.mpv_observe_property(_handle, 2, "duration", MpvFormat.Double);
                MpvNative.mpv_observe_property(_handle, 3, "pause", MpvFormat.Flag);
                MpvNative.mpv_observe_property(_handle, 4, "track-list", MpvFormat.String);
                MpvNative.mpv_observe_property(_handle, 5, "aid", MpvFormat.String);
                MpvNative.mpv_observe_property(_handle, 6, "video-params/w", MpvFormat.Int64);
                MpvNative.mpv_observe_property(_handle, 7, "video-params/h", MpvFormat.Int64);
                MpvNative.mpv_observe_property(_handle, 8, "video-codec", MpvFormat.String);
                MpvNative.mpv_observe_property(_handle, 9, "audio-codec-name", MpvFormat.String);

                _eventThread = new Thread(EventLoop)
                {
                    Name = headless ? "MpvValidatorEventLoop" : "MpvPlayerEventLoop",
                    IsBackground = true
                };
                _eventThread.Start();
            }
            catch
            {
                MpvNative.mpv_terminate_destroy(ctx);
                _handle = IntPtr.Zero;
                throw;
            }
        }

        private void EventLoop()
        {
            while (!_disposed && _handle != IntPtr.Zero)
            {
                IntPtr evPtr = MpvNative.mpv_wait_event(_handle, 0.25);
                if (evPtr == IntPtr.Zero) continue;

                var ev = Marshal.PtrToStructure<MpvEvent>(evPtr);
                if (ev.EventId == MpvEventId.None) continue;
                if (ev.EventId == MpvEventId.Shutdown) break;

                try
                {
                    switch (ev.EventId)
                    {
                        case MpvEventId.StartFile:
                            SetStatus(MpvPlaybackStatus.Opening);
                            break;

                        case MpvEventId.FileLoaded:
                            _positionSeconds = 0;
                            RefreshMediaInfoProperties();
                            SetStatus(_isPaused ? MpvPlaybackStatus.Paused : MpvPlaybackStatus.Playing);
                            OpenCompleted?.Invoke(true, null);
                            TracksChanged?.Invoke();
                            break;

                        case MpvEventId.PlaybackRestart:
                            SetStatus(_isPaused ? MpvPlaybackStatus.Paused : MpvPlaybackStatus.Playing);
                            break;

                        case MpvEventId.EndFile:
                            var endFile = ev.Data != IntPtr.Zero
                                ? Marshal.PtrToStructure<MpvEventEndFile>(ev.Data)
                                : default;
                            _positionSeconds = 0;
                            _durationSeconds = 0;
                            if (endFile.Reason == MpvEndFileReason.Error)
                            {
                                string errMsg = MpvNative.GetErrorString(endFile.Error);
                                SetStatus(MpvPlaybackStatus.Failed);
                                OpenCompleted?.Invoke(false, errMsg);
                            }
                            else if (endFile.Reason == MpvEndFileReason.Eof)
                            {
                                SetStatus(MpvPlaybackStatus.Ended);
                            }
                            else if (endFile.Reason == MpvEndFileReason.Stop)
                            {
                                SetStatus(MpvPlaybackStatus.Stopped);
                            }
                            break;

                        case MpvEventId.PropertyChange:
                            HandlePropertyChange(ev.Data);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogWarning($"[MPV] EventLoop uyarısı: {ex.Message}");
                }
            }
        }

        private void HandlePropertyChange(IntPtr dataPtr)
        {
            if (dataPtr == IntPtr.Zero) return;
            var prop = Marshal.PtrToStructure<MpvEventProperty>(dataPtr);
            string name = prop.Name != IntPtr.Zero ? (Marshal.PtrToStringUTF8(prop.Name) ?? "") : "";

            switch (name)
            {
                case "time-pos":
                    if (prop.Format == MpvFormat.Double && prop.Data != IntPtr.Zero)
                    {
                        byte[] bytes = new byte[8];
                        Marshal.Copy(prop.Data, bytes, 0, 8);
                        _positionSeconds = BitConverter.ToDouble(bytes, 0);
                    }
                    break;

                case "duration":
                    if (prop.Format == MpvFormat.Double && prop.Data != IntPtr.Zero)
                    {
                        byte[] bytes = new byte[8];
                        Marshal.Copy(prop.Data, bytes, 0, 8);
                        _durationSeconds = BitConverter.ToDouble(bytes, 0);
                    }
                    else if (prop.Format == MpvFormat.None)
                    {
                        _durationSeconds = 0;
                    }
                    break;

                case "pause":
                    if (prop.Format == MpvFormat.Flag && prop.Data != IntPtr.Zero)
                    {
                        _isPaused = Marshal.ReadInt32(prop.Data) != 0;
                        if (_status == MpvPlaybackStatus.Playing || _status == MpvPlaybackStatus.Paused)
                            SetStatus(_isPaused ? MpvPlaybackStatus.Paused : MpvPlaybackStatus.Playing);
                    }
                    break;

                case "track-list":
                case "aid":
                    TracksChanged?.Invoke();
                    break;

                case "video-params/w":
                    if (prop.Format == MpvFormat.Int64 && prop.Data != IntPtr.Zero)
                        _videoWidth = (int)Marshal.ReadInt64(prop.Data);
                    break;

                case "video-params/h":
                    if (prop.Format == MpvFormat.Int64 && prop.Data != IntPtr.Zero)
                        _videoHeight = (int)Marshal.ReadInt64(prop.Data);
                    break;

                case "video-codec":
                    if (prop.Format == MpvFormat.String && prop.Data != IntPtr.Zero)
                    {
                        IntPtr strPtr = Marshal.ReadIntPtr(prop.Data);
                        _videoCodec = strPtr != IntPtr.Zero ? (Marshal.PtrToStringUTF8(strPtr) ?? "") : "";
                    }
                    break;

                case "audio-codec-name":
                    if (prop.Format == MpvFormat.String && prop.Data != IntPtr.Zero)
                    {
                        IntPtr strPtr = Marshal.ReadIntPtr(prop.Data);
                        _audioCodec = strPtr != IntPtr.Zero ? (Marshal.PtrToStringUTF8(strPtr) ?? "") : "";
                    }
                    break;
            }
        }

        private void RefreshMediaInfoProperties()
        {
            if (_handle == IntPtr.Zero) return;
            if (MpvNative.mpv_get_property(_handle, "video-params/w", MpvFormat.Int64, out long w) >= 0) _videoWidth = (int)w;
            if (MpvNative.mpv_get_property(_handle, "video-params/h", MpvFormat.Int64, out long h) >= 0) _videoHeight = (int)h;
            if (MpvNative.mpv_get_property(_handle, "duration", MpvFormat.Double, out double d) >= 0) _durationSeconds = d;
            _videoCodec = MpvNative.GetPropertyString(_handle, "video-codec") ?? "";
            _audioCodec = MpvNative.GetPropertyString(_handle, "audio-codec-name") ?? "";
        }

        private void SetStatus(MpvPlaybackStatus newStatus)
        {
            if (_status == newStatus) return;
            _status = newStatus;
            StatusChanged?.Invoke(newStatus);
        }

        public void ConfigureRuntimeOptions(bool hwAccel, int cacheMs, string userAgent, IReadOnlyList<string>? headerLines, bool isAceStream, bool isHls)
        {
            if (_handle == IntPtr.Zero || _disposed) return;
            lock (_cmdLock)
            {
                MpvNative.mpv_set_property_string(_handle, "hwdec", hwAccel ? "auto-safe" : "no");

                int minCacheMs = isAceStream ? 5000 : (isHls ? 4000 : 3000);
                double cacheSecs = Math.Clamp(Math.Max(cacheMs, minCacheMs) / 1000.0, 2.5, 15.0);
                MpvNative.mpv_set_property_string(_handle, "cache-secs", cacheSecs.ToString("0.0", CultureInfo.InvariantCulture));
                MpvNative.mpv_set_property_string(_handle, "network-timeout", isAceStream ? "30" : "15");

                if (!string.IsNullOrWhiteSpace(userAgent))
                    MpvNative.mpv_set_property_string(_handle, "user-agent", userAgent);

                MpvNative.SetPropertyStringList(_handle, "http-header-fields", headerLines ?? Array.Empty<string>());
            }
        }

        public void Open(string url)
        {
            if (_handle == IntPtr.Zero || _disposed || string.IsNullOrWhiteSpace(url)) return;
            lock (_cmdLock)
            {
                _positionSeconds = 0;
                _durationSeconds = 0;
                _videoWidth = 0;
                _videoHeight = 0;
                _videoCodec = "";
                _audioCodec = "";
                _isPaused = false;
                MpvNative.mpv_set_property_string(_handle, "pause", "no");
                MpvNative.mpv_set_property_string(_handle, "aid", "auto");
                int res = MpvNative.Command(_handle, "loadfile", url, "replace");
                if (res < 0)
                {
                    SetStatus(MpvPlaybackStatus.Failed);
                    OpenCompleted?.Invoke(false, MpvNative.GetErrorString(res));
                }
            }
        }

        public void Play()
        {
            if (_handle == IntPtr.Zero || _disposed) return;
            lock (_cmdLock)
            {
                _isPaused = false;
                MpvNative.mpv_set_property_string(_handle, "pause", "no");
            }
        }

        public void Pause()
        {
            if (_handle == IntPtr.Zero || _disposed) return;
            lock (_cmdLock)
            {
                _isPaused = true;
                MpvNative.mpv_set_property_string(_handle, "pause", "yes");
            }
        }

        public void Stop()
        {
            if (_handle == IntPtr.Zero || _disposed) return;
            lock (_cmdLock)
            {
                _isPaused = false;
                _positionSeconds = 0;
                _durationSeconds = 0;
                MpvNative.Command(_handle, "stop");
                SetStatus(MpvPlaybackStatus.Stopped);
            }
        }

        public void SeekMs(long targetMs)
        {
            if (_handle == IntPtr.Zero || _disposed) return;
            lock (_cmdLock)
            {
                double secs = Math.Max(0, targetMs / 1000.0);
                MpvNative.Command(_handle, "seek", secs.ToString("0.000", CultureInfo.InvariantCulture), "absolute");
            }
        }

        public void SetVolume(int volume)
        {
            _volume = Math.Clamp(volume, 0, 100);
            if (_handle == IntPtr.Zero || _disposed) return;
            lock (_cmdLock)
            {
                double v = _volume;
                MpvNative.mpv_set_property(_handle, "volume", MpvFormat.Double, ref v);
            }
        }

        public void SetMute(bool mute)
        {
            _isMuted = mute;
            if (_handle == IntPtr.Zero || _disposed) return;
            lock (_cmdLock)
            {
                MpvNative.mpv_set_property_string(_handle, "mute", mute ? "yes" : "no");
            }
        }

        /// <summary>
        /// Instant, zero-reconnect audio track switch (works seamlessly on live AceStream, MPEG-TS, HLS, VOD).
        /// </summary>
        public bool SwitchAudioTrack(int trackId)
        {
            if (_handle == IntPtr.Zero || _disposed || trackId <= 0) return false;
            lock (_cmdLock)
            {
                int res = MpvNative.mpv_set_property_string(_handle, "aid", trackId.ToString(CultureInfo.InvariantCulture));
                if (_isMuted)
                {
                    _isMuted = false;
                    MpvNative.mpv_set_property_string(_handle, "mute", "no");
                }
                return res >= 0;
            }
        }

        /// <summary>
        /// Live runtime audio normalization via zero-lookahead FFmpeg acompressor without interrupting playback.
        /// </summary>
        public void SetAudioNormalization(bool enabled)
        {
            if (_handle == IntPtr.Zero || _disposed) return;
            lock (_cmdLock)
            {
                string filter = enabled
                    ? "lavfi=[acompressor=threshold=-20dB:ratio=3:attack=5:release=60:makeup=3dB]"
                    : "";
                MpvNative.mpv_set_property_string(_handle, "af", filter);
            }
        }

        /// <summary>
        /// Live runtime GPU video enhancement (contrast, saturation, brightness, sharpen) with zero stream interruption.
        /// </summary>
        public void SetVideoEnhancement(bool enabled)
        {
            if (_handle == IntPtr.Zero || _disposed) return;
            lock (_cmdLock)
            {
                double contrast = enabled ? 8.0 : 0.0;
                double saturation = enabled ? 10.0 : 0.0;
                double brightness = enabled ? 2.0 : 0.0;
                double sharpen = enabled ? 0.65 : 0.0;

                MpvNative.mpv_set_property(_handle, "contrast", MpvFormat.Double, ref contrast);
                MpvNative.mpv_set_property(_handle, "saturation", MpvFormat.Double, ref saturation);
                MpvNative.mpv_set_property(_handle, "brightness", MpvFormat.Double, ref brightness);
                MpvNative.mpv_set_property(_handle, "sharpen", MpvFormat.Double, ref sharpen);
            }
        }

        public bool TakeSnapshot(string filePath)
        {
            if (_handle == IntPtr.Zero || _disposed || string.IsNullOrWhiteSpace(filePath)) return false;
            lock (_cmdLock)
            {
                return MpvNative.Command(_handle, "screenshot-to-file", filePath, "video") >= 0;
            }
        }

        public List<MpvTrackInfo> GetAudioTracks()
        {
            var result = new List<MpvTrackInfo>();
            if (_handle == IntPtr.Zero || _disposed) return result;

            try
            {
                string? json = MpvNative.GetPropertyString(_handle, "track-list");
                if (string.IsNullOrWhiteSpace(json)) return result;

                var arr = JArray.Parse(json);
                foreach (var item in arr)
                {
                    string type = item["type"]?.ToString() ?? "";
                    if (!string.Equals(type, "audio", StringComparison.OrdinalIgnoreCase)) continue;

                    string? chStr = item["demux-channels"]?.ToString();
                    int channels = item["demux-channel-count"]?.Value<int?>() ?? 0;
                    if (channels <= 0 && !string.IsNullOrWhiteSpace(chStr))
                    {
                        if (chStr.Contains("5.1", StringComparison.OrdinalIgnoreCase) || chStr.Split(',').Length >= 6) channels = 6;
                        else if (chStr.Contains("mono", StringComparison.OrdinalIgnoreCase)) channels = 1;
                        else channels = 2;
                    }
                    if (channels <= 0) channels = 2;

                    result.Add(new MpvTrackInfo
                    {
                        Id = item["id"]?.Value<int>() ?? 0,
                        Type = type,
                        Lang = item["lang"]?.ToString() ?? "",
                        Title = item["title"]?.ToString() ?? "",
                        Codec = item["codec"]?.ToString() ?? "AAC",
                        Channels = channels,
                        BitRate = item["demux-bitrate"]?.Value<long?>() ?? 0,
                        Selected = item["selected"]?.Value<bool?>() ?? false
                    });
                }
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"[MPV] Track list parse hatası: {ex.Message}");
            }

            return result;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            IntPtr ctx = _handle;
            _handle = IntPtr.Zero;

            if (ctx != IntPtr.Zero)
            {
                try { MpvNative.mpv_wakeup(ctx); } catch { }
                try { _eventThread?.Join(500); } catch { }
                try { MpvNative.mpv_terminate_destroy(ctx); } catch { }
            }
        }
    }
}
