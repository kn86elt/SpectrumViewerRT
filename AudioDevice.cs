namespace SpectrumViewerRT;

public sealed record AudioDevice(int Id, string Name)
{
    public string? WasapiId { get; init; }

    public override string ToString() => Name;
}
