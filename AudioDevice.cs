namespace SpectrumViewerRT;

public sealed record AudioDevice(int Id, string Name)
{
    public override string ToString() => Name;
}
