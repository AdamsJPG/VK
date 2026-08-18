using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DataCompare.App;

/// <summary>
/// Renders the twin-cylinder silhouette (see <see cref="VkGeometry"/>) to a bitmap — a solid
/// front copy plus a lighter, offset "echo" copy behind it, for the window/taskbar icon.
/// </summary>
public static class VkIconFactory
{
    private static readonly Color FrontColor = AppTheme.AccentColor;
    private static readonly Color BackColor = Color.FromArgb(140, FrontColor.R, FrontColor.G, FrontColor.B);
    private static readonly Color SeamColor = Darken(FrontColor, 0.75);

    public static BitmapSource Render(int sizePixels)
    {
        var silhouette = VkGeometry.CreateCylinderSilhouette();
        var seams = VkGeometry.CreateSeamLines();
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var scale = sizePixels / 100.0;
            var offset = sizePixels * 0.12;
            // Thickness is in the same 100-unit geometry space as the silhouette/seams, not output
            // pixels — the ScaleTransform below converts it to screen size, same as the XAML paths.
            var seamPen = new Pen(new SolidColorBrush(SeamColor), 7);

            context.PushTransform(new TransformGroup
            {
                Children = { new ScaleTransform(scale, scale), new TranslateTransform(offset, offset) },
            });
            context.DrawGeometry(new SolidColorBrush(BackColor), null, silhouette);
            context.Pop();

            context.PushTransform(new ScaleTransform(scale, scale));
            context.DrawGeometry(new SolidColorBrush(FrontColor), null, silhouette);
            context.DrawGeometry(null, seamPen, seams);
            context.Pop();
        }

        var bitmap = new RenderTargetBitmap(sizePixels, sizePixels, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static Color Darken(Color color, double factor) => Color.FromRgb(
        (byte)(color.R * factor), (byte)(color.G * factor), (byte)(color.B * factor));
}
