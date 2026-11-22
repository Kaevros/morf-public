using System;
using System.Globalization;
using System.Media;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Widgates
{
    internal sealed partial class IslandWindow
    {
        private const string CountdownGlyph = "M 14 9 A 6 6 0 1 1 2 9 A 6 6 0 1 1 14 9 M 8 6 L 8 9 L 11 9 M 6 1 L 10 1";
        private const string StopwatchGlyph = "M 14 9 A 6 6 0 1 1 2 9 A 6 6 0 1 1 14 9 M 8 6 L 8 9 M 6 1 L 10 1 M 12 4 L 14 2";
        private static readonly Geometry CountdownIcon = Geometry.Parse(CountdownGlyph);
        private static readonly Geometry StopwatchIcon = Geometry.Parse(StopwatchGlyph);
        private static readonly Geometry FinishedIcon = Geometry.Parse("M 2 8 L 6 12 L 14 4");
        private static readonly Geometry ClockPlayIcon = Geometry.Parse("M 3 2 L 13 8 L 3 14 Z");
        private static readonly Geometry ClockPauseIcon = Geometry.Parse("M 5 3 L 5 13 M 11 3 L 11 13");
        private const int CompactClockIconWidth = 24;
        private const int StopwatchSaveInterval = 10000;

        private readonly ClockTools clocks;
        private readonly string stopwatchPath;
        private long? savedStopwatchElapsed, stopwatchSaveDue;
        private readonly StackPanel playerContent = new StackPanel();
        private DispatcherTimer clockToolsTick;
        private long? clockToolsWakeAt;
        private Border clockPanel, clockSheen;
        private Grid clockChooser;
        private StackPanel clockDuration;
        private Button compactClockTools, countdownChoice, stopwatchChoice, clockToggle, clockReset;
        private Path compactClockGlyph, clockPanelGlyph, clockActionGlyph;
        private TextBlock compactClockValue, clockTitle, clockValue, clockNotice;
        private TextBox clockHours, clockMinutes, clockSeconds;
        private bool clockSetupOpen;
        private ClockTool selectedClockTool = ClockTool.Countdown;

        private bool ShowCompactClockTools { get { return clocks.Preview != ClockTool.None && !settings.EdgeOnly && !settings.AutoHide; } }

        private void InitializeCompactClockTools()
        {
            clockToolsTick = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
            clockToolsTick.Tick += (_, __) => { clockToolsTick.Stop(); UpdateLiveVisuals(); };

            compactClockGlyph = TransportIcon(CountdownGlyph);
            compactClockGlyph.Width = compactClockGlyph.Height = 12;
            compactClockValue = Label("", 14, ThemePalette.Text, true);
            compactClockValue.Margin = new Thickness(6, 0, 0, 0);
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(compactClockGlyph);
            content.Children.Add(compactClockValue);
            compactClockTools = CreateCustomButton(content, OpenClockTools, null, "Zaman araçları", double.NaN, 28, subtle: true);
            compactClockTools.Margin = new Thickness(0);
            compactClockTools.HorizontalAlignment = HorizontalAlignment.Center;
            compactClockTools.VerticalAlignment = VerticalAlignment.Center;
            compactClockTools.Visibility = Visibility.Collapsed;
            compact.Children.Add(compactClockTools);
        }

        private Border CreateClockPanel()
        {
            var tools = new StackPanel();
            clockChooser = new Grid { Margin = new Thickness(0, 0, 0, 9) };
            clockChooser.ColumnDefinitions.Add(new ColumnDefinition());
            clockChooser.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var choices = new WrapPanel();
            countdownChoice = CreateClockChoice("Geri sayım", ClockTool.Countdown);
            stopwatchChoice = CreateClockChoice("Kronometre", ClockTool.Stopwatch);
            choices.Children.Add(countdownChoice);
            choices.Children.Add(stopwatchChoice);
            clockChooser.Children.Add(choices);
            var close = CreateCustomButton(TransportIcon("M 4 4 L 12 12 M 12 4 L 4 12"), CloseClockSetup,
                "Sayaç ayarını kapat", "Sayaç ayarını kapat", 26, 26, subtle: true);
            Grid.SetColumn(close, 1);
            clockChooser.Children.Add(close);
            tools.Children.Add(clockChooser);

            var row = new DockPanel();
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            clockActionGlyph = TransportIcon("M 3 2 L 13 8 L 3 14 Z");
            clockActionGlyph.Stroke = ThemePalette.Text;
            clockToggle = CreateCustomButton(clockActionGlyph, ToggleClockTool, "Başlat", "Başlat", 34, 32);
            clockReset = CreateCustomButton(TransportIcon("M 4 6 A 5 5 0 1 1 4 12 M 4 2 L 4 6 L 8 6"), ResetClockTool,
                "Sıfırla", "Sayacı sıfırla", 30, 30, subtle: true);
            actions.Children.Add(clockReset);
            actions.Children.Add(clockToggle);
            DockPanel.SetDock(actions, Dock.Right);
            row.Children.Add(actions);

            clockPanelGlyph = TransportIcon(CountdownGlyph);
            clockPanelGlyph.Width = clockPanelGlyph.Height = 16;
            clockPanelGlyph.Margin = new Thickness(0, 0, 9, 0);
            DockPanel.SetDock(clockPanelGlyph, Dock.Left);
            row.Children.Add(clockPanelGlyph);
            var readout = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            clockTitle = Label("", 11, Muted, true);
            clockValue = Label("00:00", 24, ThemePalette.Text, true);
            clockValue.FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            Typography.SetNumeralAlignment(clockValue, FontNumeralAlignment.Tabular);
            readout.Children.Add(clockTitle);
            readout.Children.Add(clockValue);
            clockDuration = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            clockHours = AddDurationField("Saat", "00");
            clockDuration.Children.Add(Label(":", 12, Muted));
            clockMinutes = AddDurationField("Dakika", "05");
            clockDuration.Children.Add(Label(":", 12, Muted));
            clockSeconds = AddDurationField("Saniye", "00");
            readout.Children.Add(clockDuration);
            row.Children.Add(readout);
            tools.Children.Add(row);

            clockNotice = Label("", 11, ThemePalette.Text);
            clockNotice.Margin = new Thickness(0, 8, 0, 0);
            clockNotice.TextWrapping = TextWrapping.Wrap;
            clockNotice.Visibility = Visibility.Collapsed;
            tools.Children.Add(clockNotice);
            clockSheen = new Border { CornerRadius = new CornerRadius(13), IsHitTestVisible = false };
            var layers = new Grid();
            layers.Children.Add(clockSheen);
            layers.Children.Add(tools);
            tools.Margin = new Thickness(9, 7, 7, 7);
            return new Border
            {
                Child = layers, Background = ShelfBg, Visibility = Visibility.Collapsed,
                CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Margin = new Thickness(0, 10, 0, 0)
            };
        }

        private void UpdateClockMaterial()
        {
            clockPanel.Background = material.InsetBackground ?? ShelfBg;
            clockPanel.BorderBrush = material.InsetEdge ?? Brushes.Transparent;
            clockSheen.Background = material.InsetBackground == null ? Brushes.Transparent : material.Highlight;
        }

        private Button CreateClockChoice(string label, ClockTool mode)
        {
            var button = CreateActionButton(label, () => SelectClockTool(mode), null, label);
            button.Height = 26;
            button.Margin = new Thickness(0, 0, 4, 0);
            button.Padding = new Thickness(7, 2, 7, 2);
            return button;
        }

        private TextBox AddDurationField(string name, string initialValue)
        {
            var input = new TextBox
            {
                Text = initialValue, Width = 32, MinHeight = 25, MaxLength = 2, FontSize = 13,
                Background = ThemePalette.Button, Foreground = ThemePalette.Text, BorderBrush = Brushes.Transparent, CaretBrush = ThemePalette.Text,
                TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center
            };
            var template = new ControlTemplate(typeof(TextBox));
            var surface = new FrameworkElementFactory(typeof(Border)) { Name = "FieldSurface" };
            surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            surface.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
            surface.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(BorderBrushProperty));
            surface.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            var editor = new FrameworkElementFactory(typeof(ScrollViewer)) { Name = "PART_ContentHost" };
            editor.SetValue(FocusableProperty, false);
            surface.AppendChild(editor);
            template.VisualTree = surface;
            var focus = new Trigger { Property = IsKeyboardFocusWithinProperty, Value = true };
            focus.Setters.Add(new Setter(Border.BorderBrushProperty, Accent, "FieldSurface"));
            template.Triggers.Add(focus);
            input.Template = template;
            AutomationProperties.SetName(input, "Geri sayım · " + name);
            input.ToolTip = name;
            PopupTheme.DelayTooltip(input);
            input.TextChanged += (_, __) => { if (clockNotice != null) SetClockNotice(""); };
            input.PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                ToggleClockTool(); e.Handled = true;
            };
            clockDuration.Children.Add(input);
            return input;
        }

        private static MenuItem CreateClockMenuItem(string name, string glyph, Action action)
        {
            var icon = new Grid { Width = 24, Height = 24 };
            icon.Children.Add(TransportIcon(glyph));
            var item = new MenuItem { Header = icon, ToolTip = name };
            AutomationProperties.SetName(item, name);
            PopupTheme.DelayTooltip(item);
            item.Click += (_, __) => action();
            return item;
        }

        private void OpenClockTools()
        {
            if (closed || SettingsOpen || Suppressed) return;
            applicationMenu.IsOpen = false;
            sessions.IsDropDownOpen = false;
            clockSetupOpen = true;
            RenderClockPanel(ClockTools.GetTickCount64());
            bool wasExpanded = expanded;
            OpenPinned();
            if (wasExpanded) UpdateClockLayout(false);
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (closed || !expanded || !clockPanel.IsVisible) return;
                clockToggle.Focus();
            }));
        }

        private void CloseClockSetup()
        {
            clockSetupOpen = false;
            SetClockNotice("");
            RefreshClockTools();
            launchButton.Focus();
        }

        private void SelectClockTool(ClockTool tool)
        {
            if (clocks.Preview != ClockTool.None || selectedClockTool == tool) return;
            selectedClockTool = tool;
            SetClockNotice("");
            RefreshClockTools();
        }

        private long ReadCountdownDuration()
        {
            int hours, minutes, seconds;
            if (!ReadDurationField(clockHours, 23, out hours) || !ReadDurationField(clockMinutes, 59, out minutes)
                || !ReadDurationField(clockSeconds, 59, out seconds)) return 0;
            return (hours * 3600L + minutes * 60 + seconds) * 1000;
        }

        private static bool ReadDurationField(TextBox field, int maximum, out int value)
        {
            return int.TryParse(field.Text.Length == 0 ? "0" : field.Text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value <= maximum;
        }

        private void ToggleClockTool()
        {
            long now = ClockTools.GetTickCount64();
            if (FinishCountdownIfDue(now)) { UpdateLiveVisuals(); return; }
            bool restoreFocus = clockDuration.IsKeyboardFocusWithin;
            bool saved = true;
            if (selectedClockTool == ClockTool.Stopwatch)
            {
                clocks.ToggleStopwatch(now);
                saved = SaveStopwatch(now);
            }
            else if (clocks.Countdown == CountdownPhase.Running) clocks.PauseCountdown(now);
            else if (clocks.Countdown == CountdownPhase.Paused) clocks.ResumeCountdown(now);
            else
            {
                long duration = ReadCountdownDuration();
                if (duration == 0) { SetClockNotice("1 saniye ile 23 saat 59 dakika 59 saniye arasında bir süre girin."); return; }
                clocks.StartCountdown(now, duration);
            }
            SetClockNotice(saved ? "" : "Kronometre durumu kaydedilemedi.");
            RefreshClockTools();
            if (restoreFocus) clockToggle.Focus();
        }

        private void ResetClockTool()
        {
            bool saved = true;
            if (selectedClockTool == ClockTool.Countdown) clocks.ResetCountdown();
            else
            {
                clocks.ResetStopwatch();
                saved = SaveStopwatch(ClockTools.GetTickCount64());
            }
            SetClockNotice(saved ? "" : "Kronometre durumu kaydedilemedi.");
            RefreshClockTools();
            clockToggle.Focus();
        }

        private bool SaveStopwatch(long now)
        {
            if (stopwatchPath == null) return true;
            stopwatchSaveDue = clocks.StopwatchRunning ? now + StopwatchSaveInterval : (long?)null;
            long? elapsed = clocks.StopwatchActive ? clocks.Elapsed(now) : (long?)null;
            if (elapsed == savedStopwatchElapsed) return true;
            if (!StopwatchState.Save(stopwatchPath, elapsed)) return false;
            savedStopwatchElapsed = elapsed;
            return true;
        }

        private void SetClockNotice(string text)
        {
            if (clockNotice.Text == text) return;
            clockNotice.Text = text;
            clockNotice.Visibility = text.Length != 0 ? Visibility.Visible : Visibility.Collapsed;
            if (expanded && clockPanel.IsVisible) UpdateClockLayout(false);
        }

        private void RefreshClockTools()
        {
            RenderClockPanel(ClockTools.GetTickCount64());
            UpdateCompactContent();
            UpdateClockLayout(true);
        }

        private void UpdateClockLayout(bool animate)
        {
            // Commit nested visibility changes before measuring the expanded shell.
            if (expanded) panel.UpdateLayout();
            UpdateState(animate);
        }

        private void ConfigureCompactClockTools(bool visible, bool playing)
        {
            compactClockTools.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (!visible) return;
            Grid.SetColumn(compactClockTools, playing ? 3 : 0);
            Grid.SetColumnSpan(compactClockTools, playing ? 1 : 4);
            compactClockTools.Width = playing ? CompactClockIconWidth : double.NaN;
            compactClockValue.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
            var preview = clocks.Preview;
            bool finished = preview == ClockTool.Countdown && clocks.Countdown == CountdownPhase.Finished;
            compactClockGlyph.Data = finished ? FinishedIcon : preview == ClockTool.Countdown ? CountdownIcon : StopwatchIcon;
            compactClockGlyph.Stroke = Accent;
            string title = finished ? "Geri sayım tamamlandı" : preview == ClockTool.Countdown ? "Geri sayım" : "Kronometre";
            compactClockTools.ToolTip = title + " · Zaman araçlarını aç";
            AutomationProperties.SetName(compactClockTools, (string)compactClockTools.ToolTip);
        }

        private bool FinishCountdownIfDue(long now)
        {
            if (!clocks.CheckCountdown(now)) return false;
            UpdateCompactContent();
            SystemSounds.Exclamation.Play();
            return true;
        }

        private void UpdateClockTools(bool visible, bool compactVisible)
        {
            if (closed) return;
            long now = ClockTools.GetTickCount64();
            FinishCountdownIfDue(now);
            if (stopwatchSaveDue.HasValue && now >= stopwatchSaveDue.Value) SaveStopwatch(now);
            long remaining = clocks.Remaining(now), elapsed = clocks.Elapsed(now);
            bool showPanel = visible && expanded && clockPanel.Visibility == Visibility.Visible;
            bool showCompactValue = compactVisible && ShowCompactClockTools && !media.IsPlaying;
            var preview = clocks.Preview;
            if (showPanel) RenderClockPanel(now);
            if (showCompactValue)
            {
                string text = preview == ClockTool.Countdown ? ClockTools.Format(remaining, true) : ClockTools.Format(elapsed, false);
                if (compactClockValue.Text != text)
                {
                    compactClockValue.Text = text;
                    AutomationProperties.SetName(compactClockValue, text);
                }
            }

            // Hidden views wake only to save a running stopwatch or complete a countdown.
            long? wakeAt = stopwatchSaveDue;
            if (clocks.Countdown == CountdownPhase.Running)
            {
                long next = remaining;
                if (showPanel || (showCompactValue && preview == ClockTool.Countdown))
                    next = Math.Min(remaining, (remaining - 1) % 1000 + 1);
                wakeAt = now + next;
            }
            if (clocks.StopwatchRunning && (showPanel || (showCompactValue && preview == ClockTool.Stopwatch)))
                wakeAt = Math.Min(wakeAt ?? long.MaxValue, now + 1000 - elapsed % 1000);
            if (wakeAt == clockToolsWakeAt && (!wakeAt.HasValue || clockToolsTick.IsEnabled)) return;
            clockToolsWakeAt = wakeAt;
            clockToolsTick.Stop();
            if (!wakeAt.HasValue) return;
            clockToolsTick.Interval = TimeSpan.FromMilliseconds(Math.Max(1, wakeAt.Value - now));
            clockToolsTick.Start();
        }

        private void RenderClockPanel(long now)
        {
            bool active = clocks.Preview != ClockTool.None;
            clockPanel.Visibility = clockSetupOpen || active ? Visibility.Visible : Visibility.Collapsed;
            if (clockPanel.Visibility == Visibility.Collapsed) return;

            bool countdown = selectedClockTool == ClockTool.Countdown;
            var phase = clocks.Countdown;
            bool finished = phase == CountdownPhase.Finished;
            bool running = countdown ? phase == CountdownPhase.Running : clocks.StopwatchRunning;
            bool editing = countdown && !active;
            string tool = countdown ? "Geri sayım" : "Kronometre";
            clockChooser.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
            ((TextBlock)countdownChoice.Content).Foreground = countdown ? Accent : Muted;
            ((TextBlock)stopwatchChoice.Content).Foreground = countdown ? Muted : Accent;
            clockPanelGlyph.Data = finished ? FinishedIcon : countdown ? CountdownIcon : StopwatchIcon;
            clockPanelGlyph.Stroke = active ? Accent : Muted;
            clockTitle.Text = finished ? "Süre doldu" : editing ? "Saat · Dakika · Saniye" : tool;
            clockTitle.Foreground = finished ? Accent : Muted;
            clockDuration.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            clockValue.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            if (!editing) clockValue.Text = ClockTools.Format(countdown ? clocks.Remaining(now) : clocks.Elapsed(now), countdown);
            clockReset.Visibility = active ? Visibility.Visible : Visibility.Collapsed;

            string action = running ? "Duraklat" : finished ? "Yeniden başlat" : active ? "Sürdür" : "Başlat";
            clockActionGlyph.Data = running ? ClockPauseIcon : ClockPlayIcon;
            string name = tool + " · " + action;
            if (AutomationProperties.GetName(clockToggle) != name)
            {
                clockToggle.ToolTip = action;
                AutomationProperties.SetName(clockToggle, name);
            }
        }
    }
}
