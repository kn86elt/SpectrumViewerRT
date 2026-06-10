using System.Windows;
using System.Windows.Media;

namespace SpectrumViewerRT;

public sealed class VfdStatusDisplay : FrameworkElement
{
    public static readonly DependencyProperty TimeTextProperty =
        DependencyProperty.Register(nameof(TimeText), typeof(string), typeof(VfdStatusDisplay),
            new FrameworkPropertyMetadata("LIVE 00:00", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DisplayStyleProperty =
        DependencyProperty.Register(nameof(DisplayStyle), typeof(int), typeof(VfdStatusDisplay),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SecondaryTextProperty =
        DependencyProperty.Register(nameof(SecondaryText), typeof(string), typeof(VfdStatusDisplay),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ColorThemeProperty =
        DependencyProperty.Register(nameof(ColorTheme), typeof(int), typeof(VfdStatusDisplay),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowUnlitSegmentsProperty =
        DependencyProperty.Register(nameof(ShowUnlitSegments), typeof(bool), typeof(VfdStatusDisplay),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GlowEnabledProperty =
        DependencyProperty.Register(nameof(GlowEnabled), typeof(bool), typeof(VfdStatusDisplay),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextureEnabledProperty =
        DependencyProperty.Register(nameof(TextureEnabled), typeof(bool), typeof(VfdStatusDisplay),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly IReadOnlyDictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
    {
        [' '] = new[] { "00000", "00000", "00000", "00000", "00000", "00000", "00000" },
        ['-'] = new[] { "00000", "00000", "00000", "11111", "00000", "00000", "00000" },
        ['.'] = new[] { "00000", "00000", "00000", "00000", "00000", "00110", "00110" },
        ['/'] = new[] { "00001", "00010", "00010", "00100", "01000", "01000", "10000" },
        [':'] = new[] { "00000", "00110", "00110", "00000", "00110", "00110", "00000" },
        ['0'] = new[] { "01110", "10001", "10011", "10101", "11001", "10001", "01110" },
        ['1'] = new[] { "00100", "01100", "00100", "00100", "00100", "00100", "01110" },
        ['2'] = new[] { "01110", "10001", "00001", "00010", "00100", "01000", "11111" },
        ['3'] = new[] { "11110", "00001", "00001", "01110", "00001", "00001", "11110" },
        ['4'] = new[] { "00010", "00110", "01010", "10010", "11111", "00010", "00010" },
        ['5'] = new[] { "11111", "10000", "10000", "11110", "00001", "00001", "11110" },
        ['6'] = new[] { "01110", "10000", "10000", "11110", "10001", "10001", "01110" },
        ['7'] = new[] { "11111", "00001", "00010", "00100", "01000", "01000", "01000" },
        ['8'] = new[] { "01110", "10001", "10001", "01110", "10001", "10001", "01110" },
        ['9'] = new[] { "01110", "10001", "10001", "01111", "00001", "00001", "01110" },
        ['A'] = new[] { "01110", "10001", "10001", "11111", "10001", "10001", "10001" },
        ['B'] = new[] { "11110", "10001", "10001", "11110", "10001", "10001", "11110" },
        ['C'] = new[] { "01111", "10000", "10000", "10000", "10000", "10000", "01111" },
        ['D'] = new[] { "11110", "10001", "10001", "10001", "10001", "10001", "11110" },
        ['E'] = new[] { "11111", "10000", "10000", "11110", "10000", "10000", "11111" },
        ['F'] = new[] { "11111", "10000", "10000", "11110", "10000", "10000", "10000" },
        ['I'] = new[] { "01110", "00100", "00100", "00100", "00100", "00100", "01110" },
        ['K'] = new[] { "10001", "10010", "10100", "11000", "10100", "10010", "10001" },
        ['L'] = new[] { "10000", "10000", "10000", "10000", "10000", "10000", "11111" },
        ['M'] = new[] { "10001", "11011", "10101", "10101", "10001", "10001", "10001" },
        ['N'] = new[] { "10001", "11001", "11001", "10101", "10011", "10011", "10001" },
        ['O'] = new[] { "01110", "10001", "10001", "10001", "10001", "10001", "01110" },
        ['P'] = new[] { "11110", "10001", "10001", "11110", "10000", "10000", "10000" },
        ['R'] = new[] { "11110", "10001", "10001", "11110", "10100", "10010", "10001" },
        ['S'] = new[] { "01111", "10000", "10000", "01110", "00001", "00001", "11110" },
        ['T'] = new[] { "11111", "00100", "00100", "00100", "00100", "00100", "00100" },
        ['U'] = new[] { "10001", "10001", "10001", "10001", "10001", "10001", "01110" },
        ['V'] = new[] { "10001", "10001", "10001", "10001", "10001", "01010", "00100" },
        ['W'] = new[] { "10001", "10001", "10001", "10101", "10101", "10101", "01010" },
        ['Y'] = new[] { "10001", "10001", "01010", "00100", "00100", "00100", "00100" }
    };

    private readonly struct SegmentLine
    {
        public SegmentLine(double x1, double y1, double x2, double y2)
        {
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
        }

        public double X1 { get; }
        public double Y1 { get; }
        public double X2 { get; }
        public double Y2 { get; }
    }

    private static readonly SegmentLine[] SegmentLines =
    {
        new(2, 1, 5, 1), new(6, 1, 9, 1),
        new(1, 2, 1, 9), new(10, 2, 10, 9),
        new(2, 10, 5, 10), new(6, 10, 9, 10),
        new(1, 11, 1, 18), new(10, 11, 10, 18),
        new(2, 19, 5, 19), new(6, 19, 9, 19),
        new(2, 2, 5, 9), new(9, 2, 6, 9),
        new(5, 11, 2, 18), new(6, 11, 9, 18),
        new(5.5, 2, 5.5, 9), new(5.5, 11, 5.5, 18)
    };

    public string TimeText
    {
        get => (string)GetValue(TimeTextProperty);
        set => SetValue(TimeTextProperty, value);
    }

    public int DisplayStyle
    {
        get => (int)GetValue(DisplayStyleProperty);
        set => SetValue(DisplayStyleProperty, value);
    }

    public string SecondaryText
    {
        get => (string)GetValue(SecondaryTextProperty);
        set => SetValue(SecondaryTextProperty, value);
    }

    public int ColorTheme
    {
        get => (int)GetValue(ColorThemeProperty);
        set => SetValue(ColorThemeProperty, value);
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
        double.IsInfinity(availableSize.Width) ? 210 : Math.Max(0, availableSize.Width),
        double.IsInfinity(availableSize.Height) ? 62 : Math.Max(0, availableSize.Height));

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRoundedRectangle(
            new SolidColorBrush(Color.FromRgb(4, 12, 14)),
            new Pen(new SolidColorBrush(Color.FromRgb(31, 57, 58)), 1),
            bounds, 3, 3);

        double horizontalPadding = Math.Min(15, ActualWidth * 0.07);
        bool hasSecondaryText = !string.IsNullOrWhiteSpace(SecondaryText);
        if (hasSecondaryText)
        {
            DrawClockLayout(dc, bounds, horizontalPadding);
        }
        else
        {
            double contentWidth = Math.Max(1, ActualWidth - horizontalPadding * 2);
            double clockTimeWidth = Math.Max(1, contentWidth * 0.68 - 5);
            double clockTimeHeight = Math.Max(1, ActualHeight - 16);
            if (DisplayStyle == 1)
            {
                double scale = Math.Min(
                    CalculateSegmentScale("88:88:88", clockTimeHeight, clockTimeWidth),
                    CalculateSegmentScale(TimeText, clockTimeHeight, contentWidth));
                double textWidth = SegmentTextWidth(TimeText, scale);
                double textHeight = 19 * scale;
                DrawSegmentText(
                    dc,
                    TimeText,
                    (ActualWidth - textWidth) / 2.0,
                    (ActualHeight - textHeight) / 2.0,
                    clockTimeHeight,
                    textWidth,
                    scale);
            }
            else
            {
                double dotSize = Math.Min(
                    CalculateDotSize("88:88:88", clockTimeHeight, clockTimeWidth),
                    CalculateDotSize(TimeText, clockTimeHeight, contentWidth));
                double textWidth = DotTextWidth(TimeText, dotSize);
                double textHeight = DotGlyphHeight(dotSize);
                DrawDotText(
                    dc,
                    TimeText,
                    (ActualWidth - textWidth) / 2.0,
                    (ActualHeight - textHeight) / 2.0,
                    clockTimeHeight,
                    textWidth,
                    dotSize);
            }
        }

        if (TextureEnabled)
        {
            var texturePen = new Pen(new SolidColorBrush(Color.FromArgb(22, 0, 0, 0)), 1);
            for (double scanY = 2.5; scanY < ActualHeight; scanY += 3)
                dc.DrawLine(texturePen, new Point(1, scanY), new Point(Math.Max(1, ActualWidth - 1), scanY));
        }
    }

    private void DrawClockLayout(DrawingContext dc, Rect bounds, double padding)
    {
        const double columnGap = 10;
        double contentWidth = Math.Max(1, bounds.Width - padding * 2);
        double timeWidth = Math.Max(1, contentWidth * 0.68 - columnGap / 2);
        double infoWidth = Math.Max(1, contentWidth - timeWidth - columnGap);
        double timeHeight = Math.Max(1, bounds.Height - 16);
        double infoX = padding + timeWidth + columnGap;
        string[] infoLines = SecondaryText.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        string dateText = infoLines.Length > 0 ? infoLines[0] : string.Empty;
        string weekdayText = infoLines.Length > 1 ? infoLines[1] : string.Empty;

        if (DisplayStyle == 1)
        {
            double timeScale = CalculateSegmentScale(TimeText, timeHeight, timeWidth);
            double timeGlyphHeight = 19 * timeScale;
            double infoGlyphHeight = Math.Max(1, (timeGlyphHeight - 2) / 2.0);
            double infoScale = Math.Min(
                CalculateSegmentScale(dateText, infoGlyphHeight + 3, infoWidth),
                infoGlyphHeight / 19.0);
            infoGlyphHeight = 19 * infoScale;
            double infoBlockHeight = infoGlyphHeight * 2 + 2;
            double timeTop = Math.Max(2, (bounds.Height - timeGlyphHeight) / 2.0);
            double infoTop = timeTop + Math.Max(0, (timeGlyphHeight - infoBlockHeight) / 2.0);
            DrawSegmentText(dc, TimeText, CenteredTextX(padding, timeWidth, SegmentTextWidth(TimeText, timeScale)), timeTop, timeHeight, timeWidth, timeScale);
            DrawSegmentText(dc, dateText, CenteredTextX(infoX, infoWidth, SegmentTextWidth(dateText, infoScale)), infoTop, infoGlyphHeight + 3, infoWidth, infoScale);
            DrawSegmentText(dc, weekdayText, CenteredTextX(infoX, infoWidth, SegmentTextWidth(weekdayText, infoScale)), infoTop + infoGlyphHeight + 2, infoGlyphHeight + 3, infoWidth, infoScale);
        }
        else
        {
            double timeDotSize = CalculateDotSize(TimeText, timeHeight, timeWidth);
            double timeGlyphHeight = DotGlyphHeight(timeDotSize);
            double targetInfoGlyphHeight = Math.Max(1, (timeGlyphHeight - 2) / 2.0);
            double infoDotSize = Math.Min(
                CalculateDotSize(dateText, targetInfoGlyphHeight + 2, infoWidth),
                targetInfoGlyphHeight / 8.92);
            double infoGlyphHeight = DotGlyphHeight(infoDotSize);
            double infoBlockHeight = infoGlyphHeight * 2 + 2;
            double timeTop = Math.Max(2, (bounds.Height - timeGlyphHeight) / 2.0);
            double infoTop = timeTop + Math.Max(0, (timeGlyphHeight - infoBlockHeight) / 2.0);
            DrawDotText(dc, TimeText, CenteredTextX(padding, timeWidth, DotTextWidth(TimeText, timeDotSize)), timeTop, timeHeight, timeWidth, timeDotSize);
            DrawDotText(dc, dateText, CenteredTextX(infoX, infoWidth, DotTextWidth(dateText, infoDotSize)), infoTop, targetInfoGlyphHeight + 2, infoWidth, infoDotSize);
            DrawDotText(dc, weekdayText, CenteredTextX(infoX, infoWidth, DotTextWidth(weekdayText, infoDotSize)), infoTop + infoGlyphHeight + 2, targetInfoGlyphHeight + 2, infoWidth, infoDotSize);
        }
    }

    private void DrawSegmentText(
        DrawingContext dc,
        string text,
        double x,
        double y,
        double height,
        double width,
        double maximumScale = double.PositiveInfinity)
    {
        text = (text ?? string.Empty).ToUpperInvariant();
        double availableWidth = Math.Max(1, width);
        double scale = Math.Min(CalculateSegmentScale(text, height, availableWidth), maximumScale);
        double charWidth = 11 * scale;
        double charGap = 3 * scale;
        double colonWidth = 3 * scale;
        double maxX = x + availableWidth;
        Color active = ActiveColor();
        Color inactive = InactiveColor();

        foreach (char character in text)
        {
            double currentWidth = character == ':' ? colonWidth : charWidth;
            if (x + currentWidth > maxX)
                break;

            if (character == ':')
            {
                DrawColon(dc, x, y, colonWidth, scale, active);
                x += colonWidth + charGap;
                continue;
            }

            ushort mask = SegmentMask(character);
            for (int segment = 0; segment < SegmentLines.Length; segment++)
            {
                bool lit = (mask & (1 << segment)) != 0;
                if (!lit && !ShowUnlitSegments)
                    continue;

                var line = SegmentLines[segment];
                var start = new Point(x + line.X1 * scale, y + line.Y1 * scale);
                var end = new Point(x + line.X2 * scale, y + line.Y2 * scale);
                Color color = lit ? active : inactive;
                if (lit && GlowEnabled)
                {
                    dc.DrawLine(CreateSegmentPen(WithAlpha(active, 28), 5.4 * scale), start, end);
                    dc.DrawLine(CreateSegmentPen(WithAlpha(active, 78), 3.0 * scale), start, end);
                }
                dc.DrawLine(CreateSegmentPen(color, Math.Max(1, 1.35 * scale)), start, end);
            }

            x += charWidth + charGap;
        }
    }

    private static double CalculateSegmentScale(string text, double height, double width)
    {
        double baseTextWidth = Math.Max(1, (text ?? string.Empty).Sum(character => character == ':' ? 6.0 : 14.0) + 12.0);
        return Math.Min(height / 22.0, Math.Max(1, width) / baseTextWidth);
    }

    private static double SegmentTextWidth(string text, double scale)
    {
        text = (text ?? string.Empty).ToUpperInvariant();
        if (text.Length == 0)
            return 0;

        double width = text.Sum(character => character == ':' ? 3.0 : 11.0) * scale;
        return width + (text.Length - 1) * 3 * scale;
    }

    private void DrawDotText(
        DrawingContext dc,
        string text,
        double x,
        double y,
        double height,
        double width,
        double maximumDotSize = double.PositiveInfinity)
    {
        text = (text ?? string.Empty).ToUpperInvariant();
        double availableWidth = Math.Max(1, width);
        double dotSize = Math.Min(CalculateDotSize(text, height, availableWidth), maximumDotSize);
        double dotGap = dotSize * 0.32;
        double charWidth = dotSize * 5 + dotGap * 4;
        double charGap = dotSize * 1.18;
        double maxX = x + availableWidth;
        Color active = ActiveColor();
        Color inactive = InactiveColor();

        foreach (char character in text)
        {
            if (x + charWidth > maxX)
                break;

            var glyph = Glyphs.TryGetValue(character, out var value) ? value : Glyphs[' '];
            for (int row = 0; row < 7; row++)
            {
                for (int column = 0; column < 5; column++)
                {
                    bool lit = glyph[row][column] == '1';
                    if (!lit && !ShowUnlitSegments)
                        continue;

                    var rect = new Rect(
                        x + column * (dotSize + dotGap),
                        y + row * (dotSize + dotGap),
                        dotSize,
                        dotSize);
                    Color color = lit ? active : inactive;
                    if (lit && GlowEnabled)
                    {
                        var glowRect = rect;
                        glowRect.Inflate(dotSize * 0.75, dotSize * 0.75);
                        dc.DrawRoundedRectangle(new SolidColorBrush(WithAlpha(active, 52)), null, glowRect, dotSize, dotSize);
                    }
                    dc.DrawRoundedRectangle(new SolidColorBrush(color), null, rect, dotSize * 0.35, dotSize * 0.35);
                }
            }
            x += charWidth + charGap;
        }
    }

    private static double CalculateDotSize(string text, double height, double width)
    {
        double widthUnits = Math.Max(1, (text ?? string.Empty).Length * 7.46 - 1.18);
        return Math.Max(0.15, Math.Min(height / 10.2, Math.Max(1, width) / (widthUnits + 4.0)));
    }

    private static double DotGlyphHeight(double dotSize) => dotSize * 8.92;

    private static double DotTextWidth(string text, double dotSize)
    {
        int length = (text ?? string.Empty).Length;
        if (length == 0)
            return 0;

        double dotGap = dotSize * 0.32;
        double charWidth = dotSize * 5 + dotGap * 4;
        double charGap = dotSize * 1.18;
        return length * charWidth + (length - 1) * charGap;
    }

    private static double CenteredTextX(double left, double width, double textWidth) =>
        left + Math.Max(0, (width - textWidth) / 2.0);

    private void DrawColon(DrawingContext dc, double x, double y, double width, double scale, Color color)
    {
        double dotSize = Math.Max(0.2, Math.Min(width, 1.7 * scale));
        double centerX = x + width / 2.0 - dotSize / 2.0;
        foreach (double centerY in new[] { y + 5.5 * scale, y + 14.5 * scale })
        {
            var rect = new Rect(centerX, centerY - dotSize / 2.0, dotSize, dotSize);
            if (GlowEnabled)
            {
                var glowRect = rect;
                glowRect.Inflate(dotSize, dotSize);
                dc.DrawEllipse(new SolidColorBrush(WithAlpha(color, 58)), null,
                    new Point(glowRect.Left + glowRect.Width / 2.0, glowRect.Top + glowRect.Height / 2.0),
                    glowRect.Width / 2.0, glowRect.Height / 2.0);
            }
            dc.DrawEllipse(new SolidColorBrush(color), null,
                new Point(rect.Left + rect.Width / 2.0, rect.Top + rect.Height / 2.0),
                rect.Width / 2.0, rect.Height / 2.0);
        }
    }

    private static ushort SegmentMask(char character) => character switch
    {
        '0' => Mask(0, 1, 2, 3, 6, 7, 8, 9),
        '1' => Mask(3, 7),
        '2' => Mask(0, 1, 3, 4, 5, 6, 8, 9),
        '3' => Mask(0, 1, 3, 4, 5, 7, 8, 9),
        '4' => Mask(2, 3, 4, 5, 7),
        '5' => Mask(0, 1, 2, 4, 5, 7, 8, 9),
        '6' => Mask(0, 1, 2, 4, 5, 6, 7, 8, 9),
        '7' => Mask(0, 1, 3, 7),
        '8' => Mask(0, 1, 2, 3, 4, 5, 6, 7, 8, 9),
        '9' => Mask(0, 1, 2, 3, 4, 5, 7, 8, 9),
        'A' => Mask(0, 1, 2, 3, 4, 5, 6, 7),
        'C' => Mask(0, 1, 2, 6, 8, 9),
        'D' => Mask(0, 1, 3, 7, 8, 9, 14, 15),
        'E' => Mask(0, 1, 2, 4, 5, 6, 8, 9),
        'F' => Mask(0, 1, 2, 4, 5, 6),
        'H' => Mask(2, 3, 4, 5, 6, 7),
        'I' => Mask(0, 1, 8, 9, 14, 15),
        'L' => Mask(2, 6, 8, 9),
        'M' => Mask(2, 3, 6, 7, 10, 11),
        'N' => Mask(2, 3, 6, 7, 10, 13),
        'O' => Mask(0, 1, 2, 3, 6, 7, 8, 9),
        'P' => Mask(0, 1, 2, 3, 4, 5, 6),
        'R' => Mask(0, 1, 2, 3, 4, 5, 6, 13),
        'S' => Mask(0, 1, 2, 4, 5, 7, 8, 9),
        'T' => Mask(0, 1, 14, 15),
        'U' => Mask(2, 3, 6, 7, 8, 9),
        'V' => Mask(2, 6, 11, 12),
        'W' => Mask(2, 3, 6, 7, 12, 13),
        'Y' => Mask(10, 11, 15),
        '-' => Mask(4, 5),
        '/' => Mask(11, 12),
        _ => 0
    };

    private static ushort Mask(params int[] segments)
    {
        ushort mask = 0;
        foreach (int segment in segments)
            mask |= (ushort)(1 << segment);
        return mask;
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
            (byte)Math.Clamp(active.R * 0.08, 3, 20),
            (byte)Math.Clamp(active.G * 0.11, 8, 34),
            (byte)Math.Clamp(active.B * 0.11, 8, 36));
    }

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Pen CreateSegmentPen(Color color, double thickness) => new(new SolidColorBrush(color), thickness)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round
    };
}
