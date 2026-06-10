# Spectrum Viewer RT

[日本語](README.ja.md)

Spectrum Viewer RT is a Windows desktop application for real-time audio monitoring, recording, playback, WAV export, and VFD-inspired audio visualization.

![Spectrogram mode](screenshot.jpg)

Spectrogram mode  
Audio generated with [imagetoaudio](https://nsspot.herokuapp.com/imagetoaudio/).

![Spectrum analyzer mode](screenshot2.jpg)

Spectrum analyzer mode

## Features

- Microphone capture and Windows default-output capture via WASAPI loopback
- Continuous live monitoring with source and input-device switching
- Real-time scrolling spectrogram and waveform displays
- Three display modes: Spectrogram, Spectrum Analyzer (Mono), and Spectrum Analyzer (Stereo)
- Waveform display remains available in both spectrogram and analyzer modes
- Linear or logarithmic frequency scale and selectable maximum frequency
- Toggleable frequency/time grid with adjustable time division
- VFD-style stereo level meter with peak hold and optional VU normalization
- VFD color themes, block/fine-line meter styles, dim segments, glow, and display texture
- LIVE/REC/PLAY time display with dot-matrix or 16-segment characters
- Real-time recording, playback with seeking, and WAV export
- Stereo WAV export for System Output recordings
- 48 kHz / 16-bit processing path
- Adjustable recording level and Spectrogram gain, dynamic range, scale, frequency limit, and time grid
- Global FPS presets (15/30/60) with a live detailed slider and numeric input
- Normal and Compact window layouts
- Individually toggleable transport, settings, main display, waveform, and level-meter panels
- Independent panel visibility settings for Normal and Compact modes
- Borderless Compact mode with window dragging
- Right-click menu operation in both Normal and Compact modes
- Resizable displays that adapt their panel and VFD rendering to the window size
- Always-on-top option
- Persistent settings under `%AppData%\SpectrumViewerRT\settings.json`

## Requirements

- Windows x64
- [.NET 6 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/6.0)

## Run

### Release zip

Download and extract `SpectrumViewerRT-vX.Y.Z-win-x64.zip`, then run:

```powershell
.\SpectrumViewerRT.exe
```

The release archive is framework-dependent. Install the .NET 6 Desktop Runtime for Windows x64 if the application does not start.

### From source

```powershell
dotnet run
```

To publish a local Release build:

```powershell
dotnet publish -c Release
.\bin\Release\net6.0-windows\publish\SpectrumViewerRT.exe
```

## Basic Use

1. Select `Microphone` or `System Output` from `Source`.
2. When using `Microphone`, select an input device from `Device`.
3. Adjust `Rec Level` for the recorded signal and live input displays.
4. Select `Spectrogram`, `Spectrum Analyzer (Mono)`, or `Spectrum Analyzer (Stereo)` from `Display`.
5. Press `Record` to start recording and `Stop` to return to live monitoring.
6. Use `Play`, the seek bar, and `Save WAV` after recording.

Changing `Scale`, `Max Hz`, or `Display` resets the display history. Changing `Range` updates subsequent spectrogram drawing without clearing the existing history, making its effect easier to compare. The waveform uses the same time axis as the spectrogram. `Grid Time/div` controls the horizontal time scale; the visible duration is ten divisions.

`Gain` controls FFT/display intensity and does not alter recorded audio. `Range` controls the spectrogram dynamic range in dB. `FPS` controls the rendering update rate.

The Spectrogram-only controls are placed below the Display row and are disabled in either Spectrum Analyzer mode. The analyzer uses a fixed 20 kHz range and does not use the Spectrogram gain or maximum-frequency settings.

Use the `FPS` button for 15, 30, or 60 FPS presets. `Detailed settings...` opens a small live adjustment panel with a slider and numeric input from 12 to 60 FPS.

`VU` boosts only the level-meter and spectrum-analyzer presentation so nominal levels approach 0 dB, similar to an analog recorder. It does not change captured or exported audio.

`Spectrum Analyzer (Stereo)` displays separate left and right analyzer panels when stereo samples are available.

Double-click a setting control to restore its default value, or press `Default Settings` to reset the display, meter, grid, and window settings.

## Window Layout

Right-click anywhere in the application background to show or hide:

- Recording/playback controls
- Settings
- Main display
- Waveform
- Level meter

The application does not display a traditional menu bar; layout and window commands are available from the right-click menu in both modes. Compact mode also hides the settings panel and removes the normal title bar. Drag an unused area of the application body to move the Compact window.

Normal and Compact modes remember panel visibility independently. For example, Normal mode can show every panel while Compact mode shows only the level meter.

The window and visible panels resize together. The main display receives additional vertical space when available, while utility panels such as the level meter retain a practical maximum height.

## Audio Notes

In `System Output` mode, the application captures the Windows default playback device using WASAPI loopback. These recordings are exported as 48 kHz / 16-bit stereo WAV files.

Microphone recordings currently use the mono capture path.

## License

Code in this repository is licensed under the MIT License.
