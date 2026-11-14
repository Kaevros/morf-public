using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Widgates
{
    // Created on demand and released on close. No media subscription, polling or display clock.
    internal sealed partial class SettingsWindow : Window
    {
        private readonly WidgetSettings settings;
        private readonly Action<bool> apply;
        private readonly Func<ThemePreference, bool> selectTheme;
        private readonly Func<bool, bool> configureStartup;
        private readonly SettingsActions actions;
        private readonly StackPanel page;
        private readonly Button[] navigation;
        private readonly TextBlock heading, subtitle, notice;
        private readonly ScrollViewer scroll;
        private bool dirty;
        private TextBlock sleepStatus, mediaStatus;
        private Button sleepStart;
        private StackPanel cardTarget;
        private readonly TextBlock shortcuts;
        private Border resetConfirmation;
        private readonly Grid layout, sidebar, body;
        private readonly ComboBox compactNavigation;
        private readonly Button compactExit;
        private readonly Button refreshButton, compactRefresh;
        private Border exitOverlay;
        private bool refreshingApplication;
        private bool syncingNavigation;
        internal int SelectedPage { get; private set; }
        private static readonly string[] PageNames = { "Görünüm", "Davranış", "Medya ve ses", "Sistem", "Kısayollar", "Hakkında ve bakım", "Uygulamalar" };
        private static readonly int[] PageOrder = { 0, 1, 2, 6, 3, 4, 5 };
        private static readonly string[] PageDescriptions = {
            "Morf'un temasını ve yüzeyini kişiselleştirin.",
            "Ekranda ne kadar yer kaplayacağını ve nasıl açılacağını seçin.",
            "Oynatma önceliğini, süreyi ve ses denetimini yönetin.",
            "Oyunlar, ekranlar, güç kullanımı ve Windows başlangıcını yönetin.",
            "Kişisel tuşlarınızı sürekli klavye taraması olmadan kullanın.",
            "Sürüm, yerel tanılama ve varsayılan ayarlar.",
            "Uygulama bağlantılarını ve süreli bildirimleri yönetin." };

        internal SettingsWindow(WidgetSettings settings, Action<bool> apply, Func<ThemePreference, bool> selectTheme,
            Func<bool, bool> configureStartup, Action exit)
            : this(settings, new SettingsActions { Apply = apply, SelectTheme = selectTheme, ConfigureStartup = configureStartup, Exit = exit }) { }

        internal SettingsWindow(WidgetSettings settings, SettingsActions actions)
        {
            this.settings = settings; this.actions = actions; this.apply = actions.Apply; this.selectTheme = actions.SelectTheme; this.configureStartup = actions.ConfigureStartup;
            Title = "Morf · Ayarlar"; Icon = BrandAssets.WindowIcon;
            Width = Math.Min(760, Math.Max(240, SystemParameters.WorkArea.Width - 24));
            Height = Math.Min(640, Math.Max(240, SystemParameters.WorkArea.Height - 24));
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
            ShowInTaskbar = false; Background = Brushes.Transparent;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"); FontSize = 13;
            UseLayoutRounding = true;
            PopupTheme.ApplyControls(Resources); PopupTheme.ApplyTooltips(Resources);
            var root = new Border { Background = ThemePalette.Surface, BorderBrush = ThemePalette.Stroke,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), ClipToBounds = true };
            Content = root;
            layout = new Grid(); root.Child = layout;
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(176) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sidebar = new Grid { Background = ThemePalette.Shelf, Margin = new Thickness(8, 8, 0, 8) };
            sidebar.SizeChanged += (_, __) => sidebar.Clip = new RectangleGeometry(new Rect(0, 0, sidebar.ActualWidth, sidebar.ActualHeight), 12, 12);
            sidebar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            sidebar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            sidebar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.Children.Add(sidebar);
            var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 19, 8, 26) };
            brand.Children.Add(new Image { Source = BrandAssets.HeaderLogo, Width = 30, Height = 30, Margin = new Thickness(0, 0, 9, 0) });
            brand.Children.Add(Text("Morf", 20, true)); sidebar.Children.Add(brand);
            var nav = new StackPanel { Margin = new Thickness(6, 0, 8, 0) };
            var navScroll = new ScrollViewer { Content = nav, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetRow(navScroll, 1); sidebar.Children.Add(navScroll);
            navigation = new Button[PageNames.Length];
            foreach (int i in PageOrder)
            {
                int index = i;
                var button = Button(PageNames[i], () => SelectPage(index), navigation: true);
                button.HorizontalContentAlignment = HorizontalAlignment.Left;
                button.Margin = new Thickness(0, 0, 0, 6);
                navigation[i] = button; nav.Children.Add(button);
            }
            var bottom = new StackPanel { Margin = new Thickness(14, 12, 12, 12) };
            shortcuts = Text(settings.PanelHotkey + " · Aç / kapat\nEsc · Paneli kapat", 10);
            shortcuts.Foreground = ThemePalette.Muted; shortcuts.Margin = new Thickness(0, 0, 0, 14);
            bottom.Children.Add(shortcuts);
            refreshButton = Button("Uygulamayı yinele", async () => await RefreshApplicationAsync());
            refreshButton.ToolTip = "Görünümü ve bağlantıları yenile; raf ve ayarlar korunur.";
            bottom.Children.Add(refreshButton);
            var exitButton = Button("Morf'tan Çık", RequestExitConfirmation, true);
            AutomationProperties.SetName(exitButton, "Morf uygulamasından çık"); bottom.Children.Add(exitButton);
            Grid.SetRow(bottom, 2); sidebar.Children.Add(bottom);
            body = new Grid { Margin = new Thickness(26, 20, 22, 16) }; Grid.SetColumn(body, 1); layout.Children.Add(body);
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var titleRow = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
            var close = Button("×", () => Close()); close.Width = 30; close.Height = 30; close.Padding = new Thickness(0);
            close.FontSize = 21; close.VerticalAlignment = VerticalAlignment.Top;
            AutomationProperties.SetName(close, "Yalnızca ayarları kapat");
            DockPanel.SetDock(close, Dock.Right); titleRow.Children.Add(close);
            var labels = new StackPanel(); heading = Text("", 22, true); subtitle = Text("", 11);
            compactNavigation = new ComboBox { ItemsSource = PageOrder.Select(index => new ComboBoxItem { Content = PageNames[index], Tag = index }).ToArray(), SelectedValuePath = "Tag", SelectedValue = 0, Visibility = Visibility.Collapsed, MinHeight = 34, Foreground = ThemePalette.Text };
            IslandWindow.StyleSessionPicker(compactNavigation); AutomationProperties.SetName(compactNavigation, "Ayar bölümü");
            compactNavigation.SelectionChanged += (_, __) => { if (!syncingNavigation && compactNavigation.SelectedValue is int) SelectPage((int)compactNavigation.SelectedValue); };
            labels.Children.Add(compactNavigation);
            subtitle.Foreground = ThemePalette.Muted; subtitle.Margin = new Thickness(0, 7, 8, 0);
            labels.Children.Add(heading); labels.Children.Add(subtitle); titleRow.Children.Add(labels);
            titleRow.MouseLeftButtonDown += (_, e) => { if (e.Source == titleRow || e.Source is TextBlock) { try { DragMove(); } catch (InvalidOperationException) { } } };
            body.Children.Add(titleRow);
            page = new StackPanel();
            scroll = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 10, 0) };
            Grid.SetRow(scroll, 1); body.Children.Add(scroll);
            notice = Text("", 11); notice.Foreground = ThemePalette.Muted; notice.Visibility = Visibility.Collapsed;
            notice.Margin = new Thickness(0, 12, 0, 0);
            var footer = new StackPanel(); footer.Children.Add(notice);
            compactRefresh = Button("Uygulamayı yinele", async () => await RefreshApplicationAsync()); compactRefresh.Visibility = Visibility.Collapsed; footer.Children.Add(compactRefresh);
            compactExit = Button("Morf'tan Çık", RequestExitConfirmation, true); compactExit.Visibility = Visibility.Collapsed; compactExit.Margin = new Thickness(0, 8, 0, 0); footer.Children.Add(compactExit);
            Grid.SetRow(footer, 2); body.Children.Add(footer);
            PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; if (exitOverlay != null) DismissExitConfirmation(); else if (resetConfirmation != null) CancelReset(); else Close(); } };
            Loaded += (_, __) => MonitorCatalog.PlaceSettings(this, settings.MonitorDevice);
            SizeChanged += (_, __) => ApplyResponsive(ActualWidth);
            Closing += (_, __) => { CloseApplicationsPickers(); FinishApplicationsMotion(); Commit(); };
            SelectPage(0);
        }

        internal void SetNotice(string value) { notice.Text = value; notice.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible; }
        internal void ApplyResponsive(double width)
        {
            bool narrow = width < 520;
            sidebar.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
            layout.ColumnDefinitions[0].Width = new GridLength(narrow ? 0 : width < 600 ? 136 : 176);
            body.Margin = new Thickness(width < 600 ? 12 : 26, 20, 16, 16);
            compactNavigation.Visibility = compactExit.Visibility = compactRefresh.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;
            heading.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        }

        internal void SelectPage(int index)
        {
            if (index < 0 || index >= PageNames.Length) return;
            ResetApplicationsDrawer();
            Commit(); CancelReset(); SelectedPage = index; sleepStatus = mediaStatus = notificationStatus = null; sleepStart = null; cardTarget = null;
            notificationConnect = null; appleMusicStatus = spotifyStatus = null;
            syncingNavigation = true; compactNavigation.SelectedValue = index; syncingNavigation = false;
            for (int i = 0; i < navigation.Length; i++)
            {
                navigation[i].Background = i == index ? ThemePalette.AccentDark : Brushes.Transparent;
                navigation[i].Tag = i == index;
                AutomationProperties.SetItemStatus(navigation[i], i == index ? "Seçili" : "");
            }
            heading.Text = PageNames[index]; subtitle.Text = PageDescriptions[index];
            page.Children.Clear(); scroll.ScrollToTop();
            if (index == 0)
            {
                var theme = Labels("Tema", "Sistem seçimi Windows'un uygulama temasını izler.");
                theme.Children.Add(SettingsChoices.Create("Tema", new[] { "Sistem", "Açık", "Koyu" }, (int)ThemePalette.Preference,
                    value => selectTheme((ThemePreference)value), "theme")); Card(theme);
                Border opacityCard = null, liquidCard = null, glassCard = null;
                Action updateSurfaceControls = () =>
                {
                    bool liquid = settings.Style == SurfaceStyle.LiquidGlass;
                    if (opacityCard != null) opacityCard.Visibility = liquid ? Visibility.Collapsed : Visibility.Visible;
                    if (liquidCard != null) liquidCard.Visibility = liquid ? Visibility.Visible : Visibility.Collapsed;
                    if (glassCard != null) glassCard.Visibility = settings.Style == SurfaceStyle.SoftGlass ? Visibility.Visible : Visibility.Collapsed;
                };
                var surface = Labels("Yüzey", "Düz, saydam veya ışık kenarlı cam yüzey. Masaüstü bulanıklığı uygulanmaz.");
                surface.Children.Add(SettingsChoices.Create("Yüzey", new[] { "Düz", "Yumuşak cam", "Liquid Glass" }, (int)settings.Style,
                    value => { settings.Style = (SurfaceStyle)value; updateSurfaceControls(); apply(true); }, "surface")); Card(surface);
                Range("Yüzey opaklığı", "Metinler ve düğmeler net kalır.", 45, 100, settings.Opacity * 100,
                    value => settings.Opacity = value / 100, !SystemParameters.HighContrast);
                opacityCard = (Border)page.Children[page.Children.Count - 1];
                Range("Liquid Glass şeffaflığı", "Yalnızca Liquid Glass yüzeyini ayarlar; metinler ve denetimler net kalır.", 0, 90, settings.LiquidTransparency * 100,
                    value => settings.LiquidTransparency = value / 100, !SystemParameters.HighContrast);
                liquidCard = (Border)page.Children[page.Children.Count - 1];
                Range("Cam etkisi", "Yumuşak cam yüzeyinin saydamlığını ayarlar.", 0, 100, settings.GlassStrength * 100,
                    value => settings.GlassStrength = value / 100, !SystemParameters.HighContrast);
                glassCard = (Border)page.Children[page.Children.Count - 1];
                updateSurfaceControls();
            }
            else if (index == 1)
            {
                Choice("Konum", "Otomatik konum, yatay görev çubuğunun karşı kenarıdır.", new[] { "Otomatik", "Üst kenar", "Alt kenar" }, (int)settings.Dock,
                    value => { settings.Dock = (DockPreference)value; apply(true); });
                var mode = Labels("Kapalı görünüm", "Kapsül, kenar çizgisi veya otomatik gizleme seçin. Dinamik ada ekran kenarından ayrık, yuvarlak bir kapsüldür.");
                mode.Children.Add(SettingsChoices.Create("Kapalı görünüm", new[] { "Kapsül", "İnce çizgi", "Otomatik gizle", "Dinamik ada" }, (int)settings.DisplayMode,
                    value => { settings.DisplayMode = (DisplayMode)value; apply(true); }, "display")); Card(mode);
                Toggle("Fare üzerine gelince aç", "Kapsülü tıklamadan açın. Çizgi modu her zaman hover ile açılır.", settings.OpenOnHover, value => settings.OpenOnHover = value);
                Toggle("Gizliyken parça değişimini göster", "Yeni parça kısa süre görünür. Oyun, kilit ve bildirim tercihleri korunur.", settings.PeekTrackChanges, value => settings.PeekTrackChanges = value);
                Choice("Mini bildirim süresi", "Üzerinde beklerken kapanmaz; fare ayrılınca kalan süre devam eder.", new[] { "2 saniye", "4 saniye", "6 saniye" }, settings.PeekSeconds / 2 - 1,
                    value => { settings.PeekSeconds = (value + 1) * 2; apply(true); });
                Advanced("Gelişmiş davranış ayarları", () => {
                    Range("Açılma gecikmesi", "İmleç yalnızca geçerken yanlış açılmayı azaltır.", 0, 1000, settings.HoverDelayMs, value => settings.HoverDelayMs = (int)Math.Round(value), true, "ms");
                    Range("Kapsül genişliği", "Medya içeren kapalı görünümün genişliği.", 150, 260, settings.CompactWidth, value => settings.CompactWidth = (int)Math.Round(value), true, "DIP");
                    Toggle("Boştayken gizle", "Medya veya raf öğesi yoksa kapsül gizlenir.", settings.HideWhenIdle, value => settings.HideWhenIdle = value);
                    Toggle("Uzun başlıkları kaydır", "Yalnızca görünür kapsülde medya çalarken hareket eder.", settings.ScrollTitles, value => settings.ScrollTitles = value);
                    Toggle("Windows bildirim durumuna uy", "Windows sunum, meşgul veya sessiz durumunu bildirdiğinde mini bildirimi göstermez. Windows'un her Rahatsız Etmeyin durumunu kapsamayabilir.", settings.RespectQuietState, value => settings.RespectQuietState = value);
                });
            }
            else if (index == 2)
            {
                Toggle("Yeni medya başlayınca diğerini duraklat", "Windows'a yayımlanan diğer çalan oturumlara duraklat komutu gönderir.", settings.AutoPauseOthers, value => settings.AutoPauseOthers = value);
                Toggle("Araya giren medya kapanınca müziğe dön", "Üstteki öncelik açıkken, yalnızca Morf'un duraklattığı uygun müziği devam ettirir. Sadece duraklatılan video kapanmış sayılmaz.", settings.ResumeInterrupted, value => settings.ResumeInterrupted = value);
                SourceRuleControls(false);
                Toggle("Süre çubuğunu göster", "Oynatıcı destekliyorsa tek tıkla veya sürükleyerek konum seçin.", settings.ShowTimeline, value => settings.ShowTimeline = value);
                Toggle("Windows ses denetimi", "Hoparlör, ses kaydırıcısı ve ada üzerinde tekerlekle ses ayarı.", settings.ScrollVolume, value => settings.ScrollVolume = value);
                Choice("Ses hedefi", "Oynatıcı sesi yalnızca seçili medya uygulamasını etkiler. Tarayıcının sekmeleri aynı uygulama olarak değerlendirilir.", new[] { "Sistem sesi", "Oynatıcı sesi" }, settings.PlayerVolume ? 1 : 0,
                    value => { settings.PlayerVolume = value == 1; apply(true); });
                var note = Text("Karıştırma, tekrar ve baştan oynatma yalnızca kaynağın desteklediği durumlarda görünür.", 11);
                note.Foreground = ThemePalette.Muted; note.Margin = new Thickness(4, 8, 4, 0); page.Children.Add(note);
                Choice("Ses tekerleği alanı", "Yalnızca ses denetimi açıkken uygulanır. Dosya ve süre kaydırma hareketleri korunur.", new[] { "Tüm hap", "Yalnızca hoparlör" }, settings.ScrollVolumeOnWholeIsland ? 0 : 1,
                    value => { settings.ScrollVolumeOnWholeIsland = value == 0; apply(true); });
                var status = Labels("Önceki müziğe dönüş", "Otomatik dönüş güvenli bir kapanış bekler. Duraklatılmış tarayıcı oturumu açık sayılır.");
                mediaStatus = Text("", 11); mediaStatus.Margin = new Thickness(0, 10, 0, 8); status.Children.Add(mediaStatus);
                status.Children.Add(Button("Önceki müziğe dön", async () => { if (actions.ResumePrevious != null) await actions.ResumePrevious(); RefreshRuntimeStatus(); })); Card(status);
                SleepControls();
            }
            else if (index == 3)
            {
                var startup = Toggle("Windows ile başlat", "Yalnızca bu seçeneği açtığınızda Windows başlangıcına eklenir.", settings.StartWithWindows, null);
                startup.Click += (_, __) => { if (configureStartup(startup.IsChecked == true)) apply(true); else startup.IsChecked = settings.StartWithWindows; };
                Toggle("Bildirim alanı simgesi", "Aç, ayarlar, 15 dakika gizle ve çıkış seçeneklerine erişim sağlar.", settings.ShowTrayIcon, value => settings.ShowTrayIcon = value);
                Toggle("Oyunlarda gizle", "Algılanan oyunlarda kısayolla da ekran önüne gelmez.", settings.HideInGames, value => settings.HideInGames = value);
                var hide = Labels("Geçici gizleme", "15 dakika boyunca ana hap ve mini bildirim görünmez. Kısayol veya Morf'u yeniden açmak gizlemeyi iptal eder.");
                hide.Children.Add(Button("15 dakika gizle", () => { if (actions.HideTemporarily != null) actions.HideTemporarily(); })); Card(hide);
                Advanced("Gelişmiş sistem ayarları", () => {
                    MonitorChoice();
                    Choice("Hafif mod", "Otomatik: pilde veya tasarrufta hareketi azaltır. Kilitte görsel güncellemeler durur.", new[] { "Otomatik (varsayılan)", "Her zaman", "Kapalı" }, (int)settings.Lightweight, value => { settings.Lightweight = (LightweightMode)value; apply(true); });
                    Toggle("Diğer tam ekran pencerelerde de gizle", "Tarayıcı ve video gibi tam ekran pencereler için ayrı tercih.", settings.HideInFullscreen, value => settings.HideInFullscreen = value);
                    Toggle("Sanal masaüstlerini izle", "Yalnızca Morf penceresini geçerli masaüstüne taşır.", settings.FollowDesktops, value => settings.FollowDesktops = value);
                    RulesControls();
                });
            }
            else if (index == 4) HotkeyControls();
            else if (index == 6) ApplicationsControls();
            else MaintenanceControls();
            RefreshRuntimeStatus();
        }

        private void Commit() { if (!dirty) return; dirty = false; apply(true); }

        internal void RefreshRuntimeStatus()
        {
            if (notificationStatus != null) notificationStatus.Text = !settings.WindowsNotifications ? "" : actions.NotificationStatus == null ? "Bağlı değil" : actions.NotificationStatus();
            if (notificationConnect != null) notificationConnect.IsEnabled = settings.WindowsNotifications && actions.NotificationAvailable != null && actions.NotificationAvailable();
            if (appleMusicStatus != null) appleMusicStatus.Text = actions.SourceStatus == null ? "Windows medya oturumu bekleniyor" : actions.SourceStatus("Apple Music");
            if (spotifyStatus != null) spotifyStatus.Text = actions.SourceStatus == null ? "Windows medya oturumu bekleniyor" : actions.SourceStatus("Spotify");
            shortcuts.Text = settings.PanelHotkey + " · Aç / kapat\nEsc · Paneli kapat";
            if (mediaStatus != null) mediaStatus.Text = actions.MediaStatus == null ? "Medya durumu kullanılamıyor." : actions.MediaStatus();
            if (sleepStart != null) sleepStart.Content = actions.SleepActive != null && actions.SleepActive() ? "Değiştir" : "Başlat";
            if (sleepStatus != null) sleepStatus.Text = actions.SleepStatus == null ? "Kapalı" : actions.SleepStatus();
        }

        private void MonitorChoice()
        {
            var monitors = MonitorCatalog.All(); var labels = new System.Collections.Generic.List<string> { "Ana ekran (varsayılan)" };
            var devices = new System.Collections.Generic.List<string> { "" }; int selected = 0;
            foreach (var monitor in monitors) { labels.Add(monitor.ToString()); devices.Add(monitor.Device); }
            if (!string.IsNullOrEmpty(settings.MonitorDevice) && !devices.Contains(settings.MonitorDevice))
            { labels.Add("Bağlı değil · " + settings.MonitorDevice.Replace("\\\\.\\", "")); devices.Add(settings.MonitorDevice); }
            selected = devices.IndexOf(settings.MonitorDevice ?? "");
            Choice("Ekran", "İsteğe bağlıdır. Seçili ekran çıkarılırsa ana ekrana dönülür; tercih bağlı ekran geri gelince kullanılır.", labels.ToArray(), Math.Max(0, selected),
                value => { settings.MonitorDevice = devices[value]; apply(true); });
        }

        private void RulesControls()
        {
            var card = Labels("Uygulama kuralları", "Bir uygulama seçin, ardından Morf'un gizlenmesini veya görünmesini belirleyin. Göster, genel oyun/tam ekran gizlemesini geçersiz kılar. En fazla 32 uygulama.");
            var add = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            add.ColumnDefinitions.Add(new ColumnDefinition()); add.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var picker = new ComboBox { MinHeight = 36, HorizontalAlignment = HorizontalAlignment.Stretch, Foreground = ThemePalette.Text, FontSize = 12 };
            IslandWindow.StyleSessionPicker(picker); AutomationProperties.SetName(picker, "Kural için uygulama seç");
            picker.Items.Add(new ComboBoxItem { Content = "Açık uygulamalardan seç…" }); picker.SelectedIndex = 0; add.Children.Add(picker);
            var addButton = Button("Ekle", () =>
            {
                var selected = picker.SelectedItem as ComboBoxItem;
                if (selected != null && selected.Tag is string) AddApplicationRule((string)selected.Tag);
            });
            addButton.IsEnabled = false; Grid.SetColumn(addButton, 1); add.Children.Add(addButton);
            picker.SelectionChanged += (_, __) => { var item = picker.SelectedItem as ComboBoxItem; addButton.IsEnabled = item != null && item.Tag is string; };
            bool loaded = false, loading = false;
            Func<Task> refresh = async () =>
            {
                if (loading) return; loading = true;
                try
                {
                    string[] names = await Task.Run(() => ApplicationCatalog.OpenProcessNames());
                    picker.Items.Clear(); picker.Items.Add(new ComboBoxItem { Content = names.Length == 0 ? "Açık uygulama bulunamadı" : "Uygulama seç…" });
                    foreach (string name in names) picker.Items.Add(new ComboBoxItem { Content = name + ".exe", Tag = name });
                    picker.SelectedIndex = 0; loaded = true;
                }
                catch { SetNotice("Uygulama listesi okunamadı. Dosyadan seçim yapabilir veya yeniden deneyebilirsiniz."); }
                finally { loading = false; }
            };
            picker.DropDownOpened += async (_, __) => { if (!loaded) await refresh(); };
            card.Children.Add(add);
            var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 8) };
            buttons.Children.Add(Button("Dosyadan seç…", SelectApplicationFile));
            buttons.Children.Add(Button("Listeyi yenile", async () => await refresh())); card.Children.Add(buttons);
            ApplicationRules rules; ApplicationRules.TryParse(settings.AppRules, out rules);
            if (rules.Entries.Length == 0) card.Children.Add(Text("Henüz özel kural yok. Eklenen uygulama ilk olarak gizlenir; aşağıdaki seçimden değiştirebilirsiniz.", 11));
            foreach (var entry in rules.Entries)
            {
                string process = entry.Key;
                var item = new StackPanel { Margin = new Thickness(0, 10, 0, 0), Tag = process };
                var title = new DockPanel(); var remove = Button("Kaldır", () => ChangeApplicationRule(process, null));
                AutomationProperties.SetName(remove, process + " kuralını kaldır"); DockPanel.SetDock(remove, Dock.Right); title.Children.Add(remove);
                title.Children.Add(Text(process + ".exe", 12, true)); item.Children.Add(title);
                item.Children.Add(SettingsChoices.Create(process + " kuralı", new[] { "Gizle", "Göster" }, entry.Value ? 0 : 1,
                    value => ChangeApplicationRule(process, value == 0)));
                card.Children.Add(item);
            }
            Card(card);
        }

        private void AddApplicationRule(string process)
        {
            ApplicationRules rules; ApplicationRules.TryParse(settings.AppRules, out rules);
            if (rules.Hidden(process).HasValue) { SetNotice("Bu uygulama için zaten bir kural var; mevcut seçimi değiştirebilirsiniz."); return; }
            ChangeApplicationRule(process, true);
        }
        private void ChangeApplicationRule(string process, bool? hidden)
        {
            ApplicationRules rules; ApplicationRules.TryParse(settings.AppRules, out rules);
            var entries = rules.Entries.Where(item => !item.Key.Equals(process, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Key + ".exe = " + (item.Value ? "gizle" : "göster")).ToList();
            if (hidden.HasValue) entries.Add(process + ".exe = " + (hidden.Value ? "gizle" : "göster"));
            if (!ApplicationRules.TryParse(string.Join("\n", entries), out rules)) { SetNotice("Kural eklenemedi. En fazla 32 geçerli uygulama seçilebilir."); return; }
            settings.AppRules = rules.Text; apply(true); SelectPage(3);
            // Keep the edited rules in view instead of jumping to the top of this long page.
            var advanced = page.Children.OfType<Expander>().Last(); advanced.IsExpanded = true;
            var group = (StackPanel)advanced.Content;
            var card = ((Border)group.Children[group.Children.Count - 1]).Child as StackPanel;
            var edited = card.Children.OfType<StackPanel>().FirstOrDefault(item => string.Equals(item.Tag as string, process, StringComparison.OrdinalIgnoreCase));
            (edited ?? card).BringIntoView();
        }
        private void SelectApplicationFile()
        {
            try
            {
                var picker = new OpenFileDialog { Title = "Kural için uygulama seç", Filter = "Windows uygulaması (*.exe)|*.exe", CheckFileExists = true, Multiselect = false };
                if (picker.ShowDialog(this) == true && System.IO.Path.GetExtension(picker.FileName).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                    AddApplicationRule(System.IO.Path.GetFileNameWithoutExtension(picker.FileName));
            }
            catch { SetNotice("Uygulama seçilemedi. Yeniden deneyebilirsiniz."); }
        }

        private void HotkeyControls()
        {
            var inputs = new TextBox[4]; string[] titles = { "Paneli aç / kapat", "Oynat / duraklat", "Önceki parça", "Sonraki parça" };
            string[] values = settings.Hotkeys;
            for (int i = 0; i < 4; i++)
            {
                string[] examples = { "", "Ctrl+Alt+P", "Ctrl+Alt+J", "Ctrl+Alt+K" };
                var card = Labels(titles[i], i == 0 ? "Varsayılan: Ctrl+Alt+M. Örnek: Ctrl+Shift+M." : "İsteğe bağlı; boş bırakılırsa kapalıdır. Örnek: " + examples[i] + ".");
                inputs[i] = Editor(values[i], 40); AutomationProperties.SetName(inputs[i], titles[i] + " kısayolu"); card.Children.Add(inputs[i]); Card(card);
            }
            page.Children.Add(Button("Kısayolları uygula", () =>
            {
                if (actions.ConfigureHotkeys == null) { SetNotice("Kısayollar bu görünümde değiştirilemiyor."); return; }
                if (actions.ConfigureHotkeys(new[] { inputs[0].Text, inputs[1].Text, inputs[2].Text, inputs[3].Text }))
                { SetNotice("Kısayollar uygulandı."); SelectPage(4); }
            }));
        }

        private void SleepControls()
        {
            var card = new Grid(); card.ColumnDefinitions.Add(new ColumnDefinition()); card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var labels = Labels("Uyku zamanlayıcısı", "Süre sonunda çalan medya duraklatılır. Bilgisayar kapanmaz; Morf'tan çıkınca iptal olur.");
            labels.Margin = new Thickness(0, 0, 14, 0); card.Children.Add(labels);
            sleepStatus = Text("", 11); sleepStatus.Foreground = ThemePalette.Muted; sleepStatus.Margin = new Thickness(0, 10, 0, 0); labels.Children.Add(sleepStatus);
            int minutes = actions.SleepMinutes == null ? 30 : actions.SleepMinutes();
            int selected = minutes == 15 ? 0 : minutes == 60 ? 2 : 1;
            var controls = new StackPanel { Width = 203, VerticalAlignment = VerticalAlignment.Center };
            controls.Children.Add(SettingsChoices.Create("Uyku süresi", new[] { "15 dk", "30 dk", "60 dk" }, selected, value => selected = value));
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
            sleepStart = Button("Başlat", () => { if (actions.StartSleep != null) actions.StartSleep(new[] { 15, 30, 60 }[selected]); RefreshRuntimeStatus(); }); row.Children.Add(sleepStart);
            row.Children.Add(Button("İptal et", () => { if (actions.CancelSleep != null) actions.CancelSleep(); RefreshRuntimeStatus(); }));
            controls.Children.Add(row); Grid.SetColumn(controls, 1); card.Children.Add(controls);
            card.SizeChanged += (_, __) => {
                bool narrow = card.ActualWidth < 380;
                Grid.SetColumnSpan(labels, narrow ? 2 : 1); Grid.SetColumn(controls, narrow ? 0 : 1);
                Grid.SetColumnSpan(controls, narrow ? 2 : 1); Grid.SetRow(controls, narrow ? 1 : 0);
                controls.Width = narrow ? double.NaN : 203;
            };
            Card(card);
        }

        private void MaintenanceControls()
        {
            var card = Labels("Morf " + BuildInfo.Version, "Yerel tanılama yalnızca bu sayfa açılırken veya Yenile'ye basınca okunur. Otomatik kayıt veya gönderim yoktur.");
            var report = Editor("", 10000); report.IsReadOnly = true; report.Height = 225; report.AcceptsReturn = true;
            report.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; AutomationProperties.SetName(report, "Yerel tanılama raporu");
            Action refresh = () => { try { report.Text = actions.Diagnostics == null ? "Tanılama bu görünümde kullanılamıyor." : actions.Diagnostics(); } catch { report.Text = "Tanılama okunamadı. Yeniden deneyebilirsiniz."; } };
            refresh(); card.Children.Add(report);
            var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            buttons.Children.Add(Button("Yenile", () => refresh()));
            buttons.Children.Add(Button("Tanılamayı kopyala", () => { if (actions.CopyDiagnostics != null) actions.CopyDiagnostics(report.Text); }));
            card.Children.Add(buttons); Card(card);
            var reset = Labels("Varsayılan ayarlar", "Tema, görünüm, kurallar, ekran, kısayollar ve başlangıç tercihi varsayılana döner; uyku zamanlayıcısı iptal edilir. Geçici raf ve dosyaların asılları korunur.");
            reset.Children.Add(Button("Varsayılan ayarlara döndür…", ShowResetConfirmation)); Card(reset);
        }

        private void ShowResetConfirmation()
        {
            if (resetConfirmation != null) return;
            Commit();
            var content = Labels("Varsayılan ayarlara dönülsün mü?", "Bu işlem tercihlerinizin üzerine yazar. Windows ile başlat kapatılır. Raf öğeleri kaldırılmaz.");
            var row = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            var cancel = Button("Vazgeç", CancelReset); cancel.IsDefault = true;
            row.Children.Add(cancel);
            row.Children.Add(Button("Evet, varsayılanlara dön", () =>
            {
                if (actions.Reset != null && actions.Reset()) { CancelReset(); SelectPage(5); }
                else if (actions.Reset == null) SetNotice("Sıfırlama bu görünümde kullanılamıyor.");
            }));
            content.Children.Add(row);
            resetConfirmation = new Border { Child = content, Background = ThemePalette.Card, BorderBrush = ThemePalette.Accent, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 12) };
            page.Children.Add(resetConfirmation); resetConfirmation.BringIntoView(); cancel.Focus();
        }
        private void CancelReset() { if (resetConfirmation != null) { page.Children.Remove(resetConfirmation); resetConfirmation = null; } }
        internal void RequestExitConfirmation()
        {
            if (exitOverlay != null) return;
            Commit(); CancelReset();
            int count = actions.ShelfCount == null ? 0 : actions.ShelfCount();
            var content = Labels("Morf'tan çıkmak istediğinize emin misiniz?", count > 0
                ? "Raftaki " + count + " öğenin geçici listesi boşalacak. Dosyaların asılları korunur. Müzik kendi uygulamasında devam eder."
                : "Morf kapanacak. Müzik kendi uygulamasında çalmaya devam eder.");
            var row = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var cancel = Button("Vazgeç", DismissExitConfirmation); cancel.IsDefault = true; cancel.IsCancel = true;
            row.Children.Add(cancel); row.Children.Add(Button("Evet, Morf'tan çık", () => { DismissExitConfirmation(); if (actions.Exit != null) actions.Exit(); }, true)); content.Children.Add(row);
            var card = new Border { Child = content, Background = ThemePalette.Surface, BorderBrush = ThemePalette.Stroke,
                BorderThickness = new Thickness(1), Padding = new Thickness(24), CornerRadius = new CornerRadius(16),
                MaxWidth = 420, Margin = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            exitOverlay = new Border { Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), CornerRadius = new CornerRadius(18), Child = card };
            Grid.SetColumnSpan(exitOverlay, 2); Panel.SetZIndex(exitOverlay, 100);
            KeyboardNavigation.SetTabNavigation(exitOverlay, KeyboardNavigationMode.Cycle);
            sidebar.IsEnabled = body.IsEnabled = false;
            layout.Children.Add(exitOverlay); cancel.Focus();
        }
        private void DismissExitConfirmation()
        {
            if (exitOverlay == null) return;
            layout.Children.Remove(exitOverlay); exitOverlay = null;
            sidebar.IsEnabled = body.IsEnabled = true;
            (compactExit.Visibility == Visibility.Visible ? compactExit : navigation[SelectedPage]).Focus();
        }
        private async Task RefreshApplicationAsync()
        {
            if (refreshingApplication || actions.RefreshApplication == null) return;
            refreshingApplication = true; refreshButton.IsEnabled = compactRefresh.IsEnabled = false;
            SetNotice("Görünüm ve bağlantılar yenileniyor…");
            try { Commit(); SetNotice(await actions.RefreshApplication()); }
            catch (Exception) { SetNotice("Yenileme tamamlanamadı. Raf ve ayarlar korundu; yeniden deneyebilirsiniz."); }
            finally { refreshingApplication = false; refreshButton.IsEnabled = compactRefresh.IsEnabled = true; }
        }

        private static TextBox Editor(string value, int maximum)
        {
            return new TextBox { Text = value ?? "", MaxLength = maximum, Foreground = ThemePalette.Text, Background = ThemePalette.Shelf,
                CaretBrush = ThemePalette.Text, BorderBrush = ThemePalette.Stroke, BorderThickness = new Thickness(1), Padding = new Thickness(9, 7, 9, 7),
                Margin = new Thickness(0, 10, 0, 0), FontSize = 12, TextWrapping = TextWrapping.Wrap, SelectionBrush = ThemePalette.Accent };
        }

        private CheckBox Toggle(string title, string detail, bool enabled, Action<bool> change)
        {
            var check = new CheckBox { IsChecked = enabled, Width = 40, Height = 24, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
            AutomationProperties.SetName(check, title);
            var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var labels = Labels(title, detail); labels.Margin = new Thickness(0, 0, 18, 0); row.Children.Add(labels);
            Grid.SetColumn(check, 1); row.Children.Add(check); Card(row);
            if (change != null) check.Click += (_, __) => { change(check.IsChecked == true); apply(true); };
            return check;
        }

        private void Choice(string title, string detail, string[] choices, int selected, Action<int> change)
        {
            var card = Labels(title, detail);
            var picker = new ComboBox { ItemsSource = choices, SelectedIndex = selected, MinWidth = 160, MinHeight = 34, HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 12, 0, 0), Foreground = ThemePalette.Text, FontSize = 12 };
            AutomationProperties.SetName(picker, title); IslandWindow.StyleSessionPicker(picker);
            picker.SelectionChanged += (_, __) => { if (picker.SelectedIndex >= 0) change(picker.SelectedIndex); };
            card.Children.Add(picker); Card(card);
        }

        private Slider Range(string title, string detail, double minimum, double maximum, double value, Action<double> change, bool enabled, string unit = "%")
        {
            Func<double, string> format = amount => unit == "%" ? "%" + Math.Round(amount) : Math.Round(amount) + " " + unit;
            var card = Labels(title + " · " + format(value), detail);
            var label = (TextBlock)card.Children[0];
            var slider = new Slider { Minimum = minimum, Maximum = maximum, Value = value, SmallChange = unit == "ms" ? 50 : 5, LargeChange = unit == "ms" ? 100 : 10,
                IsMoveToPointEnabled = true, IsEnabled = enabled, Margin = new Thickness(0, 10, 0, 0), TickFrequency = unit == "ms" ? 50 : 5, IsSnapToTickEnabled = unit != "%" };
            AutomationProperties.SetName(slider, title);
            slider.ValueChanged += (_, __) => { label.Text = title + " · " + format(slider.Value); change(slider.Value); dirty = true; apply(false); };
            slider.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler((_, __) => Commit()), true);
            slider.PreviewKeyUp += (_, __) => Commit();
            slider.LostMouseCapture += (_, __) => { if (Mouse.LeftButton == MouseButtonState.Released) Commit(); };
            card.Children.Add(slider); Card(card, 20); return slider;
        }

        private void Advanced(string title, Action create)
        {
            var expander = new Expander { Header = title, Foreground = ThemePalette.Text, Margin = new Thickness(0, 8, 0, 12) };
            bool created = false;
            expander.Expanded += (_, __) => {
                if (created) return;
                created = true; var target = new StackPanel { Margin = new Thickness(0, 12, 0, 0) }; expander.Content = target;
                var previous = cardTarget; cardTarget = target;
                try { create(); } finally { cardTarget = previous; }
            };
            page.Children.Add(expander);
        }

        private void Card(UIElement content, double bottomPadding = 15) { (cardTarget ?? page).Children.Add(new Border { Child = content, Background = ThemePalette.Card, BorderBrush = ThemePalette.Stroke,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(17, 15, 17, bottomPadding), Margin = new Thickness(0, 0, 0, 12) }); }

        private static StackPanel Labels(string title, string detail)
        {
            var panel = new StackPanel(); panel.Children.Add(Text(title, 13, true));
            var description = Text(detail, 11); description.Foreground = ThemePalette.Muted; description.Margin = new Thickness(0, 6, 0, 0);
            panel.Children.Add(description); return panel;
        }
        private static TextBlock Text(string value, double size, bool bold = false) { return new TextBlock { Text = value, FontSize = size,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Foreground = ThemePalette.Text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }; }
        internal static Button Button(string label, Action action, bool danger = false, bool navigation = false)
        {
            var button = new Button { Content = label, Padding = new Thickness(12, 10, 12, 10), Foreground = danger ? ThemePalette.DangerInk : ThemePalette.Text, Background = danger ? ThemePalette.Danger : Brushes.Transparent,
                BorderThickness = new Thickness(0), Cursor = Cursors.Hand, HorizontalContentAlignment = HorizontalAlignment.Center };
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border)); border.Name = "Surface";
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); border.AppendChild(presenter);
            if (navigation)
            {
                var grid = new FrameworkElementFactory(typeof(Grid)); grid.AppendChild(border);
                var marker = new FrameworkElementFactory(typeof(Border)); marker.Name = "SelectionMark";
                marker.SetValue(Border.BackgroundProperty, ThemePalette.Accent);
                marker.SetValue(FrameworkElement.WidthProperty, 3.0);
                marker.SetValue(FrameworkElement.HeightProperty, 16.0);
                marker.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
                marker.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
                marker.SetValue(FrameworkElement.MarginProperty, new Thickness(3, 0, 0, 0));
                marker.SetValue(UIElement.IsHitTestVisibleProperty, false);
                marker.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
                grid.AppendChild(marker); template.VisualTree = grid;
                var selected = new Trigger { Property = FrameworkElement.TagProperty, Value = true };
                selected.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "SelectionMark"));
                template.Triggers.Add(selected);
            }
            else template.VisualTree = border;
            foreach (var property in new[] { UIElement.IsMouseOverProperty, UIElement.IsKeyboardFocusWithinProperty })
            {
                var trigger = new Trigger { Property = property, Value = true }; trigger.Setters.Add(new Setter(Border.BackgroundProperty, danger ? ThemePalette.DangerHover : ThemePalette.Hover, "Surface")); template.Triggers.Add(trigger);
            }
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45)); template.Triggers.Add(disabled);
            button.Template = template; button.Click += (_, __) => action(); PopupTheme.DelayTooltip(button); return button;
        }
    }
}
