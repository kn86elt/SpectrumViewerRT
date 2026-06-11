using System.Runtime.InteropServices;

namespace SpectrumViewerRT;

internal static class WasapiInterop
{
    public const int DeviceStateActive = 0x00000001;
    public const int ClsctxAll = 23;
    public const int AudioClientShareModeShared = 0;
    public const int AudioClientStreamFlagsLoopback = 0x00020000;
    public const int AudioClientStreamFlagsEventCallback = 0x00040000;
    public const uint CoinitMultithreaded = 0x0;
    public const int RpcESChangedMode = unchecked((int)0x80010106);
    public const int AudclntEDeviceInvalidated = unchecked((int)0x88890004);

    public static readonly Guid IAudioClientId = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    public static readonly Guid IAudioCaptureClientId = new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
    public static readonly Guid IAudioEndpointVolumeId = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    public enum EDataFlow
    {
        Render,
        Capture,
        All
    }

    public enum ERole
    {
        Console,
        Multimedia,
        Communications
    }

    [Flags]
    public enum AudioClientBufferFlags
    {
        None = 0,
        DataDiscontinuity = 0x1,
        Silent = 0x2,
        TimestampError = 0x4
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
    public struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal sealed class MMDeviceEnumeratorComObject
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out object devices);
        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig]
        int RegisterEndpointNotificationCallback(IMMNotificationClient client);
        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }

    [ComVisible(true)]
    [Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMNotificationClient
    {
        [PreserveSig]
        int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, uint newState);
        [PreserveSig]
        int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        [PreserveSig]
        int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        [PreserveSig]
        int OnDefaultDeviceChanged(
            EDataFlow flow,
            ERole role,
            [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);
        [PreserveSig]
        int OnPropertyValueChanged(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            PropertyKey propertyKey);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, uint clsctx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
        [PreserveSig]
        int OpenPropertyStore(uint access, out object properties);
        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig]
        int GetState(out uint state);
    }

    [ComImport]
    [Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioClient
    {
        [PreserveSig]
        int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, ref Guid audioSessionGuid);
        [PreserveSig]
        int GetBufferSize(out uint bufferSize);
        [PreserveSig]
        int GetStreamLatency(out long latency);
        [PreserveSig]
        int GetCurrentPadding(out uint padding);
        [PreserveSig]
        int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);
        [PreserveSig]
        int GetMixFormat(out IntPtr format);
        [PreserveSig]
        int GetDevicePeriod(out long defaultDevicePeriod, out long minimumDevicePeriod);
        [PreserveSig]
        int Start();
        [PreserveSig]
        int Stop();
        [PreserveSig]
        int Reset();
        [PreserveSig]
        int SetEventHandle(IntPtr eventHandle);
        [PreserveSig]
        int GetService(ref Guid serviceId, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport]
    [Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioCaptureClient
    {
        [PreserveSig]
        int GetBuffer(out IntPtr data, out uint frames, out AudioClientBufferFlags flags, out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig]
        int ReleaseBuffer(uint frames);
        [PreserveSig]
        int GetNextPacketSize(out uint frames);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        [PreserveSig]
        int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig]
        int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig]
        int GetChannelCount(out uint channelCount);
        [PreserveSig]
        int SetMasterVolumeLevel(float levelDb, IntPtr eventContext);
        [PreserveSig]
        int SetMasterVolumeLevelScalar(float level, IntPtr eventContext);
        [PreserveSig]
        int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig]
        int SetChannelVolumeLevel(uint channel, float levelDb, IntPtr eventContext);
        [PreserveSig]
        int SetChannelVolumeLevelScalar(uint channel, float level, IntPtr eventContext);
        [PreserveSig]
        int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig]
        int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr eventContext);
        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        [PreserveSig]
        int GetVolumeStepInfo(out uint step, out uint stepCount);
        [PreserveSig]
        int VolumeStepUp(IntPtr eventContext);
        [PreserveSig]
        int VolumeStepDown(IntPtr eventContext);
        [PreserveSig]
        int QueryHardwareSupport(out uint hardwareSupportMask);
        [PreserveSig]
        int GetVolumeRange(out float minimumDb, out float maximumDb, out float incrementDb);
    }

    [DllImport("ole32.dll")]
    public static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll")]
    public static extern void CoUninitialize();

    [DllImport("ole32.dll")]
    public static extern void CoTaskMemFree(IntPtr pv);
}
