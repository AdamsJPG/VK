using System.Windows.Media;

namespace DataCompare.App
{

    /// <summary>
    /// Single source of truth for the app's accent color — the Target banner, splash screen mark, and
    /// window/taskbar icon all derive from <see cref="AccentColor"/>. Change the hex value here to
    /// re-theme the whole app in one place.
    /// </summary>
    public static class AppTheme
    {
        /// <summary>
        /// the accent color used throughout the app's UI and generated icon assets.
        /// </summary>
        public static readonly Color AccentColor = Color.FromRgb(0xE8, 0x79, 0x2A);

        /// <summary>
        /// a frozen brush built from <see cref="AccentColor"/>, ready for use in XAML and rendering code.
        /// </summary>
        public static readonly SolidColorBrush AccentBrush = CreateFrozenBrush(AccentColor);

        /// <summary>
        /// creates a frozen System.Windows.Media.SolidColorBrush for the given color, so it can be shared
        /// safely across threads and the visual tree without further modification.
        /// </summary>
        /// <param name="color">a System.Windows.Media.Color to build the brush from.</param>
        /// <returns>returns a System.Windows.Media.SolidColorBrush that has been frozen.</returns>
        private static SolidColorBrush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
