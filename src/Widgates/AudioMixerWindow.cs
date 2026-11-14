using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Widgates
{
    internal sealed class AudioMixerWindow : Window
    {
        private readonly AudioMirror mirror;
        private readonly Func<AudioDevice[]> readDevices;
        private readonly Func<string, MixerChannel[]> readChannels;
        private readonly Func<MixerChannel, float?, bool?, VolumeState> useChannel;
        private readonly ComboBox devices;
        private readonly StackPanel columns, targets;
        private readonly ScrollViewer channelsScroll;
        private readonly TextBlock notice, mirrorStatus;
        private readonly Button startMirror, stopMirror;
        private readonly List<CheckBox> targetChoices = new List<CheckBox>();
        private bool disposed, loading, writing, loadingDevices, pendingRefresh;
        private int revision;
        private AudioMixerWatch watch;
        private readonly bool watchNative;
        private int changeQueued;
        private readonly SemaphoreSlim writeGate = new SemaphoreSlim(1);
        private int pendingWrites;
        private string renderedDevice;
        private string[] renderedChannels = new string[0];
        private readonly Dictionary<string, Action<MixerChannel>> columnUpdates = new Dictionary<string, Action<MixerChannel>>();
        internal AudioMixerWindow(AudioMirror mirror, Func<AudioDevice[]> readDevices = null, Func<string, MixerChannel[]> readChannels = null,
            Func<MixerChannel, float?, bool?, VolumeState> useChannel = null)
        {
            this.mirror = mirror; this.readDevices = readDevices ?? AudioDevices.List; this.readChannels = readChannels ?? AudioDevices.Channels; this.useChannel = useChannel ?? AudioDevices.Use;
            watchNative = readDevices == null && readChannels == null;
            Title = "Morf · Ses karıştırıcısı"; Icon = BrandAssets.WindowIcon;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize;
            Width = Math.Min(600, Math.Max(260, SystemParameters.WorkArea.Width - 24)); SizeToContent = SizeToContent.Height;
            MaxHeight = Math.Max(180, SystemParameters.WorkArea.Height - 24); Topmost = true;
            PopupTheme.ApplyControls(Resources); PopupTheme.ApplyTooltips(Resources);
            var surface = new Border { Background = ThemePalette.Surface, BorderBrush = ThemePalette.Stroke, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(18) };
            Content = surface;
            var body = new StackPanel();
            var viewport = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            surface.Child = viewport;
            var header = new DockPanel(); var close = Button("×", Close); close.Width = 32; AutomationProperties.SetName(close, "Ses karıştırıcısını kapat");
            DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
            var title = Text("Ses karıştırıcısı", 17, true); title.VerticalAlignment = VerticalAlignment.Center; header.Children.Add(title);
            header.MouseLeftButtonDown += (_, e) => { if (!(e.OriginalSource is Button) && e.LeftButton == MouseButtonState.Pressed) { try { DragMove(); } catch (InvalidOperationException) { } } };
            body.Children.Add(header);
            var deviceRow = new DockPanel { Margin = new Thickness(0, 10, 0, 10) };
            var refresh = Button("Yenile", async () => await RefreshAsync(true)); DockPanel.SetDock(refresh, Dock.Right); deviceRow.Children.Add(refresh);
            devices = new ComboBox { MinHeight = 34, Margin = new Thickness(0, 0, 6, 0), Foreground = ThemePalette.Text, Background = ThemePalette.Card }; IslandWindow.StyleSessionPicker(devices);
            devices.ToolTip = "Seslerini görüntülemek istediğiniz çıkışı seçin";
            AutomationProperties.SetName(devices, "Ses karıştırıcısında gösterilen çıkış"); deviceRow.Children.Add(devices); body.Children.Add(deviceRow);
            devices.SelectionChanged += async (_, __) => { if (!loadingDevices) await RefreshAsync(false); };
            columns = new StackPanel { Orientation = Orientation.Horizontal };
            channelsScroll = new ScrollViewer { Content = columns, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 0, 0, 8) };
            IslandWindow.StyleShelfScrollBar(channelsScroll, true);
            AutomationProperties.SetName(channelsScroll, "Uygulama sesleri · yatay kaydırma");
            channelsScroll.PreviewMouseWheel += (_, e) => {
                var source = e.OriginalSource as DependencyObject;
                while (source is Visual) { if (source is Slider) return; source = VisualTreeHelper.GetParent(source); }
                channelsScroll.ScrollToHorizontalOffset(channelsScroll.HorizontalOffset - e.Delta); e.Handled = true;
            };
            body.Children.Add(channelsScroll);
            notice = Text("Ses oturumları yükleniyor…", 11); notice.Foreground = ThemePalette.Muted; notice.Margin = new Thickness(0, 4, 0, 8); body.Children.Add(notice);
            var routing = Button("Uygulama çıkışları ↗", OpenWindowsMixer);
            routing.ToolTip = "Her uygulamanın ses çıkışını Windows ayarlarında seçin";
            AutomationProperties.SetName(routing, "Uygulama çıkışlarını Windows'ta ayarla"); body.Children.Add(routing);
            var sharing = new Expander { Header = "Birden fazla çıkışta çal", Foreground = ThemePalette.Text, Margin = new Thickness(0, 5, 0, 0) };
            var shareContent = new StackPanel { Margin = new Thickness(0, 10, 0, 0) }; sharing.Content = shareContent; body.Children.Add(sharing);
            var info = Text("Seçili çıkışın tüm seslerini aşağıdaki cihazlara da gönderir. Cihazlar arasında gecikme olabilir.", 11);
            info.Foreground = ThemePalette.Muted; shareContent.Children.Add(info);
            targets = new StackPanel { Margin = new Thickness(0, 8, 0, 8) }; shareContent.Children.Add(targets);
            var actions = new WrapPanel(); startMirror = Button("Başlat", async () => await StartMirrorAsync()); stopMirror = Button("Durdur", () => mirror.Stop());
            startMirror.Background = ThemePalette.Button; stopMirror.Background = ThemePalette.Button;
            actions.Children.Add(startMirror); actions.Children.Add(stopMirror); shareContent.Children.Add(actions);
            mirrorStatus = Text("", 11); mirrorStatus.Foreground = ThemePalette.Muted; mirrorStatus.Margin = new Thickness(0, 6, 0, 0); shareContent.Children.Add(mirrorStatus);
            var persistence = Text("Pencere kapansa da çalar; Durdur ile sonlanır. Ses kaydedilmez.", 10);
            persistence.Foreground = ThemePalette.Muted; shareContent.Children.Add(persistence);
            mirror.Changed += OnMirrorChanged;
            PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && !devices.IsDropDownOpen) { e.Handled = true; Close(); } };
            Loaded += async (_, __) => await RefreshAsync(true);
            Activated += async (_, __) => { if (IsLoaded && !disposed) await RefreshAsync(true); };
            Closed += (_, __) => { disposed = true; revision++; mirror.Changed -= OnMirrorChanged; var previous = watch; watch = null; if (previous != null) Task.Run(() => previous.Dispose()); };
            UpdateMirrorState();
        }
        private static TextBlock Text(string value, double size, bool bold = false)
        {
            return new TextBlock { Text = value, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Foreground = ThemePalette.Text,
                TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Segoe UI") };
        }
        private static Button Button(string value, Action action)
        {
            var button = SettingsWindow.Button(value, action); button.Padding = new Thickness(10, 7, 10, 7); button.Margin = new Thickness(0, 0, 4, 0);
            AutomationProperties.SetName(button, value); return button;
        }
        private void ShowNotice(string value)
        {
            notice.Text = value;
            notice.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
        }
        internal async Task RefreshAsync(bool updateDevices)
        {
            if (disposed) return;
            if (writing || loading) { pendingRefresh = true; return; }
            loading = true; int version = ++revision;
            try
            {
                string selected = devices.SelectedItem == null ? null : ((AudioDevice)devices.SelectedItem).Id;
                if (updateDevices)
                {
                    var list = await Task.Run(readDevices); if (disposed || revision != version) return;
                    loadingDevices = true; devices.ItemsSource = list;
                    devices.SelectedItem = list.FirstOrDefault(x => x.Id == selected) ?? list.FirstOrDefault(); loadingDevices = false;
                    UpdateTargets(list);
                }
                var device = devices.SelectedItem as AudioDevice;
                if (device == null) { columns.Children.Clear(); ShowNotice("Kullanılabilir ses çıkışı yok."); return; }
                var channels = await Task.Run(() => readChannels(device.Id)); if (disposed || revision != version) return;
                if (Mouse.Captured != null) { pendingRefresh = true; return; }
                var channelKeys = channels.Select(ChannelKey).ToArray();
                bool rebuild = renderedDevice != device.Id || !renderedChannels.SequenceEqual(channelKeys);
                if (watchNative && (watch == null || rebuild))
                {
                    var previous = watch; watch = null;
                    if (previous != null) await Task.Run(() => previous.Dispose());
                    AudioMixerWatch next = null;
                    try { next = await Task.Run(() => new AudioMixerWatch(device.Id, QueueAudioChange)); } catch (Exception) { }
                    if (disposed || version != revision) { if (next != null) await Task.Run(() => next.Dispose()); return; }
                    watch = next;
                }
                if (rebuild)
                {
                    double offset = renderedDevice == device.Id ? channelsScroll.HorizontalOffset : 0;
                    columns.Children.Clear(); columnUpdates.Clear(); foreach (var channel in channels) columns.Children.Add(Column(channel));
                    renderedDevice = device.Id; renderedChannels = channelKeys;
                    channelsScroll.UpdateLayout(); channelsScroll.ScrollToHorizontalOffset(offset);
                }
                else foreach (var channel in channels) columnUpdates[ChannelKey(channel)](channel);
                ShowNotice(channels.Length <= 1 ? "Uygulamaların görünmesi için ses oynatın." : "");
                UpdateTargets((devices.ItemsSource as AudioDevice[]) ?? new AudioDevice[0]);
            }
            catch (Exception) { if (!disposed) ShowNotice("Ses çıkışına erişilemiyor. Yenile ile tekrar deneyebilirsiniz."); }
            finally
            {
                loading = false; loadingDevices = false;
                SchedulePendingRefresh();
            }
        }
        private FrameworkElement Column(MixerChannel channel)
        {
            var body = new StackPanel { Width = 94, Margin = new Thickness(0, 0, 10, 4) };
            var icon = IslandWindow.SourceIcon(channel.AppId, channel.Master ? "M 1 6 L 4 6 L 8 2 L 8 14 L 4 10 L 1 10 Z M 11 5 Q 15 8 11 11" : "M 6 4 L 14 2 L 14 12 M 6 4 L 6 14 M 6 7 L 14 5 M 6 13 C 2 11 1 16 4 16 C 6 16 6 14 6 13 M 14 11 C 10 9 9 14 12 14 C 14 14 14 12 14 11", 24);
            icon.HorizontalAlignment = HorizontalAlignment.Center; icon.Margin = new Thickness(0, 4, 0, 6); body.Children.Add(icon);
            var name = Text(channel.Name, 11, true); name.TextAlignment = TextAlignment.Center; name.TextTrimming = TextTrimming.CharacterEllipsis; name.Height = 34; name.ToolTip = channel.Name; body.Children.Add(name);
            var value = Text("%" + Math.Round(channel.State.Level * 100), 11); value.TextAlignment = TextAlignment.Center; value.Margin = new Thickness(0, 3, 0, 4); body.Children.Add(value);
            var slider = new Slider { Minimum = 0, Maximum = 100, Value = channel.State.Level * 100, SmallChange = 2, LargeChange = 10,
                Orientation = Orientation.Vertical, Height = 142, Width = 36, HorizontalAlignment = HorizontalAlignment.Center, IsMoveToPointEnabled = true, IsEnabled = channel.State.Available };
            ApplyVerticalSlider(slider); AutomationProperties.SetName(slider, channel.Name + " ses düzeyi"); body.Children.Add(slider);
            var mute = Button(channel.State.Muted ? "Sesi aç" : "Sessiz", () => { });
            mute.Foreground = channel.State.Muted ? ThemePalette.Accent : ThemePalette.Text;
            mute.HorizontalAlignment = HorizontalAlignment.Center; mute.IsEnabled = channel.State.Available; AutomationProperties.SetName(mute, channel.Name + " sessize al veya aç"); body.Children.Add(mute);
            bool gesture = false;
            int editVersion = 0;
            Func<float?, bool?, Task> commit = async (level, muted) => {
                if (disposed) return;
                writing = true; pendingWrites++; int version = revision, edit = ++editVersion; mute.IsEnabled = false;
                await writeGate.WaitAsync();
                try
                {
                    if (disposed || version != revision) return;
                    var state = await Task.Run(() => useChannel(channel, level, muted)); if (disposed || version != revision) return;
                    channel.State = state;
                    if (!state.Available) { value.Text = "Yok"; ShowNotice("Ses oturumu kapandı veya çıkış bağlantısı kesildi. Listeyi yenileyin."); return; }
                    if (edit == editVersion) { slider.Value = state.Level * 100; mute.Content = state.Muted ? "Sesi aç" : "Sessiz"; mute.Foreground = state.Muted ? ThemePalette.Accent : ThemePalette.Text; slider.IsEnabled = mute.IsEnabled = true; }
                }
                catch (Exception) { if (!disposed) ShowNotice("Ses değiştirilemedi. Listeyi yenileyin."); }
                finally { writeGate.Release(); pendingWrites--; writing = pendingWrites > 0; if (!writing) SchedulePendingRefresh(); }
            };
            columnUpdates[ChannelKey(channel)] = fresh => { channel.State = fresh.State; name.Text = fresh.Name; slider.Value = fresh.State.Level * 100; slider.IsEnabled = mute.IsEnabled = fresh.State.Available; mute.Content = fresh.State.Muted ? "Sesi aç" : "Sessiz"; mute.Foreground = fresh.State.Muted ? ThemePalette.Accent : ThemePalette.Text; };
            slider.ValueChanged += (_, __) => value.Text = "%" + Math.Round(slider.Value);
            slider.PreviewMouseLeftButtonDown += (_, __) => gesture = true;
            slider.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(async (_, __) => { if (gesture) { gesture = false; await commit((float)slider.Value / 100, slider.Value == 0 ? (bool?)true : false); } }), true);
            slider.PreviewKeyUp += async (_, e) => { if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Home || e.Key == Key.End || e.Key == Key.PageUp || e.Key == Key.PageDown) await commit((float)slider.Value / 100, slider.Value == 0); };
            slider.PreviewMouseWheel += async (_, e) => { e.Handled = true; float level = (float)Math.Max(0, Math.Min(1, slider.Value / 100 + Math.Sign(e.Delta) * 0.02)); slider.Value = level * 100; await commit(level, level == 0); };
            mute.Click += async (_, __) => await commit(null, !channel.State.Muted);
            return new Border { Child = body, BorderBrush = ThemePalette.Stroke, BorderThickness = new Thickness(0, 0, 1, 0), Margin = new Thickness(0, 0, 8, 0) };
        }
        private static string ChannelKey(MixerChannel channel) { return channel.Master ? "master" : channel.InstanceId; }
        internal static void ApplyVerticalSlider(Slider slider)
        {
            var template = new ControlTemplate(typeof(Slider)); var grid = new FrameworkElementFactory(typeof(Grid));
            var line = new FrameworkElementFactory(typeof(Border)); line.SetValue(FrameworkElement.WidthProperty, 4.0); line.SetValue(Border.CornerRadiusProperty, new CornerRadius(2)); line.SetValue(Border.BackgroundProperty, ThemePalette.Stroke); line.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 14, 0, 14)); grid.AppendChild(line);
            var track = new FrameworkElementFactory(typeof(Track), "PART_Track"); track.SetValue(Track.OrientationProperty, Orientation.Vertical);
            foreach (var property in new[] { Track.MinimumProperty, Track.MaximumProperty, Track.ValueProperty, Track.IsDirectionReversedProperty })
                track.SetBinding(property, new Binding(property.Name) { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            var repeatTemplate = new ControlTemplate(typeof(RepeatButton)); var fill = new FrameworkElementFactory(typeof(Border)); fill.SetValue(FrameworkElement.WidthProperty, 4.0); fill.SetValue(Border.BackgroundProperty, ThemePalette.Accent); fill.SetValue(Border.CornerRadiusProperty, new CornerRadius(2)); repeatTemplate.VisualTree = fill;
            // Track parts must be assigned as properties, rather than visual children.
            var thumb = new Thumb { Width = 28, Height = 28, Focusable = false };
            var thumbTemplate = new ControlTemplate(typeof(Thumb)); var dot = new FrameworkElementFactory(typeof(Border), "Handle"); dot.SetValue(FrameworkElement.WidthProperty, 18.0); dot.SetValue(FrameworkElement.HeightProperty, 18.0); dot.SetValue(Border.BackgroundProperty, ThemePalette.Text); dot.SetValue(Border.BorderBrushProperty, ThemePalette.Stroke); dot.SetValue(Border.BorderThicknessProperty, new Thickness(1)); dot.SetValue(Border.CornerRadiusProperty, new CornerRadius(10)); thumbTemplate.VisualTree = dot;
            foreach (var property in new[] { UIElement.IsMouseOverProperty, Thumb.IsDraggingProperty }) { var trigger = new Trigger { Property = property, Value = true }; trigger.Setters.Add(new Setter(FrameworkElement.WidthProperty, 20.0, "Handle")); trigger.Setters.Add(new Setter(FrameworkElement.HeightProperty, 20.0, "Handle")); thumbTemplate.Triggers.Add(trigger); } thumb.Template = thumbTemplate;
            grid.AppendChild(track); template.VisualTree = grid; slider.Template = template;
            slider.ApplyTemplate();
            var actual = slider.Template.FindName("PART_Track", slider) as Track;
            if (actual != null) { actual.Thumb = thumb; actual.DecreaseRepeatButton = new RepeatButton { Command = Slider.DecreaseLarge, Focusable = false, Template = repeatTemplate }; actual.IncreaseRepeatButton = new RepeatButton { Command = Slider.IncreaseLarge, Focusable = false, Opacity = 0 }; }
        }
        private void UpdateTargets(AudioDevice[] list)
        {
            bool running = !mirror.Completion.IsCompleted;
            var selected = new HashSet<string>(running ? mirror.TargetIds : targetChoices.Where(x => x.IsChecked == true).Select(x => (string)x.Tag), StringComparer.OrdinalIgnoreCase);
            targetChoices.Clear(); targets.Children.Clear(); var source = running ? list.FirstOrDefault(x => x.Id == mirror.SourceId) : devices.SelectedItem as AudioDevice;
            foreach (var device in list.Where(x => source == null || x.Id != source.Id))
            {
                var choice = new CheckBox { Content = device.Name, Tag = device.Id, IsChecked = selected.Contains(device.Id), Foreground = ThemePalette.Text, Margin = new Thickness(0, 4, 0, 4), IsEnabled = !mirror.Active };
                ApplyTargetChoice(choice);
                AutomationProperties.SetName(choice, device.Name + " ek ses çıkışı"); targets.Children.Add(choice); targetChoices.Add(choice);
            }
            if (targetChoices.Count == 0) targets.Children.Add(Text("Çoğaltmak için ikinci bir ses çıkışı bağlayın.", 11));
            UpdateMirrorState();
        }
        private static void ApplyTargetChoice(CheckBox choice)
        {
            var template = new ControlTemplate(typeof(CheckBox)); var row = new FrameworkElementFactory(typeof(DockPanel)); row.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
            var square = new FrameworkElementFactory(typeof(Border), "Square"); square.SetValue(FrameworkElement.WidthProperty, 20.0); square.SetValue(FrameworkElement.HeightProperty, 20.0); square.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0)); square.SetValue(Border.CornerRadiusProperty, new CornerRadius(5)); square.SetValue(Border.BorderBrushProperty, ThemePalette.Stroke); square.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            var mark = new FrameworkElementFactory(typeof(TextBlock), "Mark"); mark.SetValue(TextBlock.TextProperty, "✓"); mark.SetValue(TextBlock.ForegroundProperty, ThemePalette.Accent); mark.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold); mark.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); mark.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); mark.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed); square.AppendChild(mark); row.AppendChild(square);
            var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true); row.AppendChild(content);
            template.VisualTree = row; var check = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true }; check.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "Mark")); check.Setters.Add(new Setter(Border.BorderBrushProperty, ThemePalette.Accent, "Square")); template.Triggers.Add(check);
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true }; focus.Setters.Add(new Setter(Border.BorderBrushProperty, ThemePalette.Text, "Square")); template.Triggers.Add(focus);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45)); template.Triggers.Add(disabled);
            choice.Template = template; choice.MinHeight = 28; choice.Cursor = Cursors.Hand;
        }
        private void SchedulePendingRefresh()
        {
            if (!pendingRefresh || disposed || Mouse.Captured != null) return;
            pendingRefresh = false;
            Dispatcher.BeginInvoke(new Action(async () => { if (!disposed) await RefreshAsync(true); }));
        }
        private void QueueAudioChange()
        {
            if (disposed || Dispatcher.HasShutdownStarted || Interlocked.CompareExchange(ref changeQueued, 1, 0) != 0) return;
            Dispatcher.BeginInvoke(new Action(async () => {
                Interlocked.Exchange(ref changeQueued, 0); if (disposed) return;
                if (Mouse.Captured != null || writing || loading) { pendingRefresh = true; return; }
                await RefreshAsync(true);
            }));
        }
        private async Task StartMirrorAsync()
        {
            var source = devices.SelectedItem as AudioDevice; if (source == null) return;
            var started = mirror.StartAsync(source.Id, targetChoices.Where(x => x.IsChecked == true).Select(x => (string)x.Tag).ToArray());
            UpdateMirrorState();
            string result = await started;
            if (!disposed) { UpdateMirrorState(); mirrorStatus.Text = result; }
        }
        private void OnMirrorChanged()
        {
            if (!disposed && !Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(() => { if (!disposed) UpdateMirrorState(); }));
        }
        private void UpdateMirrorState()
        {
            var source = (devices.ItemsSource as AudioDevice[] ?? new AudioDevice[0]).FirstOrDefault(x => x.Id == mirror.SourceId);
            mirrorStatus.Text = mirror.Status + (!mirror.Completion.IsCompleted && source != null ? " · Kaynak: " + source.Name : ""); stopMirror.IsEnabled = !mirror.Completion.IsCompleted;
            startMirror.IsEnabled = mirror.Completion.IsCompleted && targetChoices.Count > 0;
            foreach (var choice in targetChoices) choice.IsEnabled = mirror.Completion.IsCompleted;
        }
        private void OpenWindowsMixer()
        {
            try { using (var process = Process.Start(new ProcessStartInfo("ms-settings:apps-volume") { UseShellExecute = true })) { } }
            catch { ShowNotice("Windows ses karıştırıcısı açılamadı."); }
        }
    }
}
