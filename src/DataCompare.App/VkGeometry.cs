using System.Windows;
using System.Windows.Media;

namespace DataCompare.App
{

    /// <summary>
    /// Builds the database-cylinder silhouette used everywhere VK shows its icon — the splash
    /// screen, the window/taskbar icon, and the generated .ico — so all three stay visually identical.
    /// </summary>
    public static class VkGeometry
    {
        private const double CapRadiusX = 45;
        private const double CapRadiusY = 18;
        private const double BodyTop = 20;
        private const double BodyBottom = 80;

        /// <summary>
        /// builds the frozen database-cylinder silhouette geometry, made up of a rectangular body with
        /// elliptical top and bottom caps.
        /// </summary>
        /// <returns>returns a System.Windows.Media.Geometry describing the cylinder silhouette, frozen for reuse.</returns>
        public static Geometry CreateCylinderSilhouette()
        {
            var body = new RectangleGeometry(new Rect(5, BodyTop, 90, BodyBottom - BodyTop));
            var topCap = new EllipseGeometry(new Point(50, BodyTop), CapRadiusX, CapRadiusY);
            var bottomCap = new EllipseGeometry(new Point(50, BodyBottom), CapRadiusX, CapRadiusY);
            var caps = new CombinedGeometry(GeometryCombineMode.Union, topCap, bottomCap);
            var cylinder = new CombinedGeometry(GeometryCombineMode.Union, body, caps);
            cylinder.Freeze();
            return cylinder;
        }

        /// <summary>
        /// The two "disk seam" lines that make the silhouette read as a stacked database cylinder
        /// rather than a plain capsule — the front-facing lower arc of an ellipse at each internal
        /// third of the body, meant to be stroked (not filled) over the silhouette.
        /// </summary>
        public static Geometry CreateSeamLines()
        {
            var geometry = new GeometryGroup();
            geometry.Children.Add(CreateSeamArc(BodyTop + ((BodyBottom - BodyTop) / 3.0)));
            geometry.Children.Add(CreateSeamArc(BodyTop + ((BodyBottom - BodyTop) * 2.0 / 3.0)));
            geometry.Freeze();
            return geometry;
        }

        /// <summary>
        /// builds a single stroked seam arc at the given vertical position, spanning the width of the
        /// cylinder's caps.
        /// </summary>
        /// <param name="y">a System.Double giving the vertical position, in the 100-unit geometry space, at which to draw the seam arc.</param>
        /// <returns>returns a System.Windows.Media.Geometry containing the path for the seam arc.</returns>
        private static Geometry CreateSeamArc(double y)
        {
            var figure = new PathFigure { StartPoint = new Point(50 - CapRadiusX, y) };
            figure.Segments.Add(new ArcSegment(
                point: new Point(50 + CapRadiusX, y),
                size: new Size(CapRadiusX, CapRadiusY),
                rotationAngle: 0,
                isLargeArc: false,
                sweepDirection: SweepDirection.Clockwise,
                isStroked: true));
            return new PathGeometry([figure]);
        }
    }
}
