using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace SpectrumViewerRT;

public sealed class VfdLevelMeter : FrameworkElement
{
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

    protected override Size MeasureOverride(Size availableSize) => new(360, 68);

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(4, 12, 14)), new Pen(new SolidColorBrush(Color.FromRgb(31, 57, 58)), 1), bounds, 3, 3);
        dc.DrawRectangle(new LinearGradientBrush(Color.FromArgb(80, 24, 90, 90), Colors.Transparent, 0), null, bounds);

        double leftLabelWidth = 28;
        double top = 7;
        double scaleHeight = 19;
        double rowGap = 6;
        double rowHeight = Math.Max(8, (ActualHeight - top - scaleHeight - rowGap) / 2.0);
        double meterLeft = leftLabelWidth + 6;
        double meterRight = Math.Max(meterLeft + 20, ActualWidth - 8);
        double meterWidth = meterRight - meterLeft;

        DrawRow(dc, "L", LeftLevel, LeftPeakHold, meterLeft, top, meterWidth, rowHeight);
        DrawRow(dc, "R", RightLevel, RightPeakHold, meterLeft, top + rowHeight + rowGap, meterWidth, rowHeight);
        DrawScale(dc, meterLeft, ActualHeight - scaleHeight + 3, meterWidth);
        DrawTexture(dc, bounds);
    }

    private void DrawRow(DrawingContext dc, string label, double level, double peakHold, double x, double y, double width, double height)
    {
        DrawText(dc, label, 9, y - 1, 10, ActiveColor());

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
        double holdSegmentDb = -60 + holdSegment / (double)(segments - 1) * 74;
        var holdRect = new Rect(x + holdSegment * (segmentWidth + gap), y, segmentWidth, height);
        DrawSegment(dc, holdRect, SegmentColor(holdSegmentDb, true), true);
    }

    private void DrawSegment(DrawingContext dc, Rect rect, Color color, bool active)
    {
        if (MeterStyle == 0)
        {
            if (active && GlowEnabled)
            {
                dc.DrawRoundedRectangle(new SolidColorBrush(WithAlpha(color, 42)), null, Inflate(rect, 2.4, 2.0), 2.2, 2.2);
                dc.DrawRoundedRectangle(new SolidColorBrush(WithAlpha(color, 78)), null, Inflate(rect, 1.1, 0.9), 1.8, 1.8);
            }

            dc.DrawRoundedRectangle(new SolidColorBrush(color), null, rect, 1, 1);
            return;
        }

        var brush = new SolidColorBrush(color);
        int lines = Math.Max(2, Math.Min(4, (int)Math.Floor(rect.Width / 2.0)));
        double lineWidth = 1.0;
        double innerWidth = Math.Max(0, rect.Width - lineWidth);
        var guidelines = new GuidelineSet();
        for (int i = 0; i < lines; i++)
        {
            double t = lines == 1 ? 0 : i / (double)(lines - 1);
            double x = Math.Round(rect.Left + t * innerWidth) + 0.5;
            guidelines.GuidelinesX.Add(x);
            guidelines.GuidelinesX.Add(x + lineWidth);
        }
        guidelines.GuidelinesY.Add(Math.Round(rect.Top) + 0.5);
        guidelines.GuidelinesY.Add(Math.Round(rect.Bottom) + 0.5);
        dc.PushGuidelineSet(guidelines);
        for (int i = 0; i < lines; i++)
        {
            double t = lines == 1 ? 0 : i / (double)(lines - 1);
            double x = Math.Round(rect.Left + t * innerWidth) + 0.5;
            if (active && GlowEnabled)
            {
                var glowBrush = new SolidColorBrush(WithAlpha(color, 60));
                dc.DrawRectangle(glowBrush, null, new Rect(x - 1.5, rect.Top - 1, 4.0, rect.Height + 2));
            }
            dc.DrawRectangle(brush, null, new Rect(x, rect.Top, lineWidth, rect.Height));
        }
        dc.Pop();
    }

    private void DrawScale(DrawingContext dc, double x, double y, double width)
    {
        DrawText(dc, "dB", x - 28, y - 1, 10, ActiveColor());
        foreach (double mark in DbMarks)
        {
            double pos = x + DbToPosition(mark) * width;
            var color = mark >= 6 ? Color.FromRgb(255, 68, 42) : ActiveColor();
            string text = mark <= -60 ? "-inf" : mark.ToString("0", CultureInfo.InvariantCulture);
            DrawText(dc, text, pos - 8, y, 10, color);
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

        var horizontalPen = new Pen(new SolidColorBrush(Color.FromArgb(18, 190, 255, 235)), 1);
        for (double y = 1.5; y < bounds.Height; y += 3)
            dc.DrawLine(horizontalPen, new Point(bounds.Left, y), new Point(bounds.Right, y));

        var verticalPen = new Pen(new SolidColorBrush(Color.FromArgb(10, 210, 255, 245)), 1);
        for (double x = 4.5; x < bounds.Width; x += 11)
            dc.DrawLine(verticalPen, new Point(x, bounds.Top + 1), new Point(x, bounds.Bottom - 1));

        dc.DrawRectangle(new LinearGradientBrush(
            Color.FromArgb(36, 255, 255, 255),
            Color.FromArgb(0, 255, 255, 255),
            new Point(0, 0),
            new Point(0, 0.45)), null, bounds);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(18, 0, 0, 0)), null, new Rect(bounds.Left, bounds.Top, bounds.Width, 1));
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(24, 0, 0, 0)), null, new Rect(bounds.Left, bounds.Bottom - 1, bounds.Width, 1));
    }

    private static double LevelToDb(double level)
    {
        level = Math.Clamp(level, 0.000001, 5.2);
        return 20.0 * Math.Log10(level);
    }

    private static double DbToPosition(double db) => Math.Clamp((db + 60.0) / 74.0, 0, 1);

    private void DrawText(DrawingContext dc, string text, double x, double y, double size, Color color)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Consolas"),
            size,
            new SolidColorBrush(color),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        if (GlowEnabled)
        {
            var glowText = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Consolas"),
                size,
                new SolidColorBrush(WithAlpha(color, 60)),
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            foreach (var offset in new[] { new Point(-1, 0), new Point(1, 0), new Point(0, -1), new Point(0, 1), new Point(-1, -1), new Point(1, 1) })
                dc.DrawText(glowText, new Point(x + offset.X, y + offset.Y));
        }

        dc.DrawText(formatted, new Point(x, y));
    }
}
