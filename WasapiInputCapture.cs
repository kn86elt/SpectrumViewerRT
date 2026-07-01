using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace SpectrumViewerRT;

public sealed class WasapiInputCapture : IAudioCaptureSource
{
    private sealed record PacketSamples(short[] Mono, short[] Left, short[] Right);

    private const int OutputSampleRate = AudioCapture.DefaultSampleRate;

    public static AudioDevice DefaultInputDevice { get; } =
        new(-1, "Default Windows input");

    private readonly string? _deviceId;
    private WasapiCapture? _capture;
    private double _resamplePosition;
    private double _lastMono;
    private double _lastLeft;
    private double _lastRight;
    private LevelMeterReading _lastLevel;
    private int _stopNotificationSent;

    public WasapiInputCapture(string? deviceId)
    {
        _deviceId = deviceId;
    }

    public event Action<short[]>? SamplesAvailable;
    public event Action<short[], short[]>? StereoSamplesAvailable;
    public event Action<double>? LevelAvailable;
    public event Action<LevelMeterReading>? StereoLevelAvailable;
    public event Action<string>? StatusAvailable;
    public event Action<string>? CaptureStopped;
    public int SampleRate => OutputSampleRate;
    public bool IsStereo { get; private set; } = true;

    public static IReadOnlyList<AudioDevice> GetInputDevices()
    {
        Exception? lastError = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return GetInputDevicesCore();
            }
            catch (Exception ex)
            {
                lastError = ex;
                Thread.Sleep(150);
            }
        }

        throw new InvalidOperationException(
            lastError == null
                ? "WASAPI input device enumeration failed."
                : $"WASAPI input device enumeration failed: {lastError.Message}",
            lastError);
    }

    private static IReadOnlyList<AudioDevice> GetInputDevicesCore()
    {
        var devices = new List<AudioDevice> { DefaultInputDevice };
        using var enumerator = new MMDeviceEnumerator();
        int index = 0;
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            devices.Add(new AudioDevice(index++, device.FriendlyName) { WasapiId = device.ID });
        }

        return devices;
    }

    public void Start()
    {
        if (_capture != null)
            return;

        Interlocked.Exchange(ref _stopNotificationSent, 0);
        var capture = CreateCapture();
        _capture = capture;
        var format = capture.WaveFormat;
        IsStereo = format.Channels > 1;
        StatusAvailable?.Invoke($"WASAPI input: {format.SampleRate} Hz, {format.Channels} ch, {format.BitsPerSample} bit");
        capture.DataAvailable += Capture_DataAvailable;
        capture.RecordingStopped += Capture_RecordingStopped;
        capture.StartRecording();
        StatusAvailable?.Invoke("WASAPI input started");
    }

    public void Stop()
    {
        var capture = Interlocked.Exchange(ref _capture, null);
        if (capture == null)
            return;

        try
        {
            capture.DataAvailable -= Capture_DataAvailable;
            capture.RecordingStopped -= Capture_RecordingStopped;
            capture.StopRecording();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            capture.Dispose();
        }
    }

    public void Dispose() => Stop();

    private WasapiCapture CreateCapture()
    {
        if (string.IsNullOrWhiteSpace(_deviceId))
            return new WasapiCapture();

        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDevice(_deviceId);
        return new WasapiCapture(device);
    }

    private void Capture_DataAvailable(object? sender, WaveInEventArgs e)
    {
        var capture = _capture;
        if (capture == null || e.BytesRecorded <= 0)
            return;

        try
        {
            var format = capture.WaveFormat;
            int bytesPerSample = Math.Max(1, format.BitsPerSample / 8);
            int blockAlign = Math.Max(bytesPerSample, format.BlockAlign);
            int frames = e.BytesRecorded / blockAlign;
            bool float32 = format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32;
            var samples = ConvertPacket(
                e.Buffer,
                frames,
                format.Channels,
                blockAlign,
                format.BitsPerSample,
                float32,
                format.SampleRate);
            if (samples.Mono.Length == 0)
                return;

            SamplesAvailable?.Invoke(samples.Mono);
            StereoSamplesAvailable?.Invoke(samples.Left, samples.Right);
            LevelAvailable?.Invoke(_lastLevel.Peak);
            StereoLevelAvailable?.Invoke(_lastLevel);
        }
        catch (Exception ex)
        {
            StatusAvailable?.Invoke($"WASAPI input error: {ex.Message}");
            NotifyCaptureStopped($"WASAPI input stopped: {ex.Message}");
        }
    }

    private void Capture_RecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            StatusAvailable?.Invoke($"WASAPI input stopped: {e.Exception.Message}");
            NotifyCaptureStopped($"WASAPI input stopped: {e.Exception.Message}");
        }
    }

    private PacketSamples ConvertPacket(
        byte[] raw,
        int frames,
        int channels,
        int blockAlign,
        int bits,
        bool float32,
        int sourceRate)
    {
        if (frames <= 0 || channels <= 0)
            return new PacketSamples(Array.Empty<short>(), Array.Empty<short>(), Array.Empty<short>());

        var mono = new double[frames];
        var left = new double[frames];
        var right = new double[frames];
        double leftPeak = 0;
        double rightPeak = 0;
        int bytesPerSample = Math.Max(1, bits / 8);
        for (int frame = 0; frame < frames; frame++)
        {
            double sum = 0;
            int frameOffset = frame * blockAlign;
            double leftValue = 0;
            double rightValue = 0;
            for (int channel = 0; channel < channels; channel++)
            {
                int offset = frameOffset + channel * bytesPerSample;
                if (offset + bytesPerSample > raw.Length)
                    break;

                double value = ReadSample(raw, offset, bits, float32);
                sum += value;
                if (channel == 0)
                {
                    leftValue = value;
                    leftPeak = Math.Max(leftPeak, Math.Abs(value));
                }
                else if (channel == 1)
                {
                    rightValue = value;
                    rightPeak = Math.Max(rightPeak, Math.Abs(value));
                }
            }

            if (channels == 1)
                rightValue = leftValue;
            left[frame] = leftValue;
            right[frame] = rightValue;
            mono[frame] = sum / channels;
        }

        if (channels == 1)
            rightPeak = leftPeak;
        _lastLevel = new LevelMeterReading(leftPeak, rightPeak);
        return ResampleToOutput(mono, left, right, sourceRate);
    }

    private PacketSamples ResampleToOutput(double[] mono, double[] left, double[] right, int sourceRate)
    {
        if (mono.Length == 0)
            return new PacketSamples(Array.Empty<short>(), Array.Empty<short>(), Array.Empty<short>());

        double step = sourceRate / (double)OutputSampleRate;
        var monoOutput = new List<short>((int)(mono.Length / step) + 2);
        var leftOutput = new List<short>((int)(left.Length / step) + 2);
        var rightOutput = new List<short>((int)(right.Length / step) + 2);

        while (_resamplePosition < mono.Length)
        {
            int index = (int)_resamplePosition;
            double fraction = _resamplePosition - index;
            monoOutput.Add(ToInt16(Interpolate(mono, _lastMono, index, fraction)));
            leftOutput.Add(ToInt16(Interpolate(left, _lastLeft, index, fraction)));
            rightOutput.Add(ToInt16(Interpolate(right, _lastRight, index, fraction)));
            _resamplePosition += step;
        }

        _resamplePosition -= mono.Length;
        _lastMono = mono[^1];
        _lastLeft = left[^1];
        _lastRight = right[^1];
        return new PacketSamples(monoOutput.ToArray(), leftOutput.ToArray(), rightOutput.ToArray());
    }

    private static double Interpolate(double[] samples, double previous, int index, double fraction)
    {
        double a = index == 0 ? previous : samples[index - 1];
        double b = samples[Math.Min(index, samples.Length - 1)];
        return a + (b - a) * fraction;
    }

    private static double ReadSample(byte[] raw, int offset, int bits, bool float32)
    {
        if (float32 && bits == 32)
            return Math.Clamp(BitConverter.ToSingle(raw, offset), -1.0, 1.0);

        return bits switch
        {
            16 => BitConverter.ToInt16(raw, offset) / 32768.0,
            24 => ReadInt24(raw, offset) / 8388608.0,
            32 => BitConverter.ToInt32(raw, offset) / 2147483648.0,
            _ => 0
        };
    }

    private static int ReadInt24(byte[] raw, int offset)
    {
        int value = raw[offset] | (raw[offset + 1] << 8) | (raw[offset + 2] << 16);
        if ((value & 0x800000) != 0)
            value |= unchecked((int)0xFF000000);
        return value;
    }

    private static short ToInt16(double sample)
    {
        sample = Math.Clamp(sample, -1.0, 1.0);
        return (short)Math.Round(sample * short.MaxValue);
    }

    private void NotifyCaptureStopped(string message)
    {
        if (Interlocked.Exchange(ref _stopNotificationSent, 1) == 0)
            CaptureStopped?.Invoke(message);
    }
}
