using Microsoft.Win32;
using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SpectrumViewerRT;

public partial class MainWindow : Window
{
    private const int FftSize = 4096;
    private const int ImageWidth = 1200;
    private const int ImageHeight = 620;
    private const int WaveformWidth = 1200;
    private const int WaveformHeight = 140;
    private const int DisplaySampleRate = AudioCapture.DefaultSampleRate;

    private readonly ConcurrentQueue<short[]> _pendingSamples = new();
    private readonly List<short> _sampleWindow = new(FftSize * 2);
    private readonly List<short> _recorded = new();
    private readonly DispatcherTimer _renderTimer = new();
    private readonly DispatcherTimer _vfdDecayTimer = new();
    private readonly WriteableBitmap _spectrogram;
    private readonly byte[] _pixels = new byte[ImageWidth * ImageHeight * 4];
    private readonly WriteableBitmap _waveform;
    private readonly byte[] _waveformPixels = new byte[WaveformWidth * WaveformHeight * 4];
    private readonly List<short> _latestRenderSamples = new();
    private const double TimeDivisions = 10.0;
    private double _scrollColumnAccumulator;
    private readonly double[] _analyzerLevels = new double[48];
    private readonly double[] _analyzerHolds = Enumerable.Repeat(-90.0, 48).ToArray();
    private readonly DateTime[] _analyzerHoldUntil = new DateTime[48];
    private IAudioCaptureSource? _capture;
    private AudioPlayback? _monitor;
    private readonly MediaPlayer _playbackPlayer = new();
    private readonly DispatcherTimer _playbackTimer = new();
    private short[] _playbackSamples = Array.Empty<short>();
    private string? _playbackTempFile;
    private int _lastPlaybackSampleOffset;
    private DateTime _recordingStarted;
    private DateTime _playbackStarted;
    private double _peak;
    private LevelMeterReading _currentLevel;
    private LevelMeterReading _peakHoldLevel;
    private DateTime _leftPeakHoldUntil;
    private DateTime _rightPeakHoldUntil;
    private bool _isRecording;
    private bool _isPlayingBack;
    private bool _changingMonitorCheck;
    private bool _updatingPlaybackSlider;
    private bool _loadingSettings;
    private bool _resettingDisplay;
    private bool _uiReady;
    private DateTime _vfdDecayStarted;
    private bool _vfdDecayActive;
    private bool _meterOnlySizeApplied;
    private double _normalWindowWidth = 1180;
    private double _normalWindowHeight = 780;
    private double _normalMinWidth = 980;
    private double _normalMinHeight = 640;
    private WindowState _normalWindowState = WindowState.Normal;

    public MainWindow()
    {
        InitializeComponent();

        _loadingSettings = true;
        ModeCombo.Items.Add(new ComboBoxItem { Content = "Microphone", Tag = CaptureMode.Microphone });
        ModeCombo.Items.Add(new ComboBoxItem { Content = "System Output", Tag = CaptureMode.SystemOutput });
        ScaleCombo.Items.Add("Linear");
        ScaleCombo.Items.Add("Log");
        MaxFrequencyCombo.Items.Add("8 kHz");
        MaxFrequencyCombo.Items.Add("12 kHz");
        MaxFrequencyCombo.Items.Add("16 kHz");
        MaxFrequencyCombo.Items.Add("20 kHz");
        MaxFrequencyCombo.Items.Add("24 kHz");
        MeterColorCombo.Items.Add("Cyan");
        MeterColorCombo.Items.Add("Green");
        MeterColorCombo.Items.Add("Amber");
        MeterColorCombo.Items.Add("Blue");
        MeterStyleCombo.Items.Add("Block");
        MeterStyleCombo.Items.Add("Fine Lines");
        DisplayModeCombo.Items.Add("Spectrogram");
        DisplayModeCombo.Items.Add("Spectrum Analyzer");
        ApplySettings(AppSettings.Load());
        _loadingSettings = false;

        _spectrogram = new WriteableBitmap(ImageWidth, ImageHeight, 96, 96, PixelFormats.Bgra32, null);
        SpectrogramImage.Source = _spectrogram;
        _waveform = new WriteableBitmap(WaveformWidth, WaveformHeight, 96, 96, PixelFormats.Bgra32, null);
        WaveformImage.Source = _waveform;
        ClearSpectrogram();
        ClearWaveform();

        _renderTimer.Tick += RenderTimer_Tick;
        _playbackTimer.Interval = TimeSpan.FromMilliseconds(33);
        _playbackTimer.Tick += PlaybackTimer_Tick;
        _playbackPlayer.MediaEnded += (_, _) => FinishPlayback("Playback finished", completed: true);
        _playbackPlayer.MediaFailed += (_, e) => FinishPlayback($"Playback failed: {e.ErrorException.Message}", completed: false);
        _vfdDecayTimer.Interval = TimeSpan.FromMilliseconds(33);
        _vfdDecayTimer.Tick += VfdDecayTimer_Tick;
        RefreshDevices();
        _uiReady = true;
        ApplyDisplayMode();
        ApplyMeterOnlyState();
        Topmost = AlwaysOnTopCheck.IsChecked == true;
        UpdateControlLabels();
        UpdateGridOverlay();
        TriggerVfdFullScaleDecay();
    }

    private CaptureMode SelectedMode =>
        ModeCombo.SelectedItem is ComboBoxItem { Tag: CaptureMode mode } ? mode : CaptureMode.Microphone;

