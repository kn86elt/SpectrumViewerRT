using System.Windows;
using System.Windows.Media;

namespace SpectrumViewerRT;

internal static class VfdDrawingCache
{
    private static readonly Dictionary<uint, SolidColorBrush> Brushes = new();
    private static readonly Dictionary<(uint Color, double Thickness, bool Dotted), Pen> Pens = new();

    public static SolidColorBrush Brush(Color color)
    {
        uint key = ColorKey(color);
        if (Brushes.TryGetValue(key, out var brush))
            return brush;

        brush = new SolidColorBrush(color);
        brush.Freeze();
        Brushes[key] = brush;
        return brush;
    }

    public static Pen Pen(Color color, double thickness, bool dotted = false)
    {
        var key = (ColorKey(color), thickness, dotted);
        if (Pens.TryGetValue(key, out var pen))
            return pen;

        pen = new Pen(Brush(color), thickness);
        if (dotted)
            pen.DashStyle = DashStyles.Dot;
        pen.Freeze();
        Pens[key] = pen;
        return pen;
    }

    public static DrawingBrush HorizontalLineTexture(Color color, double pitch, double y)
    {
        var drawing = new GeometryDrawing(
            null,
            Pen(color, 1),
            new LineGeometry(new Point(0, y), new Point(1, y)));
        return CreateTiledBrush(drawing, new Rect(0, 0, 1, pitch));
    }

    public static DrawingBrush VerticalLineTexture(Color color, double pitch, double x)
    {
        var drawing = new GeometryDrawing(
            null,
            Pen(color, 1),
            new LineGeometry(new Point(x, 0), new Point(x, 1)));
        return CreateTiledBrush(drawing, new Rect(0, 0, pitch, 1));
    }

    private static DrawingBrush CreateTiledBrush(Drawing drawing, Rect tile)
    {
        drawing.Freeze();
        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewbox = tile,
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
        brush.Freeze();
        return brush;
    }

    private static uint ColorKey(Color color) =>
        ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
}
