using System.Runtime.InteropServices;

namespace SpectrumViewerRT;

internal static class AudioInterop
{
    public const int CallbackFunction = 0x00030000;
    public const int WaveMapper = -1;
    public const int MmWimData = 0x3C0;
    public const int WomDone = 0x3BD;

    public delegate void WaveInProc(IntPtr hwi, int uMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2);
    public delegate void WaveOutProc(IntPtr hwo, int uMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct WaveInCaps
    {
        public ushort ManufacturerId;
        public ushort ProductId;
        public uint DriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ProductName;
        public uint Formats;
        public ushort Channels;
        public ushort Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WaveFormatEx
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSec;
        public uint AvgBytesPerSec;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WaveHeader
    {
        public IntPtr Data;
        public uint BufferLength;
        public uint BytesRecorded;
        public IntPtr User;
        public uint Flags;
        public uint Loops;
        public IntPtr Next;
        public IntPtr Reserved;
    }

    [DllImport("winmm.dll")]
    public static extern uint waveInGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Auto)]
    public static extern uint waveInGetDevCaps(uint deviceId, out WaveInCaps caps, uint capsSize);

    [DllImport("winmm.dll")]
    public static extern uint waveInOpen(out IntPtr handle, int deviceId, ref WaveFormatEx format, WaveInProc callback, IntPtr instance, uint flags);

    [DllImport("winmm.dll")]
    public static extern uint waveInPrepareHeader(IntPtr handle, IntPtr header, uint headerSize);

    [DllImport("winmm.dll")]
    public static extern uint waveInUnprepareHeader(IntPtr handle, IntPtr header, uint headerSize);

    [DllImport("winmm.dll")]
    public static extern uint waveInAddBuffer(IntPtr handle, IntPtr header, uint headerSize);

    [DllImport("winmm.dll")]
    public static extern uint waveInStart(IntPtr handle);

    [DllImport("winmm.dll")]
    public static extern uint waveInStop(IntPtr handle);

    [DllImport("winmm.dll")]
    public static extern uint waveInReset(IntPtr handle);

    [DllImport("winmm.dll")]
    public static extern uint waveInClose(IntPtr handle);

    [DllImport("winmm.dll")]
    public static extern uint waveOutOpen(out IntPtr handle, int deviceId, ref WaveFormatEx format, WaveOutProc callback, IntPtr instance, uint flags);

    [DllImport("winmm.dll")]
    public static extern uint waveOutPrepareHeader(IntPtr handle, IntPtr header, uint headerSize);

    [DllImport("winmm.dll")]
    public static extern uint waveOutUnprepareHeader(IntPtr handle, IntPtr header, uint headerSize);

    [DllImport("winmm.dll")]
    public static extern uint waveOutWrite(IntPtr handle, IntPtr header, uint headerSize);

    [DllImport("winmm.dll")]
    public static extern uint waveOutReset(IntPtr handle);

    [DllImport("winmm.dll")]
    public static extern uint waveOutClose(IntPtr handle);

    public static WaveFormatEx Pcm16Mono(int sampleRate) => new()
    {
        FormatTag = 1,
        Channels = 1,
        SamplesPerSec = (uint)sampleRate,
        BitsPerSample = 16,
        BlockAlign = 2,
        AvgBytesPerSec = (uint)(sampleRate * 2),
        Size = 0
    };
}
