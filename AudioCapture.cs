using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace SpectrumViewerRT;

public sealed class AudioCapture : IAudioCaptureSource
{
    private sealed class BufferState
    {
        public IntPtr HeaderPtr { get; init; }
        public IntPtr DataPtr { get; init; }
        public byte[] Data { get; init; } = Array.Empty<byte>();
    }

    public const int DefaultSampleRate = 48000;
    public const int SampleRate = DefaultSampleRate;
    public event Action<short[]>? SamplesAvailable;
    public event Action<short[], short[]>? StereoSamplesAvailable;
    public event Action<double>? LevelAvailable;
    public event Action<LevelMeterReading>? StereoLevelAvailable;
    public event Action<string>? StatusAvailable;
    public event Action<string>? CaptureStopped;
    public int SampleRateValue => DefaultSampleRate;
    int IAudioCaptureSource.SampleRate => DefaultSampleRate;
    public bool IsStereo { get; private set; }

    private readonly int? _deviceId;

    private readonly List<BufferState> _buffers = new();
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _callbacksIdle = new(true);
    private readonly AudioInterop.WaveInProc _callback;
    private IntPtr _handle;
    private volatile bool _running;
    private bool _stopping;
    private int _activeCallbacks;
    private int _stopNotificationSent;

    public AudioCapture()
    {
        _callback = OnWaveIn;
    }

    public AudioCapture(int deviceId) : this()
    {
        _deviceId = deviceId;
    }

    public static AudioDevice DefaultInputDevice { get; } =
        new(AudioInterop.WaveMapper, "Default Windows input");

    public static IReadOnlyList<AudioDevice> GetInputDevices()
    {
        var devices = new List<AudioDevice>();
        var count = AudioInterop.waveInGetNumDevs();
        for (uint i = 0; i < count; i++)
        {
            if (AudioInterop.waveInGetDevCaps(i, out var caps, (uint)Marshal.SizeOf<AudioInterop.WaveInCaps>()) == 0)
            {
                string name = string.IsNullOrWhiteSpace(caps.ProductName) ? $"Input {i}" : caps.ProductName;
                devices.Add(new AudioDevice((int)i, name));
            }
        }

        return devices;
    }

    public static IReadOnlyList<AudioDevice> GetFallbackInputDevices()
    {
        return new[] { DefaultInputDevice };
    }

    public void Start(int deviceId)
    {
        lock (_gate)
        {
            if (_running)
                return;
            if (_stopping || _handle != IntPtr.Zero)
                throw new InvalidOperationException("The previous input device is still stopping.");
        }

        var format = SelectFormat(deviceId);
        IsStereo = format.Channels == 2;
        Interlocked.Exchange(ref _stopNotificationSent, 0);
        ThrowIfFailed(AudioInterop.waveInOpen(out _handle, deviceId, ref format, _callback, IntPtr.Zero, AudioInterop.CallbackFunction), "waveInOpen");

        const int bufferCount = 6;
        int bufferBytes = SampleRate / 25 * format.BlockAlign;
        for (int i = 0; i < bufferCount; i++)
            AddBuffer(bufferBytes);

        _running = true;
        try
        {
            ThrowIfFailed(AudioInterop.waveInStart(_handle), "waveInStart");
        }
        catch
        {
            _running = false;
            throw;
        }
        StatusAvailable?.Invoke(
            $"waveIn started: {DefaultSampleRate} Hz, {(IsStereo ? "stereo" : "mono")}, 16 bit");
    }

    public void Start()
    {
        if (_deviceId == null)
            throw new InvalidOperationException("Input device is not selected.");
        Start(_deviceId.Value);
    }

    public void Stop()
    {
        IntPtr handle;
        lock (_gate)
        {
            handle = _handle;
            if (handle == IntPtr.Zero || _stopping)
                return;

            _running = false;
            _stopping = true;
        }

        _callbacksIdle.Wait(TimeSpan.FromMilliseconds(100));
        try { AudioInterop.waveInStop(handle); } catch { }
        try { AudioInterop.waveInReset(handle); } catch { }

        if (!_callbacksIdle.Wait(TimeSpan.FromSeconds(1)))
            StatusAvailable?.Invoke("Input device cleanup timed out");

        lock (_gate)
        {
            foreach (var buffer in _buffers)
            {
                try { AudioInterop.waveInUnprepareHeader(handle, buffer.HeaderPtr, (uint)Marshal.SizeOf<AudioInterop.WaveHeader>()); } catch { }
                Marshal.FreeHGlobal(buffer.DataPtr);
                Marshal.FreeHGlobal(buffer.HeaderPtr);
            }

            _buffers.Clear();
            try { AudioInterop.waveInClose(handle); } catch { }
            _handle = IntPtr.Zero;
            _stopping = false;
        }
    }

