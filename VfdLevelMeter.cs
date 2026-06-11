using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace SpectrumViewerRT;

public sealed class VfdLevelMeter : FrameworkElement
{
    private static readonly Point[] GlowOffsets =
    {
        new(-1, 0), new(1, 0), new(0, -1), new(0, 1), new(-1, -1), new(1, 1)
    };
    private static readonly Brush BackgroundBrush = VfdDrawingCache.Brush(Color.FromRgb(4, 12, 14));
    private static readonly Pen BorderPen = VfdDrawingCache.Pen(Color.FromRgb(31, 57, 58), 1);
    private static readonly Brush BackgroundGradient = CreateFrozenBrush(
        new LinearGradientBrush(Color.FromArgb(80, 24, 90, 90), Colors.Transparent, 0));
    private static readonly Brush HorizontalTextureBrush = VfdDrawingCache.HorizontalLineTexture(
        Color.FromArgb(18, 190, 255, 235), 3, 1.5);
    private static readonly Brush VerticalTextureBrush = VfdDrawingCache.VerticalLineTexture(
        Color.FromArgb(10, 210, 255, 245), 11, 4.5);
    private static readonly Brush TextureHighlightBrush = CreateFrozenBrush(new LinearGradientBrush(
        Color.FromArgb(36, 255, 255, 255),
        Color.FromArgb(0, 255, 255, 255),
        new Point(0, 0),
        new Point(0, 0.45)));
    public static readonly DependencyProperty LeftLevelProperty =
        DependencyProperty.Register(nameof(LeftLevel), typeof(double), typeof(VfdLevelMeter),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RightLevelProperty =
        DependencyProperty.Register(nameof(RightLevel), typeof(double), typeof(VfdLevelMeter),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LeftPeakHoldProperty =
        DependencyProperty.Register(nameof(LeftPeakHold), typeof(double), typeof(VfdLevelMeter),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RightPeakHoldProperty =
        DependencyProperty.Register(nameof(RightPeakHold), typeof(double), typeof(VfdLevelMeter),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ColorThemeProperty =
        DependencyProperty.Register(nameof(ColorTheme), typeof(int), typeof(VfdLevelMeter),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MeterStyleProperty =
        DependencyProperty.Register(nameof(MeterStyle), typeof(int), typeof(VfdLevelMeter),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowUnlitSegmentsProperty =
        DependencyProperty.Register(nameof(ShowUnlitSegments), typeof(bool), typeof(VfdLevelMeter),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GlowEnabledProperty =
        DependencyProperty.Register(nameof(GlowEnabled), typeof(bool), typeof(VfdLevelMeter),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextureEnabledProperty =
        DependencyProperty.Register(nameof(TextureEnabled), typeof(bool), typeof(VfdLevelMeter),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly double[] DbMarks = { -60, -40, -30, -20, -10, -4, -2, 0, 2, 4, 6, 8, 10, 12, 14 };
    private double _verticalRenderScale = 1.0;
    private readonly DotMatrixVfdRasterizer _dotRasterizer = new();

    public double LeftLevel
    {
        get => (double)GetValue(LeftLevelProperty);
        set => SetValue(LeftLevelProperty, value);
    }

    public double RightLevel
    {
        get => (double)GetValue(RightLevelProperty);
        set => SetValue(RightLevelProperty, value);
    }

    public double LeftPeakHold
    {
        get => (double)GetValue(LeftPeakHoldProperty);
        set => SetValue(LeftPeakHoldProperty, value);
    }

    public double RightPeakHold
    {
        get => (double)GetValue(RightPeakHoldProperty);
        set => SetValue(RightPeakHoldProperty, value);
    }

    public int ColorTheme
    {
        get => (int)GetValue(ColorThemeProperty);
        set => SetValue(ColorThemeProperty, value);
    }

    public int MeterStyle
    {
        get => (int)GetValue(MeterStyleProperty);
        set => SetValue(MeterStyleProperty, value);
    }

    public bool ShowUnlitSegments
    {
        get => (bool)GetValue(ShowUnlitSegmentsProperty);
        set => SetValue(ShowUnlitSegmentsProperty, value);
    }

    public bool GlowEnabled
    {
        get => (bool)GetValue(GlowEnabledProperty);
        set => SetValue(GlowEnabledProperty, value);
    }

    public bool TextureEnabled
    {
        get => (bool)GetValue(TextureEnabledProperty);
        set => SetValue(TextureEnabledProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsInfinity(availableSize.Width) ? 360 : Math.Max(0, availableSize.Width),
        double.IsInfinity(availableSize.Height) ? 68 : Math.Max(0, availableSize.Height));

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRoundedRectangle(BackgroundBrush, BorderPen, bounds, 3, 3);
        dc.DrawRectangle(BackgroundGradient, null, bounds);

        const double designHeight = 68;
        double contentScale = ActualHeight / designHeight;
        if (contentScale <= 0 || ActualWidth <= 0)
            return;

        _verticalRenderScale = contentScale;
        bool dotMatrix = MeterStyle is 2 or 3;
        if (dotMatrix)
            _dotRasterizer.Begin(ActualWidth, ActualHeight, VisualTreeHelper.GetDpi(this));
        dc.PushClip(new RectangleGeometry(bounds));
        dc.PushTransform(new ScaleTransform(1.0, contentScale));
        DrawMeterContent(dc, ActualWidth, designHeight);
        dc.Pop();
        dc.Pop();
        if (dotMatrix)
        {
            var image = _dotRasterizer.Commit();
            if (image != null)
                dc.DrawImage(image, bounds);
        }
        _verticalRenderScale = 1.0;
        DrawTexture(dc, bounds);
    }

    private void DrawMeterContent(DrawingContext dc, double width, double height)
    {
        double leftLabelWidth = 28;
        double top = 7;
        double scaleHeight = 19;
        double rowGap = 6;
        double rowHeight = Math.Max(1, (height - top - scaleHeight - rowGap) / 2.0);
        double meterLeft = leftLabelWidth + 6;
        double meterRight = Math.Max(meterLeft + 20, width - 8);
        double meterWidth = meterRight - meterLeft;

        DrawRow(dc, "L", LeftLevel, LeftPeakHold, meterLeft, top, meterWidth, rowHeight);
        DrawRow(dc, "R", RightLevel, RightPeakHold, meterLeft, top + rowHeight + rowGap, meterWidth, rowHeight);
        DrawScale(dc, meterLeft, height - scaleHeight + 3, meterWidth);
    }

    private void DrawRow(DrawingContext dc, string label, double level, double peakHold, double x, double y, double width, double height)
    {
        DrawText(dc, label, 10, y, 9, ActiveColor());

        if (MeterStyle is 1 or 3)
        {
            DrawFineLineRow(dc, level, peakHold, x, y, width, height);
            return;
        }

        int segments = Math.Max(32, (int)(width / 8));
        double gap = 2;
        double segmentWidth = Math.Max(2, (width - gap * (segments - 1)) / segments);
        double db = LevelToDb(level);
        double holdDb = LevelToDb(peakHold);

        for (int i = 0; i < segments; i++)
        {
            double t = i / (double)(segments - 1);
            double segmentDb = -60 + t * 74;
            bool active = segmentDb <= db;
            var color = SegmentColor(segmentDb, active);
            var rect = new Rect(x + i * (segmentWidth + gap), y, segmentWidth, height);
            DrawSegment(dc, rect, color, active);
        }

        int holdSegment = Math.Clamp((int)Math.Round(DbToPosition(holdDb) * (segments - 1)), 0, segments - 1);
        if (holdDb > -60.0)
        {
            double holdSegmentDb = -60 + holdSegment / (double)(segments - 1) * 74;
            var holdRect = new Rect(x + holdSegment * (segmentWidth + gap), y, segmentWidth, height);
            DrawSegment(dc, holdRect, SegmentColor(holdSegmentDb, true), true);
        }
    }

    private void DrawFineLineRow(DrawingContext dc, double level, double peakHold, double x, double y, double width, double height)
    {
        var profile = FineLineVfdLayout.LevelMeter;
        double dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int firstPixel = (int)Math.Ceiling(x * dpiScale);
        int lastPixel = (int)Math.Floor((x + width) * dpiScale) - 1;
        int blockCount = Math.Max(1,
            (lastPixel - firstPixel + profile.BlockPitchPixels) /
            profile.BlockPitchPixels);
        double db = LevelToDb(level);
        double holdDb = LevelToDb(peakHold);
        int activeBlock = Math.Clamp(
            (int)Math.Floor(DbToPosition(db) * blockCount),
            0,
            blockCount);
        int holdBlock = Math.Clamp(
            (int)Math.Round(DbToPosition(holdDb) * (blockCount - 1)),
            0,
            blockCount - 1);

        dc.PushClip(new RectangleGeometry(new Rect(x, y, width, height)));
        for (int block = 0; block < blockCount; block++)
        {
            int blockPixel = firstPixel + block * profile.BlockPitchPixels;
            double blockPosition = blockCount == 1 ? 0 : block / (double)(blockCount - 1);
            double blockDb = -60 + blockPosition * 74;
            bool active = block < activeBlock || holdDb > -60.0 && block == holdBlock;
            Color color = SegmentColor(blockDb, active);

            for (int line = 0; line < profile.LinesPerBlock; line++)
            {
                int pixelX = blockPixel + line * profile.LinePitchPixels;
                if (pixelX > lastPixel)
                    break;

                DrawDevicePixelLine(
                    dc,
                    pixelX / dpiScale,
                    y,
                    height,
                    profile.LineThicknessPixels / dpiScale,
                    color,
                    active);
            }
        }
        dc.Pop();
    }

    private void DrawDevicePixelLine(
        DrawingContext dc,
        double x,
        double y,
        double height,
        double pixelWidth,
        Color color,
        bool active)
    {
        if (MeterStyle == 3)
        {
            DrawDotMatrixColumn(dc, x, y, height, pixelWidth, color, active);
            return;
        }

        if (active && GlowEnabled)
        {
            dc.DrawRectangle(
                VfdDrawingCache.Brush(WithAlpha(color, 60)),
                null,
                new Rect(x - pixelWidth, y, pixelWidth * 3, height));
        }

        dc.DrawRectangle(VfdDrawingCache.Brush(color), null, new Rect(x, y, pixelWidth, height));
    }

    private void DrawDotMatrixColumn(
        DrawingContext dc,
        double x,
        double y,
        double height,
        double pixelWidth,
        Color color,
        bool active)
    {
        _dotRasterizer.DrawDots(
            new Rect(x, y * _verticalRenderScale, pixelWidth, height * _verticalRenderScale),
            color,
            active && GlowEnabled);
    }

    private void DrawSegment(DrawingContext dc, Rect rect, Color color, bool active)
    {
        if (MeterStyle == 2)
        {
            DrawDotMatrixBlock(dc, rect, color, active);
            return;
        }

        if (active && GlowEnabled)
        {
            dc.DrawRoundedRectangle(VfdDrawingCache.Brush(WithAlpha(color, 42)), null, Inflate(rect, 2.4, 2.0), 2.2, 2.2);
            dc.DrawRoundedRectangle(VfdDrawingCache.Brush(WithAlpha(color, 78)), null, Inflate(rect, 1.1, 0.9), 1.8, 1.8);
        }

        dc.DrawRoundedRectangle(VfdDrawingCache.Brush(color), null, rect, 1, 1);
    }

    private void DrawDotMatrixBlock(DrawingContext dc, Rect rect, Color color, bool active)
    {
        _dotRasterizer.DrawDots(
            new Rect(rect.X, rect.Y * _verticalRenderScale, rect.Width, rect.Height * _verticalRenderScale),
            color,
            active && GlowEnabled);
    }

    private void DrawScale(DrawingContext dc, double x, double y, double width)
    {
        const double fontSize = 8;
        DrawText(dc, "dB", x - 27, y, fontSize, ActiveColor());
        foreach (double mark in DbMarks)
        {
            double pos = x + DbToPosition(mark) * width;
            var color = mark >= 6 ? Color.FromRgb(255, 68, 42) : ActiveColor();
            string text = mark <= -60 ? "-inf" : mark.ToString("0", CultureInfo.InvariantCulture);
            var formatted = CreateFormattedText(text, fontSize, color);
            double textX = Math.Clamp(pos - formatted.Width / 2.0, x - 1, x + width - formatted.Width);
            DrawText(dc, text, textX, y, fontSize, color);
        }
    }

    private Color SegmentColor(double db, bool active)
    {
        if (!active)
            return ShowUnlitSegments ? InactiveColor() : Color.FromRgb(3, 10, 11);

        if (db >= 6)
            return Color.FromRgb(255, 53, 35);
        if (db >= 0)
            return Color.FromRgb(255, 165, 72);
        return ActiveColor();
    }

    private Color ActiveColor() => ColorTheme switch
    {
        1 => Color.FromRgb(126, 255, 124),
        2 => Color.FromRgb(255, 205, 92),
        3 => Color.FromRgb(104, 184, 255),
        _ => Color.FromRgb(188, 255, 248)
    };

    private Color InactiveColor()
    {
        var active = ActiveColor();
        return Color.FromRgb(
            (byte)Math.Clamp(active.R * 0.09, 4, 24),
            (byte)Math.Clamp(active.G * 0.12, 10, 36),
            (byte)Math.Clamp(active.B * 0.12, 10, 38));
    }

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Rect Inflate(Rect rect, double x, double y)
    {
        rect.Inflate(x, y);
        return rect;
    }

    private void DrawTexture(DrawingContext dc, Rect bounds)
    {
        if (!TextureEnabled || bounds.Width <= 0 || bounds.Height <= 0)
            return;

        dc.DrawRectangle(HorizontalTextureBrush, null, bounds);
        dc.DrawRectangle(VerticalTextureBrush, null, bounds);
        dc.DrawRectangle(TextureHighlightBrush, null, bounds);
        dc.DrawRectangle(VfdDrawingCache.Brush(Color.FromArgb(18, 0, 0, 0)), null, new Rect(bounds.Left, bounds.Top, bounds.Width, 1));
        dc.DrawRectangle(VfdDrawingCache.Brush(Color.FromArgb(24, 0, 0, 0)), null, new Rect(bounds.Left, bounds.Bottom - 1, bounds.Width, 1));
    }

    private static double LevelToDb(double level)
    {
        level = Math.Clamp(level, 0.000001, 5.2);
        return 20.0 * Math.Log10(level);
    }

    private static double DbToPosition(double db) => Math.Clamp((db + 60.0) / 74.0, 0, 1);

    private void DrawText(DrawingContext dc, string text, double x, double y, double size, Color color)
    {
        var formatted = CreateFormattedText(text, size, color);

        if (GlowEnabled)
        {
            var glowText = CreateFormattedText(text, size, WithAlpha(color, 60));
            foreach (var offset in GlowOffsets)
                dc.DrawText(glowText, new Point(x + offset.X, y + offset.Y));
        }

        dc.DrawText(formatted, new Point(x, y));
    }

    private FormattedText CreateFormattedText(string text, double size, Color color) =>
        new(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Consolas"),
            size,
            VfdDrawingCache.Brush(color),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static T CreateFrozenBrush<T>(T brush) where T : Brush
    {
        brush.Freeze();
        return brush;
    }
}
