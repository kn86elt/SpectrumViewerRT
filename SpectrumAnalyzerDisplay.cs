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

    public int ColorTheme { get; set; }
    public int MeterStyle { get; set; }
    public bool ShowUnlitSegments { get; set; } = true;
    public bool GlowEnabled { get; set; } = true;
    public bool TextureEnabled { get; set; } = true;

    public void Update(double[] levels, double[] holds, int colorTheme, int meterStyle, bool showUnlitSegments, bool glowEnabled, bool textureEnabled)
    {
        _levels = levels;
        _holds = holds;
        _rightLevels = Array.Empty<double>();
        _rightHolds = Array.Empty<double>();
        _stereo = false;
        ApplyVisualSettings(colorTheme, meterStyle, showUnlitSegments, glowEnabled, textureEnabled);
        InvalidateVisual();
    }

    public void UpdateStereo(double[] leftLevels, double[] leftHolds, double[] rightLevels, double[] rightHolds, int colorTheme, int meterStyle, bool showUnlitSegments, bool glowEnabled, bool textureEnabled)
    {
        _levels = leftLevels;
        _holds = leftHolds;
        _rightLevels = rightLevels;
        _rightHolds = rightHolds;
        _stereo = true;
        ApplyVisualSettings(colorTheme, meterStyle, showUnlitSegments, glowEnabled, textureEnabled);
        InvalidateVisual();
    }

    private void ApplyVisualSettings(int colorTheme, int meterStyle, bool showUnlitSegments, bool glowEnabled, bool textureEnabled)
    {
        ColorTheme = colorTheme;
        MeterStyle = meterStyle;
        ShowUnlitSegments = showUnlitSegments;
        GlowEnabled = glowEnabled;
        TextureEnabled = textureEnabled;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(3, 8, 10)), null, bounds);
        dc.DrawRectangle(new LinearGradientBrush(Color.FromArgb(70, 20, 80, 80), Colors.Transparent, 90), null, bounds);

        if (_levels.Length == 0 || ActualWidth < 40 || ActualHeight < 40)
            return;

        if (_stereo && _rightLevels.Length > 0)
        {
            double gap = 24;
            double panelWidth = Math.Max(1, (ActualWidth - gap) / 2.0);
            DrawAnalyzerPanel(dc, _levels, _holds, new Rect(0, 0, panelWidth, ActualHeight), "L");
            DrawAnalyzerPanel(dc, _rightLevels, _rightHolds, new Rect(panelWidth + gap, 0, panelWidth - gap, ActualHeight), "R");
            DrawTexture(dc, bounds);
            return;
        }

        DrawAnalyzerPanel(dc, _levels, _holds, bounds, string.Empty);
        DrawTexture(dc, bounds);
    }

    private void DrawAnalyzerPanel(DrawingContext dc, double[] levels, double[] holds, Rect bounds, string label)
    {
        double left = bounds.Left + 44;
        double right = bounds.Right - 14;
        double top = 16;
        double bottom = bounds.Bottom - 30;
        double width = Math.Max(1, right - left);
        double height = Math.Max(1, bottom - top);

        DrawDbGrid(dc, left, top, width, height);
        if (!string.IsNullOrEmpty(label))
            DrawText(dc, label, bounds.Left + 14, top + 4, 14, Color.FromRgb(190, 226, 226));

        int bands = levels.Length;
        double gap = 3;
        double bandWidth = Math.Max(3, (width - gap * (bands - 1)) / bands);
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

            double holdY = bottom - (holdRow + 1) * rowHeight - holdRow * rowGap;
            var holdRect = new Rect(x, holdY, bandWidth, rowHeight);
            DrawAnalyzerSegment(dc, holdRect, SegmentColor(RowToDb(holdRow, rows), true), true);
        }

        DrawFrequencyLabels(dc, left, bottom + 7, width);
    }

    private void DrawDbGrid(DrawingContext dc, double left, double top, double width, double height)
    {
        foreach (double db in new[] { -60.0, -40.0, -20.0, 0.0, 12.0 })
        {
            double y = top + (1.0 - DbToNormalized(db)) * height;
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(38, 52, 62)), 1) { DashStyle = DashStyles.Dot }, new Point(left, y), new Point(left + width, y));
            DrawText(dc, db.ToString("0", CultureInfo.InvariantCulture), 8, y - 7, 11, Color.FromRgb(160, 235, 230));
        }
    }

    private void DrawFrequencyLabels(DrawingContext dc, double left, double y, double width)
    {
        string[] labels = { "50", "100", "200", "500", "1k", "2k", "5k", "10k", "20k" };
        for (int i = 0; i < labels.Length; i++)
        {
            double x = left + i / (double)(labels.Length - 1) * width;
            DrawText(dc, labels[i], x - 10, y, 11, Color.FromRgb(160, 235, 230));
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
        if (MeterStyle == 1)
        {
            DrawFineLineSegment(dc, rect, color, active);
            return;
        }

        if (active && GlowEnabled)
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(WithAlpha(color, 36)), null, Inflate(rect, 3.0, 2.0), 2, 2);
            dc.DrawRoundedRectangle(new SolidColorBrush(WithAlpha(color, 70)), null, Inflate(rect, 1.4, 0.9), 1.5, 1.5);
        }

        dc.DrawRoundedRectangle(new SolidColorBrush(color), null, rect, 1, 1);
    }

    private void DrawFineLineSegment(DrawingContext dc, Rect rect, Color color, bool active)
    {
        var brush = new SolidColorBrush(color);
        int lines = Math.Max(2, Math.Min(4, (int)Math.Floor(rect.Height / 2.0)));
        double lineHeight = 1.0;
        double innerHeight = Math.Max(0, rect.Height - lineHeight);
        var guidelines = new GuidelineSet();
        for (int i = 0; i < lines; i++)
        {
            double t = lines == 1 ? 0 : i / (double)(lines - 1);
            double y = Math.Round(rect.Top + t * innerHeight) + 0.5;
            guidelines.GuidelinesY.Add(y);
            guidelines.GuidelinesY.Add(y + lineHeight);
        }
        guidelines.GuidelinesX.Add(Math.Round(rect.Left) + 0.5);
        guidelines.GuidelinesX.Add(Math.Round(rect.Right) + 0.5);
        dc.PushGuidelineSet(guidelines);
        for (int i = 0; i < lines; i++)
        {
            double t = lines == 1 ? 0 : i / (double)(lines - 1);
            double y = Math.Round(rect.Top + t * innerHeight) + 0.5;
            if (active && GlowEnabled)
                dc.DrawRectangle(new SolidColorBrush(WithAlpha(color, 58)), null, new Rect(rect.Left - 1, y - 1.5, rect.Width + 2, 4.0));
            dc.DrawRectangle(brush, null, new Rect(rect.Left, y, rect.Width, lineHeight));
        }
        dc.Pop();
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
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Consolas"),
            size,
            new SolidColorBrush(color),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(formatted, new Point(x, y));
    }
}
