# Changelog

## Unreleased

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
