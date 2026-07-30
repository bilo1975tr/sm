# Implementation Plan - Broadcast Control and Auto-Stop

This plan addresses the user's request to stop the broadcast when the app is minimized and to add a comprehensive broadcast control/testing feature in the settings.

## User Review Required

> [!IMPORTANT]
> - **Deep Inspection:** The "Most Detailed" test will require downloading a few seconds of video to analyze codecs and resolution. This might consume significant bandwidth if used on many channels.
> - **Verification Logic:** We will need to decide if the verified information (country, language) should automatically update the database or just be shown in logs for manual review. By default, I will implement it to update the `Channel` object's `IsVerified` status and log the findings.

## Proposed Changes

### 1. Stop Playback on Minimize/Hide
Detect window state changes and visibility changes to stop the VLC player.

#### [MODIFY] [MainWindow.xaml.cs](file:///C:/Users/Administrator/Downloads/streammesh/UI/Windows/MainWindow.xaml.cs)
- Add event handlers for `StateChanged` and `IsVisibleChanged`.
- If `WindowState == Minimized` or `Visibility != Visible`, call `_playerView.Stop()`.

### 2. Broadcast Control Settings Tab
Create a new UI section within the settings to perform broadcast health checks.

#### [MODIFY] [SettingsView.xaml](file:///C:/Users/Administrator/Downloads/streammesh/UI/Views/SettingsView.xaml)
- Add a new `TabItem` named "Yayın Kontrolü" (Broadcast Control).
- UI will include:
    - RadioButtons for selecting test level: **Hızlı (Fast)**, **Detaylı (Detailed)**, **En Detaylı (Full Analysis)**.
    - Start/Stop Buttons.
    - `ListBox` for real-time logs.
    - `ProgressBar` with percentage and estimated time remaining.

#### [MODIFY] [SettingsView.xaml.cs](file:///C:/Users/Administrator/Downloads/streammesh/UI/Views/SettingsView.xaml.cs)
- Logic to initiate the broadcast check using a background task.
- Progress reporting using `IProgress`.
- Log collection in an `ObservableCollection<string>`.

### 3. Stream Validation Logic
Encapsulate the testing logic in a reusable engine.

#### [NEW] [StreamValidator.cs](file:///C:/Users/Administrator/Downloads/streammesh/Core/Utils/StreamValidator.cs)
- **Fast Test:** HTTP HEAD request to verify URL reachability.
- **Detailed Test:** Briefly open stream with `LibVLC` to confirm data flow.
- **En Detaylı Test:** Full analysis using `Media.Tracks` from `LibVLC` to get:
    - Resolution (Width x Height).
    - Video Codec.
    - Audio Codec & Language info.
- Report results and update channel metadata where possible.

## Verification Plan

### Automated Tests
- Unit tests for `StreamValidator` using mock URLs.
- Verify `MainWindow` responds to state changes (can be manually verified).

### Manual Verification
1. **Auto-Stop:** Play a channel, minimize the app, and ensure audio stops.
2. **Fast Test:** Run on a set of channels, verify it correctly identifies "Online" vs "Offline" based on HTTP status.
3. **Detailed Test:** Verify it detects streams that are reachable but unplayable.
4. **En Detaylı Test:** Verify it reports the correct resolution (e.g., 1920x1080) and codec (e.g., H264) in the logs.
5. **UI:** Verify the progress bar and estimated time correctly reflect the workload.
