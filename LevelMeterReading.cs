namespace SpectrumViewerRT;

public readonly record struct LevelMeterReading(double Left, double Right)
{
    public double Peak => Math.Max(Left, Right);

    public static LevelMeterReading Mono(double value) => new(value, value);
}
