using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Widgates
{
    // A clipped, fixed-width title. No timers; the animation exists only while visible and requested.
    internal sealed class MarqueeText : Grid
    {
        internal readonly TextBlock Primary;
        private readonly TextBlock duplicate;
        private readonly Canvas strip = new Canvas();
        private readonly TranslateTransform movement = new TranslateTransform();
        private bool active;
        private string current = "";
        internal bool Running { get; private set; }
        internal MarqueeText(double width)
        {
            Width = width; Height = 20; ClipToBounds = true; VerticalAlignment = VerticalAlignment.Center;
            PopupTheme.DelayTooltip(this);
            Primary = Create(); duplicate = Create();
            Canvas.SetTop(Primary, 2); Canvas.SetTop(duplicate, 2);
            strip.Children.Add(Primary); strip.Children.Add(duplicate);
            strip.RenderTransform = movement;
            Children.Add(strip);
            IsVisibleChanged += (_, __) => Refresh();
            SizeChanged += (_, __) => Refresh();
            Unloaded += (_, __) => Stop();
        }
        private static TextBlock Create()
        {
            return new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = ThemePalette.Text,
                TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
        }
        internal void SetText(string text)
        {
            if (current == text) return;
            current = text;
            string display = text.Length <= 512 ? text : text.Substring(0, 512) + "…";
            Primary.Text = duplicate.Text = display;
            ToolTip = text.Length <= 1024 ? text : text.Substring(0, 1024) + "…";
            Refresh();
        }
        internal void SetActive(bool value)
        {
            if (active == value) return;
            active = value; Refresh();
        }
        private void Refresh()
        {
            Stop();
            Primary.Measure(new Size(double.PositiveInfinity, 24));
            double distance = Primary.DesiredSize.Width + 28;
            Canvas.SetLeft(duplicate, distance);
            duplicate.Visibility = Visibility.Collapsed;
            if (!active || !IsVisible || !SystemParameters.ClientAreaAnimation || Primary.DesiredSize.Width <= Width) return;
            strip.CacheMode = new BitmapCache { EnableClearType = true };
            duplicate.Visibility = Visibility.Visible;
            double travel = distance / 24.0;
            var animation = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.2))));
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(-distance, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.2 + travel))));
            animation.Duration = TimeSpan.FromSeconds(1.2 + travel);
            Timeline.SetDesiredFrameRate(animation, 24);
            movement.BeginAnimation(TranslateTransform.XProperty, animation);
            Running = true;
        }
        private void Stop() { movement.BeginAnimation(TranslateTransform.XProperty, null); movement.X = 0; strip.CacheMode = null; Running = false; }
    }
}
