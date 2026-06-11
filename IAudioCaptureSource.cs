namespace SpectrumViewerRT;

public interface IAudioCaptureSource : IDisposable
{
    event Action<short[]>? SamplesAvailable;
    event Action<short[], short[]>? StereoSamplesAvailable;
    event Action<double>? LevelAvailable;
    event Action<LevelMeterReading>? StereoLevelAvailable;
    event Action<string>? StatusAvailable;
    event Action<string>? CaptureStopped;
    int SampleRate { get; }
    void Start();
    void Stop();
}
