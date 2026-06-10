using Microsoft.Win32;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Shell;
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
    private const double DefaultRecordGainDb = 0.0;

    private readonly ConcurrentQueue<short[]> _pendingSamples = new();
    private sealed record StereoPacket(short[] Left, short[] Right);
    private readonly ConcurrentQueue<StereoPacket> _pendingStereoSamples = new();
    private readonly List<short> _sampleWindow = new(FftSize * 2);
    private readonly List<short> _leftSampleWindow = new(FftSize * 2);
    private readonly List<short> _rightSampleWindow = new(FftSize * 2);
    private readonly List<short> _recorded = new();
    private readonly List<short> _recordedStereoInterleaved = new();
    private readonly DispatcherTimer _renderTimer = new();
    private readonly DispatcherTimer _vfdDecayTimer = new();
    private readonly WriteableBitmap _spectrogram;
    private readonly byte[] _pixels = new byte[ImageWidth * ImageHeight * 4];
    private readonly WriteableBitmap _waveform;
    private readonly byte[] _waveformPixels = new byte[WaveformWidth * WaveformHeight * 4];
    private readonly List<short> _latestRenderSamples = new();
    private const double TimeDivisions = 10.0;
    private double _scrollColumnAccumulator;
    private double[] _analyzerLevels = CreateAnalyzerValues(Defaults.AnalyzerBandCount);
    private double[] _analyzerHolds = CreateAnalyzerValues(Defaults.AnalyzerBandCount);
    private DateTime[] _analyzerHoldUntil = new DateTime[Defaults.AnalyzerBandCount];
    private double[] _leftAnalyzerLevels = CreateAnalyzerValues(Defaults.AnalyzerBandCount);
    private double[] _leftAnalyzerHolds = CreateAnalyzerValues(Defaults.AnalyzerBandCount);
    private DateTime[] _leftAnalyzerHoldUntil = new DateTime[Defaults.AnalyzerBandCount];
    private double[] _rightAnalyzerLevels = CreateAnalyzerValues(Defaults.AnalyzerBandCount);
    private double[] _rightAnalyzerHolds = CreateAnalyzerValues(Defaults.AnalyzerBandCount);
    private DateTime[] _rightAnalyzerHoldUntil = new DateTime[Defaults.AnalyzerBandCount];
    private int _monoAnalyzerBandCount = Defaults.AnalyzerBandCount;
    private int _stereoAnalyzerBandCount = Defaults.AnalyzerBandCount;
    private int _monoCustomAnalyzerBandCount = Defaults.AnalyzerBandCount;
    private int _stereoCustomAnalyzerBandCount = Defaults.AnalyzerBandCount;
    private double _monoAnalyzerMaxBandWidth = Defaults.MonoAnalyzerMaxBandWidth;
    private double _stereoAnalyzerMaxBandWidth = Defaults.StereoAnalyzerMaxBandWidth;
    private double _monoAnalyzerMaxBandGap = Defaults.MonoAnalyzerMaxBandGap;
    private double _stereoAnalyzerMaxBandGap = Defaults.StereoAnalyzerMaxBandGap;
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
    private DateTime _lastLevelMeterUpdate = DateTime.Now;
    private int _lastDotMeterLeft = int.MinValue;
    private int _lastDotMeterRight = int.MinValue;
    private int _lastDotMeterLeftHold = int.MinValue;
    private int _lastDotMeterRightHold = int.MinValue;
    private bool _isRecording;
    private bool _recordingStereo;
    private bool _recordedStereo;
    private bool _isPlayingBack;
    private bool _changingMonitorCheck;
    private bool _refreshingDevices;
    private bool _updatingPlaybackSlider;
    private bool _showLiveClock;
    private Point _vfdStatusPointerDown;
    private bool _vfdStatusPointerPressed;
    private bool _vfdStatusDragging;
    private bool _loadingSettings;
    private bool _resettingDisplay;
    private bool _uiReady;
    private double _recordGainMultiplier = 1.0;

    private bool VuNormalizeEnabled => VuNormalizeCheck?.IsChecked == true;

    private double VuDisplayGain => VuNormalizeEnabled ? Math.Pow(10.0, 6.0 / 20.0) : 1.0;

    private double VuDisplayDbOffset => VuNormalizeEnabled ? 6.0 : 0.0;
    private DateTime _vfdDecayStarted;
    private bool _vfdDecayActive;
    private bool _compactSizeApplied;
    private double _normalWindowWidth = 1180;
    private double _normalWindowHeight = 720;
    private WindowState _normalWindowState = WindowState.Normal;
    private bool _layoutInitialized;
    private bool _lastLayoutCompact;
    private double _normalMainDisplayHeight = 240;
    private double _compactMainDisplayHeight = 210;
    private PanelVisibilityState _normalPanelState = PanelVisibilityState.NormalDefault;
    private PanelVisibilityState _compactPanelState = PanelVisibilityState.CompactDefault;
    private double _preferredLayoutHeight = 720;

    private readonly record struct PanelVisibilityState(
        bool Transport,
        bool Settings,
        bool MainDisplay,
        bool Waveform,
        bool LevelMeter)
    {
        public static PanelVisibilityState NormalDefault => new(true, true, true, true, true);
        public static PanelVisibilityState CompactDefault => new(true, false, true, true, true);
    }

    private static double[] CreateAnalyzerValues(int count) => Enumerable.Repeat(-90.0, count).ToArray();

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
        MeterStyleCombo.Items.Add("Dot Matrix Block");
        MeterStyleCombo.Items.Add("Dot Matrix Fine Lines");
        DisplayModeCombo.Items.Add("Spectrogram");
        DisplayModeCombo.Items.Add("Spectrum Analyzer (Mono)");
        DisplayModeCombo.Items.Add("Spectrum Analyzer (Stereo)");
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
        ApplyDisplayMode();
        _uiReady = true;
        ApplyWindowLayout();
        UpdateControlLabels();
        UpdateTransportLeds();
        UpdateGridOverlay();
        TriggerVfdFullScaleDecay();
        BeginCapture(record: false, clearRecording: false);
    }

    private CaptureMode SelectedMode =>
        ModeCombo.SelectedItem is ComboBoxItem { Tag: CaptureMode mode } ? mode : CaptureMode.Microphone;

    private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady)
            return;

        RefreshDevices();
        RestartCaptureForSelectedSource();
        if (!_loadingSettings)
            SaveSettings();
    }

    private void DeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || _refreshingDevices)
            return;

        RestartCaptureForSelectedSource();
    }

    private void CompensateSystemVolumeCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_capture is WasapiLoopbackCapture loopback)
            loopback.CompensateOutputVolume = CompensateSystemVolumeCheck.IsChecked == true;

        if (_uiReady && !_loadingSettings)
            SaveSettings();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        bool wasCapturing = _capture != null || _isRecording;
        RefreshDevices();
        if (wasCapturing && !_isPlayingBack)
            RestartCaptureForSelectedSource();
    }

    private void StartButton_Click(object sender, RoutedEventArgs e) => BeginCapture(record: true, clearRecording: true);

    private void BeginCapture(bool record, bool clearRecording = true)
    {
        try
        {
            StopPlayback(updateStatus: false, restartLive: false);
            StopCapture(triggerVfdDecay: false);
            CancelVfdDecay();
            if (record && clearRecording)
            {
                _recorded.Clear();
                _recordedStereoInterleaved.Clear();
                _recordedStereo = false;
                _playbackSamples = Array.Empty<short>();
                UpdatePlaybackSliderBounds();
            }
            _sampleWindow.Clear();
            _leftSampleWindow.Clear();
            _rightSampleWindow.Clear();
            _latestRenderSamples.Clear();
            _scrollColumnAccumulator = 0;
            _peak = 0;
            _currentLevel = default;
            _peakHoldLevel = default;
            while (_pendingSamples.TryDequeue(out _)) { }
            while (_pendingStereoSamples.TryDequeue(out _)) { }
            ClearSpectrogram();
            ClearWaveform();

            _isRecording = record;
            if (record && clearRecording)
                _recordingStereo = SelectedMode == CaptureMode.SystemOutput;
            _capture = CreateCaptureSource();
            _capture.SamplesAvailable += Capture_SamplesAvailable;
            _capture.StereoSamplesAvailable += Capture_StereoSamplesAvailable;
            _capture.LevelAvailable += level => _peak = Math.Max(_peak * 0.92, ApplyLevelGain(level));
            _capture.StereoLevelAvailable += Capture_StereoLevelAvailable;
            _capture.StatusAvailable += message => Dispatcher.BeginInvoke(() => SetStatus(message));

            _monitor = null;
            _capture.Start();

            _recordingStarted = DateTime.Now;
            _renderTimer.Interval = TimeSpan.FromSeconds(1.0 / Math.Max(1.0, FpsSlider.Value));
            _renderTimer.Start();

            SetInputControlsEnabled(true);
            UpdateTransportButtons();
            SetStatus(record
                ? (SelectedMode == CaptureMode.SystemOutput ? "Recording system output" : "Recording microphone")
                : (SelectedMode == CaptureMode.SystemOutput ? "Monitoring system output" : "Monitoring microphone"));
        }
        catch (Exception ex)
        {
            StopCapture(triggerVfdDecay: false);
            SetStoppedState();
            SetStatus(ex.Message);
        }
    }

    private IAudioCaptureSource CreateCaptureSource()
    {
        if (SelectedMode == CaptureMode.SystemOutput)
            return new WasapiLoopbackCapture(CompensateSystemVolumeCheck.IsChecked == true);

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

        if (_isRecording)
        {
            BeginCapture(record: false, clearRecording: false);
            SetStatus(_recorded.Count > 0 ? "Recording stopped" : "Monitoring");
        }
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
        StopPlayback(updateStatus: false, restartLive: false);

        var recorded = _recorded.ToArray();
        if (recorded.Length == 0)
            return;

        _playbackSamples = recorded;
        startOffset = Math.Clamp(startOffset, 0, recorded.Length - 1);

        PreparePlaybackDisplay(startOffset);
        StopCapture(triggerVfdDecay: false);
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
        UpdateTransportButtons();
        SetStatus("Playing recording");
    }

    private void StopPlayback(bool updateStatus = true, bool restartLive = true)
    {
        bool wasPlaying = _isPlayingBack;
        _playbackTimer.Stop();
        _playbackPlayer.Stop();
        _playbackPlayer.Close();
        _isPlayingBack = false;
        while (_pendingSamples.TryDequeue(out _)) { }
        SetStoppedState();
        DeletePlaybackTempFile();
        if (wasPlaying && restartLive)
            BeginCapture(record: false, clearRecording: false);
        if (updateStatus)
            SetStatus(_recorded.Count > 0 ? "Playback stopped" : "Ready");
    }

    private void FinishPlayback(string status, bool completed)
    {
        _playbackTimer.Stop();
        _playbackPlayer.Stop();
        _playbackPlayer.Close();
        _isPlayingBack = false;
        SetStoppedState();
        SetStatus(status);
        DeletePlaybackTempFile();
        if (completed)
            UpdatePlaybackPosition(_playbackSamples.Length);
        BeginCapture(record: false, clearRecording: false);
    }

    private void PreparePlaybackDisplay(int startOffset)
    {
        _sampleWindow.Clear();
        _leftSampleWindow.Clear();
        _rightSampleWindow.Clear();
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
            int startOffset = _lastPlaybackSampleOffset;
            int length = Math.Min(chunk, currentOffset - _lastPlaybackSampleOffset);
            var samples = new short[length];
            Array.Copy(_playbackSamples, _lastPlaybackSampleOffset, samples, 0, length);
            _lastPlaybackSampleOffset += length;
            EnqueuePlaybackSamples(samples, _lastPlaybackSampleOffset);
            AppendPlaybackStereoWindow(startOffset, length);
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

    private void AppendPlaybackStereoWindow(int startOffset, int length)
    {
        if (!_recordedStereo || _recordedStereoInterleaved.Count < (startOffset + length) * 2)
            return;

        for (int i = 0; i < length; i++)
        {
            int index = (startOffset + i) * 2;
            _leftSampleWindow.Add(_recordedStereoInterleaved[index]);
            _rightSampleWindow.Add(_recordedStereoInterleaved[index + 1]);
        }

        TrimSampleWindow(_leftSampleWindow);
        TrimSampleWindow(_rightSampleWindow);
    }

    private string CreatePlaybackWaveFile(short[] samples)
    {
        DeletePlaybackTempFile();
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"SpectrumViewerRT-playback-{Environment.ProcessId}.wav");
        if (_recordedStereo && _recordedStereoInterleaved.Count >= samples.Length * 2)
            WaveFile.Save16BitStereo(path, _recordedStereoInterleaved, DisplaySampleRate);
        else
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
            if (_recordedStereo && _recordedStereoInterleaved.Count > 0)
                WaveFile.Save16BitStereo(dialog.FileName, _recordedStereoInterleaved, DisplaySampleRate);
            else
                WaveFile.Save16BitMono(dialog.FileName, _recorded, DisplaySampleRate);
            SetStatus($"Saved: {dialog.FileName}");
        }
    }

    private void Capture_SamplesAvailable(short[] samples)
    {
        if (samples.Length == 0)
            return;

        var adjusted = ApplyRecordGain(samples);
        _pendingSamples.Enqueue(adjusted);
        _monitor?.Play(adjusted);
    }

    private void Capture_StereoSamplesAvailable(short[] left, short[] right)
    {
        if (left.Length == 0 || right.Length == 0)
            return;

        _pendingStereoSamples.Enqueue(new StereoPacket(ApplyRecordGain(left), ApplyRecordGain(right)));
    }

    private void Capture_StereoLevelAvailable(LevelMeterReading level)
    {
        _currentLevel = ApplyLevelGain(level);
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
                AddRecordedSamples(samples);
            _sampleWindow.AddRange(samples);
            _latestRenderSamples.AddRange(samples);
        }

        while (_pendingStereoSamples.TryDequeue(out var stereo))
        {
            int length = Math.Min(stereo.Left.Length, stereo.Right.Length);
            if (length == 0)
                continue;

            if (_isRecording && _recordingStereo)
                AddRecordedStereoSamples(stereo.Left, stereo.Right, length);

            AppendStereoWindow(_leftSampleWindow, stereo.Left, length);
            AppendStereoWindow(_rightSampleWindow, stereo.Right, length);
        }

        if (_sampleWindow.Count > FftSize * 2)
            _sampleWindow.RemoveRange(0, _sampleWindow.Count - FftSize * 2);
        TrimSampleWindow(_leftSampleWindow);
        TrimSampleWindow(_rightSampleWindow);

        if (appended > 0 && _sampleWindow.Count >= FftSize)
        {
            _scrollColumnAccumulator += PixelsPerSecond / Math.Max(1.0, FpsSlider.Value);
            int columns = Math.Clamp((int)_scrollColumnAccumulator, 0, ImageWidth);
            if (columns > 0)
            {
                _scrollColumnAccumulator -= columns;
                if (DisplayModeCombo.SelectedIndex > 0)
                    DrawSpectrumAnalyzerFrame();
                else
                    DrawSpectrumColumns(columns);
                DrawWaveformColumns(columns);
                _latestRenderSamples.Clear();
            }
        }
        else if (DisplayModeCombo.SelectedIndex > 0 && _capture != null)
        {
            DecaySpectrumAnalyzerHolds(DateTime.Now);
        }

        UpdateLevelMeter();
        UpdateVfdStatusTime();
        _renderTimer.Interval = TimeSpan.FromSeconds(1.0 / Math.Max(1.0, FpsSlider.Value));
    }

    private void UpdateVfdStatusTime()
    {
        DateTime now = DateTime.Now;
        bool liveClock = _showLiveClock && _capture != null && !_isRecording && !_isPlayingBack;
        if (liveClock)
        {
            VfdStatus.TimeText = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            VfdStatus.SecondaryText = now.ToString("MM/dd ddd", CultureInfo.InvariantCulture).ToUpperInvariant();
            return;
        }

        VfdStatus.SecondaryText = string.Empty;
        VfdStatus.TimeText = _isPlayingBack
            ? $"PLAY {(now - _playbackStarted):mm\\:ss}"
            : $"{(_isRecording ? "REC" : "LIVE")} {(now - _recordingStarted):mm\\:ss}";
    }

    private void VfdStatus_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _vfdStatusPointerDown = e.GetPosition(this);
        _vfdStatusPointerPressed = true;
        _vfdStatusDragging = false;
        VfdStatus.CaptureMouse();
    }

    private void VfdStatus_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_vfdStatusPointerPressed ||
            CompactMenuItem.IsChecked != true ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Point current = e.GetPosition(this);
        if (Math.Abs(current.X - _vfdStatusPointerDown.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _vfdStatusPointerDown.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _vfdStatusDragging = true;
        _vfdStatusPointerPressed = false;
        VfdStatus.ReleaseMouseCapture();
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void VfdStatus_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        VfdStatus.ReleaseMouseCapture();
        bool wasDragging = _vfdStatusDragging;
        _vfdStatusPointerPressed = false;
        _vfdStatusDragging = false;
        if (wasDragging)
            return;

        if (_capture == null || _isRecording || _isPlayingBack)
            return;

        _showLiveClock = !_showLiveClock;
        UpdateVfdStatusTime();
        e.Handled = true;
    }

    private void AddRecordedSamples(short[] samples)
    {
        _recorded.AddRange(samples);
    }

    private void AddRecordedStereoSamples(short[] left, short[] right, int length)
    {
        _recordedStereo = true;
        for (int i = 0; i < length; i++)
        {
            _recordedStereoInterleaved.Add(left[i]);
            _recordedStereoInterleaved.Add(right[i]);
        }
    }

    private static void AppendStereoWindow(List<short> window, short[] samples, int length)
    {
        for (int i = 0; i < length; i++)
            window.Add(samples[i]);
    }

    private static void TrimSampleWindow(List<short> window)
    {
        if (window.Count > FftSize * 2)
            window.RemoveRange(0, window.Count - FftSize * 2);
    }

    private short[] ApplyRecordGain(short[] samples)
    {
        double gain = RecordGainMultiplier;
        if (Math.Abs(gain - 1.0) < 0.0001)
            return samples;

        var adjusted = new short[samples.Length];
        for (int i = 0; i < samples.Length; i++)
            adjusted[i] = ApplySampleGain(samples[i], gain);
        return adjusted;
    }

    private double ApplyLevelGain(double level) => Math.Clamp(level * RecordGainMultiplier, 0, 2);

    private LevelMeterReading ApplyLevelGain(LevelMeterReading level)
    {
        double gain = RecordGainMultiplier;
        return new LevelMeterReading(
            Math.Clamp(level.Left * gain, 0, 2),
            Math.Clamp(level.Right * gain, 0, 2));
    }

    private double RecordGainMultiplier => System.Threading.Volatile.Read(ref _recordGainMultiplier);

    private static short ApplySampleGain(short sample, double gain) =>
        (short)Math.Clamp((int)Math.Round(sample * gain), short.MinValue, short.MaxValue);

    private void UpdateLevelMeter()
    {
        var now = DateTime.Now;
        double elapsedSeconds = Math.Clamp((now - _lastLevelMeterUpdate).TotalSeconds, 0, 0.25);
        _lastLevelMeterUpdate = now;

        if (now > _leftPeakHoldUntil)
            _peakHoldLevel = _peakHoldLevel with
            {
                Left = DecayPeakLevel(_peakHoldLevel.Left, _currentLevel.Left, elapsedSeconds)
            };
        if (now > _rightPeakHoldUntil)
            _peakHoldLevel = _peakHoldLevel with
            {
                Right = DecayPeakLevel(_peakHoldLevel.Right, _currentLevel.Right, elapsedSeconds)
            };

        double displayGain = VuDisplayGain;
        double leftLevel = Math.Clamp(_currentLevel.Left * displayGain, 0, 2);
        double rightLevel = Math.Clamp(_currentLevel.Right * displayGain, 0, 2);
        double leftHold = Math.Clamp(_peakHoldLevel.Left * displayGain, 0, 2);
        double rightHold = Math.Clamp(_peakHoldLevel.Right * displayGain, 0, 2);
        if (MeterStyleCombo.SelectedIndex is 2 or 3)
            UpdateQuantizedDotMeter(leftLevel, rightLevel, leftHold, rightHold);
        else
        {
            _lastDotMeterLeft = _lastDotMeterRight = _lastDotMeterLeftHold = _lastDotMeterRightHold = int.MinValue;
            LevelMeter.LeftLevel = leftLevel;
            LevelMeter.RightLevel = rightLevel;
            LevelMeter.LeftPeakHold = leftHold;
            LevelMeter.RightPeakHold = rightHold;
        }
        LevelMeter.ColorTheme = Math.Max(0, MeterColorCombo.SelectedIndex);
        LevelMeter.MeterStyle = Math.Max(0, MeterStyleCombo.SelectedIndex);
        LevelMeter.ShowUnlitSegments = ShowUnlitCheck.IsChecked == true;
        LevelMeter.GlowEnabled = GlowCheck.IsChecked == true;
        LevelMeter.TextureEnabled = TextureCheck.IsChecked == true;
        VfdStatus.ColorTheme = LevelMeter.ColorTheme;
        VfdStatus.ShowUnlitSegments = LevelMeter.ShowUnlitSegments;
        VfdStatus.GlowEnabled = LevelMeter.GlowEnabled;
        VfdStatus.TextureEnabled = LevelMeter.TextureEnabled;
        VfdStatus.DisplayStyle = StatusSegmentMenuItem.IsChecked ? 1 : 0;
    }

    private void UpdateQuantizedDotMeter(double left, double right, double leftHold, double rightHold)
    {
        int steps = Math.Max(16, (int)Math.Round(Math.Max(1, LevelMeter.ActualWidth - 42) / 12.0));
        int leftStep = QuantizeMeterLevel(left, steps);
        int rightStep = QuantizeMeterLevel(right, steps);
        int leftHoldStep = QuantizeMeterLevel(leftHold, steps);
        int rightHoldStep = QuantizeMeterLevel(rightHold, steps);

        if (leftStep != _lastDotMeterLeft)
        {
            _lastDotMeterLeft = leftStep;
            LevelMeter.LeftLevel = MeterStepToLevel(leftStep, steps);
        }
        if (rightStep != _lastDotMeterRight)
        {
            _lastDotMeterRight = rightStep;
            LevelMeter.RightLevel = MeterStepToLevel(rightStep, steps);
        }
        if (leftHoldStep != _lastDotMeterLeftHold)
        {
            _lastDotMeterLeftHold = leftHoldStep;
            LevelMeter.LeftPeakHold = MeterStepToLevel(leftHoldStep, steps);
        }
        if (rightHoldStep != _lastDotMeterRightHold)
        {
            _lastDotMeterRightHold = rightHoldStep;
            LevelMeter.RightPeakHold = MeterStepToLevel(rightHoldStep, steps);
        }
    }

    private static int QuantizeMeterLevel(double level, int steps)
    {
        if (level <= 0)
            return 0;
        double db = Math.Clamp(20.0 * Math.Log10(level), -60, 14);
        return Math.Clamp((int)Math.Round((db + 60.0) / 74.0 * steps), 0, steps);
    }

    private static double MeterStepToLevel(int step, int steps)
    {
        if (step <= 0)
            return 0;
        double db = -60.0 + step / (double)steps * 74.0;
        return Math.Pow(10.0, db / 20.0);
    }

    private static double DecayPeakLevel(double peak, double current, double elapsedSeconds)
    {
        if (peak <= 0 && current <= 0)
            return 0;

        double peakDb = peak <= 0 ? -90.0 : 20.0 * Math.Log10(peak);
        double currentDb = current <= 0 ? -90.0 : 20.0 * Math.Log10(current);
        double decayedDb = Math.Max(currentDb, peakDb - 55.0 * elapsedSeconds);
        return decayedDb <= -60.0 ? 0.0 : Math.Pow(10.0, decayedDb / 20.0);
    }

    private void DrawSpectrumAnalyzerFrame()
    {
        if (_sampleWindow.Count < FftSize)
            return;

        if (DisplayModeCombo.SelectedIndex == 2 && _leftSampleWindow.Count >= FftSize && _rightSampleWindow.Count >= FftSize)
        {
            ComputeAnalyzerLevels(_leftSampleWindow, _leftAnalyzerLevels, _leftAnalyzerHolds, _leftAnalyzerHoldUntil);
            ComputeAnalyzerLevels(_rightSampleWindow, _rightAnalyzerLevels, _rightAnalyzerHolds, _rightAnalyzerHoldUntil);
            UpdateSpectrumAnalyzerDisplay();
            return;
        }

        ComputeAnalyzerLevels(_sampleWindow, _analyzerLevels, _analyzerHolds, _analyzerHoldUntil);
        UpdateSpectrumAnalyzerDisplay();
    }

    private void ComputeAnalyzerLevels(List<short> samples, double[] levels, double[] holds, DateTime[] holdUntil)
    {
        var real = new double[FftSize];
        var imaginary = new double[FftSize];
        int start = samples.Count - FftSize;
        for (int i = 0; i < FftSize; i++)
        {
            double hann = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (FftSize - 1));
            real[i] = samples[start + i] / 32768.0 * hann;
        }

        Fft.Transform(real, imaginary);
        var now = DateTime.Now;
        const double analyzerMaxFrequency = 20000.0;
        for (int band = 0; band < levels.Length; band++)
        {
            double startPosition = band / (double)levels.Length;
            if (levels.Length <= 10 && band >= levels.Length - 2)
            {
                double overlapBands = band == levels.Length - 1 ? 0.75 : 0.45;
                startPosition = Math.Max(0, startPosition - overlapBands / levels.Length);
            }
            double startFrequency = 20.0 * Math.Pow(analyzerMaxFrequency / 20.0, startPosition);
            double endFrequency = 20.0 * Math.Pow(analyzerMaxFrequency / 20.0, (band + 1) / (double)levels.Length);
            int startBin = Math.Clamp((int)(startFrequency / DisplaySampleRate * FftSize), 1, FftSize / 2 - 2);
            int endBin = Math.Clamp((int)(endFrequency / DisplaySampleRate * FftSize), startBin + 1, FftSize / 2 - 1);
            double sum = 0;
            double peakMagnitude = 0;
            for (int bin = startBin; bin <= endBin; bin++)
            {
                double binMagnitude = Magnitude(real, imaginary, bin);
                sum += binMagnitude;
                peakMagnitude = Math.Max(peakMagnitude, binMagnitude);
            }
            double averageMagnitude = sum / Math.Max(1, endBin - startBin + 1);
            double magnitude = levels.Length <= 10 && band >= levels.Length - 2
                ? averageMagnitude * 0.4 + peakMagnitude * 0.6
                : averageMagnitude;
            magnitude /= FftSize * 0.5;
            double db = Math.Clamp(20.0 * Math.Log10(magnitude + 0.0000000001) + VuDisplayDbOffset, -90, 14);
            levels[band] = Math.Max(db, levels[band] - 2.0);
            if (db > -60.0 && db >= holds[band] + 0.05)
            {
                holds[band] = db;
                holdUntil[band] = now.AddMilliseconds(900);
            }
            else if (now > holdUntil[band])
            {
                holds[band] = Math.Max(db, holds[band] - AnalyzerPeakDecayStep);
                if (holds[band] <= -60.0)
                    holds[band] = -90.0;
            }
        }
    }

    private void DecaySpectrumAnalyzerHolds(DateTime now)
    {
        bool changed = DecayAnalyzer(_analyzerLevels, _analyzerHolds, _analyzerHoldUntil, now);
        changed |= DecayAnalyzer(_leftAnalyzerLevels, _leftAnalyzerHolds, _leftAnalyzerHoldUntil, now);
        changed |= DecayAnalyzer(_rightAnalyzerLevels, _rightAnalyzerHolds, _rightAnalyzerHoldUntil, now);

        if (changed)
            UpdateSpectrumAnalyzerDisplay();
    }

    private double AnalyzerPeakDecayStep => 55.0 / Math.Max(12.0, FpsSlider.Value);

    private bool DecayAnalyzer(double[] levels, double[] holds, DateTime[] holdUntil, DateTime now)
    {
        bool changed = false;
        for (int band = 0; band < holds.Length; band++)
        {
            levels[band] = Math.Max(-90.0, levels[band] - 2.0);
            if (now <= holdUntil[band])
                continue;

            double previous = holds[band];
            holds[band] = Math.Max(levels[band], holds[band] - AnalyzerPeakDecayStep);
            if (holds[band] <= -60.0)
                holds[band] = -90.0;
            changed |= Math.Abs(previous - holds[band]) > 0.001;
        }

        return changed;
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

        _refreshingDevices = true;
        try
        {
        if (SelectedMode == CaptureMode.SystemOutput)
        {
            DeviceCombo.ItemsSource = new[] { new AudioDevice(-1, "Default Windows output") };
            DeviceCombo.SelectedIndex = 0;
            DeviceCombo.IsEnabled = false;
            CompensateSystemVolumeCheck.Visibility = Visibility.Visible;
            CompensateSystemVolumeCheck.IsEnabled = true;
            SetStatus("System output mode uses WASAPI loopback");
            return;
        }

        CompensateSystemVolumeCheck.Visibility = Visibility.Collapsed;
        CompensateSystemVolumeCheck.IsEnabled = false;
        int? previousDeviceId = DeviceCombo.SelectedItem is AudioDevice previous ? previous.Id : null;
        var devices = AudioCapture.GetInputDevices();
        DeviceCombo.ItemsSource = devices;
        DeviceCombo.IsEnabled = devices.Count > 0;
        if (devices.Count > 0)
        {
            int selectedIndex = previousDeviceId.HasValue
                ? devices.ToList().FindIndex(device => device.Id == previousDeviceId.Value)
                : -1;
            DeviceCombo.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
        }
        SetStatus(devices.Count > 0 ? $"{devices.Count} input device(s)" : "No input devices");
        }
        finally
        {
            _refreshingDevices = false;
        }
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

        if (!ReferenceEquals(sender, RangeSlider))
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
        if (GainValueText == null || RecordGainValueText == null || RangeValueText == null || FpsButton == null ||
            TimeDivisionText == null || VisibleTimeText == null || GainSlider == null || RecordGainSlider == null ||
            RangeSlider == null || FpsSlider == null || TimeDivisionSlider == null)
            return;

        GainValueText.Text = $"x{GainSlider.Value:0.0}";
        RecordGainValueText.Text = $"{RecordGainSlider.Value:+0.0;-0.0;0.0} dB";
        System.Threading.Volatile.Write(ref _recordGainMultiplier, Math.Pow(10.0, RecordGainSlider.Value / 20.0));
        RangeValueText.Text = $"{RangeSlider.Value:0} dB";
        FpsButton.Content = $"FPS: {FpsSlider.Value:0}";
        if (FpsTextBox != null && !FpsTextBox.IsKeyboardFocusWithin)
            FpsTextBox.Text = FpsSlider.Value.ToString("0", CultureInfo.InvariantCulture);
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

    private void RestartCaptureForSelectedSource()
    {
        if (_isPlayingBack)
            return;

        bool record = _isRecording;
        BeginCapture(record, clearRecording: false);
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
        UpdateTransportButtons();
    }

    private void UpdateTransportButtons()
    {
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = _isRecording || _isPlayingBack;
        SetInputControlsEnabled(true);
        PlayButton.IsEnabled = _recorded.Count > 0 && !_isRecording && !_isPlayingBack;
        SaveButton.IsEnabled = _recorded.Count > 0 && !_isRecording && !_isPlayingBack;
        StartButton.IsEnabled = !_isRecording && !_isPlayingBack;
        UpdateTransportLeds();
        UpdatePlaybackSliderBounds();
    }

    private void UpdateTransportLeds()
    {
        if (RecordLed != null)
        {
            RecordLed.Fill = new SolidColorBrush(_isRecording ? Color.FromRgb(255, 48, 36) : Color.FromRgb(54, 16, 16));
            RecordLed.Stroke = new SolidColorBrush(_isRecording ? Color.FromRgb(255, 128, 112) : Color.FromRgb(90, 27, 27));
            RecordLed.Effect = _isRecording
                ? new DropShadowEffect { Color = Color.FromRgb(255, 38, 28), BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9 }
                : null;
        }

        if (PlayLed != null)
        {
            PlayLed.Fill = new SolidColorBrush(_isPlayingBack ? Color.FromRgb(74, 235, 112) : Color.FromRgb(15, 44, 24));
            PlayLed.Stroke = new SolidColorBrush(_isPlayingBack ? Color.FromRgb(164, 255, 184) : Color.FromRgb(28, 79, 43));
            PlayLed.Effect = _isPlayingBack
                ? new DropShadowEffect { Color = Color.FromRgb(80, 245, 122), BlurRadius = 8, ShadowDepth = 0, Opacity = 0.85 }
                : null;
        }
    }

    private void SetInputControlsEnabled(bool enabled)
    {
        if (ModeCombo == null)
            return;

        ModeCombo.IsEnabled = enabled;
        DeviceCombo.IsEnabled = enabled && SelectedMode == CaptureMode.Microphone;
        CompensateSystemVolumeCheck.IsEnabled = enabled && SelectedMode == CaptureMode.SystemOutput;
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
        Array.Fill(_leftAnalyzerLevels, -90.0);
        Array.Fill(_leftAnalyzerHolds, -90.0);
        Array.Fill(_leftAnalyzerHoldUntil, DateTime.MinValue);
        Array.Fill(_rightAnalyzerLevels, -90.0);
        Array.Fill(_rightAnalyzerHolds, -90.0);
        Array.Fill(_rightAnalyzerHoldUntil, DateTime.MinValue);
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
        Array.Fill(_leftAnalyzerLevels, db);
        Array.Fill(_leftAnalyzerHolds, db);
        Array.Fill(_rightAnalyzerLevels, db);
        Array.Fill(_rightAnalyzerHolds, db);
        UpdateSpectrumAnalyzerDisplay();
    }

    private void ApplySettings(AppSettings settings)
    {
        ModeCombo.SelectedIndex = settings.SourceIndex;
        GainSlider.Value = settings.Gain;
        RecordGainSlider.Value = DefaultRecordGainDb;
        RangeSlider.Value = settings.RangeDb;
        FpsSlider.Value = settings.Fps;
        TimeDivisionSlider.Value = settings.TimeDivisionSeconds;
        ScaleCombo.SelectedIndex = settings.ScaleIndex;
        MaxFrequencyCombo.SelectedIndex = settings.MaxFrequencyIndex;
        MeterColorCombo.SelectedIndex = settings.MeterColorIndex;
        MeterStyleCombo.SelectedIndex = settings.MeterStyleIndex;
        SetStatusDisplayStyle(settings.StatusDisplayStyleIndex);
        DisplayModeCombo.SelectedIndex = settings.DisplayModeIndex == 0
            ? 0
            : settings.AnalyzerModeIndex == 1 || settings.DisplayModeIndex == 2 ? 2 : 1;
        _monoAnalyzerBandCount = settings.MonoAnalyzerBandCount;
        _stereoAnalyzerBandCount = settings.StereoAnalyzerBandCount;
        _monoCustomAnalyzerBandCount = settings.MonoCustomAnalyzerBandCount;
        _stereoCustomAnalyzerBandCount = settings.StereoCustomAnalyzerBandCount;
        _monoAnalyzerMaxBandWidth = settings.MonoAnalyzerMaxBandWidth;
        _stereoAnalyzerMaxBandWidth = settings.StereoAnalyzerMaxBandWidth;
        _monoAnalyzerMaxBandGap = settings.MonoAnalyzerMaxBandGap;
        _stereoAnalyzerMaxBandGap = settings.StereoAnalyzerMaxBandGap;
        ResizeAnalyzerBuffers();
        _normalPanelState = new PanelVisibilityState(
            settings.ShowTransportPanel,
            settings.ShowSettingsPanel,
            settings.ShowMainDisplay,
            settings.ShowWaveform,
            settings.ShowLevelMeter);
        _compactPanelState = new PanelVisibilityState(
            settings.CompactShowTransportPanel,
            settings.CompactShowSettingsPanel,
            settings.CompactShowMainDisplay,
            settings.CompactShowWaveform,
            settings.CompactShowLevelMeter);
        CompactMenuItem.IsChecked = settings.CompactMode;
        ApplyPanelState(settings.CompactMode ? _compactPanelState : _normalPanelState);
        AlwaysOnTopMenuItem.IsChecked = settings.AlwaysOnTop;
        GridCheck.IsChecked = settings.GridEnabled;
        ShowUnlitCheck.IsChecked = settings.ShowUnlitSegments;
        GlowCheck.IsChecked = settings.GlowEnabled;
        TextureCheck.IsChecked = settings.TextureEnabled;
        VuNormalizeCheck.IsChecked = settings.VuNormalizeEnabled;
        CompensateSystemVolumeCheck.IsChecked = settings.CompensateSystemOutputVolume;
        LevelMeter.ColorTheme = settings.MeterColorIndex;
        LevelMeter.MeterStyle = settings.MeterStyleIndex;
        ApplyMeterVisualSettings();
    }

    private AppSettings CurrentSettings()
    {
        CapturePanelState(CompactMenuItem.IsChecked);
        return new AppSettings
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
        StatusDisplayStyleIndex = StatusSegmentMenuItem.IsChecked ? 1 : 0,
        DisplayModeIndex = DisplayModeCombo.SelectedIndex,
        AnalyzerModeIndex = DisplayModeCombo.SelectedIndex == 2 ? 1 : 0,
        MonoAnalyzerBandCount = _monoAnalyzerBandCount,
        StereoAnalyzerBandCount = _stereoAnalyzerBandCount,
        MonoCustomAnalyzerBandCount = _monoCustomAnalyzerBandCount,
        StereoCustomAnalyzerBandCount = _stereoCustomAnalyzerBandCount,
        MonoAnalyzerMaxBandWidth = _monoAnalyzerMaxBandWidth,
        StereoAnalyzerMaxBandWidth = _stereoAnalyzerMaxBandWidth,
        MonoAnalyzerMaxBandGap = _monoAnalyzerMaxBandGap,
        StereoAnalyzerMaxBandGap = _stereoAnalyzerMaxBandGap,
        AlwaysOnTop = AlwaysOnTopMenuItem.IsChecked,
        ShowTransportPanel = _normalPanelState.Transport,
        ShowSettingsPanel = _normalPanelState.Settings,
        ShowMainDisplay = _normalPanelState.MainDisplay,
        ShowWaveform = _normalPanelState.Waveform,
        ShowLevelMeter = _normalPanelState.LevelMeter,
        CompactShowTransportPanel = _compactPanelState.Transport,
        CompactShowSettingsPanel = _compactPanelState.Settings,
        CompactShowMainDisplay = _compactPanelState.MainDisplay,
        CompactShowWaveform = _compactPanelState.Waveform,
        CompactShowLevelMeter = _compactPanelState.LevelMeter,
        CompactMode = CompactMenuItem.IsChecked,
        GridEnabled = GridCheck.IsChecked == true,
        ShowUnlitSegments = ShowUnlitCheck.IsChecked == true,
        GlowEnabled = GlowCheck.IsChecked == true,
        TextureEnabled = TextureCheck.IsChecked == true,
        VuNormalizeEnabled = VuNormalizeCheck.IsChecked == true,
        CompensateSystemOutputVolume = CompensateSystemVolumeCheck.IsChecked == true
        };
    }

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
        ApplyWindowLayout();
        UpdateControlLabels();
        UpdateGridOverlay();
        if (save)
            SaveSettings();
    }

    private void GainSlider_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        GainSlider.Value = Defaults.Gain;
    }

    private void RecordGainSlider_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        RecordGainSlider.Value = DefaultRecordGainDb;
    }

    private void RangeSlider_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        RangeSlider.Value = Defaults.RangeDb;
    }

    private void FpsSlider_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        FpsSlider.Value = Defaults.Fps;
    }

    private void FpsButton_Click(object sender, RoutedEventArgs e)
    {
        if (FpsButton.ContextMenu == null)
            return;

        FpsButton.ContextMenu.PlacementTarget = FpsButton;
        FpsButton.ContextMenu.Placement = PlacementMode.Bottom;
        FpsButton.ContextMenu.IsOpen = true;
    }

    private void FpsPresetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string value } &&
            double.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out double fps))
        {
            FpsSlider.Value = fps;
        }
    }

    private void FpsDetailsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            UpdateControlLabels();
            FpsDetailPopup.IsOpen = true;
            FpsTextBox.Focus();
            FpsTextBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void FpsTextBox_LostFocus(object sender, RoutedEventArgs e) => CommitFpsText();

    private void FpsTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitFpsText();
            FpsSlider.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            FpsDetailPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void CommitFpsText()
    {
        if (double.TryParse(FpsTextBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double fps) ||
            double.TryParse(FpsTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out fps))
        {
            FpsSlider.Value = Math.Clamp(fps, Defaults.MinFps, Defaults.MaxFps);
        }

        FpsTextBox.Text = FpsSlider.Value.ToString("0", CultureInfo.InvariantCulture);
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
        UpdateLevelMeter();
        ApplyAnalyzerLayoutSettings();
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
        ApplyAnalyzerLayoutSettings();
        if (DisplayModeCombo.SelectedIndex == 2)
        {
            SpectrumAnalyzer.UpdateStereo(
                _leftAnalyzerLevels,
                _leftAnalyzerHolds,
                _rightAnalyzerLevels,
                _rightAnalyzerHolds,
                Math.Max(0, MeterColorCombo.SelectedIndex),
                Math.Max(0, MeterStyleCombo.SelectedIndex),
                ShowUnlitCheck.IsChecked == true,
                GlowCheck.IsChecked == true,
                TextureCheck.IsChecked == true);
            return;
        }

        SpectrumAnalyzer.Update(
            _analyzerLevels,
            _analyzerHolds,
            Math.Max(0, MeterColorCombo.SelectedIndex),
            Math.Max(0, MeterStyleCombo.SelectedIndex),
            ShowUnlitCheck.IsChecked == true,
            GlowCheck.IsChecked == true,
            TextureCheck.IsChecked == true);
    }

    private void ApplyAnalyzerLayoutSettings()
    {
        bool stereo = DisplayModeCombo.SelectedIndex == 2;
        SpectrumAnalyzer.MaximumBandWidth = stereo
            ? _stereoAnalyzerMaxBandWidth
            : _monoAnalyzerMaxBandWidth;
        SpectrumAnalyzer.MaximumBandGap = stereo
            ? _stereoAnalyzerMaxBandGap
            : _monoAnalyzerMaxBandGap;
    }

    private void DisplayModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady)
            return;

        ApplyDisplayMode();
        ResizeAnalyzerBuffers();
        ResetDisplayHistory();
        SaveSettings();
    }

    private void DisplayModeContextMenuItem_Click(object sender, RoutedEventArgs e)
    {
        int index = sender == ContextDisplayAnalyzerMonoMenuItem
            ? 1
            : sender == ContextDisplayAnalyzerStereoMenuItem ? 2 : 0;

        if (DisplayModeCombo.SelectedIndex == index)
        {
            SyncDisplayModeContextMenu();
            return;
        }

        DisplayModeCombo.SelectedIndex = index;
    }

    private void ApplyDisplayMode()
    {
        bool analyzer = DisplayModeCombo.SelectedIndex > 0;
        SyncDisplayModeContextMenu();
        UpdateAnalyzerBandsButton();
        SettingsPrimaryPanel.IsEnabled = !analyzer;
        SettingsSecondaryPanel.IsEnabled = !analyzer;
        SettingsPrimaryPanel.Opacity = analyzer ? 0.42 : 1.0;
        SettingsSecondaryPanel.Opacity = analyzer ? 0.42 : 1.0;
        SpectrogramImage.Visibility = analyzer ? Visibility.Collapsed : Visibility.Visible;
        SpectrogramGridCanvas.Visibility = analyzer ? Visibility.Collapsed : Visibility.Visible;
        TimeAxisCanvas.Visibility = analyzer ? Visibility.Collapsed : Visibility.Visible;
        SpectrumAnalyzer.Visibility = analyzer ? Visibility.Visible : Visibility.Collapsed;
        WaveformImage.Visibility = Visibility.Visible;
        WaveformGridCanvas.Visibility = Visibility.Visible;
        if (_uiReady)
            ApplyWindowLayout();
    }

    private int ActiveAnalyzerBandCount =>
        DisplayModeCombo.SelectedIndex == 2 ? _stereoAnalyzerBandCount : _monoAnalyzerBandCount;

    private void AnalyzerBandsButton_Click(object sender, RoutedEventArgs e)
    {
        if (AnalyzerBandsButton.ContextMenu == null)
            return;

        SyncAnalyzerBandMenuChecks();
        AnalyzerBandsButton.ContextMenu.PlacementTarget = AnalyzerBandsButton;
        AnalyzerBandsButton.ContextMenu.Placement = PlacementMode.Bottom;
        AnalyzerBandsButton.ContextMenu.IsOpen = true;
    }

    private void AnalyzerBandPresetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string value } ||
            !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
        {
            return;
        }

        SetActiveAnalyzerBandCount(count);
    }

    private void AnalyzerBandCustomMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SetActiveAnalyzerBandCount(
            DisplayModeCombo.SelectedIndex == 2
                ? _stereoCustomAnalyzerBandCount
                : _monoCustomAnalyzerBandCount);
    }

    private void AnalyzerBandDetailsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = "Analyzer Band Settings",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(17, 20, 24)),
            ShowInTaskbar = false
        };

        var monoBox = CreateBandCountTextBox(_monoCustomAnalyzerBandCount);
        var stereoBox = CreateBandCountTextBox(_stereoCustomAnalyzerBandCount);
        var monoWidthBox = CreateAnalyzerLayoutTextBox(_monoAnalyzerMaxBandWidth);
        var stereoWidthBox = CreateAnalyzerLayoutTextBox(_stereoAnalyzerMaxBandWidth);
        var monoGapBox = CreateAnalyzerLayoutTextBox(_monoAnalyzerMaxBandGap);
        var stereoGapBox = CreateAnalyzerLayoutTextBox(_stereoAnalyzerMaxBandGap);
        var okButton = new Button { Content = "OK", MinWidth = 76, IsDefault = true };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 76, IsCancel = true };
        okButton.Click += (_, _) =>
        {
            if (!TryReadBandCount(monoBox, out int mono) || !TryReadBandCount(stereoBox, out int stereo) ||
                !TryReadLayoutValue(monoWidthBox, 4, 320, out double monoWidth) ||
                !TryReadLayoutValue(stereoWidthBox, 4, 160, out double stereoWidth) ||
                !TryReadLayoutValue(monoGapBox, 1, 48, out double monoGap) ||
                !TryReadLayoutValue(stereoGapBox, 1, 32, out double stereoGap))
            {
                MessageBox.Show(
                    dialog,
                    $"Bands: {Defaults.MinAnalyzerBandCount}-{Defaults.MaxAnalyzerBandCount}\n" +
                    "Mono width: 4-320, Stereo width: 4-160\n" +
                    "Mono gap: 1-48, Stereo gap: 1-32",
                    "Analyzer Band Settings",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            _monoCustomAnalyzerBandCount = mono;
            _stereoCustomAnalyzerBandCount = stereo;
            _monoAnalyzerMaxBandWidth = monoWidth;
            _stereoAnalyzerMaxBandWidth = stereoWidth;
            _monoAnalyzerMaxBandGap = monoGap;
            _stereoAnalyzerMaxBandGap = stereoGap;
            if (DisplayModeCombo.SelectedIndex == 1)
                _monoAnalyzerBandCount = mono;
            else if (DisplayModeCombo.SelectedIndex == 2)
                _stereoAnalyzerBandCount = stereo;
            dialog.DialogResult = true;
        };

        var grid = new Grid { Margin = new Thickness(18) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        for (int row = 0; row < 7; row++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddBandDialogRow(grid, 0, "Mono custom bands", monoBox);
        AddBandDialogRow(grid, 1, "Stereo custom bands", stereoBox);
        AddBandDialogRow(grid, 2, "Mono max bar width", monoWidthBox);
        AddBandDialogRow(grid, 3, "Stereo max bar width", stereoWidthBox);
        AddBandDialogRow(grid, 4, "Mono max band gap", monoGapBox);
        AddBandDialogRow(grid, 5, "Stereo max band gap", stereoGapBox);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        buttons.Children.Add(okButton);
        buttons.Children.Add(cancelButton);
        Grid.SetRow(buttons, 6);
        Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(buttons);
        dialog.Content = grid;

        if (dialog.ShowDialog() == true)
        {
            ResizeAnalyzerBuffers();
            ResetDisplayHistory();
            UpdateAnalyzerBandsButton();
            SaveSettings();
        }
    }

    private static TextBox CreateBandCountTextBox(int value) => new()
    {
        Text = value.ToString(CultureInfo.InvariantCulture),
        Width = 72,
        Height = 28,
        Margin = new Thickness(12, 4, 0, 4),
        Foreground = new SolidColorBrush(Color.FromRgb(230, 237, 243)),
        Background = new SolidColorBrush(Color.FromRgb(10, 15, 20)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 79)),
        TextAlignment = TextAlignment.Right,
        VerticalContentAlignment = VerticalAlignment.Center
    };

    private static TextBox CreateAnalyzerLayoutTextBox(double value)
    {
        var textBox = CreateBandCountTextBox((int)Math.Round(value));
        textBox.Text = value.ToString("0.#", CultureInfo.InvariantCulture);
        return textBox;
    }

    private static void AddBandDialogRow(Grid grid, int row, string label, TextBox textBox)
    {
        var text = new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromRgb(230, 237, 243)),
            Margin = new Thickness(0, 4, 0, 4)
        };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        Grid.SetRow(textBox, row);
        Grid.SetColumn(textBox, 1);
        grid.Children.Add(text);
        grid.Children.Add(textBox);
    }

    private static bool TryReadBandCount(TextBox textBox, out int count) =>
        int.TryParse(textBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out count) &&
        count is >= Defaults.MinAnalyzerBandCount and <= Defaults.MaxAnalyzerBandCount;

    private static bool TryReadLayoutValue(TextBox textBox, double minimum, double maximum, out double value) =>
        (double.TryParse(textBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
         double.TryParse(textBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) &&
        value >= minimum && value <= maximum;

    private void SetActiveAnalyzerBandCount(int count)
    {
        count = Math.Clamp(count, Defaults.MinAnalyzerBandCount, Defaults.MaxAnalyzerBandCount);
        if (DisplayModeCombo.SelectedIndex == 2)
            _stereoAnalyzerBandCount = count;
        else
            _monoAnalyzerBandCount = count;

        ResizeAnalyzerBuffers();
        ResetDisplayHistory();
        UpdateAnalyzerBandsButton();
        SaveSettings();
    }

    private void ResizeAnalyzerBuffers()
    {
        int monoCount = Math.Clamp(_monoAnalyzerBandCount, Defaults.MinAnalyzerBandCount, Defaults.MaxAnalyzerBandCount);
        int stereoCount = Math.Clamp(_stereoAnalyzerBandCount, Defaults.MinAnalyzerBandCount, Defaults.MaxAnalyzerBandCount);
        if (_analyzerLevels.Length != monoCount)
        {
            _analyzerLevels = CreateAnalyzerValues(monoCount);
            _analyzerHolds = CreateAnalyzerValues(monoCount);
            _analyzerHoldUntil = new DateTime[monoCount];
        }

        if (_leftAnalyzerLevels.Length != stereoCount)
        {
            _leftAnalyzerLevels = CreateAnalyzerValues(stereoCount);
            _leftAnalyzerHolds = CreateAnalyzerValues(stereoCount);
            _leftAnalyzerHoldUntil = new DateTime[stereoCount];
            _rightAnalyzerLevels = CreateAnalyzerValues(stereoCount);
            _rightAnalyzerHolds = CreateAnalyzerValues(stereoCount);
            _rightAnalyzerHoldUntil = new DateTime[stereoCount];
        }
    }

    private void UpdateAnalyzerBandsButton()
    {
        if (AnalyzerBandsButton == null)
            return;

        AnalyzerBandsButton.Content = $"Bands: {ActiveAnalyzerBandCount}";
        AnalyzerBandsButton.IsEnabled = DisplayModeCombo.SelectedIndex > 0;
        AnalyzerBandsButton.Opacity = AnalyzerBandsButton.IsEnabled ? 1.0 : 0.42;
    }

    private void SyncAnalyzerBandMenuChecks()
    {
        if (AnalyzerBandsButton.ContextMenu == null)
            return;

        int active = ActiveAnalyzerBandCount;
        int custom = DisplayModeCombo.SelectedIndex == 2
            ? _stereoCustomAnalyzerBandCount
            : _monoCustomAnalyzerBandCount;
        bool presetMatched = false;
        foreach (var item in AnalyzerBandsButton.ContextMenu.Items.OfType<MenuItem>())
        {
            if (item.Tag is string value &&
                int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
            {
                item.IsChecked = count == active;
                presetMatched |= item.IsChecked;
            }
        }

        AnalyzerBandCustomMenuItem.IsChecked = !presetMatched && active == custom;
    }

    private void SyncDisplayModeContextMenu()
    {
        if (ContextDisplaySpectrogramMenuItem == null)
            return;

        ContextDisplaySpectrogramMenuItem.IsChecked = DisplayModeCombo.SelectedIndex == 0;
        ContextDisplayAnalyzerMonoMenuItem.IsChecked = DisplayModeCombo.SelectedIndex == 1;
        ContextDisplayAnalyzerStereoMenuItem.IsChecked = DisplayModeCombo.SelectedIndex == 2;
    }

    private void LayoutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!_uiReady)
            return;

        bool modeChanged = sender == CompactMenuItem || sender == ContextCompactMenuItem;
        if (modeChanged)
            CapturePanelState(_lastLayoutCompact);

        if (sender == ContextTransportMenuItem)
            TransportMenuItem.IsChecked = ContextTransportMenuItem.IsChecked;
        else if (sender == ContextSettingsMenuItem)
            SettingsMenuItem.IsChecked = ContextSettingsMenuItem.IsChecked;
        else if (sender == ContextMainDisplayMenuItem)
            MainDisplayMenuItem.IsChecked = ContextMainDisplayMenuItem.IsChecked;
        else if (sender == ContextWaveformMenuItem)
            WaveformMenuItem.IsChecked = ContextWaveformMenuItem.IsChecked;
        else if (sender == ContextLevelMeterMenuItem)
            LevelMeterMenuItem.IsChecked = ContextLevelMeterMenuItem.IsChecked;
        else if (sender == ContextCompactMenuItem)
            CompactMenuItem.IsChecked = ContextCompactMenuItem.IsChecked;
        else if (sender == ContextAlwaysOnTopMenuItem)
            AlwaysOnTopMenuItem.IsChecked = ContextAlwaysOnTopMenuItem.IsChecked;

        if (modeChanged)
            ApplyPanelState(CompactMenuItem.IsChecked ? _compactPanelState : _normalPanelState);
        else
            CapturePanelState(CompactMenuItem.IsChecked);

        ApplyWindowLayout();
        SaveSettings();
    }

    private void StatusDisplayStyleMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!_uiReady)
            return;

        SetStatusDisplayStyle(sender == StatusSegmentMenuItem || sender == ContextStatusSegmentMenuItem ? 1 : 0);
        ApplyMeterVisualSettings();
        SaveSettings();
    }

    private void SetStatusDisplayStyle(int style)
    {
        bool segmented = style == 1;
        StatusDotMenuItem.IsChecked = !segmented;
        StatusSegmentMenuItem.IsChecked = segmented;
        ContextStatusDotMenuItem.IsChecked = !segmented;
        ContextStatusSegmentMenuItem.IsChecked = segmented;
        if (VfdStatus != null)
            VfdStatus.DisplayStyle = segmented ? 1 : 0;
    }

    private void ShowAllPanelsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!_uiReady)
            return;

        TransportMenuItem.IsChecked = true;
        SettingsMenuItem.IsChecked = true;
        MainDisplayMenuItem.IsChecked = true;
        WaveformMenuItem.IsChecked = true;
        LevelMeterMenuItem.IsChecked = true;
        CapturePanelState(CompactMenuItem.IsChecked);
        ApplyWindowLayout();
        SaveSettings();
    }

    private void ResetLayoutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!_uiReady)
            return;

        _normalPanelState = PanelVisibilityState.NormalDefault;
        _compactPanelState = PanelVisibilityState.CompactDefault;
        CompactMenuItem.IsChecked = false;
        ApplyPanelState(_normalPanelState);
        AlwaysOnTopMenuItem.IsChecked = false;
        ApplyWindowLayout();
        SaveSettings();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e) => Close();

    private void ApplyWindowLayout()
    {
        if (MainContentGrid == null)
            return;

        bool compact = CompactMenuItem.IsChecked;
        bool compactChanged = _layoutInitialized && compact != _lastLayoutCompact;
        double oldTopHeight = TopPanel.Visibility == Visibility.Visible ? TopPanel.ActualHeight : 0;
        double oldMainHeight = MainDisplayPanel.Visibility == Visibility.Visible
            ? MainContentGrid.RowDefinitions[0].ActualHeight
            : 0;
        double oldTimeAxisHeight = MainContentGrid.RowDefinitions[1].ActualHeight;
        double oldWaveformHeight = MainContentGrid.RowDefinitions[2].ActualHeight;
        double oldLevelMeterHeight = MainContentGrid.RowDefinitions[3].ActualHeight;
        if (oldMainHeight > 0)
        {
            if (!_lastLayoutCompact)
                _normalMainDisplayHeight = oldMainHeight;
        }

        bool showTransport = TransportMenuItem.IsChecked;
        bool showSettings = SettingsMenuItem.IsChecked && !compact;
        bool showMainDisplay = MainDisplayMenuItem.IsChecked;
        bool showWaveform = WaveformMenuItem.IsChecked;
        bool showLevelMeter = LevelMeterMenuItem.IsChecked;

        TransportPrimaryPanel.Visibility = showTransport ? Visibility.Visible : Visibility.Collapsed;
        TransportSecondaryPanel.Visibility = showTransport ? Visibility.Visible : Visibility.Collapsed;
        SettingsPrimaryPanel.Visibility = showSettings ? Visibility.Visible : Visibility.Collapsed;
        SettingsSecondaryPanel.Visibility = showSettings ? Visibility.Visible : Visibility.Collapsed;
        SettingsDisplayPanel.Visibility = showSettings ? Visibility.Visible : Visibility.Collapsed;
        TopPanel.Visibility = showTransport || showSettings ? Visibility.Visible : Visibility.Collapsed;

        MainDisplayPanel.Visibility = showMainDisplay ? Visibility.Visible : Visibility.Collapsed;
        bool showTimeAxis = showMainDisplay && DisplayModeCombo.SelectedIndex == 0;
        TimeAxisPanel.Visibility = showTimeAxis ? Visibility.Visible : Visibility.Collapsed;
        WaveformPanel.Visibility = showWaveform ? Visibility.Visible : Visibility.Collapsed;
        LevelMeterPanel.Visibility = showLevelMeter ? Visibility.Visible : Visibility.Collapsed;

        TopPanel.Padding = compact ? new Thickness(7, 5, 7, 5) : new Thickness(14, 12, 14, 12);
        TransportSecondaryPanel.Margin = compact ? new Thickness(0, 4, 0, 0) : new Thickness(0, 12, 0, 0);
        SettingsPrimaryPanel.Margin = compact ? new Thickness(0, 4, 0, 0) : new Thickness(0, 10, 0, 0);
        SettingsSecondaryPanel.Margin = compact ? new Thickness(0, 4, 0, 0) : new Thickness(0, 10, 0, 0);
        SettingsDisplayPanel.Margin = compact ? new Thickness(0, 4, 0, 0) : new Thickness(0, 10, 0, 0);
        MainContentGrid.Margin = compact ? new Thickness(5) : new Thickness(14);
        WaveformPanel.Margin = showWaveform ? new Thickness(0, compact ? 4 : 12, 0, 0) : new Thickness(0);
        LevelMeterPanel.Margin = showLevelMeter ? new Thickness(0, compact ? 4 : 12, 0, 0) : new Thickness(0);
        LevelMeterPanel.Padding = compact ? new Thickness(5) : new Thickness(8);
        VfdStatusColumn.Width = new GridLength(compact ? 190 : 240);
        VfdStatus.Margin = compact
            ? new Thickness(7, 0, 3, 0)
            : new Thickness(14, 0, 8, 0);
        ApplyCompactControlSizing(compact);

        SettingsMenuItem.IsEnabled = !compact;
        ContextSettingsMenuItem.IsEnabled = !compact;
        MinWidth = compact || !showSettings ? 480 : 760;
        MinHeight = compact ? 120 : showMainDisplay ? 320 : 150;

        TopPanel.Measure(new Size(Math.Max(1, ActualWidth), double.PositiveInfinity));
        double newTopHeight = TopPanel.Visibility == Visibility.Visible ? TopPanel.DesiredSize.Height : 0;
        double newMainHeight = showMainDisplay
            ? compact
                ? _compactMainDisplayHeight
                : oldMainHeight > 0 ? oldMainHeight : _normalMainDisplayHeight
            : 0;
        double newTimeAxisHeight = showTimeAxis ? compact ? 18 : 24 : 0;
        double newWaveformHeight = showWaveform ? compact ? 66 : 112 : 0;
        double newLevelMeterHeight = showLevelMeter ? compact ? 78 : 90 : 0;
        double compactWindowHeight =
            newTopHeight +
            newMainHeight +
            newTimeAxisHeight +
            newWaveformHeight +
            newLevelMeterHeight +
            MainContentGrid.Margin.Top +
            MainContentGrid.Margin.Bottom;
        _preferredLayoutHeight = compact
            ? Math.Max(120, compactWindowHeight)
            : Math.Max(320, 720 - oldTopHeight + newTopHeight);
        ApplyResponsivePanelSizing();

        if (_layoutInitialized && !compactChanged && WindowState == WindowState.Normal)
        {
            double heightDelta =
                newTopHeight - oldTopHeight +
                newMainHeight - oldMainHeight +
                newTimeAxisHeight - oldTimeAxisHeight +
                newWaveformHeight - oldWaveformHeight +
                newLevelMeterHeight - oldLevelMeterHeight;
            Height = Math.Max(MinHeight, Height + heightDelta);
        }

        SyncLayoutMenus();
        Topmost = AlwaysOnTopMenuItem.IsChecked;
        ApplyCompactChrome(compact);
        ApplyCompactWindowSize(compact, compactWindowHeight);
        _lastLayoutCompact = compact;
        _layoutInitialized = true;
        UpdateGridOverlay();
    }

    private void ApplyCompactControlSizing(bool compact)
    {
        DeviceCombo.MinWidth = compact ? 180 : 270;
        RecordGainSlider.Width = compact ? 92 : 130;
        PlaybackSlider.Width = compact ? 190 : 360;
        GainSlider.Width = compact ? 82 : 118;
        RangeSlider.Width = compact ? 88 : 130;
        FpsSlider.Width = 180;
        TimeDivisionSlider.Width = compact ? 96 : 150;

        DisplayModeCombo.Width = compact ? 185 : 210;
        DisplayModeCombo.MinWidth = compact ? 185 : 210;
        FpsButton.MinWidth = compact ? 72 : 78;
        MeterColorCombo.Width = compact ? 72 : 96;
        MeterColorCombo.MinWidth = compact ? 72 : 96;
        MeterStyleCombo.Width = compact ? 118 : 150;
        MeterStyleCombo.MinWidth = compact ? 118 : 150;

        foreach (var combo in new[] { DisplayModeCombo, MeterColorCombo, MeterStyleCombo })
            combo.Margin = compact ? new Thickness(4, 0, 7, 0) : new Thickness(8, 0, 14, 0);

        foreach (var checkBox in new[] { VuNormalizeCheck, ShowUnlitCheck, GlowCheck, TextureCheck })
            checkBox.Margin = compact ? new Thickness(0, 0, 6, 0) : new Thickness(0, 0, 12, 0);

        foreach (var button in new[] { StartButton, StopButton, PlayButton, SaveButton })
            button.MinWidth = compact ? 68 : 90;

        DefaultsButton.MinWidth = compact ? 72 : 90;
        DefaultsButton.Content = compact ? "Defaults" : "Default Settings";
        DefaultsButton.Margin = compact ? new Thickness(6, 0, 0, 0) : new Thickness(12, 0, 0, 0);
        SettingsVisualOptionsPanel.Margin = compact ? new Thickness(0) : new Thickness(0);
    }

    private void SyncLayoutMenus()
    {
        ContextTransportMenuItem.IsChecked = TransportMenuItem.IsChecked;
        ContextSettingsMenuItem.IsChecked = SettingsMenuItem.IsChecked;
        ContextMainDisplayMenuItem.IsChecked = MainDisplayMenuItem.IsChecked;
        ContextWaveformMenuItem.IsChecked = WaveformMenuItem.IsChecked;
        ContextLevelMeterMenuItem.IsChecked = LevelMeterMenuItem.IsChecked;
        ContextCompactMenuItem.IsChecked = CompactMenuItem.IsChecked;
        ContextAlwaysOnTopMenuItem.IsChecked = AlwaysOnTopMenuItem.IsChecked;
    }

    private void CapturePanelState(bool compact)
    {
        var state = new PanelVisibilityState(
            TransportMenuItem.IsChecked,
            SettingsMenuItem.IsChecked,
            MainDisplayMenuItem.IsChecked,
            WaveformMenuItem.IsChecked,
            LevelMeterMenuItem.IsChecked);
        if (compact)
            _compactPanelState = state;
        else
            _normalPanelState = state;
    }

    private void ApplyPanelState(PanelVisibilityState state)
    {
        TransportMenuItem.IsChecked = state.Transport;
        SettingsMenuItem.IsChecked = state.Settings;
        MainDisplayMenuItem.IsChecked = state.MainDisplay;
        WaveformMenuItem.IsChecked = state.Waveform;
        LevelMeterMenuItem.IsChecked = state.LevelMeter;
    }

    private void ApplyCompactChrome(bool compact)
    {
        MainMenu.Visibility = Visibility.Collapsed;
        if (compact)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0),
                ResizeBorderThickness = new Thickness(6),
                UseAeroCaptionButtons = false
            });
        }
        else
        {
            WindowChrome.SetWindowChrome(this, null);
            ResizeMode = ResizeMode.CanResize;
            WindowStyle = WindowStyle.SingleBorderWindow;
        }
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_uiReady)
            ApplyResponsivePanelSizing();
    }

    private void ApplyResponsivePanelSizing()
    {
        if (MainContentGrid == null || MainContentGrid.RowDefinitions.Count < 4)
            return;

        bool compact = CompactMenuItem.IsChecked;
        double currentHeight = ActualHeight > 0 ? ActualHeight : Height;
        double scale = Math.Clamp(currentHeight / Math.Max(1, _preferredLayoutHeight), 0.2, 1.35);
        bool showMain = MainDisplayPanel.Visibility == Visibility.Visible;
        bool showTimeAxis = TimeAxisPanel.Visibility == Visibility.Visible;
        bool showWaveform = WaveformPanel.Visibility == Visibility.Visible;
        bool showLevelMeter = LevelMeterPanel.Visibility == Visibility.Visible;

        MainContentGrid.RowDefinitions[0].Height = showMain
            ? compact
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
        double fixedPanelScale = Math.Min(1, scale);
        MainContentGrid.RowDefinitions[1].Height = showTimeAxis
            ? new GridLength((compact ? 18 : 24) * fixedPanelScale)
            : new GridLength(0);
        MainContentGrid.RowDefinitions[2].Height = showWaveform
            ? new GridLength((compact ? 66 : 112) * fixedPanelScale)
            : new GridLength(0);
        MainContentGrid.RowDefinitions[3].Height = showLevelMeter
            ? new GridLength((compact ? 78 : 90) * fixedPanelScale)
            : new GridLength(0);

        double spacingScale = fixedPanelScale;
        MainContentGrid.Margin = compact
            ? new Thickness(5 * spacingScale)
            : new Thickness(14 * spacingScale);
        WaveformPanel.Margin = showWaveform
            ? new Thickness(0, (compact ? 4 : 12) * spacingScale, 0, 0)
            : new Thickness(0);
        LevelMeterPanel.Margin = showLevelMeter
            ? new Thickness(0, (compact ? 4 : 12) * spacingScale, 0, 0)
            : new Thickness(0);
        LevelMeterPanel.Padding = new Thickness((compact ? 5 : 8) * spacingScale);
        VfdStatus.Margin = compact
            ? new Thickness(7 * spacingScale, 0, 3 * spacingScale, 0)
            : new Thickness(14 * spacingScale, 0, 8 * spacingScale, 0);

        double availableWidth = Math.Max(1, MainContentGrid.ActualWidth);
        double preferredStatusWidth = compact ? 190 : 240;
        double minimumStatusWidth = compact ? 140 : 170;
        VfdStatusColumn.Width = new GridLength(
            Math.Clamp(preferredStatusWidth * Math.Clamp(availableWidth / (compact ? 750 : 1120), 0.72, 1.15),
                minimumStatusWidth,
                preferredStatusWidth * 1.15));
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (CompactMenuItem.IsChecked != true || e.LeftButton != MouseButtonState.Pressed)
            return;

        DependencyObject? source = e.OriginalSource as DependencyObject;
        while (source != null)
        {
            if (source is VfdStatusDisplay or ButtonBase or Selector or Slider or MenuBase or TextBoxBase or ScrollBar or Thumb)
                return;
            source = VisualTreeHelper.GetParent(source);
        }

        try
        {
            DragMove();
            e.Handled = true;
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void ApplyCompactWindowSize(bool compact, double compactWindowHeight)
    {
        if (compact)
        {
            if (!_compactSizeApplied)
            {
                _normalWindowState = WindowState;
                _normalWindowWidth = WindowState == WindowState.Normal ? Width : RestoreBounds.Width;
                _normalWindowHeight = WindowState == WindowState.Normal ? Height : RestoreBounds.Height;
            }

            WindowState = WindowState.Normal;
            MinWidth = 480;
            MinHeight = 120;
            Width = Math.Max(MinWidth, Math.Min(_normalWindowWidth, 760));
            Height = Math.Max(MinHeight, compactWindowHeight);
            _compactSizeApplied = true;
            return;
        }

        if (!_compactSizeApplied)
            return;

        Width = Math.Max(MinWidth, _normalWindowWidth);
        Height = Math.Max(MinHeight, _normalWindowHeight);
        WindowState = _normalWindowState == WindowState.Minimized ? WindowState.Normal : _normalWindowState;
        _compactSizeApplied = false;
    }

    protected override void OnClosed(EventArgs e)
    {
        StopPlayback(updateStatus: false, restartLive: false);
        StopCapture(triggerVfdDecay: false);
        CancelVfdDecay();
        base.OnClosed(e);
    }
}
