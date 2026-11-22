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
    internal sealed partial class IslandWindow : Window
    {
        private const double HostWidth = 420;
        private static readonly Brush Surface = ThemePalette.Surface;
        private static readonly Brush Accent = ThemePalette.Accent;
        private static readonly Brush Muted = ThemePalette.Muted;
        private static readonly Brush Stroke = ThemePalette.Stroke;
        private static readonly Brush StrokeHover = ThemePalette.StrokeHover;
        private static readonly Brush CardBg = ThemePalette.Card;
        private static readonly Brush ShelfBg = ThemePalette.Shelf;

        private readonly FileShelf shelf = new FileShelf();
        private readonly WidgetSettings settings;
        private readonly MaterialSurface material = new MaterialSurface();
        private readonly Border sheen;
        private static readonly Brush EdgeHitArea = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        private readonly MediaController media;
        private readonly DispatcherTimer collapse;
        private readonly Grid hoverSurface;
        private readonly Border topHoverGap;
        private readonly Border shell;
        private readonly Grid shellGrid;
        private readonly Grid compact;
        private readonly Grid compactMarkContainer;
        private readonly TextBlock compactClock;
        private readonly DispatcherTimer clockTimer;
        private readonly StackPanel panel;
        private readonly ScrollViewer panelViewport;
        private readonly Grid expandedLayout;
        private readonly Ellipse compactDot;
        private readonly Border compactCoverBorder;
        private readonly Image compactCoverImage;
        private readonly System.Windows.Shapes.Path compactMusicFallback;
        private readonly TextBlock compactTitle, compactCount;
        private readonly TextBlock compactArtist;
        private readonly MarqueeText compactMarquee;

        private readonly Border expandedCoverBorder;
        private readonly Image expandedCoverImage;
        private readonly System.Windows.Shapes.Path expandedMusicFallback;
        private readonly TextBlock mediaTitle, mediaArtist, mediaNotice;
        private readonly TextBlock shelfHeaderCount, shelfNotice;
        private readonly Button play, previous, next, retry, copy, remove, volumeButton;
        private readonly Button shuffle, repeat, restart, muteButton;
        private readonly System.Windows.Shapes.Path shuffleIcon, repeatIcon;
        private readonly Button mediaApplicationButton;
        private readonly StackPanel primaryControls, extraControls;
        private readonly TextBlock repeatBadge, volumePercent, elapsedLabel, durationLabel;
        private readonly Border volumePanel;
        private readonly StackPanel timelinePanel;
        private readonly StackPanel applicationPanel;
        private readonly Grid playbackControls;
        private readonly StackPanel playerFooter;
        private readonly MediaAppLauncher applicationLauncher = new MediaAppLauncher();
        private readonly ContextMenu applicationMenu;
        private readonly Button launchButton;
        private readonly TrackSwipe trackSwipe = new TrackSwipe();
        private Session swipeSession;
        private string swipeTrack;
        private object sessionSnapshot;
        private string sourceIconIdentity;
        private readonly Slider volumeSlider, seekSlider;
        private readonly DispatcherTimer progress;
        private readonly HoverIntent hover;
        private readonly SleepPauseTimer sleep;
        private readonly TrackPeek peek;
        private PowerStateMonitor power;
        private bool scrubbing;
        private bool? lastPlayGlyph;
        private Session seekSession;
        private readonly System.Windows.Shapes.Path muteGlyph;
        private int pendingVolumeDelta;
        private bool changingVolume;
        private readonly System.Windows.Shapes.Path playIcon;
        private readonly ComboBox sessions;
        private readonly Border shelfDropBorder;
        private readonly StackPanel shelfSection;
        private readonly Border dropHint;
        private readonly TextBlock dropHintText;
        private SettingsWindow settingsWindow;
        private readonly ListBox files;

        private DesktopPlacement placement;
        private readonly PanelState panelState = new PanelState();
        private bool expanded { get { return panelState.Expanded; } set { panelState.Expanded = value; } }
        private bool pinned { get { return panelState.Latched; } set { panelState.Latched = value; } }
        private bool draggingOut, syncingSessions, isDragOver, closed;
        private readonly Button pinButton;
        private Point? dragStart;
        private int dragVersion;
        private DockEdge dockEdge;
        private string settingsMessage = "";
        private bool SettingsOpen { get { return settingsWindow != null; } }
        private bool restoringDesktop;
        private string shortcutWarning = "";
        private double stateWidth = double.NaN, stateHeight = double.NaN;
        private bool stateExpanded;
        private int expansionRevision;

        internal IslandWindow() : this(WidgetSettings.Load(WidgetSettings.DefaultPath)) { }

        internal IslandWindow(WidgetSettings settings, string stopwatchPath = null)
        {
            this.settings = settings;
            this.stopwatchPath = stopwatchPath;
            savedStopwatchElapsed = stopwatchPath == null ? null : StopwatchState.Load(stopwatchPath);
            clocks = new ClockTools(savedStopwatchElapsed);
            if (clocks.StopwatchActive)
            {
                selectedClockTool = ClockTool.Stopwatch;
                clockSetupOpen = true;
            }
            Title = "Morf";
            Icon = BrandAssets.WindowIcon;
            Width = HostWidth;
            Height = 36;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            AllowsTransparency = true;
            Background = null;
            Topmost = true;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif");
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            // Native popups and menus must use the same palette as the island.
            Resources[SystemColors.WindowBrushKey] = CardBg;
            Resources[SystemColors.WindowTextBrushKey] = ThemePalette.Text;
            Resources[SystemColors.ControlBrushKey] = Surface;
            Resources[SystemColors.ControlTextBrushKey] = ThemePalette.Text;
            Resources[SystemColors.MenuBrushKey] = Surface;
            Resources[SystemColors.MenuTextBrushKey] = ThemePalette.Text;
            Resources[SystemColors.HighlightBrushKey] = ThemePalette.Hover;
            Resources[SystemColors.HighlightTextBrushKey] = ThemePalette.Text;
            PopupTheme.ApplyTooltips(Resources);
            PopupTheme.ApplyScrollBars(Resources);

            media = new MediaController(Dispatcher);
            hover = new HoverIntent(Dispatcher, () => SetExpanded(true), () => !closed && IsVisible && IsPointerOverIsland && panelState.CanHover(Suppressed, temporaryHide.Active));
            sleep = new SleepPauseTimer(Dispatcher, valid => media.PausePlayingAsync(valid));
            peek = new TrackPeek(Dispatcher);
            temporaryHide = new TemporaryHide(Dispatcher);
            temporaryHide.Changed += () => { if (!closed) { UpdateVisibility(); UpdateLiveVisuals(); } };
            media.AllowAutomaticResume = () => !closed && (power == null || power.State.RenderAllowed);
            sleep.Changed += () => { if (settingsWindow != null) settingsWindow.RefreshRuntimeStatus(); };
            progress = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(1) };
            progress.Tick += (_, __) => UpdateTimelineDisplay();
            clockTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
            clockTimer.Tick += (_, __) => { clockTimer.Stop(); UpdateLiveVisuals(); };
            collapse = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(180) };
            collapse.Tick += (_, __) =>
            {
                collapse.Stop();
                if (!IsPointerOverIsland && !pinned && !draggingOut && !sessions.IsDropDownOpen && !applicationMenu.IsOpen && !trackSwipe.Active && !messages.Active && !isDragOver && !SettingsOpen)
                    SetExpanded(false);
            };

            // Compact capsule.
            compact = new Grid
            {
                Height = 36,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            compact.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Dot / Cover
            compact.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Title
            compact.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Badge / Count
            compact.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Clock tools

            // Dot / Cover container
            compactMarkContainer = new Grid
            {
                Width = 22,
                Height = 22,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(9, 0, 8, 0)
            };

            compactDot = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = Muted,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            compactMarkContainer.Children.Add(compactDot);

            compactMusicFallback = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 12 3 L 12 11 A 3 3 0 1 1 9 8 L 9 4 L 5 5 L 5 13 A 3 3 0 1 1 2 10 L 2 3 Z"),
                Fill = Accent,
                Width = 12,
                Height = 12,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            compactMarkContainer.Children.Add(compactMusicFallback);

            compactCoverBorder = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(6),
                ClipToBounds = true,
                Visibility = Visibility.Collapsed,
                Background = CardBg
            };
            compactCoverImage = new Image { Stretch = Stretch.UniformToFill };
            compactCoverBorder.Child = compactCoverImage;
            compactMarkContainer.Children.Add(compactCoverBorder);

            Grid.SetColumn(compactMarkContainer, 0);
            compact.Children.Add(compactMarkContainer);

            compactMarquee = new MarqueeText(108) { Margin = new Thickness(0, 0, 9, 0) };
            compactTitle = compactMarquee.Primary;
            var compactDetails = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            compactDetails.Children.Add(compactMarquee);
            compactArtist = Label("", 11, Muted); compactArtist.TextTrimming = TextTrimming.CharacterEllipsis;
            compactArtist.Visibility = Visibility.Collapsed; compactDetails.Children.Add(compactArtist);
            Grid.SetColumn(compactDetails, 1); compact.Children.Add(compactDetails);

            compactCount = Label("", 11, Muted, true);
            compactCount.VerticalAlignment = VerticalAlignment.Center;
            compactCount.Margin = new Thickness(0, 0, 8, 0);
            compactCount.Visibility = Visibility.Collapsed;
            Grid.SetColumn(compactCount, 2);
            compact.Children.Add(compactCount);

            compactClock = Label("", 13, ThemePalette.Text, true);
            compactClock.HorizontalAlignment = HorizontalAlignment.Center;
            compactClock.VerticalAlignment = VerticalAlignment.Center;
            compactClock.IsHitTestVisible = false;
            Grid.SetColumnSpan(compactClock, 3);
            compact.Children.Add(compactClock);
            InitializeCompactClockTools();

            // Expanded player.
            panel = new StackPanel
            {
                Visibility = Visibility.Collapsed,
                Opacity = 0,
                RenderTransform = new TranslateTransform(),
                Margin = new Thickness(20, 14, 20, 14)
            };

            // Header
            var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 14) };
            var settingsButton = CreateIconButton(Geometry.Parse("M 10 2 L 14 2 L 14.6 5 L 16.4 6 L 19.2 5 L 21.2 8.4 L 19 10.4 L 19 13.6 L 21.2 15.6 L 19.2 19 L 16.4 18 L 14.6 19 L 14 22 L 10 22 L 9.4 19 L 7.6 18 L 4.8 19 L 2.8 15.6 L 5 13.6 L 5 10.4 L 2.8 8.4 L 4.8 5 L 7.6 6 L 9.4 5 Z M 15.5 12 A 3.5 3.5 0 1 1 8.5 12 A 3.5 3.5 0 1 1 15.5 12 Z"),
                16, ThemePalette.Text, OpenSettings, "Ayarlar", "Ayarları aç", 26, 24, true);
            var settingsGlyph = (System.Windows.Shapes.Path)settingsButton.Content;
            settingsGlyph.Fill = Brushes.Transparent; settingsGlyph.StrokeLineJoin = PenLineJoin.Round;
            DockPanel.SetDock(settingsButton, Dock.Right);
            header.Children.Add(settingsButton);
            pinButton = CreateIconButton(Geometry.Parse("M 5 2 L 11 2 L 10 7 L 13 10 L 9 10 L 8 15 L 7 10 L 3 10 L 6 7 Z"),
                14, Muted, TogglePersistent, "Paneli sabitle", "Paneli sabitle", 26, 24, true);
            DockPanel.SetDock(pinButton, Dock.Right); header.Children.Add(pinButton);
            var volumeGlyph = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 3 2 L 3 14 M 8 2 L 8 14 M 13 2 L 13 14 M 1 5 L 5 5 M 6 11 L 10 11 M 11 7 L 15 7"),
                Stroke = Muted, StrokeThickness = 1.4, Width = 14, Height = 14, Stretch = Stretch.Uniform
            };
            volumeButton = CreateCustomButton(volumeGlyph, OpenAudioMixer,
                null, "Ses karıştırıcısını aç · Uygulama sesleri ve ses çıkışları", 28, 28, subtle: true);
            DockPanel.SetDock(volumeButton, Dock.Right);
            header.Children.Add(volumeButton);
            applicationMenu = new ContextMenu { FontFamily = FontFamily, FontSize = 12, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            PopupTheme.ApplyMenu(applicationMenu);
            PopupTheme.ApplyTooltips(applicationMenu.Resources);
            var applicationStrip = new FrameworkElementFactory(typeof(StackPanel));
            applicationStrip.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            applicationMenu.ItemsPanel = new ItemsPanelTemplate(applicationStrip);
            foreach (MediaApplication application in Enum.GetValues(typeof(MediaApplication)))
            {
                MediaApplication selected = application;
                var item = new MenuItem { Header = ApplicationIcon(selected), ToolTip = MediaAppLauncher.Name(selected) + " aç" };
                AutomationProperties.SetName(item, MediaAppLauncher.Name(selected) + " aç");
                PopupTheme.DelayTooltip(item);
                item.Click += async (_, __) => await OpenMediaApplicationAsync(selected);
                applicationMenu.Items.Add(item);
            }
            applicationMenu.Items.Add(CreateClockMenuItem("Geri sayım ve kronometre", CountdownGlyph, OpenClockTools));
            applicationMenu.Opened += (_, __) => { collapse.Stop(); sessions.IsDropDownOpen = false; };
            applicationMenu.Closed += (_, __) => { if (!closed) collapse.Start(); };
            launchButton = CreateIconButton(Geometry.Parse("M 2 2 L 6 2 L 6 6 L 2 6 Z M 10 2 L 14 2 L 14 6 L 10 6 Z M 2 10 L 6 10 L 6 14 L 2 14 Z M 10 10 L 14 10 L 14 14 L 10 14 Z"),
                14, Muted, () => { applicationMenu.PlacementTarget = launchButton; applicationMenu.IsOpen = !applicationMenu.IsOpen; }, "Medya uygulaması aç", "Medya uygulaması aç", 28, 28, true);
            DockPanel.SetDock(launchButton, Dock.Right); header.Children.Add(launchButton);
            foreach (var button in new[] { settingsButton, pinButton, volumeButton, launchButton })
                button.Width = button.Height = 32;

            // Media session picker
            sessions = new ComboBox
            {
                Margin = new Thickness(-7, 0, 6, 0),
                MinHeight = 32,
                MaxWidth = 230,
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent,
                Foreground = Muted,
                BorderBrush = Stroke,
                BorderThickness = new Thickness(1),
                FontSize = 12,
                ToolTip = "Medya kaynağı seçimi"
            };
            AutomationProperties.SetName(sessions, "Medya kaynağı");
            StyleSessionPicker(sessions, true);
            sessions.SelectionChanged += (_, __) =>
            {
                if (!syncingSessions)
                    media.Select(sessions.SelectedIndex > 0 ? (Session)((ComboBoxItem)sessions.SelectedItem).Tag : null);
            };
            sessions.DropDownOpened += (_, __) => { collapse.Stop(); applicationMenu.IsOpen = false; };
            sessions.DropDownClosed += (_, __) => { UpdateSourcePicker(); collapse.Start(); };
            sessions.PreviewMouseWheel += (_, e) => { if (!sessions.IsDropDownOpen) e.Handled = true; };
            header.Children.Add(sessions);
            panel.Children.Add(header);
            panel.Children.Add(playerContent);

            // Now playing card
            var mediaCard = new Border
            {
                Background = Brushes.Transparent,
                Margin = new Thickness(0, 0, 0, 14)
            };
            var mediaCardGrid = new Grid();
            mediaCardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            mediaCardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            expandedCoverBorder = new Border
            {
                Width = 64,
                Height = 64,
                CornerRadius = new CornerRadius(14),
                ClipToBounds = true,
                Background = ShelfBg
            };
            var coverGrid = new Grid();
            expandedMusicFallback = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 15 3 L 15 13 A 4 4 0 1 1 11 9 L 11 5 L 6 6 L 6 15 A 4 4 0 1 1 2 11 L 2 3 Z"),
                Fill = Muted,
                Width = 26,
                Height = 26,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            coverGrid.Children.Add(expandedMusicFallback);
            expandedCoverImage = new Image { Stretch = Stretch.UniformToFill, Visibility = Visibility.Collapsed };
            RoundImage(expandedCoverImage, 14);
            RoundImage(compactCoverImage, 6);
            coverGrid.Children.Add(expandedCoverImage);
            expandedCoverBorder.Child = coverGrid;
            Grid.SetColumn(expandedCoverBorder, 0);
            mediaCardGrid.Children.Add(expandedCoverBorder);

            var mediaDetails = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            mediaTitle = Label("Medya bekleniyor", 16, ThemePalette.Text, true);
            mediaTitle.MaxWidth = 235;
            mediaArtist = Label("Hazır", 13, Muted);
            mediaArtist.MaxWidth = 235;
            mediaArtist.Margin = new Thickness(0, 4, 0, 0);
            mediaNotice = Label("", 11, Muted);
            mediaNotice.Visibility = Visibility.Collapsed;
            mediaNotice.MaxWidth = 235;
            mediaNotice.Margin = new Thickness(0, 2, 0, 0);
            mediaDetails.Children.Add(mediaTitle);
            mediaDetails.Children.Add(mediaArtist);
            mediaDetails.Children.Add(mediaNotice);
            Grid.SetColumn(mediaDetails, 1);
            mediaCardGrid.Children.Add(mediaDetails);
            mediaApplicationButton = CreateCustomButton(mediaCardGrid, async () => {
                MediaApplication application;
                if (MediaAppLauncher.TryIdentify(media.Active == null ? "" : media.Active.SourceAppUserModelId, out application)) await OpenMediaApplicationAsync(application);
            }, "Müzik uygulamasını aç", "Çalan müzik uygulamasını aç", double.NaN, double.NaN, subtle: true);
            mediaApplicationButton.Padding = new Thickness(0); mediaApplicationButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            var mediaTemplate = new ControlTemplate(typeof(Button));
            var mediaHitArea = new FrameworkElementFactory(typeof(Grid)); mediaHitArea.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
            var mediaContent = new FrameworkElementFactory(typeof(ContentPresenter));
            mediaContent.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            mediaContent.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            mediaHitArea.AppendChild(mediaContent); mediaTemplate.VisualTree = mediaHitArea;
            mediaApplicationButton.Template = mediaTemplate;
            mediaApplicationButton.ToolTip = null;
            ToolTipService.SetIsEnabled(mediaApplicationButton, false);
            mediaApplicationButton.PreviewMouseLeftButtonDown += BeginTrackSwipe;
            mediaApplicationButton.PreviewMouseMove += MoveTrackSwipe;
            mediaApplicationButton.PreviewMouseLeftButtonUp += EndTrackSwipe;
            mediaApplicationButton.LostMouseCapture += (_, __) => CancelTrackSwipe();
            mediaCard.Child = mediaApplicationButton;
            playerContent.Children.Add(mediaCard);

            applicationPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            var applicationRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (MediaApplication application in Enum.GetValues(typeof(MediaApplication)))
            {
                MediaApplication selected = application;
                var button = CreateCustomButton(ApplicationIcon(selected), async () => await OpenMediaApplicationAsync(selected), MediaAppLauncher.Name(selected) + " aç",
                    MediaAppLauncher.Name(selected) + " aç", 44, 40, subtle: true);
                button.Margin = new Thickness(6, 0, 6, 0);
                applicationRow.Children.Add(button);
            }
            applicationPanel.Children.Add(applicationRow); playerContent.Children.Add(applicationPanel);

            timelinePanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(2, 0, 2, 10) };
            seekSlider = new Slider { Minimum = 0, Maximum = 1, Height = 28, IsMoveToPointEnabled = true,
                SmallChange = 5, LargeChange = 10, ToolTip = "İstenen konuma git" };
            PopupTheme.ApplySlider(seekSlider);
            AutomationProperties.SetName(seekSlider, "Oynatma konumu");
            ToolTipService.SetShowOnDisabled(seekSlider, true);
            seekSlider.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((_, __) => BeginSeek()), true);
            seekSlider.PreviewMouseWheel += (_, e) => e.Handled = true;
            seekSlider.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(async (_, __) => await CommitSeekAsync()), true);
            seekSlider.PreviewKeyDown += (_, e) => { if (IsSliderKey(e.Key)) BeginSeek(); };
            seekSlider.PreviewKeyUp += async (_, e) => { if (IsSliderKey(e.Key)) await CommitSeekAsync(); };
            seekSlider.LostMouseCapture += async (_, __) => { if (scrubbing && Mouse.LeftButton == MouseButtonState.Released) await CommitSeekAsync(); };
            seekSlider.ValueChanged += (_, __) => { if (scrubbing) elapsedLabel.Text = MediaTimeline.Format(seekSlider.Value); };
            timelinePanel.Children.Add(seekSlider);
            var timeLabels = new DockPanel();
            durationLabel = Label("0:00", 11, Muted, true);
            elapsedLabel = Label("0:00", 11, Muted, true);
            DockPanel.SetDock(durationLabel, Dock.Right);
            timeLabels.Children.Add(durationLabel);
            timeLabels.Children.Add(elapsedLabel);
            timelinePanel.Children.Add(timeLabels);
            playerContent.Children.Add(timelinePanel);

            // Playback controls row
            var controls = playbackControls = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 2)
            };
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            previous = CreateIconButton(
                Geometry.Parse("M 5 5 L 7 5 L 7 15 L 5 15 Z M 17 5 L 8 10 L 17 15 Z"),
                18,
                ThemePalette.Text,
                async () => await media.CommandAsync("previous"),
                "Önceki parça",
                "Önceki parça",
                48,
                44,
                true);
            playIcon = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 0 0 L 12 8 L 0 16 Z"),
                Fill = ThemePalette.PlayInk,
                Width = 12,
                Height = 16,
                Stretch = Stretch.Uniform,
                RenderTransform = new TranslateTransform(1, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            play = CreateCustomButton(
                playIcon,
                async () => await media.CommandAsync("play"),
                "Oynat / Duraklat",
                "Oynat veya Duraklat",
                44,
                44,
                accent: true);
            next = CreateIconButton(
                Geometry.Parse("M 15 5 L 17 5 L 17 15 L 15 15 Z M 5 5 L 14 10 L 5 15 Z"),
                18,
                ThemePalette.Text,
                async () => await media.CommandAsync("next"),
                "Sonraki parça",
                "Sonraki parça",
                48,
                44,
                true);
            retry = CreateTextButton("Yeniden Bağlan", async () => await media.ConnectAsync(), "Windows medyasına yeniden bağlan", "Yeniden Bağlan");
            retry.Visibility = Visibility.Collapsed;

            shuffleIcon = TransportIcon("M 1 3 L 4 3 C 8 3 8 13 12 13 L 15 13 M 12 10 L 15 13 L 12 16 M 1 13 L 4 13 C 8 13 8 3 12 3 L 15 3 M 12 0 L 15 3 L 12 6");
            shuffle = CreateCustomButton(shuffleIcon, async () => await media.CommandAsync("shuffle"), "Karıştır", "Karıştırmayı değiştir", 30, 36, subtle: true);
            repeatIcon = TransportIcon("M 2 7 L 2 5 Q 2 2 5 2 L 14 2 M 11 0 L 14 2 L 11 4 M 14 9 L 14 11 Q 14 14 11 14 L 2 14 M 5 12 L 2 14 L 5 16");
            repeatBadge = Label("1", 8, Accent, true);
            repeatBadge.HorizontalAlignment = HorizontalAlignment.Center;
            repeatBadge.Visibility = Visibility.Collapsed;
            var repeatContent = new Grid(); repeatContent.Children.Add(repeatIcon); repeatContent.Children.Add(repeatBadge);
            repeat = CreateCustomButton(repeatContent, async () => await media.CommandAsync("repeat"), "Tekrarlama", "Tekrarlama modunu değiştir", 30, 36, subtle: true);
            restart = CreateCustomButton(TransportIcon("M 3 5 A 6 6 0 1 1 2 11 M 3 1 L 3 5 L 7 5"),
                async () => await media.CommandAsync("restart"), "Baştan oynat", "İçeriği baştan oynat", 30, 36, subtle: true);
            controls.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            controls.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            primaryControls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            primaryControls.Children.Add(previous); primaryControls.Children.Add(play); primaryControls.Children.Add(next);
            Grid.SetColumn(primaryControls, 1); controls.Children.Add(primaryControls);
            shuffle.HorizontalAlignment = HorizontalAlignment.Left;
            shuffle.VerticalAlignment = VerticalAlignment.Center;
            controls.Children.Add(shuffle);
            extraControls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            extraControls.Children.Add(repeat); extraControls.Children.Add(restart);
            Grid.SetColumn(extraControls, 2); controls.Children.Add(extraControls);
            foreach (var control in new UIElement[] { previous, play, next, shuffle, repeat, restart, seekSlider })
                ToolTipService.SetIsEnabled(control, false);
            var volumeControls = new DockPanel { LastChildFill = true };
            muteGlyph = TransportIcon("M 1 6 L 4 6 L 8 2 L 8 14 L 4 10 L 1 10 Z M 11 5 Q 15 8 11 11");
            muteButton = CreateCustomButton(muteGlyph, async () => await UseVolumeAsync(volume => volume.ToggleMuteAsync()),
                "Sesi kapat veya aç", "Windows sesini sessize al veya aç", 28, 26, subtle: true);
            DockPanel.SetDock(muteButton, Dock.Left); volumeControls.Children.Add(muteButton);
            volumePercent = Label("—", 11, Muted); volumePercent.Width = 40; volumePercent.TextAlignment = TextAlignment.Right;
            DockPanel.SetDock(volumePercent, Dock.Right); volumeControls.Children.Add(volumePercent);
            volumeSlider = new Slider { Minimum = 0, Maximum = 100, SmallChange = 2, LargeChange = 10, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
            PopupTheme.ApplySlider(volumeSlider);
            AutomationProperties.SetName(volumeSlider, "Windows ses düzeyi");
            volumeSlider.ValueChanged += (_, __) => volumePercent.Text = "%" + Math.Round(volumeSlider.Value);
            volumeSlider.PreviewMouseLeftButtonDown += (_, __) => volumeGestureTarget = CurrentVolumeTarget;
            volumeSlider.PreviewKeyDown += (_, e) => { if (IsSliderKey(e.Key)) volumeGestureTarget = CurrentVolumeTarget; };
            volumeSlider.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(async (_, __) => await CommitVolumeLevelAsync()), true);
            volumeSlider.PreviewKeyUp += async (_, e) => { if (IsSliderKey(e.Key)) await CommitVolumeLevelAsync(); };
            volumeControls.Children.Add(volumeSlider);
            var volumeContent = new StackPanel();
            volumeTargetLabel = Label("Sistem sesi", 10, Muted); volumeTargetLabel.TextWrapping = TextWrapping.Wrap; volumeTargetLabel.Visibility = Visibility.Collapsed;
            volumeTargetLabel.Margin = new Thickness(0, 4, 0, 0);
            volumeContent.Children.Add(volumeControls); volumeContent.Children.Add(volumeTargetLabel);
            volumePanel = new Border { Child = volumeContent, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Padding = new Thickness(0, 5, 0, 5), Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
            volumePanel.MouseEnter += async (_, __) => await UseVolumeAsync(volume => volume.ReadAsync());
            volumePanel.GotKeyboardFocus += async (_, __) => await UseVolumeAsync(volume => volume.ReadAsync());
            retry.HorizontalAlignment = HorizontalAlignment.Center;
            retry.Margin = new Thickness(0, 8, 0, 0);
            playerContent.Children.Add(retry);

            // Divider
            shelfSection = new StackPanel { Visibility = Visibility.Collapsed };
            panel.Children.Add(shelfSection);
            shelfSection.Children.Add(Divider());

            // The shelf exists visually only while it holds items.
            var shelfHeader = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 2, 0, 4) };
            shelfHeaderCount = Label("0 / 24 öğe", 11, Muted);
            DockPanel.SetDock(shelfHeaderCount, Dock.Right);
            shelfHeader.Children.Add(shelfHeaderCount);
            shelfHeader.Children.Add(Label("Dosyalar", 12, ThemePalette.Text, true));
            shelfSection.Children.Add(shelfHeader);

            shelfNotice = Label("Dosya veya klasörleri buraya bırakın.", 11, Muted);
            shelfNotice.Margin = new Thickness(0, 0, 0, 6);
            shelfSection.Children.Add(shelfNotice);

            // Shelf drop border & list
            shelfDropBorder = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = Stroke,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 0, 8)
            };
            files = new ListBox
            {
                Height = 114,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = ThemePalette.Text,
                SelectionMode = SelectionMode.Extended,
                FontSize = 12
            };
            AutomationProperties.SetName(files, "Dosya rafındaki öğeler");
            var horizontalItems = new FrameworkElementFactory(typeof(StackPanel));
            horizontalItems.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            files.ItemsPanel = new ItemsPanelTemplate(horizontalItems);
            ScrollViewer.SetHorizontalScrollBarVisibility(files, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(files, ScrollBarVisibility.Disabled);
            ScrollViewer.SetCanContentScroll(files, false);
            StyleShelfScrollBar(files);
            files.PreviewMouseWheel += (_, e) =>
            {
                var viewer = FindScrollViewer(files);
                if (viewer != null && viewer.ScrollableWidth > 0)
                {
                    viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset - e.Delta);
                    e.Handled = true;
                }
            };
            files.SelectionChanged += (_, __) => UpdateShelfButtons();
            files.PreviewMouseLeftButtonDown += StartFileDrag;
            files.PreviewMouseLeftButtonUp += (_, __) => dragStart = null;
            files.PreviewMouseMove += TryDragFiles;
            shelfDropBorder.Child = files;
            shelfSection.Children.Add(shelfDropBorder);

            // Shelf actions
            var shelfActions = new StackPanel { Orientation = Orientation.Horizontal };
            copy = CreateActionButton("Kopyala", CopyFiles, "Seçili dosyaları panoya kopyala (Ctrl+V)", "Seçili dosyaları panoya kopyala");
            remove = CreateActionButton("Raftan kaldır", RemoveFiles, "Seçili dosyaları raftan kaldır", "Seçili dosyaları raftan kaldır");
            shelfActions.Children.Add(copy);
            shelfActions.Children.Add(remove);
            shelfSection.Children.Add(shelfActions);
            InitializeShelfTools();

            dropHintText = Label("Dosya veya fotoğrafları rafa bırakın", 12, Accent, true);
            dropHintText.HorizontalAlignment = HorizontalAlignment.Center;
            dropHint = new Border
            {
                Child = dropHintText, Visibility = Visibility.Collapsed,
                Background = ShelfBg, BorderBrush = Accent, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16), Padding = new Thickness(10, 20, 10, 20),
                Margin = new Thickness(0, 8, 0, 0), IsHitTestVisible = false
            };
            panel.Children.Add(dropHint);

            // Shared shell for compact and expanded states.
            shellGrid = new Grid();
            shellGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            shellGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shellGrid.Children.Add(compact);
            expandedLayout = new Grid();
            expandedLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            panelViewport = new ScrollViewer { Content = panel, BorderThickness = new Thickness(0), Padding = new Thickness(0), VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            expandedLayout.Children.Add(panelViewport);
            playerFooter = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            playerFooter.Children.Add(playbackControls); playerFooter.Children.Add(volumePanel);
            clockPanel = CreateClockPanel();
            RenderClockPanel(ClockTools.GetTickCount64());
            playerFooter.Children.Add(clockPanel);
            // Keep the complete player together. Shelf growth starts below its controls.
            playerContent.Children.Add(playerFooter);
            BindingOperations.SetBinding(expandedLayout, UIElement.VisibilityProperty, new Binding("Visibility") { Source = panel });
            BindingOperations.SetBinding(expandedLayout, UIElement.OpacityProperty, new Binding("Opacity") { Source = panel });
            shellGrid.Children.Add(expandedLayout);
            InitializeNotifications(); shellGrid.Children.Add(messageView);
            sheen = new Border { IsHitTestVisible = false };
            shellGrid.Children.Add(sheen);

            shell = new Border
            {
                Child = shellGrid,
                Background = ThemePalette.Glass,
                BorderBrush = ThemePalette.GlassEdge,
                BorderThickness = DockLayout.Outline(dockEdge),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(0),
                AllowDrop = true,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Width = 112,
                Height = 36,
                ClipToBounds = true
            };
            topHoverGap = new Border
            {
                // A layered window needs nonzero alpha to receive pointer events in the gap.
                Background = EdgeHitArea, Visibility = Visibility.Collapsed,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top
            };
            topHoverGap.SetBinding(WidthProperty, new Binding("ActualWidth") { Source = shell });
            topHoverGap.SetBinding(HeightProperty, new Binding("Margin.Top") { Source = shell });
            hoverSurface = new Grid();
            hoverSurface.Children.Add(topHoverGap);
            hoverSurface.Children.Add(shell);
            Content = hoverSurface;

            // Drag and drop events on shell
            shell.PreviewDragEnter += OnDragOver;
            shell.PreviewDragOver += OnDragOver;
            shell.PreviewDragLeave += OnDragLeave;
            shell.PreviewDrop += OnDrop;
            shell.PreviewMouseWheel += OnVolumeWheel;

            // Moving between the shell and its top gap stays within this hover surface.
            hoverSurface.MouseEnter += (_, __) => OnShellMouseEnter();
            shell.MouseLeftButtonDown += (_, e) =>
            {
                if (!expanded)
                {
                    OpenPinned();
                    e.Handled = true;
                }
            };
            hoverSurface.MouseLeave += (_, __) =>
            {
                peek.Release();
                messages.Release();
                hover.Cancel();
                if (expanded && !pinned && !draggingOut && !sessions.IsDropDownOpen && !applicationMenu.IsOpen && !trackSwipe.Active && !messages.Active && !isDragOver && !SettingsOpen)
                    collapse.Start();
            };

            Deactivated += (_, __) =>
            {
                if (closed) return;
                CancelTrackSwipe();
                if (pinned && !panelState.Persistent && !draggingOut && !sessions.IsDropDownOpen && !applicationMenu.IsOpen && !SettingsOpen)
                    ClosePanel();
            };
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && shelf.CanUndo && !(Keyboard.FocusedElement is TextBox)) { UndoShelfRemoval(); e.Handled = true; }
                else if (e.Key == Key.Escape && messages.Active) { messages.Cancel(); e.Handled = true; }
                else if (e.Key == Key.Escape && trackSwipe.Active)
                {
                    CancelTrackSwipe(); mediaApplicationButton.ReleaseMouseCapture(); e.Handled = true;
                }
                else if (e.Key == Key.Escape && !sessions.IsDropDownOpen && !applicationMenu.IsOpen)
                {
                    ClosePanel();
                    e.Handled = true;
                }
            };

            shell.ContextMenuOpening += (_, e) => { if (!(e.Source is TextBox)) { e.Handled = true; OpenSettings(); } };
            IsVisibleChanged += (_, __) => { if (!IsVisible) { applicationMenu.IsOpen = false; CancelTrackSwipe(); if (mixerWindow != null) mixerWindow.Close(); } };
            shell.MouseRightButtonUp += (_, e) => { if (!clockPanel.IsMouseOver) { e.Handled = true; OpenSettings(); } };
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            SystemEvents.TimeChanged += OnClockTimeChanged;
            SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;

            SourceInitialized += (_, __) => InitializePlacement();
            SizeChanged += (_, __) => { if (placement != null) placement.Reposition(); };
            Loaded += async (_, __) =>
            {
                InitializePlacement();
                if (placement != null)
                    placement.Reposition();
                UpdateVisibility();
                await media.ConnectAsync();
            };
            Closing += (_, e) =>
            {
                if (shellServices && !exitConfirmed) { e.Cancel = true; Dispatcher.BeginInvoke(new Action(RequestExit)); return; }
                SaveStopwatch(ClockTools.GetTickCount64());
                closed = true;
                if (mixerWindow != null) mixerWindow.Close(); audioMirror.Dispose();
                if (bannerRouting != null) bannerRouting.Dispose();
                appNotifications.Dispose(); messages.Dispose();
                applicationMenu.IsOpen = false; CancelTrackSwipe();
                temporaryHide.Dispose(); if (tray != null) tray.Dispose();
                CloseSettings();
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                SystemEvents.TimeChanged -= OnClockTimeChanged;
                SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
                collapse.Stop();
                progress.Stop();
                clockTimer.Stop();
                clockToolsTick.Stop();
                hover.Dispose(); sleep.Dispose(); peek.Dispose(); if (power != null) power.Dispose();
                compactMarquee.SetActive(false);
                media.Dispose();
                if (placement != null)
                    placement.Dispose();
            };

            media.Changed += UpdateMedia;
            peek.Changed += () => { if (!closed) { if (peek.Active) lastMediaPreviewIdentity = media.TrackIdentity; UpdateCompactContent(); UpdateState(true); } };
            media.TimelineChanged += () =>
            {
                if (!expanded || !IsVisible) return;
                bool wasVisible = timelinePanel.Visibility == Visibility.Visible;
                UpdateTimelineDisplay();
                if (wasVisible != (timelinePanel.Visibility == Visibility.Visible)) UpdateState(false);
            };
            IsVisibleChanged += (_, __) => UpdateLiveVisuals();
            UpdateMedia();
            UpdateShelf();
            ApplySettings(false);
        }

        private void InitializePlacement()
        {
            if (placement != null)
                return;
            var source = PresentationSource.FromVisual(this) as HwndSource;
            if (source == null || source.Handle == IntPtr.Zero)
            {
                shortcutWarning = "Kısayol bu oturumda kullanılamıyor.";
                ShowSettingsNotice("");
                return;
            }
            placement = new DesktopPlacement(this, source, settings.HideInFullscreen);
            placement.Preference = settings.Dock;
            placement.FollowDesktops = settings.FollowDesktops;
            placement.MonitorDevice = settings.MonitorDevice;
            placement.ConfigureRules(settings.AppRules);
            placement.ConfigureHotkeys(settings.Hotkeys);
            placement.MediaHotkeyPressed += async command => await media.CommandAsync(command);
            power = new PowerStateMonitor(source);
            power.Changed += () =>
            {
                if (closed) return;
                if (!power.State.RenderAllowed) { media.CancelAutomaticReturn(); peek.Cancel(false); UpdateCompactContent(); if (mixerWindow != null) mixerWindow.Close(); }
                sleep.CheckDue(); UpdateState(false);
            };
            placement.HotkeyPressed += async () =>
            {
                if (restoringDesktop || closed) return;
                temporaryHide.Cancel();
                if (settingsWindow != null) { settingsWindow.Activate(); return; }
                if (Suppressed) return;
                restoringDesktop = true;
                try
                {
                    if (await placement.EnsureCurrentDesktopAsync() && !closed) TogglePinned();
                }
                finally { restoringDesktop = false; }
            };
            placement.FullscreenChanged += UpdateVisibility;
            placement.DesktopChanged += OnDesktopChanged;
            placement.DockChanged += () =>
            {
                dockEdge = placement.Edge;
                UpdateState(false);
            };
            placement.DisplayGeometryChanged += OnDisplayGeometryChanged;
            placement.Reposition();
            if (!placement.HotkeyRegistered)
            {
                shortcutWarning = "Panel kısayolu kaydedilemedi. Kenara tıklayarak açabilirsiniz. " + placement.HotkeyFailure;
                ShowSettingsNotice("");
            }
        }

        private void OnDisplayGeometryChanged()
        {
            if (closed) return;
            hover.Cancel(); sessions.IsDropDownOpen = false; applicationMenu.IsOpen = false;
            Width = Math.Min(HostWidth, placement.AvailableSize.Width);
            SizeToContent = SizeToContent.Height;
            UpdateState(false); InvalidateMeasure(); UpdateLayout();
        }

        private void TogglePinned()
        {
            if (pinned) { ClosePanel(); return; }
            OpenPinned();
        }

        private void OpenPinned()
        {
            if (SettingsOpen) { settingsWindow.Activate(); return; }
            if (Suppressed) return;
            hover.Cancel();
            pinned = true;
            UpdateVisibility();
            SetExpanded(true);
            Activate();
            sessions.Focus();
        }

        private void ClosePanel()
        {
            messages.Cancel(false);
            peek.Cancel();
            hover.Cancel();
            pinned = false;
            panelState.Persistent = false; RefreshPin();
            SetExpanded(false);
            UpdateVisibility();
        }

        private void UpdateVisibility()
        {
            if (closed) return;
            if (messages.Active && !MessageAllowed) messages.Cancel(false);
            if (placement == null)
                return;
            if (temporaryHide.Active) { peek.Cancel(false); UpdateState(false); Hide(); return; }
            if (!placement.OnCurrentDesktop) return;
            if (Suppressed)
            {
                peek.Cancel(false); UpdateCompactContent(); UpdateState(false);
                pinned = false;
                collapse.Stop();
                sessions.IsDropDownOpen = false;
                if (!placement.OwnForeground) CloseSettings();
                SetExpanded(false);
                Hide();
                return;
            }
            if (panelState.Persistent && !SettingsOpen && !Suppressed) { pinned = true; SetExpanded(true); }
            if (settings.HideInFullscreen && !placement.RuleShown && !pinned && !draggingOut && !isDragOver && !SettingsOpen && placement.Fullscreen)
            {
                peek.Cancel(false); UpdateCompactContent(); UpdateState(false);
                SetExpanded(false);
                Hide();
            }
            else if (!IsVisible)
            {
                Show();
                placement.Reposition();
            }
            placement.EnsureTopmost();
        }

        private void OnDesktopChanged()
        {
            if (closed) return;
            messages.Cancel(false);
            peek.Cancel(false); UpdateCompactContent(); UpdateState(false);
            pinned = false;
            collapse.Stop();
            sessions.IsDropDownOpen = false;
            CloseSettings();
            if (!draggingOut)
            {
                SetDragTargetVisual(false);
                SetExpanded(false);
            }
            UpdateVisibility();
        }

        private void SetExpanded(bool value)
        {
            hover.Cancel();
            if (value)
            {
                peek.Cancel(false);
                messages.Filter(message => !IsMediaNotification(message), false);
                UpdateCompactContent();
            }
            if (expanded == value)
                return;
            expanded = value;
            volumePanel.Visibility = value && settings.ScrollVolume ? Visibility.Visible : Visibility.Collapsed;
            if (!value)
            {
                applicationMenu.IsOpen = false;
                volumePanel.Visibility = Visibility.Collapsed;
                scrubbing = false;
                seekSession = null;
            }
            UpdateState(animate: true);
        }

        private bool HasActivity
        {
            get { return media.IsPlaying || !string.IsNullOrWhiteSpace(media.Title) || shelf.Paths.Count > 0 || clocks.Preview != ClockTool.None; }
        }
        private bool Suppressed { get { return placement != null && (placement.RuleHidden || (settings.HideInGames && placement.GameActive && !placement.RuleShown)); } }
        private bool LightMode { get { return power == null ? settings.Lightweight == LightweightMode.Always : power.State.Light(settings.Lightweight); } }

        private bool HiddenAtRest
        {
            get { return HiddenPreference && !peek.Active && !messages.Active; }
        }
        private bool FloatingMode { get { return settings.DisplayMode == DisplayMode.DynamicIsland; } }
        private bool IsPointerOverIsland { get { return hoverSurface.IsMouseOver; } }
        private bool HiddenPreference { get { return !expanded && !FloatingMode && (settings.EdgeOnly || settings.AutoHide || (settings.HideWhenIdle && !HasActivity)); } }

        private double NormalIslandGap
        {
            get
            {
                double available = placement == null ? SystemParameters.WorkArea.Height : placement.AvailableSize.Height;
                double gap = !expanded && dockEdge == DockEdge.Top ? 8 : 16;
                return FloatingMode ? Math.Min(gap, Math.Max(0, available - 36)) : 0;
            }
        }
        private bool PeekAllowed { get { return settings.PeekTrackChanges && !messages.Active && !temporaryHide.Active && HiddenPreference && !SettingsOpen && !pinned && !Suppressed && IsVisible
            && !draggingOut && !isDragOver && (placement == null || (placement.OnCurrentDesktop && !(settings.HideInFullscreen && !placement.RuleShown && placement.Fullscreen)))
            && (power == null || power.State.RenderAllowed) && (!settings.RespectQuietState || NotificationPolicy.AllowsPeek()); } }

        private void OnShellMouseEnter()
        {
            collapse.Stop();
            if (messages.Active) { messages.Hold(); return; }
            if (peek.Active) { peek.Hold(); return; }
            if (!expanded && panelState.CanHover(Suppressed, temporaryHide.Active) && (settings.OpenOnHover || settings.EdgeOnly)) hover.Request(settings.HoverDelayMs);
        }
        private void TogglePersistent()
        {
            panelState.Persistent = !panelState.Persistent; pinned = panelState.Persistent;
            if (panelState.Persistent) SetExpanded(true); else collapse.Start();
            RefreshPin();
        }
        private void RefreshPin()
        {
            pinButton.ToolTip = panelState.Persistent ? "Sabitlemeyi kaldır" : "Paneli sabitle";
            AutomationProperties.SetName(pinButton, (string)pinButton.ToolTip);
            ((System.Windows.Shapes.Path)pinButton.Content).Fill = panelState.Persistent ? Accent : Muted;
        }

        private void UpdateState(bool animate)
        {
            topHoverGap.Visibility = expanded && FloatingMode && dockEdge == DockEdge.Top ? Visibility.Visible : Visibility.Collapsed;
            if (!MessageContextAllowed) pendingNotification = null;
            if (messages.Active && !MessageAllowed) messages.Cancel(false);
            bool hidden = HiddenAtRest;
            bool animateToHidden = hidden && animate && (compact.Visibility == Visibility.Visible || messageView.Visibility == Visibility.Visible) && stateHeight > 4 && !expanded;
            material.Update(settings, SystemParameters.HighContrast);
            UpdateClockMaterial();
            if (messages.Active && !expanded) { ReleaseExpansionLayout(); RenderNotification(animate); return; }
            double inlineHeight = messages.Active ? RenderInlineNotification() : 0;
            if (!messages.Active) HideNotificationView(animate);
            shell.BorderThickness = hidden ? new Thickness(0) : FloatingMode ? new Thickness(SystemParameters.HighContrast || settings.Style == SurfaceStyle.LiquidGlass ? 1 : 0) : DockLayout.Outline(dockEdge);
            if (!expanded) { applicationMenu.IsOpen = false; CancelTrackSwipe(); }
            double targetWidth;
            double targetHeight;
            CornerRadius targetRadius;

            if (expanded)
            {
                Size available = placement == null ? new Size(SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height) : placement.AvailableSize;
                targetWidth = PanelSizing.Fit(new Size(380, 1), available).Width;
                ApplyPlayerWidth(targetWidth);
                panel.Visibility = Visibility.Visible;
                panel.Measure(new Size(targetWidth - shell.BorderThickness.Left - shell.BorderThickness.Right, double.PositiveInfinity));
                double availableHeight = Math.Max(0, available.Height - NormalIslandGap);
                double contentHeight = panel.DesiredSize.Height;
                double viewportHeight = Math.Max(0, availableHeight - inlineHeight - shell.BorderThickness.Top - shell.BorderThickness.Bottom);
                panelViewport.MaxHeight = viewportHeight;
                panelViewport.VerticalScrollBarVisibility = clockPanel.Visibility == Visibility.Visible && contentHeight <= viewportHeight
                    ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Hidden;
                targetHeight = Math.Min(availableHeight, contentHeight + inlineHeight + 4);
                targetRadius = FloatingMode ? new CornerRadius(28) : DockLayout.Corners(dockEdge, 28);
            }
            else
            {
                bool hasTrack = !string.IsNullOrWhiteSpace(media.Title);
                bool isPlaying = media.IsPlaying || hasTrack;
                if (isPlaying && !(ShowCompactClockTools && !media.IsPlaying))
                {
                    targetWidth = peek.Active ? Math.Max(240, settings.CompactWidth) : settings.CompactWidth;
                }
                else
                {
                    targetWidth = 112;
                }
                targetHeight = peek.Active ? 48 : 36;
                targetRadius = FloatingMode ? new CornerRadius(targetHeight / 2) : DockLayout.Corners(dockEdge, 18);
            }

            shell.Background = hidden ? (SystemParameters.HighContrast ? SystemColors.WindowTextBrush : settings.EdgeOnly ? Muted : EdgeHitArea) : material.Background;
            shell.BorderBrush = hidden ? Brushes.Transparent : (isDragOver ? Accent : material.Edge);
            shell.Opacity = hidden && settings.EdgeOnly && !SystemParameters.HighContrast ? 0.5 : 1;
            sheen.Background = material.Highlight;
            sheen.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
            shell.ToolTip = hidden ? "Tıklayın veya " + settings.PanelHotkey + " ile açın" : null;
            AutomationProperties.SetName(shell, hidden ? "Morf'u aç" : "Morf paneli");
            if (hidden)
            {
                targetWidth = settings.EdgeOnly ? 72 : 96;
                targetHeight = settings.EdgeOnly ? 3 : 4;
                targetRadius = new CornerRadius(0);
            }
            sheen.CornerRadius = targetRadius;

            bool opening = expanded && !stateExpanded;
            bool unchanged = stateWidth == targetWidth && stateHeight == targetHeight && stateExpanded == expanded;
            stateWidth = targetWidth;
            stateHeight = targetHeight;
            stateExpanded = expanded;
            UpdateLiveVisuals();

            bool reducedMotion = !SystemParameters.ClientAreaAnimation || LightMode || (power != null && !power.State.RenderAllowed);
            if (!animate || reducedMotion || (expanded && messages.Active) || (hidden && !animateToHidden))
            {
                ReleaseExpansionLayout();
                shell.BeginAnimation(FrameworkElement.WidthProperty, null);
                shell.BeginAnimation(FrameworkElement.HeightProperty, null);
                compact.BeginAnimation(UIElement.OpacityProperty, null);
                panel.BeginAnimation(UIElement.OpacityProperty, null);
                ((TranslateTransform)panel.RenderTransform).BeginAnimation(TranslateTransform.YProperty, null);

                shell.Width = targetWidth;
                shell.Height = targetHeight;
                shell.CornerRadius = targetRadius;
                compact.Opacity = expanded || hidden ? 0 : 1;
                compact.Visibility = expanded || hidden ? Visibility.Collapsed : Visibility.Visible;
                panel.Opacity = expanded ? 1 : 0;
                panel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                return;
            }

            shell.CornerRadius = targetRadius;
            if (unchanged) return;

            var duration = TimeSpan.FromMilliseconds(220);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            var widthAnim = new DoubleAnimation { To = targetWidth, Duration = duration, EasingFunction = ease };
            var heightAnim = new DoubleAnimation { To = targetHeight, Duration = duration, EasingFunction = ease };
            Timeline.SetDesiredFrameRate(widthAnim, 60); Timeline.SetDesiredFrameRate(heightAnim, 60);

            // Reveal a stable layout through the resizing shell. Measuring text and the
            // viewport at every intermediate width causes wrapping and transient overflow.
            if (expanded || panel.Visibility == Visibility.Visible)
            {
                double layoutWidth = expanded ? targetWidth - shell.BorderThickness.Left - shell.BorderThickness.Right : expandedLayout.ActualWidth;
                double layoutHeight = expanded ? targetHeight - inlineHeight - shell.BorderThickness.Top - shell.BorderThickness.Bottom : expandedLayout.ActualHeight;
                expandedLayout.Width = Math.Max(1, layoutWidth);
                expandedLayout.Height = Math.Max(1, layoutHeight);
                expandedLayout.HorizontalAlignment = HorizontalAlignment.Center;
                expandedLayout.VerticalAlignment = dockEdge == DockEdge.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            }
            int revision = ++expansionRevision;
            heightAnim.Completed += (_, __) =>
            {
                if (closed || revision != expansionRevision) return;
                // Commit the final values and release completed WPF clocks. This also
                // handles the fade completion order when two animations end together.
                shell.Width = targetWidth; shell.Height = targetHeight;
                shell.BeginAnimation(FrameworkElement.WidthProperty, null);
                shell.BeginAnimation(FrameworkElement.HeightProperty, null);
                compact.Opacity = expanded || hidden ? 0 : 1;
                panel.Opacity = expanded ? 1 : 0;
                compact.BeginAnimation(UIElement.OpacityProperty, null);
                panel.BeginAnimation(UIElement.OpacityProperty, null);
                compact.Visibility = expanded || hidden ? Visibility.Collapsed : Visibility.Visible;
                panel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                ReleaseExpansionLayout();
            };

            if (hidden)
            {
                shell.Background = material.Background; shell.BorderBrush = material.Edge; shell.BorderThickness = DockLayout.Outline(dockEdge); shell.Opacity = 1;
                shell.CornerRadius = DockLayout.Corners(dockEdge, 18); compact.Visibility = Visibility.Visible;
                var fade = new DoubleAnimation { To = 0, Duration = duration, EasingFunction = ease }; Timeline.SetDesiredFrameRate(fade, 60);
                heightAnim.Completed += (_, __) => { if (!closed && HiddenAtRest && stateWidth == targetWidth) UpdateState(false); };
                compact.BeginAnimation(UIElement.OpacityProperty, fade);
                shell.BeginAnimation(FrameworkElement.WidthProperty, widthAnim); shell.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);
                return;
            }

            shell.CornerRadius = targetRadius;
            shell.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);
            shell.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);

            if (expanded)
            {
                panel.Visibility = Visibility.Visible;
                if (opening)
                {
                    var slide = new DoubleAnimation(dockEdge == DockEdge.Top ? -4 : 4, 0, duration)
                        { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
                    Timeline.SetDesiredFrameRate(slide, 60);
                    ((TranslateTransform)panel.RenderTransform).BeginAnimation(TranslateTransform.YProperty, slide);
                }
                var panelFade = new DoubleAnimation { To = 1, Duration = duration, EasingFunction = ease };
                var compactFade = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(120), EasingFunction = ease };
                compactFade.Completed += (_, __) =>
                {
                    if (expanded)
                        compact.Visibility = Visibility.Collapsed;
                };
                compact.BeginAnimation(UIElement.OpacityProperty, compactFade);
                panel.BeginAnimation(UIElement.OpacityProperty, panelFade);
            }
            else
            {
                ((TranslateTransform)panel.RenderTransform).BeginAnimation(TranslateTransform.YProperty, null);
                compact.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
                var compactFade = new DoubleAnimation { To = 1, Duration = duration, EasingFunction = ease };
                var panelFade = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(120), EasingFunction = ease };
                panelFade.Completed += (_, __) =>
                {
                    if (!expanded)
                        panel.Visibility = Visibility.Collapsed;
                };
                panel.BeginAnimation(UIElement.OpacityProperty, panelFade);
                compact.BeginAnimation(UIElement.OpacityProperty, compactFade);
            }
        }

        private void ReleaseExpansionLayout()
        {
            expansionRevision++;
            expandedLayout.Width = expandedLayout.Height = double.NaN;
            expandedLayout.HorizontalAlignment = HorizontalAlignment.Stretch;
            expandedLayout.VerticalAlignment = VerticalAlignment.Stretch;
        }

        private void UpdateSourcePicker()
        {
            // Keep an open list stable under metadata/session event bursts.
            if (closed || sessions.IsDropDownOpen) return;
            syncingSessions = true;
            if (!ReferenceEquals(sessionSnapshot, media.Sessions))
            {
                sessions.Items.Clear();
                sessions.Items.Add(new ComboBoxItem { Content = "Otomatik takip", ToolTip = "Yeni çalmaya başlayan kaynağı otomatik izle" });
                PopupTheme.DelayTooltip((ComboBoxItem)sessions.Items[0]);
                foreach (var session in media.Sessions)
                    sessions.Items.Add(new ComboBoxItem { Content = MediaSourceNames.Friendly(session.SourceAppUserModelId), Tag = session });
                sessionSnapshot = media.Sessions;
            }
            foreach (var item in sessions.Items.Cast<ComboBoxItem>().Where(choice => choice.Tag is Session))
            {
                var session = (Session)item.Tag;
                string name = MediaSourceNames.Friendly(session.SourceAppUserModelId);
                var sameName = media.Sessions.Where(sourceSession => MediaSourceNames.Friendly(sourceSession.SourceAppUserModelId) == name).ToArray();
                if (sameName.Length > 1) name += " · " + (Array.IndexOf(sameName, session) + 1);
                item.Content = name + (media.IsSessionPlaying(session) ? " · Çalıyor" : "");
                item.ToolTip = item.Content + " · Bu kaynağı sabit tut";
                PopupTheme.DelayTooltip(item);
            }
            sessions.SelectedIndex = 0;
            if (media.HasPreference)
            {
                var item = sessions.Items.Cast<ComboBoxItem>().FirstOrDefault(choice => Equals(choice.Tag, media.Active));
                if (item != null)
                    sessions.SelectedItem = item;
            }
            syncingSessions = false;
            sessions.IsEnabled = media.Sessions.Count > 0;
            sessions.Visibility = media.Sessions.Count > 0 ? Visibility.Visible : Visibility.Hidden;
            string identity = media.Active == null ? "" : media.Active.SourceAppUserModelId;
            if (sourceIconIdentity != identity)
            {
                sourceIconIdentity = identity;
                MediaApplication known;
                sessions.Tag = MediaAppLauncher.TryIdentify(identity, out known) ? ApplicationIcon(known, 18) : SourceIcon(identity);
            }
            string sourceName = media.Active == null ? "Medya kaynağı" : MediaSourceNames.Friendly(identity);
            sessions.ToolTip = sourceName + (media.HasPreference ? " · Kaynak sabit" : " · Otomatik takip");
            AutomationProperties.SetName(sessions, sourceName + " · Medya kaynağı seç");
            AutomationProperties.SetHelpText(sessions, (string)sessions.ToolTip);
        }

        private void UpdateMedia()
        {
            SynchronizeVolumeTarget();
            UpdateSourcePicker();

            string source = media.Active == null ? "" : MediaSourceNames.Friendly(media.Active.SourceAppUserModelId);
            bool hasTrack = !string.IsNullOrWhiteSpace(media.Title);
            bool isPlaying = media.IsPlaying || hasTrack;

            UpdateCompactContent();

            // Expanded View Updates
            MediaApplication identified;
            bool knownApplication = MediaAppLauncher.TryIdentify(media.Active == null ? "" : media.Active.SourceAppUserModelId, out identified);
            mediaApplicationButton.IsHitTestVisible = true;
            mediaApplicationButton.Focusable = knownApplication;
            mediaTitle.Text = hasTrack ? media.Title : (isPlaying ? "Oynatılıyor" : "Medya bekleniyor");
            string detail = string.IsNullOrWhiteSpace(media.Artist) ? source : media.Artist;
            mediaArtist.Text = string.IsNullOrWhiteSpace(detail) ? "Bir müzik uygulamasında oynatmayı başlatın" : detail;
            mediaArtist.TextWrapping = string.IsNullOrWhiteSpace(detail) ? TextWrapping.Wrap : TextWrapping.NoWrap;
            mediaNotice.Visibility = Visibility.Collapsed;
            mediaNotice.Text = string.IsNullOrWhiteSpace(media.Notice) ? "" : media.Notice;
            mediaTitle.ToolTip = mediaArtist.ToolTip = null;
            if (!media.IsPlaying) peek.Cancel();
            peek.DurationSeconds = settings.PeekSeconds;
            messages.Filter(message => !IsMediaNotification(message) || !expanded, false);
            peek.Observe(media.TrackIdentity, media.IsPlaying, PeekAllowed, media.PlaybackRevision > 0);

            if (media.Thumbnail != null)
            {
                expandedCoverImage.Source = media.Thumbnail;
                expandedCoverImage.Visibility = Visibility.Visible;
                expandedMusicFallback.Visibility = Visibility.Collapsed;
            }
            else
            {
                expandedCoverImage.Visibility = Visibility.Collapsed;
                expandedMusicFallback.Visibility = Visibility.Visible;
            }

            var playback = media.Playback;
            bool playing = playback != null && playback.PlaybackStatus == PlaybackStatus.Playing;
            var capabilities = playback == null ? null : playback.Controls;

            SetPlayGlyph(playing);
            play.ToolTip = playing ? "Duraklat" : "Oynat";
            AutomationProperties.SetName(play, playing ? "Duraklat" : "Oynat");

            play.IsEnabled = !media.Busy && capabilities != null
                && (capabilities.IsPlayPauseToggleEnabled || (playing ? capabilities.IsPauseEnabled : capabilities.IsPlayEnabled));
            previous.IsEnabled = !media.Busy && capabilities != null && capabilities.IsPreviousEnabled;
            next.IsEnabled = !media.Busy && capabilities != null && capabilities.IsNextEnabled;
            shuffle.IsEnabled = !media.Busy && capabilities != null && capabilities.IsShuffleEnabled && playback.IsShuffleActive.HasValue;
            repeat.IsEnabled = !media.Busy && capabilities != null && capabilities.IsRepeatEnabled && playback.AutoRepeatMode.HasValue;
            shuffle.Visibility = capabilities != null && capabilities.IsShuffleEnabled && playback.IsShuffleActive.HasValue ? Visibility.Visible : Visibility.Collapsed;
            repeat.Visibility = capabilities != null && capabilities.IsRepeatEnabled && playback.AutoRepeatMode.HasValue ? Visibility.Visible : Visibility.Collapsed;
            bool shuffled = playback != null && playback.IsShuffleActive.GetValueOrDefault();
            var repeatMode = playback == null ? Windows.Media.MediaPlaybackAutoRepeatMode.None : playback.AutoRepeatMode.GetValueOrDefault();
            shuffleIcon.Stroke = shuffled ? Accent : Muted;
            repeatIcon.Stroke = repeatMode == Windows.Media.MediaPlaybackAutoRepeatMode.None ? Muted : Accent;
            repeatBadge.Visibility = repeatMode == Windows.Media.MediaPlaybackAutoRepeatMode.Track ? Visibility.Visible : Visibility.Collapsed;
            shuffle.ToolTip = !shuffle.IsEnabled ? "Bu kaynak karıştırma denetimini sunmuyor" : shuffled ? "Karıştırma açık · kapat" : "Karıştırma kapalı · aç";
            repeat.ToolTip = !repeat.IsEnabled ? "Bu kaynak tekrarlama denetimini sunmuyor" : repeatMode == Windows.Media.MediaPlaybackAutoRepeatMode.None
                ? "Tekrarlama kapalı · tek parçayı tekrarla" : repeatMode == Windows.Media.MediaPlaybackAutoRepeatMode.Track ? "Tek parça tekrarı · listeyi tekrarla" : "Liste tekrarı · kapat";
            restart.IsEnabled = !media.Busy && capabilities != null && capabilities.IsPlaybackPositionEnabled && media.Timeline != null && media.Timeline.Seekable;
            restart.Visibility = capabilities != null && capabilities.IsPlaybackPositionEnabled && media.Timeline != null && media.Timeline.Seekable ? Visibility.Visible : Visibility.Collapsed;
            if (scrubbing && !Equals(seekSession, media.Active)) { scrubbing = false; seekSession = null; }
            UpdateTimelineDisplay();
            UpdateApplicationPanel();
            retry.Visibility = media.Ready ? Visibility.Collapsed : Visibility.Visible;

            UpdateState(animate: true);
            if (settingsWindow != null) settingsWindow.RefreshRuntimeStatus();
        }

        private void UpdateApplicationPanel()
        {
            bool empty = media.Active == null && string.IsNullOrWhiteSpace(media.Title);
            applicationPanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            playbackControls.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        }
        private async System.Threading.Tasks.Task OpenMediaApplicationAsync(MediaApplication application)
        {
            if (closed) return;
            if (!applicationPanel.IsEnabled) return;
            applicationMenu.IsOpen = false;
            applicationPanel.IsEnabled = false;
            launchButton.IsEnabled = false;
            try
            {
                string message = await applicationLauncher.OpenAsync(application);
                if (!closed) { ShowSettingsNotice(message); UpdateState(false); }
            }
            finally { if (!closed) { applicationPanel.IsEnabled = true; launchButton.IsEnabled = true; } }
        }

        private void UpdateCompactContent() { RenderCompactContent(media.IsPlaying); }

        private void RenderCompactContent(bool playing)
        {
            bool showClockTools = ShowCompactClockTools;
            bool showTimeValue = showClockTools && !playing;
            bool hasMedia = !showTimeValue && (playing || !string.IsNullOrWhiteSpace(media.Title));
            int count = shelf.Paths.Count;
            bool showShelf = !showTimeValue && count > 0;
            bool showClock = !showClockTools && !hasMedia && count == 0 && !settings.EdgeOnly && !settings.AutoHide;
            compactClock.Visibility = showClock ? Visibility.Visible : Visibility.Collapsed;
            compactMarkContainer.Visibility = showClock || showTimeValue ? Visibility.Collapsed : Visibility.Visible;
            double width = peek.Active ? Math.Max(240, settings.CompactWidth) : settings.CompactWidth;
            compact.Height = peek.Active ? 48 : 36;
            double clockWidth = showClockTools && playing ? CompactClockIconWidth : 0;
            compactMarquee.Width = hasMedia ? width - (showShelf ? 88 : 72) - clockWidth : showShelf ? 62 : 0;
            compactMarquee.Visibility = hasMedia || showShelf ? Visibility.Visible : Visibility.Collapsed;
            compactArtist.Text = peek.Active ? MediaNotification.Detail(media.Artist, media.Album)
                : string.IsNullOrWhiteSpace(media.Artist) ? "" : media.Artist;
            compactArtist.Width = Math.Max(0, compactMarquee.Width); compactArtist.Visibility = peek.Active ? Visibility.Visible : Visibility.Collapsed;
            compactMarquee.SetText(hasMedia ? (string.IsNullOrWhiteSpace(media.Title) ? "Medya" : media.Title)
                : showShelf ? count + " dosya" : "");
            compactTitle.Visibility = compactTitle.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            compactCount.Text = hasMedia && showShelf ? "· " + count : "";
            compactCount.Visibility = compactCount.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            compactCoverImage.Source = hasMedia ? media.Thumbnail : null;
            compactCoverBorder.Visibility = hasMedia && media.Thumbnail != null ? Visibility.Visible : Visibility.Collapsed;
            compactMusicFallback.Visibility = hasMedia && media.Thumbnail == null ? Visibility.Visible : Visibility.Collapsed;
            compactDot.Visibility = hasMedia ? Visibility.Collapsed : Visibility.Visible;
            ConfigureCompactClockTools(showClockTools, playing);
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            dragVersion++;
            bool fileDrag = !draggingOut && (e.AllowedEffects & DragDropEffects.Copy) != 0
                && e.Data.GetDataPresent(DataFormats.FileDrop, false);
            bool canDrop = fileDrag && shelf.Paths.Count < FileShelf.Capacity;
            e.Effects = canDrop ? DragDropEffects.Copy : DragDropEffects.None;
            if (fileDrag)
            {
                hover.Cancel();
                collapse.Stop();
                SetDragTargetVisual(true);
                SetExpanded(true);
            }
            else SetDragTargetVisual(false);
            e.Handled = true;
        }

        private void OnDragLeave(object sender, DragEventArgs e)
        {
            // Child transitions deliver another DragEnter/Over before this deferred reset.
            // A cancelled external drag still clears its hint, even without pointer movement.
            int version = ++dragVersion;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (closed || version != dragVersion) return;
                SetDragTargetVisual(false);
                collapse.Start();
            }));
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            dragVersion++;
            e.Handled = true;
            e.Effects = DragDropEffects.None;
            SetDragTargetVisual(false);

            if (draggingOut || (e.AllowedEffects & DragDropEffects.Copy) == 0 || !e.Data.GetDataPresent(DataFormats.FileDrop, false))
                return;
            try
            {
                var incoming = e.Data.GetData(DataFormats.FileDrop, false) as string[];
                var before = shelf.Paths.ToArray();
                int added = incoming == null ? 0 : shelf.Add(incoming);
                if (shelfFilter != null && shelfFilter.SelectedIndex > 0) shelf.Assign(shelf.Paths.Except(before, StringComparer.OrdinalIgnoreCase), (string)shelfFilter.SelectedItem);
                if (added == 0)
                {
                    shelfNotice.Text = shelf.LastAddNotice ?? "Dosya veya klasör yolları okunamadı.";
                    if (shelf.Paths.Count == 0)
                    {
                        mediaNotice.Text = "Yalnızca doğrudan yerel dosya ve klasörler eklenebilir.";
                        mediaNotice.ToolTip = mediaNotice.Text;
                        mediaNotice.Visibility = Visibility.Visible;
                    }
                    return;
                }
                e.Effects = DragDropEffects.Copy;
                mediaNotice.Visibility = Visibility.Collapsed;
                UpdateShelf();
                shelfNotice.Text = shelf.LastAddNotice + " · " + shelf.Paths.Count + " / " + FileShelf.Capacity;
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                shelfNotice.Text = "Windows bırakılan öğeleri okuyamadı.";
            }
            finally
            {
                collapse.Start();
            }
        }

        private void SetDragTargetVisual(bool active)
        {
            if (isDragOver == active) return;
            isDragOver = active;
            shell.BorderBrush = active ? Accent : material.Edge;
            shelfDropBorder.BorderBrush = active ? Accent : Stroke;
            dropHint.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            dropHintText.Text = shelf.Paths.Count == FileShelf.Capacity
                ? "Raf dolu · önce birkaç öğeyi raftan kaldırın"
                : "Dosya veya fotoğrafları rafa bırakın";
            if (expanded) UpdateState(animate: true);
        }

        private void UpdateShelf()
        {
            var selectedPaths = SelectedPaths();
            foreach (var item in files.Items.Cast<ListBoxItem>().ToArray())
                if (!shelf.Paths.Contains((string)item.Tag, StringComparer.OrdinalIgnoreCase)) files.Items.Remove(item);
            foreach (var path in shelf.Paths)
            {
                if (files.Items.Cast<ListBoxItem>().Any(item => string.Equals((string)item.Tag, path, StringComparison.OrdinalIgnoreCase)))
                    continue;
                string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var card = new StackPanel { Width = 76 };
                var preview = new Grid { Width = 54, Height = 48, Margin = new Thickness(0, 0, 0, 5) };
                bool isFolder = Directory.Exists(path);
                var icon = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse(isFolder
                        ? "M 1 4 L 6 4 L 8 6 L 15 6 L 15 14 L 1 14 Z"
                        : "M 3 2 L 9 2 L 13 6 L 13 14 C 13 14.6 12.6 15 12 15 L 3 15 C 2.4 15 2 14.6 2 14 L 2 3 C 2 2.4 2.4 2 3 2 Z M 8 2 L 8 7 L 13 7"),
                    Fill = isFolder ? Accent : Muted,
                    Width = 26,
                    Height = 28,
                    Stretch = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center
                };
                preview.Children.Add(icon);
                var image = new Image { Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
                RoundImage(image, 6);
                preview.Children.Add(image);
                card.Children.Add(preview);
                var label = Label(string.IsNullOrWhiteSpace(name) ? path : name, 11, ThemePalette.Text);
                label.TextAlignment = TextAlignment.Center;
                card.Children.Add(label);
                var entry = new ListBoxItem
                {
                    Content = card,
                    ToolTip = path,
                    Tag = path,
                    Padding = new Thickness(4, 5, 4, 5), Margin = new Thickness(2),
                    Foreground = ThemePalette.Text
                };
                AutomationProperties.SetName(entry, label.Text);
                StyleShelfEntry(entry);
                files.Items.Add(entry);
                LoadShelfPreview(path, entry, image, icon);
            }

            for (int i = 0; i < shelf.Paths.Count; i++)
            {
                var entry = files.Items.Cast<ListBoxItem>().First(x => string.Equals((string)x.Tag, shelf.Paths[i], StringComparison.OrdinalIgnoreCase));
                int position = files.Items.IndexOf(entry);
                if (position != i) { files.Items.RemoveAt(position); files.Items.Insert(i, entry); }
                entry.IsSelected = selectedPaths.Contains((string)entry.Tag, StringComparer.OrdinalIgnoreCase);
            }
            ApplyShelfFilter();
            int count = shelf.Paths.Count;
            shelfHeaderCount.Text = count + " / " + FileShelf.Capacity + " öğe";
            UpdateCompactContent();
            shelfSection.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;

            if (!isDragOver)
            {
                shelfNotice.Text = count == 0
                    ? "Dosya veya klasörleri buraya bırakın."
                    : count + " / " + FileShelf.Capacity + " öğe · seçip dışarı sürükleyin veya kopyalayın";
            }
            UpdateShelfButtons();
            UpdateState(animate: true);
        }

        private async void LoadShelfPreview(string path, ListBoxItem entry, Image image, UIElement fallback)
        {
            var thumbnail = await ShelfThumbnail.LoadAsync(path);
            if (closed || thumbnail == null || !files.Items.Contains(entry)) return;
            image.Source = thumbnail;
            image.Visibility = Visibility.Visible;
            fallback.Visibility = Visibility.Collapsed;
        }

        private static void StyleShelfEntry(ListBoxItem entry)
        {
            var template = new ControlTemplate(typeof(ListBoxItem));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Card";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
            template.VisualTree = border;
            foreach (var property in new[] { UIElement.IsMouseOverProperty, UIElement.IsKeyboardFocusWithinProperty, ListBoxItem.IsSelectedProperty })
            {
                var trigger = new Trigger { Property = property, Value = true };
                trigger.Setters.Add(new Setter(Border.BackgroundProperty, property == ListBoxItem.IsSelectedProperty ? ThemePalette.AccentDark : ThemePalette.Hover, "Card"));
                trigger.Setters.Add(new Setter(Border.BorderBrushProperty, Accent, "Card"));
                template.Triggers.Add(trigger);
            }
            entry.Template = template;
        }

        private static ScrollViewer FindScrollViewer(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var viewer = child as ScrollViewer ?? FindScrollViewer(child);
                if (viewer != null) return viewer;
            }
            return null;
        }

        internal static void StyleSessionPicker(ComboBox picker, bool sourceHeader = false)
        {
            PopupTheme.DelayTooltip(picker);
            var template = new ControlTemplate(typeof(ComboBox));
            var grid = new FrameworkElementFactory(typeof(Grid));
            var toggle = new FrameworkElementFactory(typeof(System.Windows.Controls.Primitives.ToggleButton));
            toggle.SetValue(Control.BackgroundProperty, CardBg);
            toggle.SetValue(Control.BorderBrushProperty, Stroke);
            toggle.SetValue(UIElement.FocusableProperty, false);
            toggle.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
                new System.Windows.Data.Binding("IsDropDownOpen")
                { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent, Mode = System.Windows.Data.BindingMode.TwoWay });
            var toggleTemplate = new ControlTemplate(typeof(System.Windows.Controls.Primitives.ToggleButton));
            var toggleBorder = new FrameworkElementFactory(typeof(Border));
            toggleBorder.Name = "SourceSurface";
            toggleBorder.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            toggleBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            toggleTemplate.VisualTree = toggleBorder;
            foreach (var property in new[] { UIElement.IsMouseOverProperty, System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty })
            {
                var hover = new Trigger { Property = property, Value = true };
                hover.Setters.Add(new Setter(Border.BackgroundProperty, ThemePalette.Hover, "SourceSurface"));
                toggleTemplate.Triggers.Add(hover);
            }
            toggle.SetValue(Control.TemplateProperty, toggleTemplate);
            grid.AppendChild(toggle);
            var selection = new FrameworkElementFactory(typeof(ContentPresenter));
            selection.SetValue(FrameworkElement.MarginProperty, sourceHeader ? new Thickness(9, 4, 9, 4) : new Thickness(9, 4, 26, 4));
            selection.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            selection.SetValue(UIElement.IsHitTestVisibleProperty, false);
            if (sourceHeader)
                selection.SetBinding(ContentPresenter.ContentProperty, new Binding("Tag") { RelativeSource = RelativeSource.TemplatedParent });
            else
                selection.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemProperty));
            grid.AppendChild(selection);
            if (!sourceHeader)
            {
                var arrow = new FrameworkElementFactory(typeof(TextBlock));
                arrow.SetValue(TextBlock.TextProperty, "⌄");
                arrow.SetValue(TextBlock.ForegroundProperty, Muted);
                arrow.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
                arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
                arrow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 9, 0));
                arrow.SetValue(UIElement.IsHitTestVisibleProperty, false);
                grid.AppendChild(arrow);
            }
            var popup = new FrameworkElementFactory(typeof(System.Windows.Controls.Primitives.Popup));
            popup.Name = "PART_Popup";
            popup.SetValue(System.Windows.Controls.Primitives.Popup.AllowsTransparencyProperty, true);
            popup.SetValue(System.Windows.Controls.Primitives.Popup.PlacementProperty, System.Windows.Controls.Primitives.PlacementMode.Bottom);
            if (sourceHeader)
            {
                popup.SetValue(System.Windows.Controls.Primitives.Popup.PlacementProperty, System.Windows.Controls.Primitives.PlacementMode.Custom);
                popup.SetBinding(System.Windows.Controls.Primitives.Popup.PlacementTargetProperty,
                    new Binding { RelativeSource = RelativeSource.TemplatedParent });
                popup.SetValue(System.Windows.Controls.Primitives.Popup.CustomPopupPlacementCallbackProperty,
                    new System.Windows.Controls.Primitives.CustomPopupPlacementCallback((size, target, offset) => PlaceSourcePopup(picker, size, target)));
                picker.DropDownOpened += (_, __) => FitSourcePopup(picker);
            }
            popup.SetValue(System.Windows.Controls.Primitives.Popup.IsOpenProperty, new TemplateBindingExtension(ComboBox.IsDropDownOpenProperty));
            var popupBorder = new FrameworkElementFactory(typeof(Border));
            popupBorder.SetValue(Border.BackgroundProperty, CardBg);
            popupBorder.SetValue(Border.BorderBrushProperty, Stroke);
            popupBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            popupBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            popupBorder.SetValue(Border.PaddingProperty, new Thickness(4));
            if (sourceHeader) popupBorder.SetValue(FrameworkElement.WidthProperty, 220.0);
            else popupBorder.SetValue(FrameworkElement.MinWidthProperty, new TemplateBindingExtension(FrameworkElement.ActualWidthProperty));
            var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
            scroll.SetValue(FrameworkElement.MaxHeightProperty, 220.0);
            scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            if (sourceHeader) scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
            scroll.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
            popupBorder.AppendChild(scroll);
            popup.AppendChild(popupBorder);
            grid.AppendChild(popup);
            template.VisualTree = grid;
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            focus.Setters.Add(new Setter(UIElement.OpacityProperty, 0.85));
            template.Triggers.Add(focus);
            picker.Template = template;

            var itemStyle = new Style(typeof(ComboBoxItem));
            itemStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 34.0));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, ThemePalette.Text));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(9, 6, 9, 6)));
            var itemTemplate = new ControlTemplate(typeof(ComboBoxItem));
            var itemBorder = new FrameworkElementFactory(typeof(Border));
            itemBorder.Name = "Choice";
            itemBorder.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            itemBorder.SetValue(Border.BackgroundProperty, CardBg);
            itemBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            itemBorder.SetValue(UIElement.ClipToBoundsProperty, true);
            if (sourceHeader)
            {
                var text = new FrameworkElementFactory(typeof(TextBlock));
                text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
                text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap);
                text.SetBinding(TextBlock.TextProperty, new Binding("Content") { RelativeSource = RelativeSource.TemplatedParent });
                itemBorder.AppendChild(text);
            }
            else itemBorder.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
            itemTemplate.VisualTree = itemBorder;
            var highlight = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
            highlight.Setters.Add(new Setter(Border.BackgroundProperty, ThemePalette.Hover, "Choice"));
            itemTemplate.Triggers.Add(highlight);
            var selected = new Trigger { Property = ComboBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Border.BackgroundProperty, ThemePalette.Hover, "Choice"));
            selected.Setters.Add(new Setter(System.Windows.Documents.TextElement.FontWeightProperty, FontWeights.SemiBold, "Choice"));
            itemTemplate.Triggers.Add(selected);
            itemStyle.Setters.Add(new Setter(Control.TemplateProperty, itemTemplate));
            picker.ItemContainerStyle = itemStyle;
        }

        private static void FitSourcePopup(ComboBox picker)
        {
            var owner = Window.GetWindow(picker) as IslandWindow;
            var popup = picker.Template.FindName("PART_Popup", picker) as System.Windows.Controls.Primitives.Popup;
            var surface = popup == null ? null : popup.Child as Border;
            if (owner == null || surface == null) return;
            Point origin = picker.TranslatePoint(new Point(), owner.shell);
            surface.Width = Math.Min(260, Math.Max(1, owner.shell.ActualWidth - 24));
            surface.MaxHeight = Math.Max(1, owner.shell.ActualHeight - origin.Y - picker.ActualHeight - 16);
        }

        private static System.Windows.Controls.Primitives.CustomPopupPlacement[] PlaceSourcePopup(ComboBox picker, Size size, Size target)
        {
            double x = 0;
            var owner = Window.GetWindow(picker) as IslandWindow;
            if (owner != null)
            {
                Point origin = picker.TranslatePoint(new Point(), owner.shell);
                x = Math.Max(12, Math.Min(origin.X, owner.shell.ActualWidth - size.Width - 12)) - origin.X;
            }
            return new[] { new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point(x, target.Height + 4),
                System.Windows.Controls.Primitives.PopupPrimaryAxis.None) };
        }

        internal static void StyleShelfScrollBar(FrameworkElement list, bool subtle = false)
        {
            // The inbox scrollbar uses a fixed light track even inside a dark island.
            list.Resources["MorfScrollThumb"] = Muted;
            var style = new Style(typeof(System.Windows.Controls.Primitives.ScrollBar));
            style.Setters.Add(new Setter(Control.OverridesDefaultStyleProperty, true));
            style.Setters.Add(new Setter(Control.BackgroundProperty, subtle ? Brushes.Transparent : ShelfBg));
            style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 12.0));
            style.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ScrollBar}'>
  <Grid Background='{TemplateBinding Background}'>
    <Track x:Name='PART_Track' Orientation='Horizontal' IsDirectionReversed='False'
           Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}'
           Value='{TemplateBinding Value}' ViewportSize='{TemplateBinding ViewportSize}'>
      <Track.DecreaseRepeatButton>
        <RepeatButton Command='{x:Static ScrollBar.PageLeftCommand}' Focusable='False'>
          <RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template>
        </RepeatButton>
      </Track.DecreaseRepeatButton>
      <Track.Thumb>
        <Thumb Focusable='False'>
          <Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'>
            <Grid Background='Transparent'>
              <Border x:Name='Indicator' Margin='2,0' Height='3' VerticalAlignment='Center' CornerRadius='1.5' Opacity='0.42' Background='{DynamicResource MorfScrollThumb}'/>
            </Grid>
          </ControlTemplate></Thumb.Template>
        </Thumb>
      </Track.Thumb>
      <Track.IncreaseRepeatButton>
        <RepeatButton Command='{x:Static ScrollBar.PageRightCommand}' Focusable='False'>
          <RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template>
        </RepeatButton>
      </Track.IncreaseRepeatButton>
    </Track>
  </Grid>
