namespace SpectrumViewerRT;

internal static class FineLineVfdLayout
{
    public readonly record struct Profile(
        int LineThicknessPixels,
        int LineGapPixels,
        int LinesPerBlock,
        int BlockGapPixels)
    {
        public int LinePitchPixels => LineThicknessPixels + LineGapPixels;
        public int BlockContentPixels =>
            LinesPerBlock * LineThicknessPixels + (LinesPerBlock - 1) * LineGapPixels;
        public int BlockPitchPixels => BlockContentPixels + BlockGapPixels;
    }

    public static Profile LevelMeter { get; } = new(
        LineThicknessPixels: 1,
        LineGapPixels: 2,
        LinesPerBlock: 4,
        BlockGapPixels: 5);

    public static Profile SpectrumAnalyzer { get; } = new(
        LineThicknessPixels: 1,
        LineGapPixels: 2,
        LinesPerBlock: 2,
        BlockGapPixels: 5);
}
