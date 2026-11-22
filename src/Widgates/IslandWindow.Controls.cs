using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Data;
using Microsoft.Win32;
using PlaybackStatus = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;
using Session = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using Path = System.IO.Path;

namespace Widgates
{
    internal sealed partial class IslandWindow
    {
        private static readonly ControlTemplate NormalButtonTemplate = CreateButtonTemplate(false, false);
        private static readonly ControlTemplate SubtleButtonTemplate = CreateButtonTemplate(false, true);
        private static readonly ControlTemplate AccentButtonTemplate = CreateButtonTemplate(true, false);

        private static TextBlock Label(string text, double size, Brush color, bool bold = false)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = size,
                Foreground = color,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        private static System.Windows.Shapes.Path TransportIcon(string geometry)
        {
            return new System.Windows.Shapes.Path { Data = Geometry.Parse(geometry), Width = 16, Height = 16,
                Stroke = Muted, StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center };
        }

        private static Button CreateIconButton(
            Geometry iconGeometry,
            double iconSize,
            Brush iconBrush,
            Action action,
            string tooltip,
            string accessibleName,
            double width = 32,
            double height = 28,
            bool subtle = false)
        {
            var path = new System.Windows.Shapes.Path
            {
                Data = iconGeometry,
                Width = iconSize,
                Height = iconSize,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (iconBrush != null)
            {
                path.Fill = iconBrush;
                path.Stroke = iconBrush;
                path.StrokeThickness = 1.2;
            }

            return CreateCustomButton(path, action, tooltip, accessibleName, width, height, subtle: subtle);
        }

        private static Button CreateTextButton(string label, Action action, string tooltip, string accessibleName)
        {
            var text = Label(label, 12, ThemePalette.Text, true);
            return CreateCustomButton(text, action, tooltip, accessibleName, 110, 28);
        }

        private static Button CreateActionButton(string label, Action action, string tooltip, string accessibleName)
        {
            var text = Label(label, 12, ThemePalette.Text, true);
            var button = CreateCustomButton(text, action, tooltip, accessibleName, double.NaN, 28);
            button.Padding = new Thickness(12, 4, 12, 4);
            button.Margin = new Thickness(0, 0, 8, 0);
            return button;
        }

        private static Button CreateCustomButton(
            UIElement content,
            Action action,
            string tooltip,
            string accessibleName,
            double width,
            double height,
            bool accent = false,
            bool subtle = false)
        {
            var button = new Button
            {
                Content = content,
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
                Margin = new Thickness(2, 0, 2, 0)
            };
            if (!double.IsNaN(width)) button.Width = width;
            if (!double.IsNaN(height)) button.Height = height;

            button.Template = accent ? AccentButtonTemplate : subtle ? SubtleButtonTemplate : NormalButtonTemplate;
            ToolTipService.SetShowOnDisabled(button, true);
            PopupTheme.DelayTooltip(button);
            AutomationProperties.SetName(button, accessibleName);
            button.Click += (_, __) => action();
            return button;
        }

        private static ControlTemplate CreateButtonTemplate(bool accent, bool subtle)
        {
            Brush normalBg = accent ? ThemePalette.PlaySurface : (subtle ? Brushes.Transparent : ThemePalette.Button);
            Brush hoverBg = accent ? ThemePalette.AccentHover : ThemePalette.Hover;
            Brush pressedBg = accent ? ThemePalette.AccentHover : ThemePalette.Pressed;
            Brush normalBorder = Brushes.Transparent;
            Brush hoverBorder = subtle || accent ? Brushes.Transparent : ThemePalette.StrokeHover;

            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "BtnBorder";
            borderFactory.SetValue(Border.BackgroundProperty, normalBg);
            borderFactory.SetValue(Border.BorderBrushProperty, normalBorder);
            borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(accent ? 22 : 12));

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            borderFactory.AppendChild(presenter);

            template.VisualTree = borderFactory;

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, hoverBg, "BtnBorder"));
            hoverTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, hoverBorder, "BtnBorder"));
            template.Triggers.Add(hoverTrigger);

            var focusTrigger = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            focusTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, ThemePalette.Accent, "BtnBorder"));
            template.Triggers.Add(focusTrigger);

            var pressedTrigger = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pressedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, pressedBg, "BtnBorder"));
            template.Triggers.Add(pressedTrigger);

            var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4));
            template.Triggers.Add(disabledTrigger);

            return template;
        }

    }
}
