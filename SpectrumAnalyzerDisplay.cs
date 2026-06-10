using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace SpectrumViewerRT;

public sealed class SpectrumAnalyzerDisplay : FrameworkElement
{
    private double[] _levels = Array.Empty<double>();
    private double[] _holds = Array.Empty<double>();
    private double[] _rightLevels = Array.Empty<double>();
    private double[] _rightHolds = Array.Empty<double>();
    private bool _stereo;
    private readonly DotMatrixVfdRasterizer _dotRasterizer = new();

    public int ColorTheme { get; set; }
    public int MeterStyle { get; set; }
    public bool ShowUnlitSegments { get; set; } = true;
    public bool GlowEnabled { get; set; } = true;
    public bool TextureEnabled { get; set; } = true;
    public double MaximumBandWidth { get; set; } = Defaults.MonoAnalyzerMaxBandWidth;
    public double MaximumBandGap { get; set; } = Defaults.MonoAnalyzerMaxBandGap;

    public void Update(double[] levels, double[] holds, int colorTheme, int meterStyle, bool showUnlitSegments, bool glowEnabled, bool textureEnabled)
    {
        _levels = levels;
        _holds = holds;
        _rightLevels = Array.Empty<double>();
        _rightHolds = Array.Empty<double>();
        _stereo = false;
        ApplyVisualSettings(colorTheme, meterStyle, showUnlitSegments, glowEnabled, textureEnabled);
        InvalidateWhenVisualChanges();
    }

    public void UpdateStereo(double[] leftLevels, double[] leftHolds, double[] rightLevels, double[] rightHolds, int colorTheme, int meterStyle, bool showUnlitSegments, bool glowEnabled, bool textureEnabled)
    {
        _levels = leftLevels;
        _holds = leftHolds;
        _rightLevels = rightLevels;
        _rightHolds = rightHolds;
        _stereo = true;
        ApplyVisualSettings(colorTheme, meterStyle, showUnlitSegments, glowEnabled, textureEnabled);
        InvalidateWhenVisualChanges();
    }

    private void ApplyVisualSettings(int colorTheme, int meterStyle, bool showUnlitSegments, bool glowEnabled, bool textureEnabled)
    {
        ColorTheme = colorTheme;
        MeterStyle = meterStyle;
        ShowUnlitSegments = showUnlitSegments;
        GlowEnabled = glowEnabled;
        TextureEnabled = textureEnabled;
    }

