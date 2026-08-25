# Fix Plan for Proven Technical Issues

This plan addresses four confirmed bugs and architectural issues identified during the targeted audit. The goal is to fix these issues with minimal risk of regression, following standard protocols and best practices.

## Proposed Changes

### 1. HLS EVENT / Sliding Window Fix
Correct the HLS manifest generation to follow sliding-window semantics for live streams with a rolling buffer.

#### [MODIFY] [HlsProxyEngine.cs](file:///C:/Users/Administrator/Downloads/streammesh/Core/Media/HlsProxyEngine.cs)
- Update `GenerateManifest` to omit `#EXT-X-PLAYLIST-TYPE:EVENT` when the stream is a live rolling window (to allow segment removal without protocol violation).
- Ensure `MEDIA-SEQUENCE` is always based on the `SequenceNumber` of the first segment currently in the `session.Segments` list.
- Keep the `EVENT` tag ONLY if `session.IsLive` is false (VOD mode) or if we are serving a static teleport window.

### 2. Database Migration Startup Race Fix
Synchronize database initialization to ensure all migrations and schema setup are complete before other services or the UI attempt to access the data.

#### [MODIFY] [DatabaseEngine.cs](file:///C:/Users/Administrator/Downloads/streammesh/Core/Database/DatabaseEngine.cs)
- Introduce a `public Task InitializeAsync()` method.
- Move schema creation and the `EnsureDataMigrationAsync` call into this method.
- Use a `TaskCompletionSource` or a simple `await` chain to ensure initialization is finished exactly once.

#### [MODIFY] [App.xaml.cs](file:///C:/Users/Administrator/Downloads/streammesh/App.xaml.cs)
- Change `InitializeServices` to be `async Task`.
- `await` the `DatabaseEngine` initialization before starting `MediaServer`, `AceEngine`, or background tasks.

### 3. EPG DateTime Parsing Fix
Implement robust XMLTV datetime parsing that correctly handles UTC ('Z') and various offset formats (`+HHmm`, `+HH:mm`).

#### [MODIFY] [EpgEngine.cs](file:///C:/Users/Administrator/Downloads/streammesh/Core/Media/EpgEngine.cs)
- Rewrite `TryParseXmlTime` to use `DateTimeOffset.TryParse` or a more flexible custom parser.
- Prioritize explicit timezone information (Z or offset) over URL-based heuristics.
- Ensure format `+HH:mm` (with colon) is handled correctly by removing fixed substring dependencies.

### 4. HLS Shutdown / Resource Lifecycle Fix
Ensure the local HLS proxy server and its associated background tasks are cleanly shut down when the application exits.

#### [MODIFY] [App.xaml.cs](file:///C:/Users/Administrator/Downloads/streammesh/App.xaml.cs)
- Call `HlsProxyEngine.Instance.Stop()` within the `OnExit` method to close the `HttpListener` and cancel all active pollers.

## Verification Plan

### Automated Tests
- **Build Check**: Verify the project compiles without errors.
- **EPG Format Check**: Mentally verify parsing logic against the 5 specified formats (Z, +HHmm, +HH:mm, etc.).

### Manual Verification
- **HLS Protocol**: Inspect a generated manifest via the browser (`/playlist.m3u8?session=...`) to ensure `#EXT-X-PLAYLIST-TYPE:EVENT` is removed for live rolling streams.
- **Startup Stability**: Start the app with a fresh database and verify no crashes or empty lists occur due to race conditions.
- **Port Cleanup**: Close the app and immediately restart it to verify no "Port in use" errors occur for the HLS proxy (port 48931+).
- **EPG Accuracy**: Check OSD program times for a known UTC-based EPG source.