</ControlTemplate>")));
            list.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = style;
        }

        private void OpenSettings()
        {
            if (closed) return;
            if (mixerWindow != null) mixerWindow.Close();
            messages.Cancel(false);
            peek.Cancel(false); UpdateCompactContent(); UpdateState(false);
            if (settingsWindow != null) { settingsWindow.Activate(); return; }
            collapse.Stop();
            if (!new StartupRegistration().Initialize(settings))
                ShowSettingsNotice("Windows başlangıç durumu okunamadı; mevcut tercih değiştirilmedi.");
            var window = new SettingsWindow(settings, new SettingsActions { Apply = ApplySettings, SelectTheme = SelectTheme,
                ConfigureStartup = ConfigureStartup, Exit = () => { exitConfirmed = true; Close(); }, RefreshApplication = RefreshApplicationAsync, ShelfCount = () => shelf.Paths.Count,
                MotionAllowed = () => !LightMode && (power == null || power.State.RenderAllowed),
                HideTemporarily = HideTemporarily, ConfigureHotkeys = ConfigureHotkeys, Reset = ResetDefaults,
                Diagnostics = Diagnostics, CopyDiagnostics = CopyDiagnostics, StartSleep = minutes => sleep.Start(minutes), CancelSleep = () => sleep.Cancel(),
                SleepMinutes = () => sleep.SelectedMinutes, SleepActive = () => sleep.Active,
                MediaStatus = () => string.IsNullOrWhiteSpace(media.Notice) ? media.ReturnStatus : media.Notice + "\n" + media.ReturnStatus, ResumePrevious = media.ResumePreviousAsync,
                NotificationStatus = NotificationRuntimeStatus, NotificationAvailable = () => AppNotificationListener.HasIdentity && !appNotifications.Connecting,
                ConnectNotifications = () => ConnectNotificationsAsync(true), RefreshNotifications = () => ConnectNotificationsAsync(false),
                PreviewNotification = PreviewNotification, OpenNotificationSettings = OpenWindowsNotificationSettings,
                OpenNotificationAccess = () => OpenWindowsNotificationSettings(true),
                SourceStatus = name => media.Sessions.Any(source => MediaSourceNames.Friendly(source.SourceAppUserModelId) == name) ? "Windows medya oturumu bağlı" : "Windows medya oturumu bekleniyor",
                NotificationSources = () => appNotifications.Sources,
                MediaSources = () => media.Sessions.Select(source => new SourceChoice { Id = source.SourceAppUserModelId, Name = MediaSourceNames.Friendly(source.SourceAppUserModelId) }).GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToArray(),
                SleepStatus = () => sleep.Due.HasValue ? "Etkin · " + sleep.Due.Value.ToLocalTime().ToString("HH:mm") + " saatinde medya duraklatılacak" : "Kapalı" });
            settingsWindow = window;
            window.SetNotice(SettingsNotice);
            window.Closed += (_, __) =>
            {
                settingsWindow = null;
                if (!closed)
                {
                    panelState.RestoreAfterSettings(!Suppressed && (placement == null || placement.OnCurrentDesktop));
                    UpdateState(false); UpdateVisibility();
                    if (!pinned) collapse.Start(); UpdateLiveVisuals();
                    FlushPendingNotification();
                }
            };
            panelState.SuspendForSettings();
            UpdateState(false);
            pinned = false;
            SetExpanded(false);
            UpdateLiveVisuals();
            window.Show();
        }

        private void CloseSettings() { if (settingsWindow != null) settingsWindow.Close(); }

        private bool SelectTheme(ThemePreference preference)
        {
            bool saved = ThemePalette.Select(preference);
            ApplySettings(false);
            ShowSettingsNotice(saved ? "" : "Tema bu oturumda uygulandı; ayar kaydedilemedi.");
            return saved;
        }

        private bool ConfigureStartup(bool enabled)
        {
            if (!new StartupRegistration().Configure(enabled)) { ShowStartupFailure(); return false; }
            settings.StartWithWindows = enabled;
            settings.StartupConfigured = true;
            return true;
        }

        private bool ConfigureHotkeys(string[] values)
        {
            if (placement == null) { ShowSettingsNotice("Kısayollar pencere hazır olduğunda değiştirilebilir."); return false; }
            if (!placement.ConfigureHotkeys(values)) { ShowSettingsNotice(placement.HotkeyFailure); return false; }
            var parsed = new HotkeyBinding[4]; for (int i = 0; i < 4; i++) HotkeyBinding.TryParse(values[i], out parsed[i]);
            settings.PanelHotkey = parsed[0].Text; settings.PlayHotkey = parsed[1].Text; settings.PreviousHotkey = parsed[2].Text; settings.NextHotkey = parsed[3].Text;
            shortcutWarning = ""; SaveSettings(); return true;
        }

        private bool ResetDefaults()
        {
            var previous = settings.Clone(); var defaults = new WidgetSettings { StartupConfigured = true };
            if (placement != null && !placement.ConfigureHotkeys(defaults.Hotkeys)) { ShowSettingsNotice("Varsayılan kısayol kullanılamıyor; ayarlar korunuyor. " + placement.HotkeyFailure); return false; }
            if (!new StartupRegistration().Configure(defaults.StartWithWindows))
            {
                if (placement != null) placement.ConfigureHotkeys(previous.Hotkeys);
                ShowStartupFailure(); return false;
            }
            settings.CopyFrom(defaults); sleep.Cancel(); audioMirror.Stop(); shortcutWarning = "";
            media.CancelAutomaticReturn(); peek.Cancel(false);
            bool themeSaved = ThemePalette.Select(ThemePreference.System);
            bool preferencesSaved = settings.Save(WidgetSettings.DefaultPath);
            ApplySettings(false);
            ShowSettingsNotice(themeSaved && preferencesSaved ? "Varsayılan ayarlar uygulandı. Geçici dosya rafı korundu." : "Varsayılanlar bu oturumda uygulandı; bazı tercihler kaydedilemedi.");
            return true;
        }

        private string Diagnostics()
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine("Morf " + BuildInfo.Version);
            text.AppendLine("Windows: " + Environment.OSVersion.Version);
            text.AppendLine("Çalışma zamanı: " + Environment.Version);
            using (var process = System.Diagnostics.Process.GetCurrentProcess())
            {
                process.Refresh(); text.AppendLine("Özel bellek: " + (process.PrivateMemorySize64 / 1048576.0).ToString("F1") + " MiB");
                text.AppendLine("Çalışma kümesi: " + (process.WorkingSet64 / 1048576.0).ToString("F1") + " MiB");
            }
            text.AppendLine("Medya API: " + (media.Ready ? "Hazır" : "Kullanılamıyor") + " · Oturum sayısı: " + media.Sessions.Count);
            text.AppendLine("Müziğe dönüş · Açık: " + settings.ResumeInterrupted + " · Bekleyen sahipli duraklatma: " + media.PendingReturns);
            text.AppendLine("Parça bildirimi · Açık: " + settings.PeekTrackChanges + " · Görünür: " + peek.Active);
            text.AppendLine("Windows bildirimleri · Açık: " + settings.WindowsNotifications + " · Windows kimliği: " + AppNotificationListener.HasIdentity
                + " · Bağlantı: " + appNotifications.Status + " · Kuyruk: " + messages.Queued);
            if (!string.IsNullOrEmpty(appNotifications.LastFailure)) text.AppendLine("Bildirim hata kodu: " + appNotifications.LastFailure);
            if (bannerRouting != null) text.AppendLine("Windows başlık yönetimi: " + bannerRouting.Status);
            if (bannerRouting != null && !string.IsNullOrEmpty(bannerRouting.LastFailure)) text.AppendLine("Başlık yönetimi hata kodu: " + bannerRouting.LastFailure);
            text.AppendLine("Kaynak: " + (media.Active == null ? "Yok" : MediaSourceNames.Friendly(media.Active.SourceAppUserModelId)));
            var playback = media.Playback;
            if (playback != null) text.AppendLine("Yetkiler · Karıştırma: " + playback.Controls.IsShuffleEnabled + " · Tekrar: " + playback.Controls.IsRepeatEnabled + " · Seek: " + playback.Controls.IsPlaybackPositionEnabled);
            text.AppendLine("Ekran: " + (placement == null ? "Hazırlanıyor" : placement.MonitorStatus));
            text.AppendLine("Kısayollar:\n" + (placement == null ? "Hazırlanıyor" : placement.HotkeyStatus));
            text.AppendLine("Hafif mod: " + settings.Lightweight + " · Etkin: " + LightMode);
            if (power != null) text.AppendLine("Pil: " + power.State.OnBattery + " · Tasarruf: " + power.State.Saving + " · Kilit: " + power.State.Locked + " · Ekran açık: " + power.State.DisplayOn + " · Bildirimler: " + power.NotificationsAvailable);
            text.AppendLine("Uyku zamanlayıcısı: " + (sleep.Due.HasValue ? sleep.Due.Value.ToLocalTime().ToString("HH:mm") : "Kapalı"));
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    text.AppendLine("Windows başlangıcı · Tercih: " + settings.StartWithWindows + " · Kayıt var: " + (key != null && key.GetValue("WindowsWidgates.Morf") is string));
            }
            catch { text.AppendLine("Windows başlangıç kaydı okunamadı."); }
            text.AppendLine("Bu rapor şarkı adlarını, raf yollarını veya dinleme geçmişini içermez. Otomatik gönderilmez.");
            return text.ToString();
        }
        private void CopyDiagnostics(string text)
        {
            try { Clipboard.SetText(text); ShowSettingsNotice("Tanılama panoya kopyalandı."); }
            catch (System.Runtime.InteropServices.ExternalException) { ShowSettingsNotice("Pano meşgul; yeniden deneyebilirsiniz."); }
        }

        private void ApplySettings(bool save)
        {
            if (closed) { if (save) SaveSettings(); return; }
            media.AutoPauseOthers = settings.AutoPauseOthers;
            media.ResumeInterrupted = settings.ResumeInterrupted;
            media.ConfigureRules(settings.MediaRules);
            if (bannerRouting != null) bannerRouting.ConfigureRules(settings.NotificationRules);
            var notificationRules = SourceRules.Parse(settings.NotificationRules);
            messages.Filter(message => notificationRules.Get(message.AppId) != 2);
            messages.Seconds = settings.NotificationSeconds;
            if (!settings.WindowsNotifications) pendingNotification = null;
            if (!settings.WindowsNotifications && messages.Active && !messages.Current.Sample) messages.Cancel(false);
            if (messages.Active && !messages.Current.Sample && SourceRules.Parse(settings.NotificationRules).Get(messages.Current.AppId) == 2) messages.Cancel(false);
            if (pendingNotification != null && SourceRules.Parse(settings.NotificationRules).Get(pendingNotification.AppId) == 2) pendingNotification = null;
            appNotifications.Configure(settings.WindowsNotifications);
            if (tray != null) tray.SetEnabled(settings.ShowTrayIcon);
            if (!settings.PeekTrackChanges) peek.Cancel(false);
            UpdateCompactContent();
            volumeButton.Visibility = Visibility.Visible;
            SynchronizeVolumeTarget();
            volumePanel.Visibility = settings.ScrollVolume && expanded ? Visibility.Visible : Visibility.Collapsed;
            UpdateTimelineDisplay();
            settings.Opacity = WidgetSettings.ClampOpacity(settings.Opacity);
            settings.LiquidTransparency = WidgetSettings.ClampLiquidTransparency(settings.LiquidTransparency);
            settings.GlassStrength = WidgetSettings.ClampStrength(settings.GlassStrength);
            settings.HoverDelayMs = Math.Max(0, Math.Min(1000, settings.HoverDelayMs));
            settings.CompactWidth = Math.Max(150, Math.Min(260, settings.CompactWidth));
            hover.Cancel();
            if (placement != null)
            {
                placement.Preference = settings.Dock;
                placement.FollowDesktops = settings.FollowDesktops;
                placement.TrackFullscreen = settings.HideInFullscreen;
                placement.MonitorDevice = settings.MonitorDevice;
                placement.ConfigureRules(settings.AppRules);
                placement.Reposition();
            }
            else dockEdge = DockLayout.Resolve(settings.Dock, TaskbarEdge.Bottom);
            UpdateState(false);
            UpdateVisibility();
            if (save) SaveSettings();
        }

        private void SaveSettings()
        {
            bool saved = settings.Save(WidgetSettings.DefaultPath);
            ShowSettingsNotice(saved ? "" : "Ayar bu oturumda uygulandı; diske kaydedilemedi.");
        }

        private string SettingsNotice
        {
            get { return string.IsNullOrEmpty(shortcutWarning) ? settingsMessage : string.IsNullOrEmpty(settingsMessage) ? shortcutWarning : shortcutWarning + "\n" + settingsMessage; }
        }

        private void ShowSettingsNotice(string message)
        {
            settingsMessage = message;
            if (settingsWindow != null) settingsWindow.SetNotice(SettingsNotice);
        }
        internal void ShowStartupFailure()
        {
            ShowSettingsNotice("Windows başlangıç kaydı doğrulanamadı. Windows veya güvenlik yazılımı değişikliği engellemiş olabilir; ayar uygulanmış sayılmadı.");
        }

        private void UpdateLiveVisuals()
        {
            UpdateNotificationRouting();
            if (compactMarquee == null || timelinePanel == null) return;
            if (messages.Active && !MessageAllowed) { messages.Cancel(false); UpdateState(false); return; }
            bool visible = !closed && !SettingsOpen && IsVisible && (placement == null || placement.OnCurrentDesktop) && !Suppressed && (power == null || power.State.RenderAllowed);
            if (settingsWindow != null && (!SystemParameters.ClientAreaAnimation || LightMode || (power != null && !power.State.RenderAllowed)))
                settingsWindow.FinishApplicationsMotion();
            if (!visible || messages.Active || LightMode || !SystemParameters.ClientAreaAnimation)
            {
                ((TranslateTransform)panel.RenderTransform).BeginAnimation(TranslateTransform.YProperty, null);
                volumePanel.BeginAnimation(UIElement.OpacityProperty, null);
            }
            compactMarquee.SetActive(visible && !messages.Active && !LightMode && !expanded && !HiddenAtRest && settings.ScrollTitles && media.IsPlaying);
            bool compactVisible = visible && !messages.Active && !expanded && !HiddenAtRest;
            UpdateClockTools(visible, compactVisible);
            UpdateIdleClock(compactVisible);
            bool ticking = visible && expanded && settings.ShowTimeline && media.IsPlaying
                && media.Timeline != null && media.Timeline.Valid && !scrubbing;
            UpdateProgressClock(ticking);
        }

        private void UpdateProgressClock(bool ticking)
        {
            // Assigning even the same interval moves an enabled DispatcherTimer's deadline.
            // Media events must not postpone the next local progress update.
            var interval = TimeSpan.FromSeconds(LightMode ? 2 : 1);
            if (progress.Interval != interval) progress.Interval = interval;
            if (ticking) { if (!progress.IsEnabled) progress.Start(); } else progress.Stop();
        }

        private void UpdateTimelineDisplay()
        {
            if (timelinePanel == null) return;
            var timeline = media.Timeline;
            bool available = settings.ShowTimeline && timeline != null && timeline.Valid;
            timelinePanel.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            seekSlider.IsEnabled = available && !media.Busy && timeline.Seekable && media.Playback != null && media.Playback.Controls.IsPlaybackPositionEnabled;
            seekSlider.ToolTip = seekSlider.IsEnabled ? "İstenen konuma git" : "Bu kaynak konum değiştirme denetimini Windows'a sunmuyor";
            if (available && !scrubbing)
            {
                seekSlider.Maximum = timeline.Duration;
                seekSlider.Value = Math.Floor(timeline.PositionAt(DateTimeOffset.UtcNow, media.IsPlaying));
                elapsedLabel.Text = MediaTimeline.Format(seekSlider.Value);
                durationLabel.Text = MediaTimeline.Format(timeline.Duration);
            }
            UpdateLiveVisuals();
        }

        private static bool IsSliderKey(Key key)
        {
            return key == Key.Left || key == Key.Right || key == Key.Up || key == Key.Down || key == Key.Home || key == Key.End || key == Key.PageUp || key == Key.PageDown;
        }

        private void SetPlayGlyph(bool playing)
        {
            if (lastPlayGlyph == playing) return;
            lastPlayGlyph = playing;
            playIcon.Data = playing
                ? Geometry.Parse("M 0 0 L 4 0 L 4 16 L 0 16 Z M 8 0 L 12 0 L 12 16 L 8 16 Z")
                : Geometry.Parse("M 0 0 L 12 8 L 0 16 Z");
            playIcon.RenderTransform = new TranslateTransform(playing ? 0 : 1, 0);
        }

        private void BeginSeek()
        {
            if (!seekSlider.IsEnabled) return;
            scrubbing = true;
            seekSession = media.Active;
            progress.Stop();
            elapsedLabel.Text = MediaTimeline.Format(seekSlider.Value);
        }

        private async System.Threading.Tasks.Task CommitSeekAsync()
        {
            if (!scrubbing) return;
            double requested = seekSlider.Value;
            var expected = seekSession;
            scrubbing = false;
            seekSession = null;
            if (expected != null && Equals(expected, media.Active)) await media.CommandAsync("seek", requested, expected);
            UpdateTimelineDisplay();
        }

        private async void OnVolumeWheel(object sender, MouseWheelEventArgs e)
        {
            if (messages.Active && (!expanded || messageView.IsMouseOver)) return;
            if (clockPanel.IsMouseOver) return;
            if (!settings.ScrollVolumeOnWholeIsland && !volumeButton.IsMouseOver && !volumePanel.IsMouseOver) return;
            if (panelViewport.ScrollableHeight > 0 && panelViewport.IsMouseOver && !volumeButton.IsMouseOver && !volumePanel.IsMouseOver) return;
            if (!settings.ScrollVolume || SettingsOpen || sessions.IsDropDownOpen || files.IsMouseOver || sessions.IsMouseOver || seekSlider.IsMouseOver || e.Delta == 0) return;
            e.Handled = true;
            SynchronizeVolumeTarget();
            if (pendingVolumeTarget != CurrentVolumeTarget) pendingVolumeDelta = 0;
            pendingVolumeTarget = CurrentVolumeTarget;
            pendingVolumeDelta += e.Delta;
            if (changingVolume) return;
            changingVolume = true;
            try
            {
                while (pendingVolumeDelta != 0 && !closed)
                {
                    int delta = pendingVolumeDelta;
                    string target = pendingVolumeTarget;
                    pendingVolumeDelta = 0;
                    await UseVolumeAsync(volume => volume.AdjustAsync(delta), target);
                }
            }
            finally { changingVolume = false; }
        }

        private void UpdateVolumeDisplay(VolumeState state)
        {
            if (closed) return;
            if (!state.Available)
            {
                volumePercent.Text = "Yok";
                volumeSlider.IsEnabled = muteButton.IsEnabled = false;
                volumeTargetLabel.Text = settings.PlayerVolume ? "Oynatıcının ses oturumu bulunamadı. Sistem sesi değiştirilmez." : "Ses aygıtı kullanılamıyor.";
                volumeTargetLabel.Visibility = Visibility.Visible;
                if (expanded && volumePanel.Visibility == Visibility.Visible) UpdateState(false);
                return;
            }
            volumeTargetLabel.Text = settings.PlayerVolume ? PlayerVolumeName + " · Uygulamanın ses düzeyi" : "Windows'un genel ses düzeyi";
            volumeTargetLabel.Visibility = Visibility.Collapsed;
            volumeTargetLabel.ToolTip = settings.PlayerVolume ? "Seçili uygulamanın tüm eşleşen ses oturumları etkilenir. Tarayıcı sekmeleri ayrı ayrı ayarlanmaz." : null;
            volumeSlider.ToolTip = volumeTargetLabel.Text;
            volumeSlider.IsEnabled = muteButton.IsEnabled = true;
            volumeSlider.Value = state.Level * 100;
            volumePercent.Text = "%" + Math.Round(state.Level * 100);
            muteGlyph.Data = Geometry.Parse(state.Muted
                ? "M 1 6 L 4 6 L 8 2 L 8 14 L 4 10 L 1 10 Z M 11 5 L 15 11 M 15 5 L 11 11"
                : "M 1 6 L 4 6 L 8 2 L 8 14 L 4 10 L 1 10 Z M 11 5 Q 15 8 11 11");
            muteGlyph.Stroke = state.Muted ? Accent : Muted;
            muteButton.ToolTip = state.Muted ? "Sesi aç" : "Sessize al";
            string volumeName = settings.PlayerVolume ? PlayerVolumeName : "Windows";
            AutomationProperties.SetName(muteButton, state.Muted ? volumeName + " sesini aç" : volumeName + " sesini sessize al");
            AutomationProperties.SetName(volumeSlider, volumeName + " ses düzeyi");
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (closed) return;
                    ThemePalette.Refresh();
                    ApplySettings(false);
                    if (settingsWindow != null) settingsWindow.SelectPage(settingsWindow.SelectedPage);
                }));
        }

        private void OnSystemParametersChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "HighContrast" || e.PropertyName == "ClientAreaAnimation")
                OnUserPreferenceChanged(sender, null);
        }

        private void UpdateShelfButtons()
        {
            copy.IsEnabled = remove.IsEnabled = files.SelectedItems.Count > 0;
            if (shelfUndo != null) { shelfUndo.IsEnabled = shelf.CanUndo; shelfLeft.IsEnabled = shelfRight.IsEnabled = shelfAssign.IsEnabled = files.SelectedItems.Count > 0; }
            if (shelfRecovery != null) shelfRecovery.Visibility = shelf.Paths.Count == 0 && shelf.CanUndo ? Visibility.Visible : Visibility.Collapsed;
        }

        private string[] SelectedPaths()
        {
            return files.SelectedItems.Cast<ListBoxItem>().Select(item => (string)item.Tag).ToArray();
        }

        private string[] AvailablePaths()
        {
            var paths = SelectedPaths().Where(FileShelf.Exists).ToArray();
            if (paths.Length == 0)
                shelfNotice.Text = "Seçilen dosyalar artık orijinal konumlarında bulunmuyor.";
            else if (paths.Length != files.SelectedItems.Count)
                shelfNotice.Text = "Seçilen bazı dosyalar artık erişilebilir değil.";
            return paths;
        }

        private void CopyFiles()
        {
            var paths = AvailablePaths();
            if (paths.Length == 0)
                return;
            try
            {
                var list = new StringCollection();
                list.AddRange(paths);
                var data = new DataObject();
                data.SetFileDropList(list);
                data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes((int)DragDropEffects.Copy)));
                Clipboard.SetDataObject(data, true);
                shelfNotice.Text = paths.Length + " dosya kopyalandı · Explorer'da Ctrl + V ile yapıştırın";
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                shelfNotice.Text = "Pano şu an meşgul. Tekrar deneyin.";
            }
        }

        private void RemoveFiles()
        {
            shelf.Remove(SelectedPaths());
            UpdateShelf();
        }

        private void StartFileDrag(object sender, MouseButtonEventArgs e)
        {
            dragStart = ItemsControl.ContainerFromElement(files, e.OriginalSource as DependencyObject) is ListBoxItem
                ? (Point?)e.GetPosition(files)
                : null;
        }

        private void TryDragFiles(object sender, MouseEventArgs e)
        {
            if (draggingOut || !dragStart.HasValue || e.LeftButton != MouseButtonState.Pressed)
                return;
            var now = e.GetPosition(files);
            if (Math.Abs(now.X - dragStart.Value.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(now.Y - dragStart.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;
            dragStart = null;
            var paths = AvailablePaths();
            if (paths.Length == 0)
                return;
            draggingOut = true;
            try
            {
                DragDrop.DoDragDrop(files, new DataObject(DataFormats.FileDrop, paths), DragDropEffects.Copy);
            }
            finally
            {
                draggingOut = false;
                collapse.Start();
                UpdateVisibility();
            }
        }

        private static Border Divider()
        {
            return new Border { Height = 1, Background = Stroke, Opacity = 0.6, Margin = new Thickness(0, 10, 0, 10) };
        }

        private static void RoundImage(Image image, double radius)
        {
            image.SizeChanged += (_, __) => image.Clip = new RectangleGeometry(
                new Rect(0, 0, image.ActualWidth, image.ActualHeight), radius, radius);
        }

    }
}