    private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady)
            return;

        if (_capture != null && !_isRecording)
            StopLiveMonitor();
        RefreshDevices();
        if (!_loadingSettings)
            SaveSettings();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshDevices();

    private void StartButton_Click(object sender, RoutedEventArgs e) => BeginCapture(record: true);

    private void BeginCapture(bool record)
    {
        try
        {
            StopPlayback(updateStatus: false);
            StopCapture(triggerVfdDecay: false);
            CancelVfdDecay();
            if (record)
            {
                _recorded.Clear();
                _playbackSamples = Array.Empty<short>();
                UpdatePlaybackSliderBounds();
            }
            _sampleWindow.Clear();
            _latestRenderSamples.Clear();
            _scrollColumnAccumulator = 0;
            _peak = 0;
            _currentLevel = default;
            _peakHoldLevel = default;
            while (_pendingSamples.TryDequeue(out _)) { }
            ClearSpectrogram();
            ClearWaveform();

            _isRecording = record;
            _capture = CreateCaptureSource();
            _capture.SamplesAvailable += Capture_SamplesAvailable;
            _capture.LevelAvailable += level => _peak = Math.Max(_peak * 0.92, level);
            _capture.StereoLevelAvailable += Capture_StereoLevelAvailable;
            _capture.StatusAvailable += message => Dispatcher.BeginInvoke(() => SetStatus(message));

            _monitor = null;
            _capture.Start();

            _recordingStarted = DateTime.Now;
            _renderTimer.Interval = TimeSpan.FromSeconds(1.0 / Math.Max(1.0, FpsSlider.Value));
            _renderTimer.Start();

            StartButton.IsEnabled = !record;
            StopButton.IsEnabled = true;
            SetInputControlsEnabled(false);
            PlayButton.IsEnabled = false;
            SaveButton.IsEnabled = false;
            SetStatus(record
                ? (SelectedMode == CaptureMode.SystemOutput ? "Recording system output" : "Recording microphone")
                : (SelectedMode == CaptureMode.SystemOutput ? "Monitoring system output" : "Monitoring microphone"));
        }
        catch (Exception ex)
        {
            StopCapture(triggerVfdDecay: false);
            SetMonitorChecked(false);
            SetStoppedState();
            SetStatus(ex.Message);
        }
    }

    private IAudioCaptureSource CreateCaptureSource()
    {
        if (SelectedMode == CaptureMode.SystemOutput)
            return new WasapiLoopbackCapture();

        if (DeviceCombo.SelectedItem is not AudioDevice device)
            throw new InvalidOperationException("Input device is not selected.");

        return new AudioCapture(device.Id);
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isPlayingBack)
        {
            StopPlayback();
            return;
        }

        StopCapture();
        SetMonitorChecked(false);
        SetStoppedState();
        SetStatus(_recorded.Count > 0 ? "Stopped" : "Ready");
    }

    private void MonitorCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_changingMonitorCheck)
            return;

        if (MonitorCheck.IsChecked == true)
        {
            if (_capture == null)
                BeginCapture(record: false);
            else if (!_isRecording)
                SetStatus(SelectedMode == CaptureMode.SystemOutput ? "Monitoring system output" : "Monitoring microphone");
        }
        else if (_capture != null && !_isRecording)
        {
            StopLiveMonitor();
        }
        else
        {
            SetStatus(_isRecording ? "Recording continues" : "Ready");
        }
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_recorded.Count == 0)
            return;

        int startOffset = SecondsToSampleOffset(PlaybackSlider.Value);
        if (startOffset >= _recorded.Count)
            startOffset = 0;
        StartPlayback(startOffset);
    }

    private void StartPlayback(int startOffset)
    {
        StopPlayback(updateStatus: false);

        var recorded = _recorded.ToArray();
        if (recorded.Length == 0)
            return;

        _playbackSamples = recorded;
        startOffset = Math.Clamp(startOffset, 0, recorded.Length - 1);

        PreparePlaybackDisplay(startOffset);
        try
        {
            _playbackTempFile = CreatePlaybackWaveFile(recorded);
            _playbackPlayer.Open(new Uri(_playbackTempFile));
            _playbackPlayer.Position = TimeSpan.FromSeconds(SamplesToSeconds(startOffset));
        }
        catch (Exception ex)
        {
            FinishPlayback($"Playback failed: {ex.Message}", completed: false);
            return;
        }

        _isPlayingBack = true;
        _lastPlaybackSampleOffset = startOffset;
        _playbackStarted = DateTime.Now.AddSeconds(-startOffset / (double)DisplaySampleRate);
        _renderTimer.Interval = TimeSpan.FromSeconds(1.0 / Math.Max(1.0, FpsSlider.Value));
        _renderTimer.Start();
        _playbackTimer.Start();
        _playbackPlayer.Play();
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        PlayButton.IsEnabled = false;
        SaveButton.IsEnabled = false;
        SetStatus("Playing recording");
    }

    private void StopPlayback(bool updateStatus = true)
    {
        _playbackTimer.Stop();
        _playbackPlayer.Stop();
        _playbackPlayer.Close();
        _isPlayingBack = false;
        while (_pendingSamples.TryDequeue(out _)) { }
        if (_capture == null)
            _renderTimer.Stop();
        SetStoppedState();
        DeletePlaybackTempFile();
        if (updateStatus)
            SetStatus(_recorded.Count > 0 ? "Playback stopped" : "Ready");
    }

    private void FinishPlayback(string status, bool completed)
    {
        _playbackTimer.Stop();
        _playbackPlayer.Stop();
        _playbackPlayer.Close();
        _isPlayingBack = false;
        if (_capture == null)
            _renderTimer.Stop();
        SetStoppedState();
        SetStatus(status);
        DeletePlaybackTempFile();
        if (completed)
            UpdatePlaybackPosition(_playbackSamples.Length);
    }

    private void PreparePlaybackDisplay(int startOffset)
    {
        _sampleWindow.Clear();
        _latestRenderSamples.Clear();
        _scrollColumnAccumulator = 0;
        _peak = 0;
        _currentLevel = default;
        _peakHoldLevel = default;
        while (_pendingSamples.TryDequeue(out _)) { }
        ClearSpectrogram();
        ClearWaveform();
        UpdatePlaybackSliderBounds();
        UpdatePlaybackPosition(startOffset);
    }

    private void EnqueuePlaybackSamples(short[] samples, int nextOffset)
    {
        _pendingSamples.Enqueue(samples);
        UpdatePlaybackLevel(samples);
        UpdatePlaybackPosition(nextOffset);
    }

    private void UpdatePlaybackLevel(short[] samples)
    {
        double peak = 0;
        for (int i = 0; i < samples.Length; i++)
            peak = Math.Max(peak, Math.Abs(samples[i] / 32768.0));

        _peak = Math.Max(_peak * 0.92, peak);
        _currentLevel = LevelMeterReading.Mono(peak);
    }

    private void UpdatePlaybackSliderBounds()
    {
        if (PlaybackSlider == null || PlaybackPositionText == null)
            return;

        _updatingPlaybackSlider = true;
        PlaybackSlider.Maximum = SamplesToSeconds(_recorded.Count);
        _updatingPlaybackSlider = false;
        UpdatePlaybackPosition(SecondsToSampleOffset(PlaybackSlider.Value));
    }

    private void UpdatePlaybackPosition(int sampleOffset)
    {
        if (PlaybackSlider == null || PlaybackPositionText == null)
            return;

        double position = SamplesToSeconds(Math.Clamp(sampleOffset, 0, _playbackSamples.Length > 0 ? _playbackSamples.Length : _recorded.Count));
        double duration = SamplesToSeconds(_playbackSamples.Length > 0 ? _playbackSamples.Length : _recorded.Count);
        _updatingPlaybackSlider = true;
        PlaybackSlider.Maximum = duration;
        PlaybackSlider.Value = Math.Clamp(position, PlaybackSlider.Minimum, PlaybackSlider.Maximum);
        _updatingPlaybackSlider = false;
        PlaybackPositionText.Text = $"{FormatDuration(position)} / {FormatDuration(duration)}";
    }

    private void PlaybackSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingPlaybackSlider)
            return;

        UpdatePlaybackPosition(SecondsToSampleOffset(PlaybackSlider.Value));
    }

    private void PlaybackSlider_SeekCommitted(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_recorded.Count == 0)
            return;

        int offset = SecondsToSampleOffset(PlaybackSlider.Value);
        if (_isPlayingBack)
            SeekPlayback(offset);
        else
            UpdatePlaybackPosition(offset);
    }

    private void PlaybackTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isPlayingBack || _playbackSamples.Length == 0)
            return;

        int currentOffset = Math.Clamp(SecondsToSampleOffset(_playbackPlayer.Position.TotalSeconds), 0, _playbackSamples.Length);
        if (currentOffset < _lastPlaybackSampleOffset)
            _lastPlaybackSampleOffset = currentOffset;

        const int chunk = DisplaySampleRate / 20;
        while (_lastPlaybackSampleOffset < currentOffset)
        {
            int length = Math.Min(chunk, currentOffset - _lastPlaybackSampleOffset);
            var samples = new short[length];
            Array.Copy(_playbackSamples, _lastPlaybackSampleOffset, samples, 0, length);
            _lastPlaybackSampleOffset += length;
            EnqueuePlaybackSamples(samples, _lastPlaybackSampleOffset);
        }

        UpdatePlaybackPosition(currentOffset);
    }

    private void SeekPlayback(int offset)
    {
        offset = Math.Clamp(offset, 0, Math.Max(0, _playbackSamples.Length - 1));
        PreparePlaybackDisplay(offset);
        _lastPlaybackSampleOffset = offset;
        _playbackStarted = DateTime.Now.AddSeconds(-offset / (double)DisplaySampleRate);
        _playbackPlayer.Position = TimeSpan.FromSeconds(SamplesToSeconds(offset));
        _playbackPlayer.Play();
    }

    private string CreatePlaybackWaveFile(short[] samples)
    {
        DeletePlaybackTempFile();
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"SpectrumViewerRT-playback-{Environment.ProcessId}.wav");
        WaveFile.Save16BitMono(path, samples, DisplaySampleRate);
        return path;
    }

    private void DeletePlaybackTempFile()
    {
        if (string.IsNullOrEmpty(_playbackTempFile))
            return;

        try
        {
            if (File.Exists(_playbackTempFile))
                File.Delete(_playbackTempFile);
        }
        catch
        {
            // The temp file is best-effort cleanup; playback can continue without it.
        }

        _playbackTempFile = null;
    }

    private static double SamplesToSeconds(int samples) => samples / (double)DisplaySampleRate;

    private static int SecondsToSampleOffset(double seconds) =>
        Math.Max(0, (int)Math.Round(seconds * DisplaySampleRate));

    private static string FormatDuration(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours:0}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes:00}:{time.Seconds:00}";
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_recorded.Count == 0)
            return;

        var dialog = new SaveFileDialog
        {
            Filter = "WAV file (*.wav)|*.wav",
            FileName = $"spectrum-recording-{DateTime.Now:yyyyMMdd-HHmmss}.wav"
        };

        if (dialog.ShowDialog(this) == true)
        {
            WaveFile.Save16BitMono(dialog.FileName, _recorded, DisplaySampleRate);
            SetStatus($"Saved: {dialog.FileName}");
        }
    }

    private void Capture_SamplesAvailable(short[] samples)
    {
        if (samples.Length == 0)
            return;

        _pendingSamples.Enqueue(samples);
        _monitor?.Play(samples);
    }

    private void Capture_StereoLevelAvailable(LevelMeterReading level)
    {
        _currentLevel = level;
        var now = DateTime.Now;

        if (level.Left >= _peakHoldLevel.Left)
        {
            _peakHoldLevel = _peakHoldLevel with { Left = level.Left };
            _leftPeakHoldUntil = now.AddMilliseconds(900);
        }

        if (level.Right >= _peakHoldLevel.Right)
        {
            _peakHoldLevel = _peakHoldLevel with { Right = level.Right };
            _rightPeakHoldUntil = now.AddMilliseconds(900);
        }
    }

    private void RenderTimer_Tick(object? sender, EventArgs e)
    {
        int appended = 0;
        while (_pendingSamples.TryDequeue(out var samples))
        {
            appended += samples.Length;
            if (_isRecording)
                _recorded.AddRange(samples);
            _sampleWindow.AddRange(samples);
            _latestRenderSamples.AddRange(samples);
        }

        if (_sampleWindow.Count > FftSize * 2)
            _sampleWindow.RemoveRange(0, _sampleWindow.Count - FftSize * 2);

        if (appended > 0 && _sampleWindow.Count >= FftSize)
        {
            _scrollColumnAccumulator += PixelsPerSecond / Math.Max(1.0, FpsSlider.Value);
            int columns = Math.Clamp((int)_scrollColumnAccumulator, 0, ImageWidth);
            if (columns > 0)
            {
                _scrollColumnAccumulator -= columns;
                if (DisplayModeCombo.SelectedIndex == 1)
                    DrawSpectrumAnalyzerFrame();
                else
                    DrawSpectrumColumns(columns);
                DrawWaveformColumns(columns);
                _latestRenderSamples.Clear();
            }
        }
        else if (DisplayModeCombo.SelectedIndex == 1 && _capture != null)
        {
            DecaySpectrumAnalyzerHolds(DateTime.Now);
        }

        UpdateLevelMeter();
        PeakText.Text = _peak > 0.00001 ? $"Peak: {20 * Math.Log10(_peak):0.0} dB" : "Peak: -inf dB";
        DurationText.Text = _isPlayingBack
            ? $"Playback: {(DateTime.Now - _playbackStarted):mm\\:ss}"
            : $"{(_isRecording ? "Record" : "Live")}: {(DateTime.Now - _recordingStarted):mm\\:ss}";
        _renderTimer.Interval = TimeSpan.FromSeconds(1.0 / Math.Max(1.0, FpsSlider.Value));
    }

    private void UpdateLevelMeter()
    {
        var now = DateTime.Now;
        const double decay = 0.965;

        if (now > _leftPeakHoldUntil)
            _peakHoldLevel = _peakHoldLevel with { Left = Math.Max(_currentLevel.Left, _peakHoldLevel.Left * decay) };
        if (now > _rightPeakHoldUntil)
            _peakHoldLevel = _peakHoldLevel with { Right = Math.Max(_currentLevel.Right, _peakHoldLevel.Right * decay) };

        LevelMeter.LeftLevel = Math.Clamp(_currentLevel.Left, 0, 2);
        LevelMeter.RightLevel = Math.Clamp(_currentLevel.Right, 0, 2);
        LevelMeter.LeftPeakHold = Math.Clamp(_peakHoldLevel.Left, 0, 2);
        LevelMeter.RightPeakHold = Math.Clamp(_peakHoldLevel.Right, 0, 2);
        LevelMeter.ColorTheme = Math.Max(0, MeterColorCombo.SelectedIndex);
        LevelMeter.MeterStyle = Math.Max(0, MeterStyleCombo.SelectedIndex);
        LevelMeter.ShowUnlitSegments = ShowUnlitCheck.IsChecked == true;
        LevelMeter.GlowEnabled = GlowCheck.IsChecked == true;
        LevelMeter.TextureEnabled = TextureCheck.IsChecked == true;
    }

    private void DrawSpectrumAnalyzerFrame()
    {
        if (_sampleWindow.Count < FftSize)
            return;

        var real = new double[FftSize];
        var imaginary = new double[FftSize];
        int start = _sampleWindow.Count - FftSize;
        for (int i = 0; i < FftSize; i++)
        {
            double hann = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (FftSize - 1));
            real[i] = _sampleWindow[start + i] / 32768.0 * hann * GainSlider.Value;
        }

        Fft.Transform(real, imaginary);
        var now = DateTime.Now;
        for (int band = 0; band < _analyzerLevels.Length; band++)
        {
            double startFrequency = 20.0 * Math.Pow(MaxFrequency / 20.0, band / (double)_analyzerLevels.Length);
            double endFrequency = 20.0 * Math.Pow(MaxFrequency / 20.0, (band + 1) / (double)_analyzerLevels.Length);
            int startBin = Math.Clamp((int)(startFrequency / DisplaySampleRate * FftSize), 1, FftSize / 2 - 2);
            int endBin = Math.Clamp((int)(endFrequency / DisplaySampleRate * FftSize), startBin + 1, FftSize / 2 - 1);
            double sum = 0;
            for (int bin = startBin; bin <= endBin; bin++)
                sum += Magnitude(real, imaginary, bin);
            double magnitude = sum / Math.Max(1, endBin - startBin + 1) / (FftSize * 0.5);
            double db = Math.Clamp(20.0 * Math.Log10(magnitude + 0.0000000001), -90, 14);
            _analyzerLevels[band] = Math.Max(db, _analyzerLevels[band] - 2.0);
            if (db >= _analyzerHolds[band])
            {
                _analyzerHolds[band] = db;
                _analyzerHoldUntil[band] = now.AddMilliseconds(900);
            }
            else if (now > _analyzerHoldUntil[band])
            {
                _analyzerHolds[band] = Math.Max(_analyzerLevels[band], _analyzerHolds[band] - 1.2);
            }
        }

        UpdateSpectrumAnalyzerDisplay();
    }

    private void DecaySpectrumAnalyzerHolds(DateTime now)
    {
        bool changed = false;
        for (int band = 0; band < _analyzerHolds.Length; band++)
        {
            _analyzerLevels[band] = Math.Max(-90.0, _analyzerLevels[band] - 2.0);
            if (now <= _analyzerHoldUntil[band])
                continue;

            double previous = _analyzerHolds[band];
            _analyzerHolds[band] = Math.Max(_analyzerLevels[band], _analyzerHolds[band] - 1.2);
            changed |= Math.Abs(previous - _analyzerHolds[band]) > 0.001;
        }

        if (changed)
            UpdateSpectrumAnalyzerDisplay();
    }

    private double PixelsPerSecond => ImageWidth / VisibleSeconds;

    private double VisibleSeconds => Math.Max(0.1, TimeDivisionSlider.Value * TimeDivisions);

    private void DrawSpectrumColumns(int columns)
    {
        columns = Math.Clamp(columns, 1, ImageWidth);
        int bytesPerPixel = 4;
        int rowBytes = ImageWidth * bytesPerPixel;
        int shiftBytes = columns * bytesPerPixel;
        for (int y = 0; y < ImageHeight; y++)
        {
            int rowStart = y * rowBytes;
            Buffer.BlockCopy(_pixels, rowStart + shiftBytes, _pixels, rowStart, rowBytes - shiftBytes);
            Array.Clear(_pixels, rowStart + rowBytes - shiftBytes, shiftBytes);
        }

        var real = new double[FftSize];
        var imaginary = new double[FftSize];
        int start = _sampleWindow.Count - FftSize;
        for (int i = 0; i < FftSize; i++)
        {
            double hann = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (FftSize - 1));
            real[i] = _sampleWindow[start + i] / 32768.0 * hann * GainSlider.Value;
        }

        Fft.Transform(real, imaginary);

        for (int y = 0; y < ImageHeight; y++)
        {
            double frequency = FrequencyForRow(y);
            double binPosition = Math.Clamp(frequency / DisplaySampleRate * FftSize, 1, FftSize / 2 - 2);
            int bin = (int)binPosition;
            double fraction = binPosition - bin;
            double low = Magnitude(real, imaginary, bin);
            double high = Magnitude(real, imaginary, bin + 1);
            double magnitude = (low + (high - low) * fraction) / (FftSize * 0.5);
            double db = 20.0 * Math.Log10(magnitude + 0.0000000001);
            double intensity = Math.Clamp((db + RangeSlider.Value) / RangeSlider.Value, 0, 1);
            intensity = Math.Pow(intensity, 0.72);
            var color = ColorMap(intensity);
            for (int x = ImageWidth - columns; x < ImageWidth; x++)
            {
                int index = (y * ImageWidth + x) * 4;
                _pixels[index + 0] = color.B;
                _pixels[index + 1] = color.G;
                _pixels[index + 2] = color.R;
                _pixels[index + 3] = 255;
            }
        }

        _spectrogram.WritePixels(new Int32Rect(0, 0, ImageWidth, ImageHeight), _pixels, ImageWidth * 4, 0);
    }

    private double FrequencyForRow(int y)
    {
        double topToBottom = y / (double)(ImageHeight - 1);
        double normalized = 1.0 - topToBottom;
        if (ScaleCombo.SelectedItem as string == "Log")
            return 20.0 * Math.Pow(MaxFrequency / 20.0, normalized);

        return Math.Max(20.0, normalized * MaxFrequency);
    }

    private double MaxFrequency => MaxFrequencyCombo.SelectedIndex switch
    {
        0 => 8000.0,
        1 => 12000.0,
        2 => 16000.0,
        3 => 20000.0,
        _ => DisplaySampleRate / 2.0
    };

    private static double Magnitude(double[] real, double[] imaginary, int bin) =>
        Math.Sqrt(real[bin] * real[bin] + imaginary[bin] * imaginary[bin]);

    private void DrawWaveformColumns(int columns)
    {
        columns = Math.Clamp(columns, 1, WaveformWidth);
        int bytesPerPixel = 4;
        int rowBytes = WaveformWidth * bytesPerPixel;
        int shiftBytes = columns * bytesPerPixel;
        for (int y = 0; y < WaveformHeight; y++)
        {
            int rowStart = y * rowBytes;
            Buffer.BlockCopy(_waveformPixels, rowStart + shiftBytes, _waveformPixels, rowStart, rowBytes - shiftBytes);
            Array.Clear(_waveformPixels, rowStart + rowBytes - shiftBytes, shiftBytes);
        }

        int center = WaveformHeight / 2;
        for (int x = WaveformWidth - columns; x < WaveformWidth; x++)
            SetWaveformPixel(x, center, 24, 38, 52);

        if (_latestRenderSamples.Count > 0)
        {
            short min = short.MaxValue;
            short max = short.MinValue;
            for (int i = 0; i < _latestRenderSamples.Count; i++)
            {
                min = Math.Min(min, _latestRenderSamples[i]);
                max = Math.Max(max, _latestRenderSamples[i]);
            }

            int minY = SampleToWaveformY(min);
            int maxY = SampleToWaveformY(max);
            if (minY > maxY)
                (minY, maxY) = (maxY, minY);

            for (int x = WaveformWidth - columns; x < WaveformWidth; x++)
            {
                for (int y = minY; y <= maxY; y++)
                    SetWaveformPixel(x, y, 90, 220, 255);
            }
        }

        _waveform.WritePixels(new Int32Rect(0, 0, WaveformWidth, WaveformHeight), _waveformPixels, WaveformWidth * 4, 0);
    }

    private int SampleToWaveformY(short sample)
    {
        int center = WaveformHeight / 2;
        double normalized = sample / 32768.0;
        return Math.Clamp(center - (int)Math.Round(normalized * (WaveformHeight * 0.46)), 0, WaveformHeight - 1);
    }

    private void SetWaveformPixel(int x, int y, byte r, byte g, byte b)
    {
        if ((uint)x >= WaveformWidth || (uint)y >= WaveformHeight)
            return;

        int index = (y * WaveformWidth + x) * 4;
        _waveformPixels[index + 0] = b;
        _waveformPixels[index + 1] = g;
        _waveformPixels[index + 2] = r;
        _waveformPixels[index + 3] = 255;
    }

    private static Color ColorMap(double value)
    {
        value = Math.Clamp(value, 0, 1);
        (double position, byte r, byte g, byte b)[] stops =
        {
            (0.00, 1, 7, 28),
            (0.16, 0, 26, 96),
            (0.34, 0, 116, 206),
            (0.52, 27, 191, 122),
            (0.68, 246, 215, 70),
            (0.82, 255, 125, 30),
            (1.00, 244, 20, 28)
        };

        for (int i = 0; i < stops.Length - 1; i++)
        {
            var a = stops[i];
            var b = stops[i + 1];
            if (value <= b.position)
            {
                double t = (value - a.position) / (b.position - a.position);
                return Color.FromRgb(
                    (byte)Math.Round(a.r + (b.r - a.r) * t),
                    (byte)Math.Round(a.g + (b.g - a.g) * t),
                    (byte)Math.Round(a.b + (b.b - a.b) * t));
            }
        }

        return Color.FromRgb(244, 20, 28);
    }

    private void RefreshDevices()
    {
        if (DeviceCombo == null)
            return;

        if (SelectedMode == CaptureMode.SystemOutput)
        {
            DeviceCombo.ItemsSource = new[] { new AudioDevice(-1, "Default Windows output") };
            DeviceCombo.SelectedIndex = 0;
            DeviceCombo.IsEnabled = false;
            MonitorCheck.IsEnabled = true;
            SetStatus("System output mode uses WASAPI loopback");
            return;
        }

        var devices = AudioCapture.GetInputDevices();
        DeviceCombo.ItemsSource = devices;
        DeviceCombo.IsEnabled = true;
        MonitorCheck.IsEnabled = true;
        if (devices.Count > 0)
            DeviceCombo.SelectedIndex = 0;
        SetStatus(devices.Count > 0 ? $"{devices.Count} input device(s)" : "No input devices");
    }

    private void ClearSpectrogram()
    {
        Array.Clear(_pixels);
        _spectrogram.WritePixels(new Int32Rect(0, 0, ImageWidth, ImageHeight), _pixels, ImageWidth * 4, 0);
        UpdateGridOverlay();
    }

    private void ClearWaveform()
    {
        Array.Clear(_waveformPixels);
        _waveform.WritePixels(new Int32Rect(0, 0, WaveformWidth, WaveformHeight), _waveformPixels, WaveformWidth * 4, 0);
        UpdateGridOverlay();
    }

    private void DisplaySetting_Changed(object sender, EventArgs e)
    {
        if (!_uiReady)
            return;

        UpdateControlLabels();
        if (_spectrogram == null || _loadingSettings)
            return;

        ResetDisplayHistory();
        UpdateGridOverlay();
        SaveSettings();
    }

    private void ControlValue_Changed(object sender, EventArgs e)
    {
        if (!_uiReady)
            return;

        UpdateControlLabels();
        UpdateGridOverlay();
        if (!_loadingSettings)
            SaveSettings();
    }

    private void GridSetting_Changed(object sender, EventArgs e)
    {
        if (!_uiReady)
            return;

        UpdateControlLabels();
        if (ReferenceEquals(sender, TimeDivisionSlider) && !_loadingSettings)
            ResetDisplayHistory();
        UpdateGridOverlay();
        if (!_loadingSettings)
            SaveSettings();
    }

    private void GridCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_uiReady)
            UpdateGridOverlay();
    }

    private void UpdateGridOverlay()
    {
        if (SpectrogramGridCanvas == null || WaveformGridCanvas == null || TimeAxisCanvas == null)
            return;

        SpectrogramGridCanvas.Children.Clear();
        WaveformGridCanvas.Children.Clear();
        TimeAxisCanvas.Children.Clear();
        UpdateControlLabels();

        if (GridCheck.IsChecked != true)
            return;

        DrawFrequencyGrid();
        DrawTimeGrid(SpectrogramGridCanvas);
        DrawTimeGrid(WaveformGridCanvas, false);
        DrawTimeAxis();
    }

    private void DrawFrequencyGrid()
    {
        double width = SpectrogramGridCanvas.ActualWidth;
        double height = SpectrogramGridCanvas.ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        foreach (double frequency in FrequencyGridValues())
        {
            double y = FrequencyToCanvasY(frequency, height);
            AddLine(SpectrogramGridCanvas, 0, y, width, y, 60, 72, 86, 0.55);
            AddLabel(SpectrogramGridCanvas, FormatFrequency(frequency), 8, Math.Max(2, y - 16), 150);
        }
    }

    private IEnumerable<double> FrequencyGridValues()
    {
        double[] candidates =
        {
            20, 50, 100, 200, 500, 1000, 2000, 4000,
            8000, 12000, 16000, 20000, 24000
        };

        foreach (double value in candidates)
        {
            if (value <= MaxFrequency + 1)
                yield return value;
        }
    }

    private double FrequencyToCanvasY(double frequency, double height)
    {
        double normalized;
        if (ScaleCombo.SelectedItem as string == "Log")
            normalized = Math.Log(Math.Max(20, frequency) / 20.0) / Math.Log(MaxFrequency / 20.0);
        else
            normalized = frequency / MaxFrequency;

        return (1.0 - Math.Clamp(normalized, 0, 1)) * height;
    }

    private void DrawTimeGrid(Canvas canvas, bool label = false)
    {
        double width = canvas.ActualWidth;
        double height = canvas.ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        double secondsPerDivision = Math.Max(0.05, TimeDivisionSlider.Value);
        double pixelsPerDivision = secondsPerDivision * PixelsPerSecond;
        if (pixelsPerDivision < 8)
            pixelsPerDivision = 8;

        for (double x = width; x >= 0; x -= pixelsPerDivision)
        {
            AddLine(canvas, x, 0, x, height, 60, 72, 86, 0.45);
        }
    }

    private void DrawTimeAxis()
    {
        double width = TimeAxisCanvas.ActualWidth;
        double height = TimeAxisCanvas.ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        double secondsPerDivision = Math.Max(0.05, TimeDivisionSlider.Value);
        double pixelsPerDivision = Math.Max(8, secondsPerDivision * PixelsPerSecond);
        for (double x = width; x >= 0; x -= pixelsPerDivision)
        {
            AddLine(TimeAxisCanvas, x, 0, x, 6, 84, 100, 116, 0.9);
            if (x < width - 4)
            {
                double secondsAgo = (width - x) / PixelsPerSecond;
                AddLabel(TimeAxisCanvas, $"-{secondsAgo:0.##}s", Math.Max(2, x + 4), 6, 70, withShadow: false);
            }
        }
    }

    private static void AddLine(Canvas canvas, double x1, double y1, double x2, double y2, byte r, byte g, byte b, double opacity)
    {
        canvas.Children.Add(new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = new SolidColorBrush(Color.FromRgb(r, g, b)),
            StrokeThickness = 1,
            Opacity = opacity
        });
    }

    private static void AddLabel(Canvas canvas, string text, double x, double y, double maxWidth, bool withShadow = true)
    {
        var label = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(221, 239, 255)),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            MaxWidth = maxWidth,
            Effect = withShadow ? new DropShadowEffect
            {
                BlurRadius = 4,
                ShadowDepth = 1,
                Color = Colors.Black,
                Opacity = 0.95
            } : null
        };
        Canvas.SetLeft(label, x);
        Canvas.SetTop(label, y);
        canvas.Children.Add(label);
    }

    private static string FormatFrequency(double frequency) =>
        frequency >= 1000 ? $"{frequency / 1000.0:0.#} kHz" : $"{frequency:0} Hz";

    private void UpdateControlLabels()
    {
        if (GainValueText == null || RangeValueText == null || FpsValueText == null ||
            TimeDivisionText == null || VisibleTimeText == null || GainSlider == null ||
            RangeSlider == null || FpsSlider == null || TimeDivisionSlider == null)
            return;

        GainValueText.Text = $"x{GainSlider.Value:0.0}";
        RangeValueText.Text = $"{RangeSlider.Value:0} dB";
        FpsValueText.Text = $"{FpsSlider.Value:0} fps";
        TimeDivisionText.Text = $"{TimeDivisionSlider.Value:0.00} s";
        VisibleTimeText.Text = $"{VisibleSeconds:0.0} s";
    }

    private void StopCapture(bool triggerVfdDecay = true)
    {
        _renderTimer.Stop();
        _capture?.Dispose();
        _capture = null;
        _monitor?.Dispose();
        _monitor = null;
        _isRecording = false;
        _currentLevel = default;
        _peakHoldLevel = default;
        UpdateLevelMeter();
        SetInputControlsEnabled(true);
        if (triggerVfdDecay && _uiReady)
            TriggerVfdFullScaleDecay();
    }

    private void StopLiveMonitor()
    {
        StopCapture();
        SetMonitorChecked(false);
        SetStoppedState();
        SetStatus("Ready");
    }

    private bool ShouldPlayMonitorAudio() =>
        false;

    private void SetMonitorChecked(bool value)
    {
        _changingMonitorCheck = true;
        MonitorCheck.IsChecked = value;
        _changingMonitorCheck = false;
    }

    private void SetStoppedState()
    {
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        SetInputControlsEnabled(true);
        PlayButton.IsEnabled = _recorded.Count > 0;
        SaveButton.IsEnabled = _recorded.Count > 0;
        UpdatePlaybackSliderBounds();
    }

    private void SetInputControlsEnabled(bool enabled)
    {
        if (ModeCombo == null)
            return;

        ModeCombo.IsEnabled = enabled;
        RefreshButton.IsEnabled = enabled;
        DeviceCombo.IsEnabled = enabled && SelectedMode == CaptureMode.Microphone;
    }

    private void SetStatus(string message)
    {
        if (StatusText != null)
            StatusText.Text = message;
    }

    private void ResetDisplayHistory()
    {
        if (_resettingDisplay)
            return;

        _resettingDisplay = true;
        ClearSpectrogram();
        ClearWaveform();
        _sampleWindow.Clear();
        _latestRenderSamples.Clear();
        _scrollColumnAccumulator = 0;
        _currentLevel = default;
        _peakHoldLevel = default;
        Array.Fill(_analyzerLevels, -90.0);
        Array.Fill(_analyzerHolds, -90.0);
        Array.Fill(_analyzerHoldUntil, DateTime.MinValue);
        UpdateSpectrumAnalyzerDisplay();
        _resettingDisplay = false;
    }

    private void TriggerVfdFullScaleDecay()
    {
        _vfdDecayStarted = DateTime.Now;
        _vfdDecayActive = true;
        SetVfdDecayVisuals(14.0);
        _vfdDecayTimer.Start();
    }

    private void CancelVfdDecay()
    {
        _vfdDecayActive = false;
        _vfdDecayTimer.Stop();
    }

    private void VfdDecayTimer_Tick(object? sender, EventArgs e)
    {
        if (!_vfdDecayActive)
            return;

        double elapsed = (DateTime.Now - _vfdDecayStarted).TotalSeconds;
        double normalized = Math.Exp(-elapsed / 0.65);
        double db = -60.0 + 74.0 * normalized;
        SetVfdDecayVisuals(db);

        if (elapsed >= 3.0 || db <= -59.0)
        {
            _vfdDecayActive = false;
            _vfdDecayTimer.Stop();
            SetVfdDecayVisuals(-90.0);
        }
    }

    private void SetVfdDecayVisuals(double db)
    {
        double level = db <= -80 ? 0.0 : Math.Pow(10.0, db / 20.0);
        LevelMeter.LeftLevel = level;
        LevelMeter.RightLevel = level;
        LevelMeter.LeftPeakHold = level;
        LevelMeter.RightPeakHold = level;
        LevelMeter.ColorTheme = Math.Max(0, MeterColorCombo.SelectedIndex);
        LevelMeter.MeterStyle = Math.Max(0, MeterStyleCombo.SelectedIndex);
        LevelMeter.ShowUnlitSegments = ShowUnlitCheck.IsChecked == true;
        LevelMeter.GlowEnabled = GlowCheck.IsChecked == true;
        LevelMeter.TextureEnabled = TextureCheck.IsChecked == true;

        Array.Fill(_analyzerLevels, db);
        Array.Fill(_analyzerHolds, db);
        UpdateSpectrumAnalyzerDisplay();
    }

    private void ApplySettings(AppSettings settings)
    {
        ModeCombo.SelectedIndex = settings.SourceIndex;
        GainSlider.Value = settings.Gain;
        RangeSlider.Value = settings.RangeDb;
        FpsSlider.Value = settings.Fps;
        TimeDivisionSlider.Value = settings.TimeDivisionSeconds;
        ScaleCombo.SelectedIndex = settings.ScaleIndex;
        MaxFrequencyCombo.SelectedIndex = settings.MaxFrequencyIndex;
        MeterColorCombo.SelectedIndex = settings.MeterColorIndex;
        MeterStyleCombo.SelectedIndex = settings.MeterStyleIndex;
        DisplayModeCombo.SelectedIndex = settings.DisplayModeIndex;
        AlwaysOnTopCheck.IsChecked = settings.AlwaysOnTop;
        MeterOnlyCheck.IsChecked = settings.MeterOnly;
        GridCheck.IsChecked = settings.GridEnabled;
        ShowUnlitCheck.IsChecked = settings.ShowUnlitSegments;
        GlowCheck.IsChecked = settings.GlowEnabled;
        TextureCheck.IsChecked = settings.TextureEnabled;
        LevelMeter.ColorTheme = settings.MeterColorIndex;
        LevelMeter.MeterStyle = settings.MeterStyleIndex;
        ApplyMeterVisualSettings();
    }

    private AppSettings CurrentSettings() => new()
    {
        Gain = GainSlider.Value,
        RangeDb = RangeSlider.Value,
        Fps = FpsSlider.Value,
        TimeDivisionSeconds = TimeDivisionSlider.Value,
        SourceIndex = ModeCombo.SelectedIndex,
        ScaleIndex = ScaleCombo.SelectedIndex,
        MaxFrequencyIndex = MaxFrequencyCombo.SelectedIndex,
        MeterColorIndex = MeterColorCombo.SelectedIndex,
        MeterStyleIndex = MeterStyleCombo.SelectedIndex,
        DisplayModeIndex = DisplayModeCombo.SelectedIndex,
        AlwaysOnTop = AlwaysOnTopCheck.IsChecked == true,
        MeterOnly = MeterOnlyCheck.IsChecked == true,
        GridEnabled = GridCheck.IsChecked == true,
        ShowUnlitSegments = ShowUnlitCheck.IsChecked == true,
        GlowEnabled = GlowCheck.IsChecked == true,
        TextureEnabled = TextureCheck.IsChecked == true
    };

    private void SaveSettings()
    {
        try
        {
            CurrentSettings().Save();
        }
        catch (Exception ex)
        {
            SetStatus($"Settings save failed: {ex.Message}");
        }
    }

    private void DefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyDefaultSettings(save: true);
    }

    private void ApplyDefaultSettings(bool save)
    {
        _loadingSettings = true;
        ApplySettings(new AppSettings());
        _loadingSettings = false;
        ResetDisplayHistory();
        ApplyDisplayMode();
        ApplyMeterOnlyState();
        Topmost = AlwaysOnTopCheck.IsChecked == true;
        UpdateControlLabels();
        UpdateGridOverlay();
        if (save)
            SaveSettings();
    }

    private void GainSlider_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        GainSlider.Value = Defaults.Gain;
    }

    private void RangeSlider_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        RangeSlider.Value = Defaults.RangeDb;
    }

    private void FpsSlider_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        FpsSlider.Value = Defaults.Fps;
    }

    private void TimeDivisionSlider_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        TimeDivisionSlider.Value = Defaults.TimeDivisionSeconds;
    }

    private void ScaleCombo_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ScaleCombo.SelectedIndex = Defaults.ScaleIndex;
    }

    private void MaxFrequencyCombo_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        MaxFrequencyCombo.SelectedIndex = Defaults.MaxFrequencyIndex;
    }

    private void MeterSetting_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady)
            return;

        LevelMeter.ColorTheme = Math.Max(0, MeterColorCombo.SelectedIndex);
        LevelMeter.MeterStyle = Math.Max(0, MeterStyleCombo.SelectedIndex);
        ApplyMeterVisualSettings();
        SaveSettings();
    }

    private void MeterVisualSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (!_uiReady)
            return;

        ApplyMeterVisualSettings();
        SaveSettings();
    }

    private void ApplyMeterVisualSettings()
    {
        if (LevelMeter == null || SpectrumAnalyzer == null)
            return;

        LevelMeter.ColorTheme = Math.Max(0, MeterColorCombo.SelectedIndex);
        LevelMeter.MeterStyle = Math.Max(0, MeterStyleCombo.SelectedIndex);
        LevelMeter.ShowUnlitSegments = ShowUnlitCheck.IsChecked == true;
        LevelMeter.GlowEnabled = GlowCheck.IsChecked == true;
        LevelMeter.TextureEnabled = TextureCheck.IsChecked == true;
        SpectrumAnalyzer.Update(
            _analyzerLevels,
            _analyzerHolds,
            Math.Max(0, MeterColorCombo.SelectedIndex),
            Math.Max(0, MeterStyleCombo.SelectedIndex),
            ShowUnlitCheck.IsChecked == true,
            GlowCheck.IsChecked == true,
            TextureCheck.IsChecked == true);
    }

    private void UpdateSpectrumAnalyzerDisplay()
    {
        SpectrumAnalyzer.Update(
            _analyzerLevels,
            _analyzerHolds,
            Math.Max(0, MeterColorCombo.SelectedIndex),
            Math.Max(0, MeterStyleCombo.SelectedIndex),
            ShowUnlitCheck.IsChecked == true,
            GlowCheck.IsChecked == true,
            TextureCheck.IsChecked == true);
    }

    private void DisplayModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady)
            return;

        ApplyDisplayMode();
        ResetDisplayHistory();
        SaveSettings();
    }

    private void ApplyDisplayMode()
    {
        bool analyzer = DisplayModeCombo.SelectedIndex == 1;
        SpectrogramImage.Visibility = analyzer ? Visibility.Collapsed : Visibility.Visible;
        SpectrogramGridCanvas.Visibility = analyzer ? Visibility.Collapsed : Visibility.Visible;
        TimeAxisCanvas.Visibility = analyzer ? Visibility.Collapsed : Visibility.Visible;
        SpectrumAnalyzer.Visibility = analyzer ? Visibility.Visible : Visibility.Collapsed;
        WaveformImage.Visibility = analyzer ? Visibility.Collapsed : Visibility.Visible;
        WaveformGridCanvas.Visibility = analyzer ? Visibility.Collapsed : Visibility.Visible;
    }

    private void MeterOnlyCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_uiReady)
            return;

        ApplyMeterOnlyState();
        SaveSettings();
    }

    private void ApplyMeterOnlyState()
    {
        bool meterOnly = MeterOnlyCheck.IsChecked == true;
        TopPanel.Visibility = meterOnly ? Visibility.Collapsed : Visibility.Visible;
        if (MainContentGrid == null)
            return;

        MainContentGrid.RowDefinitions[0].Height = meterOnly ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        MainContentGrid.RowDefinitions[1].Height = meterOnly ? new GridLength(0) : new GridLength(24);
        MainContentGrid.RowDefinitions[2].Height = meterOnly ? new GridLength(0) : new GridLength(112);
        MainContentGrid.RowDefinitions[3].Height = new GridLength(92);
        ApplyMeterOnlyWindowSize(meterOnly);
    }

    private void ApplyMeterOnlyWindowSize(bool meterOnly)
    {
        if (meterOnly)
        {
            if (!_meterOnlySizeApplied)
            {
                _normalWindowState = WindowState;
                _normalWindowWidth = WindowState == WindowState.Normal ? Width : RestoreBounds.Width;
                _normalWindowHeight = WindowState == WindowState.Normal ? Height : RestoreBounds.Height;
                _normalMinWidth = MinWidth;
                _normalMinHeight = MinHeight;
            }

            WindowState = WindowState.Normal;
            MinWidth = 720;
            MinHeight = 136;
            Width = Math.Max(MinWidth, Math.Min(_normalWindowWidth, 980));
            Height = MinHeight;
            _meterOnlySizeApplied = true;
            return;
        }

        if (!_meterOnlySizeApplied)
            return;

        MinWidth = _normalMinWidth;
        MinHeight = _normalMinHeight;
        Width = Math.Max(MinWidth, _normalWindowWidth);
        Height = Math.Max(MinHeight, _normalWindowHeight);
        WindowState = _normalWindowState == WindowState.Minimized ? WindowState.Normal : _normalWindowState;
        _meterOnlySizeApplied = false;
    }

    private void AlwaysOnTopCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_uiReady)
            return;

        Topmost = AlwaysOnTopCheck.IsChecked == true;
        SaveSettings();
    }

    protected override void OnClosed(EventArgs e)
    {
        StopPlayback(updateStatus: false);
        StopCapture(triggerVfdDecay: false);
        CancelVfdDecay();
        base.OnClosed(e);
    }
}
