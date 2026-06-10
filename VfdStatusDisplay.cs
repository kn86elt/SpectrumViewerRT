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
        ['N'] = new[] { "10001", "11001", "11001", "10101", "10011", "10011", "10001" },
        ['P'] = new[] { "11110", "10001", "10001", "11110", "10000", "10000", "10000" },
        ['R'] = new[] { "11110", "10001", "10001", "11110", "10100", "10010", "10001" },
        ['V'] = new[] { "10001", "10001", "10001", "10001", "10001", "01010", "00100" },
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
        double verticalPadding = Math.Min(11, ActualHeight * 0.16);
        double textHeight = Math.Max(1, ActualHeight - verticalPadding * 2);
        if (DisplayStyle == 1)
            DrawSegmentText(dc, TimeText, horizontalPadding, verticalPadding, textHeight);
        else
            DrawDotText(dc, TimeText, horizontalPadding, verticalPadding, textHeight);

        if (TextureEnabled)
        {
            var texturePen = new Pen(new SolidColorBrush(Color.FromArgb(22, 0, 0, 0)), 1);
            for (double scanY = 2.5; scanY < ActualHeight; scanY += 3)
                dc.DrawLine(texturePen, new Point(1, scanY), new Point(Math.Max(1, ActualWidth - 1), scanY));
        }
    }

    private void DrawSegmentText(DrawingContext dc, string text, double x, double y, double height)
    {
        text = (text ?? string.Empty).ToUpperInvariant();
        double availableWidth = Math.Max(1, ActualWidth - x * 2 - 8);
        double baseTextWidth = Math.Max(1, text.Sum(character => character == ':' ? 6.0 : 14.0) + 12.0);
        double scale = Math.Min(height / 22.0, availableWidth / baseTextWidth);
        double charWidth = 11 * scale;
        double charGap = 3 * scale;
        double colonWidth = 3 * scale;
        double maxX = ActualWidth - x;
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

    private void DrawDotText(DrawingContext dc, string text, double x, double y, double height)
    {
        text = (text ?? string.Empty).ToUpperInvariant();
        double availableWidth = Math.Max(1, ActualWidth - x * 2 - 8);
        double widthUnits = Math.Max(1, text.Length * 7.46 - 1.18);
        double dotSize = Math.Min(height / 10.2, availableWidth / (widthUnits + 4.0));
        dotSize = Math.Max(0.15, dotSize);
        double dotGap = dotSize * 0.32;
        double charWidth = dotSize * 5 + dotGap * 4;
        double charGap = dotSize * 1.18;
        double maxX = ActualWidth - x;
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
        'E' => Mask(0, 1, 2, 4, 5, 6, 8, 9),
        'I' => Mask(0, 1, 8, 9, 14, 15),
        'L' => Mask(2, 6, 8, 9),
        'P' => Mask(0, 1, 2, 3, 4, 5, 6),
        'R' => Mask(0, 1, 2, 3, 4, 5, 6, 13),
        'V' => Mask(2, 6, 11, 12),
        'Y' => Mask(10, 11, 15),
        '-' => Mask(4, 5),
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
