using System.Windows;
using System.Windows.Media;

namespace SpectrumViewerRT;

public sealed class LiveWaveformDisplay : FrameworkElement
{
    private static readonly Pen GridPen = CreatePen(Color.FromRgb(17, 28, 39), 1);
    private static readonly Pen CenterPen = CreatePen(Color.FromRgb(29, 44, 59), 1);
    private static readonly Pen WaveformPen = CreatePen(Color.FromRgb(90, 220, 255), 1.2);

    private short[] _samples = Array.Empty<short>();
    private bool _showGrid;
    private bool _hasFrame;

    public void SetFrame(IReadOnlyList<short> samples, int firstSample, int sampleCount, bool showGrid)
    {
        if (sampleCount <= 0)
        {
            ClearFrame();
            return;
        }

        var frameSamples = new short[sampleCount];
        for (int i = 0; i < sampleCount; i++)
            frameSamples[i] = samples[firstSample + i];

        _samples = frameSamples;
        _showGrid = showGrid;
        _hasFrame = true;
        InvalidateVisual();
    }

    public void ClearFrame()
    {
        _samples = Array.Empty<short>();
        _hasFrame = false;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        double width = RenderSize.Width;
        double height = RenderSize.Height;
        if (!_hasFrame || width <= 0 || height <= 0)
            return;

        if (_showGrid)
        {
            for (int division = 0; division < 10; division++)
            {
                double x = width * division / 10;
                drawingContext.DrawLine(GridPen, new Point(x, 0), new Point(x, height));
            }

            for (int division = 1; division <= 4; division++)
            {
                double y = height * division / 5;
                drawingContext.DrawLine(GridPen, new Point(0, y), new Point(width, y));
            }
        }

        double center = height / 2;
        drawingContext.DrawLine(CenterPen, new Point(0, center), new Point(width, center));
        if (_samples.Length == 0)
            return;

        var waveform = new StreamGeometry();
        using (StreamGeometryContext context = waveform.Open())
        {
            context.BeginFigure(MapSample(0, width, height), isFilled: false, isClosed: false);
            for (int i = 1; i < _samples.Length; i++)
                context.LineTo(MapSample(i, width, height), isStroked: true, isSmoothJoin: true);
        }

        waveform.Freeze();
        drawingContext.DrawGeometry(null, WaveformPen, waveform);
    }

    private Point MapSample(int index, double width, double height)
    {
        double x = _samples.Length <= 1 ? width / 2 : width * index / (_samples.Length - 1);
        double normalized = _samples[index] / 32768.0;
        double y = height / 2 - normalized * (height * 0.46);
        return new Point(x, y);
    }

    private static Pen CreatePen(Color color, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness);
        pen.Freeze();
        return pen;
    }
}
