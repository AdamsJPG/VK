using System.Windows.Media;

namespace DataCompare.App;

/// <summary>
/// Single source of truth for the app's accent color — the Target banner, splash screen mark, and
/// window/taskbar icon all derive from <see cref="AccentColor"/>. Change the hex value here to
/// re-theme the whole app in one place.
/// </summary>
public static class AppTheme
{
    public static readonly Color AccentColor = Color.FromRgb(0xE8, 0x79, 0x2A);

    public static readonly SolidColorBrush AccentBrush = CreateFrozenBrush(AccentColor);

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
