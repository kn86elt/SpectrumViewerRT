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
    public event Action<double>? LevelAvailable;
    public event Action<LevelMeterReading>? StereoLevelAvailable;
    public event Action<string>? StatusAvailable;
    public int SampleRateValue => DefaultSampleRate;
    int IAudioCaptureSource.SampleRate => DefaultSampleRate;

    private readonly int? _deviceId;

    private readonly List<BufferState> _buffers = new();
    private readonly object _gate = new();
    private readonly AudioInterop.WaveInProc _callback;
    private IntPtr _handle;
    private bool _running;

    public AudioCapture()
    {
        _callback = OnWaveIn;
    }

    public AudioCapture(int deviceId) : this()
    {
        _deviceId = deviceId;
    }

    public static IReadOnlyList<AudioDevice> GetInputDevices()
    {
        var devices = new List<AudioDevice>();
        var count = AudioInterop.waveInGetNumDevs();
        for (uint i = 0; i < count; i++)
        {
            if (AudioInterop.waveInGetDevCaps(i, out var caps, (uint)Marshal.SizeOf<AudioInterop.WaveInCaps>()) == 0)
                devices.Add(new AudioDevice((int)i, string.IsNullOrWhiteSpace(caps.ProductName) ? $"Input {i}" : caps.ProductName));
        }

        return devices;
    }

    public void Start(int deviceId)
    {
        if (_running)
            return;

        var format = AudioInterop.Pcm16Mono(SampleRate);
        ThrowIfFailed(AudioInterop.waveInOpen(out _handle, deviceId, ref format, _callback, IntPtr.Zero, AudioInterop.CallbackFunction), "waveInOpen");

        const int bufferCount = 6;
        int bufferBytes = SampleRate / 25 * format.BlockAlign;
        for (int i = 0; i < bufferCount; i++)
            AddBuffer(bufferBytes);

        ThrowIfFailed(AudioInterop.waveInStart(_handle), "waveInStart");
        _running = true;
        StatusAvailable?.Invoke($"waveIn started: {DefaultSampleRate} Hz, mono, 16 bit");
    }

    public void Start()
    {
        if (_deviceId == null)
            throw new InvalidOperationException("Input device is not selected.");
        Start(_deviceId.Value);
    }

    public void Stop()
    {
        if (_handle == IntPtr.Zero)
            return;

        lock (_gate)
        {
            _running = false;
            AudioInterop.waveInStop(_handle);
            AudioInterop.waveInReset(_handle);

            foreach (var buffer in _buffers)
            {
                AudioInterop.waveInUnprepareHeader(_handle, buffer.HeaderPtr, (uint)Marshal.SizeOf<AudioInterop.WaveHeader>());
                Marshal.FreeHGlobal(buffer.DataPtr);
                Marshal.FreeHGlobal(buffer.HeaderPtr);
            }

            _buffers.Clear();
            AudioInterop.waveInClose(_handle);
            _handle = IntPtr.Zero;
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
        if (message != AudioInterop.MmWimData || !_running || headerPtr == IntPtr.Zero)
            return;

        var header = Marshal.PtrToStructure<AudioInterop.WaveHeader>(headerPtr);
        int byteCount = (int)header.BytesRecorded;
        if (byteCount <= 0)
            return;

        var bytes = new byte[byteCount];
        Marshal.Copy(header.Data, bytes, 0, byteCount);
        var samples = new short[byteCount / 2];
        Buffer.BlockCopy(bytes, 0, samples, 0, byteCount);

        double peak = 0;
        for (int i = 0; i < samples.Length; i++)
            peak = Math.Max(peak, Math.Abs(samples[i] / 32768.0));

        SamplesAvailable?.Invoke(samples);
        LevelAvailable?.Invoke(peak);
        StereoLevelAvailable?.Invoke(LevelMeterReading.Mono(peak));

        lock (_gate)
        {
            if (_running && _handle != IntPtr.Zero)
                AudioInterop.waveInAddBuffer(_handle, headerPtr, (uint)Marshal.SizeOf<AudioInterop.WaveHeader>());
        }
    }

    private static void ThrowIfFailed(uint result, string operation)
    {
        if (result != 0)
            throw new InvalidOperationException($"{operation} failed: {result}");
    }

    public void Dispose() => Stop();
}
