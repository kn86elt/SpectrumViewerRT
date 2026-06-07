# Spectrum Viewer RT

Windows desktop app for realtime audio monitoring, recording, playback, WAV export, and scrolling color spectrogram display.

![Screenshot](screenshot.jpg)
Spectrogram mode
(Audio Generated via [imagetoaudio](https://nsspot.herokuapp.com/imagetoaudio/) )

## Features

- Microphone input capture
- Windows default output capture with WASAPI loopback
- Live spectrum display without recording
- Dark-blue to red spectrogram color map for clearer level differences
- Linear / log frequency scale selector
- Frequency limit selector
- Toggleable frequency and time grid
- Adjustable seconds per time division
- Realtime waveform display
- Stereo VFD-style level meter with peak hold
- Customizable level meter color and block/fine-line style
- Time labels are drawn outside the spectrogram area
- Realtime recording
- Playback of recorded audio
- WAV export
- 48 kHz / 16-bit / mono processing path
- Gain, display range, FPS controls, and visible-time readout
- Settings are saved under `%AppData%\SpectrumViewerRT\settings.json`

## Run

```powershell
dotnet run
```

Or start the published app:

```powershell
.\bin\Release\net6.0-windows\publish\SpectrumViewerRT.exe
```

## Controls

1. Select `Microphone` or `System Output` from `Source`.
2. For `Microphone`, select the input device from `Device`.
3. Turn on `Monitor` to show the live spectrogram without recording.
4. Press `Record` to record while showing the spectrogram.
5. Press `Stop`, then use `Play` or `Save WAV`.

Changing `Range`, `Scale`, or `Max Hz` clears the current display and restarts drawing with the new settings. The waveform scrolls on the same time axis as the spectrogram. `Grid` toggles the frequency/time overlay, and `Grid Time/div` controls the horizontal time scale. The visible time width is `Grid Time/div x 10` and is shown as `Visible`.

`Gain` is an FFT input multiplier for spectrogram brightness. `Range` is spectrogram dynamic range in dB. `FPS` is the render update rate and controls movement smoothness.

Double-click a setting control to restore that control's default value, or press `Defaults` to restore all display settings.

In `System Output` mode, the app captures the Windows default playback device with WASAPI loopback. `Monitor` starts live analysis, but it does not route system output back to the speakers again.



## License
Code in this repository is licensed under MIT.

