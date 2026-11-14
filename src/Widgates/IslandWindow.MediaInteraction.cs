using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Widgates
{
    internal sealed partial class IslandWindow
    {
        private static Grid ApplicationIcon(MediaApplication application, double size = 24)
        {
            string geometry = application == MediaApplication.Spotify
                ? "M 2 4 Q 8 1 14 4 M 3 8 Q 8 5 13 8 M 4 12 Q 8 10 12 12"
                : application == MediaApplication.YouTube ? "M 4 2 L 12 2 Q 14 2 14 4 L 14 12 Q 14 14 12 14 L 4 14 Q 2 14 2 12 L 2 4 Q 2 2 4 2 Z M 6 5 L 10 8 L 6 11 Z"
                : "M 6 12 L 6 3 L 13 2 L 13 11 M 6 5 L 13 4 M 6 12 C 6 15 1 15 1 13 C 1 11 6 10 6 12 M 13 11 C 13 14 8 14 8 12 C 8 10 13 9 13 11";
            return SourceIcon(MediaAppIcon.Identity(application), geometry, size);
        }

        internal static Grid SourceIcon(string identity, string geometry = null, double size = 18)
        {
            var container = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
            var fallback = TransportIcon(geometry ?? "M 6 12 L 6 3 L 13 2 L 13 11 M 6 5 L 13 4 M 6 12 C 6 15 1 15 1 13 C 1 11 6 10 6 12 M 13 11 C 13 14 8 14 8 12 C 8 10 13 9 13 11");
            fallback.Width = fallback.Height = size;
            var image = new Image { Width = size, Height = size, Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
            container.Children.Add(fallback); container.Children.Add(image);
            bool requested = false;
            container.Loaded += async (_, __) =>
            {
                if (requested || string.IsNullOrEmpty(identity)) return;
                requested = true;
                try
                {
                    var bitmap = await MediaAppIcon.GetAsync(identity);
                    if (bitmap != null) { image.Source = bitmap; image.Visibility = Visibility.Visible; fallback.Visibility = Visibility.Collapsed; }
                }
                catch { /* Shell icon extraction is optional; retain the static glyph. */ }
            };
            return container;
        }

        private void BeginTrackSwipe(object sender, MouseButtonEventArgs e)
        {
            if (media.Active == null || media.Busy) return;
            trackSwipe.Begin(e.GetPosition(mediaApplicationButton));
            swipeSession = media.Active; swipeTrack = media.TrackIdentity;
        }

        private void MoveTrackSwipe(object sender, MouseEventArgs e)
        {
            if (!trackSwipe.Active) return;
            if (e.LeftButton != MouseButtonState.Pressed) { CancelTrackSwipe(); return; }
            trackSwipe.Move(e.GetPosition(mediaApplicationButton));
            if (!trackSwipe.Moved) return;
            e.Handled = true;
        }

        private async void EndTrackSwipe(object sender, MouseButtonEventArgs e)
        {
            if (!trackSwipe.Active) return;
            trackSwipe.Move(e.GetPosition(mediaApplicationButton));
            string command = trackSwipe.Command;
            bool moved = trackSwipe.Moved;
            var expected = swipeSession;
            bool unchanged = Equals(expected, media.Active) && swipeTrack == media.TrackIdentity;
            bool supported = command == "next" ? next.IsEnabled : command == "previous" && previous.IsEnabled;
            // A normal click keeps the existing cover/title action. Every drag consumes the click,
            // even when short, unsupported, cancelled or interrupted by a new source/track.
            if (!moved) { trackSwipe.Cancel(); swipeSession = null; swipeTrack = null; return; }
            e.Handled = true;
            CancelTrackSwipe();
            if (unchanged && supported && command != null) await media.CommandAsync(command, expected: expected);
        }

        private void CancelTrackSwipe()
        {
            bool active = trackSwipe.Active;
            trackSwipe.Cancel(); swipeSession = null; swipeTrack = null;
            if (!active) return;
            if (mediaApplicationButton.IsMouseCaptured) mediaApplicationButton.ReleaseMouseCapture();
            if (!closed && !shell.IsMouseOver) collapse.Start();
        }
    }
}