    private void InvalidateWhenVisualChanges()
    {
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(3, 8, 10)), null, bounds);
        dc.DrawRectangle(new LinearGradientBrush(Color.FromArgb(70, 20, 80, 80), Colors.Transparent, 90), null, bounds);

        if (_levels.Length == 0 || ActualWidth < 40 || ActualHeight < 40)
            return;

        bool dotMatrix = MeterStyle is 2 or 3;
        if (dotMatrix)
            _dotRasterizer.Begin(ActualWidth, ActualHeight, VisualTreeHelper.GetDpi(this));

        if (_stereo && _rightLevels.Length > 0)
        {
            double gap = 24;
            double panelWidth = Math.Max(1, (ActualWidth - gap) / 2.0);
            DrawAnalyzerPanel(dc, _levels, _holds, new Rect(0, 0, panelWidth, ActualHeight), "L");
            DrawAnalyzerPanel(dc, _rightLevels, _rightHolds, new Rect(panelWidth + gap, 0, panelWidth, ActualHeight), "R");
            DrawDotMatrixLayer(dc, bounds, dotMatrix);
            DrawTexture(dc, bounds);
            return;
        }

        DrawAnalyzerPanel(dc, _levels, _holds, bounds, string.Empty);
        DrawDotMatrixLayer(dc, bounds, dotMatrix);
        DrawTexture(dc, bounds);
    }

    private void DrawDotMatrixLayer(DrawingContext dc, Rect bounds, bool enabled)
    {
        if (!enabled)
            return;

        var image = _dotRasterizer.Commit();
        if (image != null)
            dc.DrawImage(image, bounds);
    }

    private void DrawAnalyzerPanel(DrawingContext dc, double[] levels, double[] holds, Rect bounds, string label)
    {
        double left = bounds.Left + 44;
        double right = bounds.Right - 14;
        double top = string.IsNullOrEmpty(label) ? 16 : 30;
        double bottom = bounds.Bottom - 30;
        double width = Math.Max(1, right - left);
        double height = Math.Max(1, bottom - top);

        DrawDbGrid(dc, bounds.Left + 8, left, top, width, height);
        if (!string.IsNullOrEmpty(label))
            DrawTextCentered(dc, label, bounds.Left + bounds.Width / 2.0, 5, 14, ActiveColor());

        int bands = levels.Length;
        double gap = bands <= 1
            ? 0
            : Math.Clamp(width / Math.Max(1, bands * 8.0), 3, MaximumBandGap);
        double bandWidth = Math.Max(3, Math.Min(MaximumBandWidth, (width - gap * (bands - 1)) / bands));
        if (bands > 1 && bandWidth >= MaximumBandWidth)
            gap = Math.Clamp((width - bands * bandWidth) / (bands - 1), 3, MaximumBandGap);
        double usedWidth = Math.Min(width, bands * bandWidth + gap * (bands - 1));

        if (MeterStyle is 1 or 3)
        {
            DrawFineLineAnalyzer(dc, levels, holds, left, top, bottom, bandWidth, gap);
            DrawFrequencyLabels(dc, left, bottom + 7, usedWidth);
            return;
        }

        int rows = 22;
        double rowGap = 2;
        double rowHeight = Math.Max(2, (height - rowGap * (rows - 1)) / rows);

        for (int band = 0; band < bands; band++)
        {
            double x = left + band * (bandWidth + gap);
            double db = levels[band];
            double holdDb = band < holds.Length ? holds[band] : -90;
            int litRows = DbToRows(db, rows);
            int holdRow = Math.Clamp(DbToRows(holdDb, rows) - 1, 0, rows - 1);

            for (int row = 0; row < rows; row++)
            {
                double y = bottom - (row + 1) * rowHeight - row * rowGap;
                bool active = row < litRows;
                Color color = SegmentColor(RowToDb(row, rows), active);
                var rect = new Rect(x, y, bandWidth, rowHeight);
                DrawAnalyzerSegment(dc, rect, color, active);
            }

            if (holdDb > -60.0)
            {
                double holdY = bottom - (holdRow + 1) * rowHeight - holdRow * rowGap;
                var holdRect = new Rect(x, holdY, bandWidth, rowHeight);
                DrawAnalyzerSegment(dc, holdRect, SegmentColor(RowToDb(holdRow, rows), true), true);
            }
        }

        DrawFrequencyLabels(dc, left, bottom + 7, usedWidth);
    }

    private void DrawFineLineAnalyzer(
        DrawingContext dc,
        double[] levels,
        double[] holds,
        double left,
        double top,
        double bottom,
        double bandWidth,
        double bandGap)
    {
        var profile = FineLineVfdLayout.SpectrumAnalyzer;
        double dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        int topPixel = (int)Math.Ceiling(top * dpiScale);
        int bottomPixel = (int)Math.Floor(bottom * dpiScale) - 1;
        int blockCount = Math.Max(1,
            (bottomPixel - topPixel + profile.BlockPitchPixels) /
            profile.BlockPitchPixels);

        for (int band = 0; band < levels.Length; band++)
        {
            double x = left + band * (bandWidth + bandGap);
            int activeBlock = Math.Clamp(
                (int)Math.Floor(DbToNormalized(levels[band]) * blockCount),
                0,
                blockCount);
            double holdDb = band < holds.Length ? holds[band] : -90;
            int holdBlock = Math.Clamp(
                (int)Math.Round(DbToNormalized(holdDb) * (blockCount - 1)),
                0,
                blockCount - 1);

            dc.PushClip(new RectangleGeometry(new Rect(x, top, bandWidth, bottom - top)));
            for (int block = 0; block < blockCount; block++)
            {
                double blockPosition = blockCount == 1 ? 0 : block / (double)(blockCount - 1);
                double blockDb = -60 + blockPosition * 74;
                bool active = block < activeBlock || holdDb > -60.0 && block == holdBlock;
                Color color = SegmentColor(blockDb, active);
                int blockBottomPixel = bottomPixel - block * profile.BlockPitchPixels;

                for (int line = 0; line < profile.LinesPerBlock; line++)
                {
                    int pixelY = blockBottomPixel - line * profile.LinePitchPixels;
                    if (pixelY < topPixel)
                        break;

                    DrawDevicePixelRow(
                        dc,
                        x,
                        pixelY / dpiScale,
                        bandWidth,
                        profile.LineThicknessPixels / dpiScale,
                        color,
                        active);
                }
            }
            dc.Pop();
        }
    }

    private void DrawDevicePixelRow(
        DrawingContext dc,
        double x,
        double y,
        double width,
        double pixelHeight,
        Color color,
        bool active)
    {
        if (MeterStyle == 3)
        {
            DrawDotMatrixRow(dc, x, y, width, pixelHeight, color, active);
            return;
        }

        if (active && GlowEnabled)
        {
            dc.DrawRectangle(
                new SolidColorBrush(WithAlpha(color, 58)),
                null,
                new Rect(x, y - pixelHeight, width, pixelHeight * 3));
        }

        dc.DrawRectangle(new SolidColorBrush(color), null, new Rect(x, y, width, pixelHeight));
    }

    private void DrawDotMatrixRow(
        DrawingContext dc,
        double x,
        double y,
        double width,
        double pixelHeight,
        Color color,
        bool active)
    {
        _dotRasterizer.DrawDots(
            new Rect(x, y, width, pixelHeight),
            color,
            active && GlowEnabled);
    }

    private void DrawDbGrid(DrawingContext dc, double labelX, double left, double top, double width, double height)
    {
        foreach (double db in new[] { -60.0, -40.0, -20.0, 0.0, 12.0 })
        {
            double y = top + (1.0 - DbToNormalized(db)) * height;
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(38, 52, 62)), 1) { DashStyle = DashStyles.Dot }, new Point(left, y), new Point(left + width, y));
            DrawText(dc, db.ToString("0", CultureInfo.InvariantCulture), labelX, y - 7, 11, ActiveColor());
        }
    }

    private void DrawFrequencyLabels(DrawingContext dc, double left, double y, double width)
    {
        (double Frequency, string Label)[] labels = width switch
        {
            < 160 => new[] { (50.0, "50"), (1000.0, "1k"), (20000.0, "20k") },
            < 300 => new[] { (50.0, "50"), (200.0, "200"), (1000.0, "1k"), (5000.0, "5k"), (20000.0, "20k") },
            _ => new[]
            {
                (50.0, "50"), (100.0, "100"), (200.0, "200"), (500.0, "500"),
                (1000.0, "1k"), (2000.0, "2k"), (5000.0, "5k"), (10000.0, "10k"), (20000.0, "20k")
            }
        };

        const double minimumFrequency = 20.0;
        const double maximumFrequency = 20000.0;
        double logRange = Math.Log(maximumFrequency / minimumFrequency);
        foreach (var label in labels)
        {
            double position = Math.Log(label.Frequency / minimumFrequency) / logRange;
            double x = left + position * width;
            DrawTextCentered(dc, label.Label, x, y, 11, ActiveColor());
        }
    }

    private static int DbToRows(double db, int rows) => Math.Clamp((int)Math.Round(DbToNormalized(db) * rows), 0, rows);

    private static double RowToDb(int row, int rows) => -60.0 + row / Math.Max(1.0, rows - 1.0) * 74.0;

    private static double DbToNormalized(double db) => Math.Clamp((db + 60.0) / 74.0, 0, 1);

    private Color SegmentColor(double db, bool active)
    {
        if (!active)
            return ShowUnlitSegments ? InactiveColor() : Color.FromRgb(2, 7, 8);
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

    private void DrawAnalyzerSegment(DrawingContext dc, Rect rect, Color color, bool active)
    {
        if (MeterStyle == 2)
        {
            DrawDotMatrixBlock(dc, rect, color, active);
            return;
        }

        if (active && GlowEnabled)
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(WithAlpha(color, 36)), null, Inflate(rect, 3.0, 2.0), 2, 2);
            dc.DrawRoundedRectangle(new SolidColorBrush(WithAlpha(color, 70)), null, Inflate(rect, 1.4, 0.9), 1.5, 1.5);
        }

        dc.DrawRoundedRectangle(new SolidColorBrush(color), null, rect, 1, 1);
    }

    private void DrawDotMatrixBlock(DrawingContext dc, Rect rect, Color color, bool active)
    {
        _dotRasterizer.DrawDots(rect, color, active && GlowEnabled);
    }

    private Color InactiveColor()
    {
        var active = ActiveColor();
        return Color.FromRgb(
            (byte)Math.Clamp(active.R * 0.08, 3, 20),
            (byte)Math.Clamp(active.G * 0.11, 8, 34),
            (byte)Math.Clamp(active.B * 0.11, 8, 36));
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

        var horizontalPen = new Pen(new SolidColorBrush(Color.FromArgb(16, 190, 255, 235)), 1);
        for (double y = 1.5; y < bounds.Height; y += 3)
            dc.DrawLine(horizontalPen, new Point(bounds.Left, y), new Point(bounds.Right, y));

        var verticalPen = new Pen(new SolidColorBrush(Color.FromArgb(8, 210, 255, 245)), 1);
        for (double x = 4.5; x < bounds.Width; x += 13)
            dc.DrawLine(verticalPen, new Point(x, bounds.Top), new Point(x, bounds.Bottom));

        dc.DrawRectangle(new LinearGradientBrush(
            Color.FromArgb(30, 255, 255, 255),
            Color.FromArgb(0, 255, 255, 255),
            new Point(0, 0),
            new Point(0, 0.38)), null, bounds);
    }

    private void DrawText(DrawingContext dc, string text, double x, double y, double size, Color color)
    {
        var formatted = CreateFormattedText(text, size, color);

        if (GlowEnabled)
        {
            var glowText = CreateFormattedText(text, size, WithAlpha(color, 60));
            foreach (var offset in new[] { new Point(-1, 0), new Point(1, 0), new Point(0, -1), new Point(0, 1), new Point(-1, -1), new Point(1, 1) })
                dc.DrawText(glowText, new Point(x + offset.X, y + offset.Y));
        }

        dc.DrawText(formatted, new Point(x, y));
    }

    private void DrawTextCentered(DrawingContext dc, string text, double centerX, double y, double size, Color color)
    {
        var formatted = CreateFormattedText(text, size, color);
        double x = centerX - formatted.Width / 2.0;

        if (GlowEnabled)
        {
            var glowText = CreateFormattedText(text, size, WithAlpha(color, 60));
            foreach (var offset in new[] { new Point(-1, 0), new Point(1, 0), new Point(0, -1), new Point(0, 1), new Point(-1, -1), new Point(1, 1) })
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
            new SolidColorBrush(color),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
