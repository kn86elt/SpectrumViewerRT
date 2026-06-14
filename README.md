# Spectrum Viewer RT

[日本語](README.ja.md)

Spectrum Viewer RT is a Windows application that visualizes audio from your PC or microphone in real time using a spectrogram, spectrum analyzer, waveform, and level meter.

Its appearance is inspired by the vacuum fluorescent displays (VFDs) found in classic audio equipment. Use it as a music visualizer, an audio monitoring tool, or a simple recorder.

![Spectrogram display](screenshot.jpg)

Example using audio generated with [imagetoaudio](https://nsspot.herokuapp.com/imagetoaudio/)

![Spectrum analyzer display](screenshot2.jpg)

![Compact mode and right-click menu](screenshot3.jpg)

## What You Can Do

### See audio from your PC or microphone

Display audio playing through Windows or sound received from a connected microphone.

- A spectrogram that shows how sound changes over time
- Mono and stereo spectrum analyzers for viewing frequency balance
- A waveform and left/right level meter
- A LIVE elapsed-time display that can also show the current clock
- Automatic refresh of the input-device list when audio devices are connected or disconnected

This makes it easy to enjoy the movement of music, compare the left and right channels, and check input levels.

### Enjoy a classic audio-equipment look

Choose colors, glow, dim unlit elements, and display patterns inspired by classic VFD audio equipment.

Block, fine-line, and dot-matrix styles let you adjust the display to resemble older audio components or measurement equipment.

### Record and save audio

Record the audio being displayed, play it back inside the application, seek to a position, and save the result as a WAV file.

Recordings made from Windows system output are saved as stereo WAV files with separate left and right channels.

### Keep only the displays you need

Use the right-click menu to show or hide the main display, waveform, level meter, recording controls, and other panels.

Compact mode hides the settings area and title bar, making it possible to keep only a small level meter in a corner of the desktop. Normal and Compact modes remember their panel layouts separately.

## Download and Start

### 1. Download the application

1. Open the [latest release page](https://github.com/kn86elt/SpectrumViewerRT/releases/latest).
2. Find and expand the **Assets** section.
3. Download the file named `SpectrumViewerRT-vX.Y.Z-win-x64.zip`.

Choose the ZIP file containing `win-x64`, not one of the files labeled `Source code`.

### 2. Extract the ZIP file

1. Right-click the downloaded ZIP file.
2. Select **Extract All**.
3. Choose a destination and select **Extract**.

Extract the files before starting the application. Do not run it directly from inside the ZIP file.

### 3. Start the application

Double-click `SpectrumViewerRT.exe` in the extracted folder.

If the application does not start, install the [.NET 6 Desktop Runtime for Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/6.0), then try again.

The application is not digitally signed, so Windows may display a security warning. Confirm that you downloaded it from a trusted release page before choosing **More info** and running it.

## Basic Use

1. Select an audio source from `Source`.
   - `System Output`: audio currently playing through Windows
   - `Audio Input`: audio received from the selected input device
2. Choose a view from `Display`.
   - `Spectrogram`
   - `Spectrum Analyzer (Mono)`
   - `Spectrum Analyzer (Stereo)`
3. Select `Record` to begin recording and `Stop` to finish.
4. After recording, use `Play` to review it and `Save WAV` to save it.

Right-click inside the application to change visible panels, enter Compact mode, select a display mode, or keep the window always on top.

The right-click menu also provides direct selection of the VFD color and meter style.

Click the LIVE TIME display beside the level meter to switch between elapsed time and the current clock. In Compact mode, you can also drag this display to move the window.

## Keyboard Shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl+1` | Show or hide recording / playback controls |
| `Ctrl+2` | Show or hide settings |
| `Ctrl+3` | Show or hide the main display |
| `Ctrl+4` | Show or hide the waveform |
| `Ctrl+5` | Show or hide the level meter |
| `Ctrl+T` | Toggle Compact mode |
| `Ctrl+D` | Cycle through Spectrogram, Analyzer Mono, and Analyzer Stereo |
| `Ctrl+L` | Toggle the LIVE TIME display between Dot Matrix and 16-segment |
| `Ctrl+F` | Cycle through Cyan, Green, Amber, and Blue |
| `Ctrl+M` | Cycle through Block, Fine Lines, Dot Matrix Block, and Dot Matrix Fine Lines |

## Settings

Your display, color, panel layout, and window-mode choices are saved automatically and restored the next time the application starts.

Use `Default Settings` inside the application to restore the original settings.

## Requirements

- Windows 10 or 11, 64-bit
- .NET 6 Desktop Runtime

## License

Code in this repository is available under the MIT License.