    private void AddBuffer(int bytes)
    {
        var data = new byte[bytes];
        var dataPtr = Marshal.AllocHGlobal(bytes);
        Marshal.Copy(data, 0, dataPtr, bytes);

        var header = new AudioInterop.WaveHeader
        {
            Data = dataPtr,
            BufferLength = (uint)bytes
        };
        var headerPtr = Marshal.AllocHGlobal(Marshal.SizeOf<AudioInterop.WaveHeader>());
        Marshal.StructureToPtr(header, headerPtr, false);

        ThrowIfFailed(AudioInterop.waveInPrepareHeader(_handle, headerPtr, (uint)Marshal.SizeOf<AudioInterop.WaveHeader>()), "waveInPrepareHeader");
        ThrowIfFailed(AudioInterop.waveInAddBuffer(_handle, headerPtr, (uint)Marshal.SizeOf<AudioInterop.WaveHeader>()), "waveInAddBuffer");
        _buffers.Add(new BufferState { HeaderPtr = headerPtr, DataPtr = dataPtr, Data = data });
    }

    private void OnWaveIn(IntPtr hwi, int message, IntPtr instance, IntPtr headerPtr, IntPtr reserved)
    {
        if (Interlocked.Increment(ref _activeCallbacks) == 1)
            _callbacksIdle.Reset();

        try
        {
            if (message != AudioInterop.MmWimData || !_running || headerPtr == IntPtr.Zero)
                return;

            var header = Marshal.PtrToStructure<AudioInterop.WaveHeader>(headerPtr);
            int byteCount = (int)header.BytesRecorded;
            if (byteCount <= 0)
                return;

            var bytes = new byte[byteCount];
            Marshal.Copy(header.Data, bytes, 0, byteCount);
            var interleaved = new short[byteCount / 2];
            Buffer.BlockCopy(bytes, 0, interleaved, 0, byteCount);
            PublishSamples(interleaved);

            IntPtr handle;
            lock (_gate)
            {
                if (!_running || _handle == IntPtr.Zero)
                    return;
                handle = _handle;
            }

            uint result = AudioInterop.waveInAddBuffer(handle, headerPtr, (uint)Marshal.SizeOf<AudioInterop.WaveHeader>());
            if (result != 0)
            {
                _running = false;
                NotifyCaptureStopped($"Input device is no longer available ({result})");
            }
        }
        finally
        {
            if (Interlocked.Decrement(ref _activeCallbacks) == 0)
                _callbacksIdle.Set();
        }
    }

    private static void ThrowIfFailed(uint result, string operation)
    {
        if (result != 0)
            throw new InvalidOperationException($"{operation} failed: {result}");
    }

    private void NotifyCaptureStopped(string message)
    {
        if (Interlocked.Exchange(ref _stopNotificationSent, 1) != 0)
            return;

        var handler = CaptureStopped;
        if (handler == null)
            return;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                handler(message);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        });
    }

    public void Dispose() => Stop();

    private static AudioInterop.WaveFormatEx SelectFormat(int deviceId)
    {
        var stereo = AudioInterop.Pcm16Stereo(SampleRate);
        if (AudioInterop.waveInOpen(
                out _,
                deviceId,
                ref stereo,
                null!,
                IntPtr.Zero,
                AudioInterop.WaveFormatQuery) == 0)
        {
            return stereo;
        }

        return AudioInterop.Pcm16Mono(SampleRate);
    }

    private void PublishSamples(short[] interleaved)
    {
        if (!IsStereo)
        {
            double peak = Peak(interleaved);
            SamplesAvailable?.Invoke(interleaved);
            StereoSamplesAvailable?.Invoke(interleaved, interleaved);
            LevelAvailable?.Invoke(peak);
            StereoLevelAvailable?.Invoke(LevelMeterReading.Mono(peak));
            return;
        }

        int frameCount = interleaved.Length / 2;
        var left = new short[frameCount];
        var right = new short[frameCount];
        var mono = new short[frameCount];
        for (int frame = 0; frame < frameCount; frame++)
        {
            short leftSample = interleaved[frame * 2];
            short rightSample = interleaved[frame * 2 + 1];
            left[frame] = leftSample;
            right[frame] = rightSample;
            mono[frame] = (short)((leftSample + rightSample) / 2);
        }

        double leftPeak = Peak(left);
        double rightPeak = Peak(right);
        SamplesAvailable?.Invoke(mono);
        StereoSamplesAvailable?.Invoke(left, right);
        LevelAvailable?.Invoke(Math.Max(leftPeak, rightPeak));
        StereoLevelAvailable?.Invoke(new LevelMeterReading(leftPeak, rightPeak));
    }

    private static double Peak(short[] samples)
    {
        double peak = 0;
        for (int i = 0; i < samples.Length; i++)
            peak = Math.Max(peak, Math.Abs(samples[i] / 32768.0));
        return peak;
    }
}
