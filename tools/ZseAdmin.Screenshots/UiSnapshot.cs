using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZseAdmin.Screenshots;

/// <summary>
/// Renderowanie realnego wizualnego drzewa WPF do PNG bez pokazywania okna.
/// <para>
/// W sesji 0 (GitHub Actions) nie ma interaktywnego pulpitu, ale <see cref="RenderTargetBitmap"/>
/// nie potrzebuje HWND: mierzy i układa ten sam XAML, a potem rasteryzuje go w zadanym DPI.
/// Renderowanie przy 120/144 DPI odpowiada skalowaniu systemowemu 125%/150% — układ pozostaje
/// w jednostkach DIP, rośnie tylko gęstość pikseli.
/// </para>
/// </summary>
internal static class UiSnapshot
{
    public static string Render(
        FrameworkElement root,
        int width,
        int height,
        double scale,
        string path)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfLessThan(scale, 1.0);

        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();

        var pixelWidth = (int)Math.Round(width * scale, MidpointRounding.AwayFromZero);
        var pixelHeight = (int)Math.Round(height * scale, MidpointRounding.AwayFromZero);
        var dpi = 96.0 * scale;

        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(root);

        if (bitmap.PixelWidth != pixelWidth || bitmap.PixelHeight != pixelHeight)
        {
            throw new InvalidOperationException(
                $"RenderTargetBitmap ma {bitmap.PixelWidth}×{bitmap.PixelHeight} px, " +
                $"oczekiwano {pixelWidth}×{pixelHeight} px.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(path);
        encoder.Save(stream);
        stream.Flush();

        return path;
    }

    /// <summary>Pierwszy element danego typu w drzewie wizualnym (null, jeśli go nie ma).</summary>
    public static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root is T match)
        {
            return match;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var found = FindDescendant<T>(VisualTreeHelper.GetChild(root, index));
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
