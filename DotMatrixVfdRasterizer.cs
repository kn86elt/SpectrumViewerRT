using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SpectrumViewerRT;

internal sealed class DotMatrixVfdRasterizer
{
    private const int DotPitchPixels = 3;
    private WriteableBitmap? _bitmap;
    private int[] _pixels = Array.Empty<int>();
    private int _pixelWidth;
    private int _pixelHeight;
    private double _dpiScaleX = 1.0;
    private double _dpiScaleY = 1.0;

    public void Begin(double width, double height, DpiScale dpi)
    {
        _dpiScaleX = Math.Max(0.01, dpi.DpiScaleX);
        _dpiScaleY = Math.Max(0.01, dpi.DpiScaleY);
        int pixelWidth = Math.Max(1, (int)Math.Ceiling(width * _dpiScaleX));
        int pixelHeight = Math.Max(1, (int)Math.Ceiling(height * _dpiScaleY));

        if (_bitmap == null || pixelWidth != _pixelWidth || pixelHeight != _pixelHeight)
        {
            _pixelWidth = pixelWidth;
            _pixelHeight = pixelHeight;
            _pixels = new int[pixelWidth * pixelHeight];
            _bitmap = new WriteableBitmap(
                pixelWidth,
                pixelHeight,
                96.0 * _dpiScaleX,
                96.0 * _dpiScaleY,
                PixelFormats.Pbgra32,
                null);
            RenderOptions.SetBitmapScalingMode(_bitmap, BitmapScalingMode.NearestNeighbor);
        }
        else
        {
            Array.Clear(_pixels);
        }
    }

    public void DrawDots(Rect rect, Color color, bool glow)
    {
        if (_bitmap == null || rect.Width <= 0 || rect.Height <= 0)
            return;

        int left = Math.Clamp((int)Math.Ceiling(rect.Left * _dpiScaleX), 0, _pixelWidth);
        int top = Math.Clamp((int)Math.Ceiling(rect.Top * _dpiScaleY), 0, _pixelHeight);
        int right = Math.Clamp((int)Math.Floor(rect.Right * _dpiScaleX), 0, _pixelWidth);
        int bottom = Math.Clamp((int)Math.Floor(rect.Bottom * _dpiScaleY), 0, _pixelHeight);
        if (left < _pixelWidth)
            right = Math.Max(right, left + 1);
        if (top < _pixelHeight)
            bottom = Math.Max(bottom, top + 1);
        if (right <= left || bottom <= top)
            return;

        int glowPixel = PackPremultiplied(color, 48);
        int corePixel = PackPremultiplied(color, color.A);

        for (int y = top; y < bottom; y += DotPitchPixels)
        {
            for (int x = left; x < right; x += DotPitchPixels)
            {
                if (glow)
                    DrawGlow(x, y, glowPixel);
                BlendPixel(x, y, corePixel);
            }
        }
    }

    public ImageSource? Commit()
    {
        if (_bitmap == null)
            return null;

        _bitmap.WritePixels(
            new Int32Rect(0, 0, _pixelWidth, _pixelHeight),
            _pixels,
            _pixelWidth * sizeof(int),
            0);
        return _bitmap;
    }

    private void DrawGlow(int x, int y, int pixel)
    {
        BlendPixel(x - 1, y, pixel);
        BlendPixel(x + 1, y, pixel);
        BlendPixel(x, y - 1, pixel);
        BlendPixel(x, y + 1, pixel);
        BlendPixel(x - 1, y - 1, pixel);
        BlendPixel(x + 1, y - 1, pixel);
        BlendPixel(x - 1, y + 1, pixel);
        BlendPixel(x + 1, y + 1, pixel);
    }

    private void BlendPixel(int x, int y, int source)
    {
        if ((uint)x >= (uint)_pixelWidth || (uint)y >= (uint)_pixelHeight)
            return;

        int index = y * _pixelWidth + x;
        int destination = _pixels[index];
        int sourceAlpha = (int)((uint)source >> 24);
        if (sourceAlpha >= 255 || destination == 0)
        {
            _pixels[index] = source;
            return;
        }

        int inverseAlpha = 255 - sourceAlpha;
        int destinationAlpha = (int)((uint)destination >> 24);
        int alpha = sourceAlpha + (destinationAlpha * inverseAlpha + 127) / 255;
        int red = ((source >> 16) & 0xff) + (((destination >> 16) & 0xff) * inverseAlpha + 127) / 255;
        int green = ((source >> 8) & 0xff) + (((destination >> 8) & 0xff) * inverseAlpha + 127) / 255;
        int blue = (source & 0xff) + ((destination & 0xff) * inverseAlpha + 127) / 255;
        _pixels[index] =
            (Math.Min(255, alpha) << 24) |
            (Math.Min(255, red) << 16) |
            (Math.Min(255, green) << 8) |
            Math.Min(255, blue);
    }

    private static int PackPremultiplied(Color color, byte alpha)
    {
        int effectiveAlpha = alpha * color.A / 255;
        int red = color.R * effectiveAlpha / 255;
        int green = color.G * effectiveAlpha / 255;
        int blue = color.B * effectiveAlpha / 255;
        return (effectiveAlpha << 24) | (red << 16) | (green << 8) | blue;
    }
}
