using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace Widgates
{
    // Static WPF previews: no bitmap assets, effects or render clocks.
    internal static class SettingsChoices
    {
        internal static FrameworkElement Create(string name, string[] labels, int selected, Action<int> change, string preview = null)
        {
            string group = Guid.NewGuid().ToString("N");
            var row = new AdaptiveChoiceGrid { MaximumColumns = labels.Length, MinimumTileWidth = preview == null ? 64 : 110, Margin = new Thickness(0, 10, 0, 0) };
            AutomationProperties.SetName(row, name);
            for (int i = 0; i < labels.Length; i++)
            {
                int value = i;
                var content = new StackPanel();
                if (preview != null) content.Children.Add(new Viewbox { Child = Preview(preview, i), MaxWidth = 74, Height = 48, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly });
                content.Children.Add(new TextBlock { Text = labels[i], TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                    Foreground = ThemePalette.Text, FontSize = 12, Margin = new Thickness(2, preview == null ? 0 : 8, 2, 0) });
                var option = new RadioButton { Content = content, GroupName = group, IsChecked = i == selected, MinHeight = preview == null ? 32 : 103,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, i == labels.Length - 1 ? 0 : 7, 0), Cursor = System.Windows.Input.Cursors.Hand };
                AutomationProperties.SetName(option, name + " · " + labels[i].Replace("\n", " "));
                var template = new ControlTemplate(typeof(RadioButton));
                var border = new FrameworkElementFactory(typeof(Border)); border.Name = "Tile";
                border.SetValue(Border.BackgroundProperty, ThemePalette.Shelf); border.SetValue(Border.BorderBrushProperty, ThemePalette.Stroke);
                border.SetValue(Border.BorderThicknessProperty, new Thickness(1)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
                border.SetValue(Border.PaddingProperty, new Thickness(preview == null ? 7 : 10));
                border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter))); template.VisualTree = border;
                var active = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
                active.Setters.Add(new Setter(Border.BorderBrushProperty, ThemePalette.Accent, "Tile"));
                active.Setters.Add(new Setter(Border.BackgroundProperty, ThemePalette.AccentDark, "Tile")); template.Triggers.Add(active);
                foreach (var property in new[] { UIElement.IsMouseOverProperty, UIElement.IsKeyboardFocusWithinProperty })
                {
                    var trigger = new Trigger { Property = property, Value = true };
                    trigger.Setters.Add(new Setter(Border.BorderBrushProperty, ThemePalette.Accent, "Tile")); template.Triggers.Add(trigger);
                }
                option.Template = template;
                option.Checked += (_, __) => change(value);
                row.Children.Add(option);
            }
            return row;
        }

        private static FrameworkElement Preview(string kind, int index)
        {
            var canvas = new Canvas { Width = 74, Height = 48, HorizontalAlignment = HorizontalAlignment.Center, ClipToBounds = true };
            bool theme = kind == "theme";
            var back = new Border { Width = 74, Height = 48, CornerRadius = new CornerRadius(6), Background = theme
                ? Brush(index == 1 ? 0xEEEEF2 : 0x242426) : ThemePalette.Surface, BorderBrush = ThemePalette.Stroke, BorderThickness = new Thickness(1) };
            canvas.Children.Add(back);
            if (kind == "display")
            {
                bool floating = index == 3;
                var pill = new Border { Width = index == 0 || floating ? 48 : index == 1 ? 35 : 22, Height = index == 0 || floating ? 16 : 3,
                    CornerRadius = floating ? new CornerRadius(8) : new CornerRadius(0, 0, 6, 6), Background = ThemePalette.Accent, Opacity = index == 2 ? 0.35 : 1 };
                Canvas.SetLeft(pill, (74 - pill.Width) / 2); Canvas.SetTop(pill, floating ? 4 : 1); canvas.Children.Add(pill);
                return canvas;
            }
            if (theme && index == 0)
            {
                var half = new Border { Width = 36, Height = 46, Background = Brush(0xEEEEF2), CornerRadius = new CornerRadius(5, 0, 0, 5) };
                Canvas.SetLeft(half, 1); Canvas.SetTop(half, 1); canvas.Children.Add(half);
            }
            bool surface = kind == "surface";
            // A shared backdrop makes material transparency visible; text remains opaque.
            if (surface)
            {
                var backdrop = new Border { Width = 15, Height = 14, Background = ThemePalette.Accent, Opacity = 0.65 };
                Canvas.SetLeft(backdrop, 30); Canvas.SetTop(backdrop, 2); canvas.Children.Add(backdrop);
            }
            var fill = new SolidColorBrush { Opacity = index == 0 ? 1 : index == 1 ? 0.9 : 0.45 };
            BindingOperations.SetBinding(fill, SolidColorBrush.ColorProperty, new Binding("Color") { Source = ThemePalette.Surface });
            var capsule = new Border { Width = 43, Height = 18, CornerRadius = new CornerRadius(0, 0, 7, 7),
                Background = surface ? fill : Brush(index == 1 ? 0xBFC5D1 : 0x626874),
                BorderBrush = surface ? (index == 2 ? ThemePalette.GlassEdge : ThemePalette.Stroke) : Brushes.Transparent,
                BorderThickness = surface ? new Thickness(1, 0, 1, 1) : new Thickness(0) };
            if (surface)
            {
                var marks = new StackPanel { Margin = new Thickness(8, 5, 8, 3) };
                marks.Children.Add(new Border { Height = 2, Background = ThemePalette.Text, CornerRadius = new CornerRadius(1) });
                marks.Children.Add(new Border { Height = 2, Width = 16, HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 3, 0, 0), Background = ThemePalette.Muted, CornerRadius = new CornerRadius(1) });
                capsule.Child = marks;
            }
            Canvas.SetLeft(capsule, 16); Canvas.SetTop(capsule, 1); canvas.Children.Add(capsule);
            foreach (int y in new[] { 28, 34 })
            {
                var line = new Border { Width = y == 28 ? 48 : 32, Height = 3, CornerRadius = new CornerRadius(1.5), Background = ThemePalette.Muted, Opacity = 0.6 };
                Canvas.SetLeft(line, 12); Canvas.SetTop(line, y); canvas.Children.Add(line);
            }
            return canvas;
        }
        private static Brush Brush(int rgb) { var brush = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)); brush.Freeze(); return brush; }
    }
}
