using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace SpectrumViewerRT;

public sealed class AudioPlayback : IDisposable
{
    private sealed class PendingBuffer
    {
        public IntPtr HeaderPtr { get; init; }
        public IntPtr DataPtr { get; init; }
    }

    private readonly AudioInterop.WaveOutProc _callback;
    private readonly ConcurrentQueue<PendingBuffer> _pending = new();
    private IntPtr _handle;
    private bool _open;

    public AudioPlayback()
    {
        _callback = OnWaveOut;
        var format = AudioInterop.Pcm16Mono(AudioCapture.SampleRate);
        var result = AudioInterop.waveOutOpen(out _handle, AudioInterop.WaveMapper, ref format, _callback, IntPtr.Zero, AudioInterop.CallbackFunction);
        _open = result == 0;
    }

    public void Play(short[] samples)
    {
        if (!_open || samples.Length == 0)
            return;

        int bytes = samples.Length * sizeof(short);
        var dataPtr = Marshal.AllocHGlobal(bytes);
        var dataBytes = new byte[bytes];
        Buffer.BlockCopy(samples, 0, dataBytes, 0, bytes);
        Marshal.Copy(dataBytes, 0, dataPtr, bytes);

        var header = new AudioInterop.WaveHeader
        {
            Data = dataPtr,
            BufferLength = (uint)bytes
        };
        var headerPtr = Marshal.AllocHGlobal(Marshal.SizeOf<AudioInterop.WaveHeader>());
        Marshal.StructureToPtr(header, headerPtr, false);
        _pending.Enqueue(new PendingBuffer { HeaderPtr = headerPtr, DataPtr = dataPtr });

        AudioInterop.waveOutPrepareHeader(_handle, headerPtr, (uint)Marshal.SizeOf<AudioInterop.WaveHeader>());
        AudioInterop.waveOutWrite(_handle, headerPtr, (uint)Marshal.SizeOf<AudioInterop.WaveHeader>());
    }

    public void Reset()
    {
        if (_open)
            AudioInterop.waveOutReset(_handle);

        while (_pending.TryDequeue(out var buffer))
            Free(buffer);
    }

    private void OnWaveOut(IntPtr hwo, int message, IntPtr instance, IntPtr headerPtr, IntPtr reserved)
    {
        if (message != AudioInterop.WomDone)
            return;

        if (_pending.TryDequeue(out var buffer))
            Free(buffer);
    }

    private void Free(PendingBuffer buffer)
    {
        if (_open)
            AudioInterop.waveOutUnprepareHeader(_handle, buffer.HeaderPtr, (uint)Marshal.SizeOf<AudioInterop.WaveHeader>());
        Marshal.FreeHGlobal(buffer.DataPtr);
        Marshal.FreeHGlobal(buffer.HeaderPtr);
    }

    public void Dispose()
    {
        Reset();
        if (_open)
        {
            AudioInterop.waveOutClose(_handle);
            _open = false;
        }
    }
}
