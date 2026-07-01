# Changelog

## Unreleased

## 0.3.9 - 2026-07-02

### Added

- Added an icon-only device reload button.
- Added double-click copying for status and error messages.

### Changed

- Migrated Audio Input capture and device enumeration to NAudio WASAPI.
- Reworked System Output loopback capture to use NAudio WASAPI and removed the conflicting hand-written CoreAudio interop.
- Kept the last successful Audio Input device list when reload encounters a transient failure.

### Fixed

- Fixed device reload failures caused by COM type conflicts after switching to WASAPI/NAudio.
- Improved resilience when audio devices are hot-plugged while the app is running.
- Prevented reload failures from collapsing the input device selector to only the default input.

## 0.3.7 - 2026-06-16

### Added

- Added multiple named Custom layouts for saving panel visibility, display mode, Compact state, stereo split, and window size.
- Added the modal `Edit saved layouts...` screen with immediate layout preview, rename, delete, saved-content inspection, and manual Up / Down ordering.
- Added persistent Compact window sizes for each panel combination.

### Changed

- Custom layouts now remain in creation order by default and preserve user-defined ordering.
- Release ZIP packages now include the English and Japanese README files.

### Fixed

- Fixed Compact mode window dimensions being lost after leaving and re-entering Compact mode.

## 0.3.6 - 2026-06-15

### Added

- Added stereo capture for compatible Audio Input devices, with automatic mono fallback.
- Added a stereo spectrogram display mode.
- Added shared Left / Right and Top / Bottom stereo split layouts for the spectrogram and spectrum analyzer.
- Added stereo split selection to the right-click menu and persisted the selected layout.

### Changed

- Renamed the `Microphone` source label to `Audio Input`.
- Added settings migration for the expanded display-mode list.

### Fixed

- Corrected the Top / Bottom spectrum analyzer layout so both channels use equal plot heights and dB resolution.

## 0.3.4 - 2026-06-11

### Fixed

- Completed the uppercase A-Z glyph definitions for the Dot Matrix and 16-segment LIVE TIME displays.
- Prevented the application UI from becoming unresponsive when an active microphone, Bluetooth audio device, or Windows output device is disconnected.
- Improved WaveIn callback shutdown and WASAPI device invalidation handling to avoid cleanup races and deadlocks.
- Preserved UI responsiveness by moving audio-device cleanup away from the UI thread and ignoring callbacks from superseded capture sessions.
- Reduced rendering allocations by caching Hann-window coefficients, FFT work buffers, color-map data, brushes, pens, and frequency-bin mappings.
- Replaced repeated VFD texture line drawing with cached tiled drawing brushes.
- Avoided unnecessary VFD dependency-property updates when display values have not changed.
- Improved waveform detail by calculating minimum and maximum values separately for each rendered column.
- Decoupled spectrogram time resolution from the UI frame rate by generating spectrum data at a fixed 50 Hz analysis rate.
- Corrected spectrogram scrolling so low FPS settings no longer stretch one FFT result across multiple time columns.
