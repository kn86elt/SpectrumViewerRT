# Spectrum Viewer RT

Windows desktop app for realtime audio monitoring, recording, playback, WAV export, scrolling spectrogram display, and VFD-style spectrum analysis.

![Screenshot](screenshot.jpg)
Spectrogram mode
(Audio Generated via [imagetoaudio](https://nsspot.herokuapp.com/imagetoaudio/) )

![Screenshot2](screenshot2.jpg)
Spectrum Analyzer mode

## Features

- Microphone input capture
- Windows default output capture with WASAPI loopback
- Always-on live monitoring of the selected source
- Source/device switching while monitoring
- Dark-blue to red spectrogram color map for clearer level differences
- Linear / log frequency scale selector
- Frequency limit selector
- Toggleable frequency and time grid
- Adjustable seconds per time division
- Realtime waveform display
- Stereo VFD-style level meter with peak hold
- VU-style display normalization for analog-deck-like meter response
- Customizable level meter color and block/fine-line style
- Mono or left/right split spectrum analyzer
- Time labels are drawn outside the spectrogram area
- Realtime recording
- Smooth playback of recorded audio with seek bar
- WAV export, including stereo WAV for System Output recordings
- 48 kHz / 16-bit processing path
- Recording level, display gain, display range, FPS controls, and visible-time readout
- Settings are saved under `%AppData%\SpectrumViewerRT\settings.json`

## Run

### Release zip

Download and extract `SpectrumViewerRT-vX.Y.Z-win-x64.zip`, then run:

```powershell
.\SpectrumViewerRT.exe
```

The release zip is framework-dependent. Install the .NET 6 Desktop Runtime for Windows x64 if the app does not start:

https://dotnet.microsoft.com/en-us/download/dotnet/6.0

### From source

```powershell
dotnet run
```

Or start a locally published app:

```powershell
.\bin\Release\net6.0-windows\publish\SpectrumViewerRT.exe
```

## Controls

1. Select `Microphone` or `System Output` from `Source`.
2. For `Microphone`, select the input device from `Device`.
3. The selected source is monitored continuously.
4. Adjust `Rec Level` to set the level used for recording and live input displays.
5. Press `Record` to start recording, then `Stop` to return to live monitoring.
6. Use `Play`, the playback seek bar, or `Save WAV` after recording.

Changing `Range`, `Scale`, `Max Hz`, `Display`, or `Analyzer` clears the current display and restarts drawing with the new settings. The waveform scrolls on the same time axis as the spectrogram. `Grid` toggles the frequency/time overlay, and `Grid Time/div` controls the horizontal time scale. The visible time width is `Grid Time/div x 10` and is shown as `Visible`.

`Rec Level` changes the level of the recorded signal and the live input displays. It is not saved as an app setting. `Gain` is an FFT/display multiplier for spectrogram and analyzer brightness. `Range` is spectrogram dynamic range in dB. `FPS` is the render update rate and controls movement smoothness.

`VU` boosts only the level meter and spectrum analyzer display, making nominal levels touch 0 dB/red like an analog recorder. It does not change recorded audio.

`Analyzer` can be set to `Mono` or `Stereo L-R`. Stereo analyzer mode uses left/right split panels when stereo samples are available. Spectrogram and waveform display continue to use the mono display path.

Double-click a setting control to restore that control's default value, or press `Default Settings` to restore display, meter, grid, and window settings.

In `System Output` mode, the app captures the Windows default playback device with WASAPI loopback. System Output recordings are exported as 48 kHz / 16-bit stereo WAV files. Microphone recordings currently use the mono capture path.



## License
Code in this repository is licensed under MIT.
