using System.Runtime.InteropServices;

namespace SpectrumViewerRT;

public sealed class WasapiLoopbackCapture : IAudioCaptureSource
{
    private sealed record PacketSamples(short[] Mono, short[] Left, short[] Right);

    private const int OutputSampleRate = AudioCapture.DefaultSampleRate;
    private volatile bool _running;
    private Thread? _thread;
    private double _resamplePosition;
    private double _lastMono;
    private double _lastLeft;
    private double _lastRight;
    private LevelMeterReading _lastLevel;

    public event Action<short[]>? SamplesAvailable;
    public event Action<short[], short[]>? StereoSamplesAvailable;
    public event Action<double>? LevelAvailable;
    public event Action<LevelMeterReading>? StereoLevelAvailable;
    public event Action<string>? StatusAvailable;
    public int SampleRate => OutputSampleRate;

    public void Start()
    {
        if (_running)
            return;

        _running = true;
        _thread = new Thread(CaptureLoop)
        {
            IsBackground = true,
            Name = "WASAPI loopback capture"
        };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        if (_thread is { IsAlive: true })
            _thread.Join(TimeSpan.FromSeconds(1));
        _thread = null;
    }

    private void CaptureLoop()
    {
        object? audioClientObject = null;
        object? captureClientObject = null;
        object? enumeratorObject = null;
        object? deviceObject = null;
        IntPtr formatPtr = IntPtr.Zero;
        bool comInitialized = false;

        try
        {
            int coInit = WasapiInterop.CoInitializeEx(IntPtr.Zero, WasapiInterop.CoinitMultithreaded);
            comInitialized = coInit == 0;
            if (coInit < 0 && coInit != WasapiInterop.RpcESChangedMode)
                Marshal.ThrowExceptionForHR(coInit);

            enumeratorObject = new WasapiInterop.MMDeviceEnumeratorComObject();
            var enumerator = (WasapiInterop.IMMDeviceEnumerator)enumeratorObject;
            ThrowIfFailed(enumerator.GetDefaultAudioEndpoint(WasapiInterop.EDataFlow.Render, WasapiInterop.ERole.Multimedia, out var device), "GetDefaultAudioEndpoint");
            deviceObject = device;

            var audioClientId = WasapiInterop.IAudioClientId;
            ThrowIfFailed(device.Activate(ref audioClientId, WasapiInterop.ClsctxAll, IntPtr.Zero, out audioClientObject), "IMMDevice.Activate");
            var audioClient = (WasapiInterop.IAudioClient)audioClientObject;

            ThrowIfFailed(audioClient.GetMixFormat(out formatPtr), "IAudioClient.GetMixFormat");
            var format = Marshal.PtrToStructure<WasapiInterop.WaveFormatEx>(formatPtr);
            int sourceRate = (int)format.SamplesPerSec;
            int channels = Math.Max(1, (int)format.Channels);
            int bits = format.BitsPerSample;
            int blockAlign = format.BlockAlign;
            bool float32 = format.FormatTag == 3 || (format.FormatTag == 0xFFFE && bits == 32);
            StatusAvailable?.Invoke($"WASAPI loopback: {sourceRate} Hz, {channels} ch, {bits} bit");

            var session = Guid.Empty;
            long bufferDuration = 1_000_000;
            int flags = WasapiInterop.AudioClientStreamFlagsLoopback;
            ThrowIfFailed(audioClient.Initialize(WasapiInterop.AudioClientShareModeShared, flags, bufferDuration, 0, formatPtr, ref session), "IAudioClient.Initialize");

            var captureClientId = WasapiInterop.IAudioCaptureClientId;
            ThrowIfFailed(audioClient.GetService(ref captureClientId, out captureClientObject), "IAudioClient.GetService");
            var captureClient = (WasapiInterop.IAudioCaptureClient)captureClientObject;

            ThrowIfFailed(audioClient.Start(), "IAudioClient.Start");
            StatusAvailable?.Invoke("WASAPI loopback started");
            try
            {
                while (_running)
                {
                    ThrowIfFailed(captureClient.GetNextPacketSize(out var packetFrames), "IAudioCaptureClient.GetNextPacketSize");
                    if (packetFrames == 0)
                    {
                        Thread.Sleep(8);
                        continue;
                    }

                    while (packetFrames > 0 && _running)
                    {
                        ThrowIfFailed(captureClient.GetBuffer(out var data, out var frames, out var bufferFlags, out _, out _), "IAudioCaptureClient.GetBuffer");
                        try
                        {
                            var samples = ConvertPacket(data, (int)frames, channels, blockAlign, bits, float32, sourceRate, bufferFlags.HasFlag(WasapiInterop.AudioClientBufferFlags.Silent));
                            if (samples.Mono.Length > 0)
                            {
                                SamplesAvailable?.Invoke(samples.Mono);
                                StereoSamplesAvailable?.Invoke(samples.Left, samples.Right);
                                LevelAvailable?.Invoke(_lastLevel.Peak);
                                StereoLevelAvailable?.Invoke(_lastLevel);
                            }
                        }
                        finally
                        {
                            ThrowIfFailed(captureClient.ReleaseBuffer(frames), "IAudioCaptureClient.ReleaseBuffer");
                        }

                        ThrowIfFailed(captureClient.GetNextPacketSize(out packetFrames), "IAudioCaptureClient.GetNextPacketSize");
                    }
                }
            }
            finally
            {
                audioClient.Stop();
            }
        }
        catch (Exception ex)
        {
            SamplesAvailable?.Invoke(Array.Empty<short>());
            LevelAvailable?.Invoke(0);
            StereoLevelAvailable?.Invoke(default);
            StatusAvailable?.Invoke($"WASAPI error: {ex.Message}");
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            if (formatPtr != IntPtr.Zero)
                WasapiInterop.CoTaskMemFree(formatPtr);
            if (captureClientObject != null)
                Marshal.ReleaseComObject(captureClientObject);
            if (audioClientObject != null)
                Marshal.ReleaseComObject(audioClientObject);
            if (deviceObject != null)
                Marshal.ReleaseComObject(deviceObject);
            if (enumeratorObject != null)
                Marshal.ReleaseComObject(enumeratorObject);
            if (comInitialized)
                WasapiInterop.CoUninitialize();
        }
    }

    private PacketSamples ConvertPacket(IntPtr data, int frames, int channels, int blockAlign, int bits, bool float32, int sourceRate, bool silent)
    {
        if (frames <= 0)
            return new PacketSamples(Array.Empty<short>(), Array.Empty<short>(), Array.Empty<short>());

        var mono = new double[frames];
        var left = new double[frames];
        var right = new double[frames];
        double leftPeak = 0;
        double rightPeak = 0;
        if (!silent)
        {
            int bytes = frames * blockAlign;
            var raw = new byte[bytes];
            Marshal.Copy(data, raw, 0, bytes);

            for (int frame = 0; frame < frames; frame++)
            {
                double sum = 0;
                int frameOffset = frame * blockAlign;
                double leftValue = 0;
                double rightValue = 0;
                for (int channel = 0; channel < channels; channel++)
                {
                    int offset = frameOffset + channel * (bits / 8);
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

    private static void ThrowIfFailed(int result, string operation)
    {
        if (result < 0)
            Marshal.ThrowExceptionForHR(result);
    }

    public void Dispose() => Stop();
}
