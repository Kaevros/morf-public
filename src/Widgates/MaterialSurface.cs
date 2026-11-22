using System;
using System.Windows;
using System.Windows.Media;

namespace Widgates
{
    internal enum SurfaceStyle { Solid, SoftGlass, LiquidGlass }

    internal sealed class MaterialSurface
    {
        private string signature;
        internal Brush Background { get; private set; }
        internal Brush Edge { get; private set; }
        internal Brush Highlight { get; private set; }
        internal Brush InsetBackground { get; private set; }
        internal Brush InsetEdge { get; private set; }

        internal void Update(WidgetSettings settings, bool highContrast)
        {
            Color color = highContrast ? SystemColors.WindowColor : ThemePalette.Surface.Color;
            string next = settings.Style + ":" + settings.GlassStrength + ":" + settings.Opacity + ":" + settings.LiquidTransparency + ":" + highContrast + ":" + color
                + (highContrast ? SystemColors.WindowTextColor.ToString() : "");
            if (signature == next) return;
            signature = next;
            InsetBackground = null;
            InsetEdge = null;
            if (highContrast)
            {
                Background = SystemColors.WindowBrush;
                Edge = SystemColors.WindowTextBrush;
                Highlight = Brushes.Transparent;
                InsetBackground = Background;
                InsetEdge = Edge;
                return;
            }
            double strength = WidgetSettings.ClampStrength(settings.GlassStrength);
            double opacity = WidgetSettings.ClampOpacity(settings.Opacity);
            bool liquid = settings.Style == SurfaceStyle.LiquidGlass;
            if (settings.Style == SurfaceStyle.Solid)
            {
                Background = Freeze(new SolidColorBrush(color), opacity);
                Edge = Freeze(new SolidColorBrush(ThemePalette.Stroke.Color), opacity);
                Highlight = Brushes.Transparent;
                return;
            }
            color.A = liquid ? (byte)Math.Round(255 * (1 - WidgetSettings.ClampLiquidTransparency(settings.LiquidTransparency))) : (byte)(248 - 15 * strength);
            Background = Freeze(new SolidColorBrush(color), liquid ? 1 : opacity);
            if (liquid)
            {
                bool light = color.R + color.G + color.B > 384;
                var edge = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.8, 1) };
                edge.GradientStops.Add(new GradientStop(Color.FromArgb(160, 255, 255, 255), 0));
                edge.GradientStops.Add(new GradientStop(Color.FromArgb(28, 255, 255, 255), 0.42));
                edge.GradientStops.Add(new GradientStop(light ? Color.FromArgb(32, 0, 0, 0) : Color.FromArgb(12, 255, 255, 255), 0.62));
                edge.GradientStops.Add(new GradientStop(Color.FromArgb(90, 255, 255, 255), 1));
                Edge = Freeze(edge, 1);
                InsetBackground = Freeze(new SolidColorBrush(color), 0.58);
                InsetEdge = Freeze(edge.Clone(), 0.52);
                var lightFall = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                lightFall.GradientStops.Add(new GradientStop(Color.FromArgb(20, 255, 255, 255), 0));
                lightFall.GradientStops.Add(new GradientStop(Colors.Transparent, 0.45));
                lightFall.GradientStops.Add(new GradientStop(Color.FromArgb(4, 255, 255, 255), 1));
                Highlight = Freeze(lightFall, 1);
            }
            else
            {
                Edge = Freeze(new SolidColorBrush(ThemePalette.Stroke.Color), opacity);
                Highlight = Brushes.Transparent;
            }
        }

        private static Brush Freeze(Brush brush, double opacity)
        {
            brush.Opacity = opacity;
            brush.Freeze();
            return brush;
        }
    }
}
