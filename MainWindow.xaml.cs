using Microsoft.Win32;
using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SpectrumViewerRT;

public partial class MainWindow : Window
{
    private const int FftSize = 2048;
    private const int ImageWidth = 1100;
    private const int ImageHeight = 520;

    private readonly ConcurrentQueue<short[]> _pendingSamples = new();
    private readonly List<short> _sampleWindow = new(FftSize * 2);
    private readonly List<short> _recorded = new();
    private readonly DispatcherTimer _renderTimer = new();
    private readonly WriteableBitmap _spectrogram;
    private readonly byte[] _pixels = new byte[ImageWidth * ImageHeight * 4];
    private AudioCapture? _capture;
    private AudioPlayback? _monitor;
    private DateTime _recordingStarted;
    private double _peak;

    public MainWindow()
    {
        InitializeComponent();

        _spectrogram = new WriteableBitmap(ImageWidth, ImageHeight, 96, 96, PixelFormats.Bgra32, null);
        SpectrogramImage.Source = _spectrogram;
        ClearSpectrogram();

        _renderTimer.Tick += RenderTimer_Tick;
        RefreshDevices();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshDevices();

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceCombo.SelectedItem is not AudioDevice device)
        {
            SetStatus("入力デバイスが見つかりません");
            return;
        }

        try
        {
            StopCapture();
            _recorded.Clear();
            _sampleWindow.Clear();
            while (_pendingSamples.TryDequeue(out _)) { }
            ClearSpectrogram();

            _monitor = new AudioPlayback();
            _capture = new AudioCapture();
            _capture.SamplesAvailable += Capture_SamplesAvailable;
            _capture.LevelAvailable += level => _peak = Math.Max(_peak * 0.92, level);
            _capture.Start(device.Id);

            _recordingStarted = DateTime.Now;
            _renderTimer.Interval = TimeSpan.FromSeconds(1.0 / Math.Max(1.0, FpsSlider.Value));
            _renderTimer.Start();

            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            PlayButton.IsEnabled = false;
            SaveButton.IsEnabled = false;
            SetStatus($"録音中: {device.Name}");
        }
        catch (Exception ex)
        {
            StopCapture();
            SetStatus(ex.Message);
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        StopCapture();
        SetStoppedState();
        SetStatus(_recorded.Count > 0 ? "停止しました" : "準備完了");
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_recorded.Count == 0)
            return;

        Task.Run(() =>
        {
            using var player = new AudioPlayback();
            const int chunk = AudioCapture.SampleRate / 20;
            for (int offset = 0; offset < _recorded.Count; offset += chunk)
            {
                int length = Math.Min(chunk, _recorded.Count - offset);
                var samples = new short[length];
                _recorded.CopyTo(offset, samples, 0, length);
                player.Play(samples);
                Thread.Sleep(TimeSpan.FromSeconds(length / (double)AudioCapture.SampleRate));
            }
        });
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
            WaveFile.Save16BitMono(dialog.FileName, _recorded, AudioCapture.SampleRate);
            SetStatus($"保存しました: {dialog.FileName}");
        }
    }

    private void Capture_SamplesAvailable(short[] samples)
    {
        _pendingSamples.Enqueue(samples);
        if (MonitorCheck.IsChecked == true)
            _monitor?.Play(samples);
    }

    private void RenderTimer_Tick(object? sender, EventArgs e)
    {
        int appended = 0;
        while (_pendingSamples.TryDequeue(out var samples))
        {
            appended += samples.Length;
            _recorded.AddRange(samples);
            _sampleWindow.AddRange(samples);
        }

        if (_sampleWindow.Count > FftSize * 2)
            _sampleWindow.RemoveRange(0, _sampleWindow.Count - FftSize * 2);

        if (appended > 0 && _sampleWindow.Count >= FftSize)
            DrawSpectrumColumn();

        LevelBar.Value = Math.Clamp(_peak, 0, 1);
        PeakText.Text = _peak > 0.00001 ? $"Peak: {20 * Math.Log10(_peak):0.0} dB" : "Peak: -inf dB";
        DurationText.Text = $"録音: {(DateTime.Now - _recordingStarted):mm\\:ss}";
        _renderTimer.Interval = TimeSpan.FromSeconds(1.0 / Math.Max(1.0, FpsSlider.Value));
    }

    private void DrawSpectrumColumn()
    {
        Buffer.BlockCopy(_pixels, 4, _pixels, 0, _pixels.Length - 4);

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
            double normalized = 1.0 - y / (double)(ImageHeight - 1);
            double frequency = 20.0 * Math.Pow((AudioCapture.SampleRate / 2.0) / 20.0, normalized);
            int bin = Math.Clamp((int)(frequency / AudioCapture.SampleRate * FftSize), 1, FftSize / 2 - 1);
            double magnitude = Math.Sqrt(real[bin] * real[bin] + imaginary[bin] * imaginary[bin]);
            double db = 20.0 * Math.Log10(magnitude + 0.0000001);
            double intensity = Math.Clamp((db + RangeSlider.Value) / RangeSlider.Value, 0, 1);
            var color = ColorMap(intensity);
            int index = (y * ImageWidth + ImageWidth - 1) * 4;
            _pixels[index + 0] = color.B;
            _pixels[index + 1] = color.G;
            _pixels[index + 2] = color.R;
            _pixels[index + 3] = 255;
        }

        _spectrogram.WritePixels(new Int32Rect(0, 0, ImageWidth, ImageHeight), _pixels, ImageWidth * 4, 0);
    }

    private static Color ColorMap(double value)
    {
        value = Math.Clamp(value, 0, 1);
        byte r = (byte)(255 * Math.Clamp((value - 0.45) / 0.55, 0, 1));
        byte g = (byte)(255 * Math.Sin(value * Math.PI));
        byte b = (byte)(255 * Math.Clamp(1.0 - value * 1.25, 0, 1));
        return Color.FromRgb(r, g, b);
    }

    private void RefreshDevices()
    {
        var devices = AudioCapture.GetInputDevices();
        DeviceCombo.ItemsSource = devices;
        if (devices.Count > 0)
            DeviceCombo.SelectedIndex = 0;
        SetStatus(devices.Count > 0 ? $"{devices.Count} 個の入力デバイス" : "入力デバイスがありません");
    }

    private void ClearSpectrogram()
    {
        Array.Clear(_pixels);
        _spectrogram.WritePixels(new Int32Rect(0, 0, ImageWidth, ImageHeight), _pixels, ImageWidth * 4, 0);
    }

    private void StopCapture()
    {
        _renderTimer.Stop();
        _capture?.Dispose();
        _capture = null;
        _monitor?.Dispose();
        _monitor = null;
    }

    private void SetStoppedState()
    {
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        PlayButton.IsEnabled = _recorded.Count > 0;
        SaveButton.IsEnabled = _recorded.Count > 0;
    }

    private void SetStatus(string message) => StatusText.Text = message;

    protected override void OnClosed(EventArgs e)
    {
        StopCapture();
        base.OnClosed(e);
    }
}
