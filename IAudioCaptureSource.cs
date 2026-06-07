namespace SpectrumViewerRT;

public interface IAudioCaptureSource : IDisposable
{
    event Action<short[]>? SamplesAvailable;
    event Action<double>? LevelAvailable;
    event Action<LevelMeterReading>? StereoLevelAvailable;
    event Action<string>? StatusAvailable;
    int SampleRate { get; }
    void Start();
    void Stop();
}
