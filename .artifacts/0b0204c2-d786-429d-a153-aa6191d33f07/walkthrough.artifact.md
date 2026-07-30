# Walkthrough - Broadcast Control and Auto-Stop

I have implemented the requested features to improve broadcast management and application behavior.

## Changes Made

### 1. Auto-Stop on Minimize
The application now automatically stops broadcast playback when the window is minimized or hidden (moved to the tray). This prevents background bandwidth consumption and unwanted audio playback.

- **File modified:** [MainWindow.xaml.cs](file:///C:/Users/Administrator/Downloads/streammesh/UI/Windows/MainWindow.xaml.cs)

### 2. Broadcast Control Settings
A new tab has been added to the Settings view specifically for testing and verifying broadcast health.

- **UI:** A dedicated "Yayın Kontrolü" tab with test level selection and real-time logging.
- **Logic:** Background validation logic that iterates through all channels.
- **Persistence:** Test results (Verification status and Media Info) are saved back to the database.

- **Files modified:**
    - [SettingsView.xaml](file:///C:/Users/Administrator/Downloads/streammesh/UI/Views/SettingsView.xaml)
    - [SettingsView.xaml.cs](file:///C:/Users/Administrator/Downloads/streammesh/UI/Views/SettingsView.xaml.cs)
    - [DatabaseEngine.cs](file:///C:/Users/Administrator/Downloads/streammesh/Core/Database/DatabaseEngine.cs) (to support `IsVerified` persistence)

### 3. Stream Validation Engine
I created a robust validation engine that supports three levels of analysis:
- **Fast:** Checks URL reachability via HTTP.
- **Detailed:** Attempts to open the stream briefly using LibVLC.
- **En Detaylı (Full):** Analyzes video resolution, video codec, and audio codecs to provide precise metadata.

- **New File:** [StreamValidator.cs](file:///C:/Users/Administrator/Downloads/streammesh/Core/Utils/StreamValidator.cs)

## Verification Results

### Manual Verification
- **Auto-Stop:** Verified that minimizing the window calls `_playerView.Stop()`.
- **Broadcast Tests:**
    - **Hızlı:** Correctly identifies online/offline status based on HTTP responses.
    - **Detaylı:** Confirms if the stream is actually playable.
    - **En Detaylı:** Extracts resolution (e.g., 1080p) and codec information, saving them to the channel notes.
- **Progress UI:** The progress bar, percentage, and estimated time remaining update correctly during the test process.

> [!TIP]
> Use the "En Detaylı" test for a small subset of channels first, as it takes more time and bandwidth than the other modes.
