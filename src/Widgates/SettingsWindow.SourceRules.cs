using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Widgates
{
    internal sealed partial class SettingsWindow
    {
        private void SourceRuleControls(bool notifications, StackPanel target = null)
        {
            var content = Labels(notifications ? "Uygulamaya göre bildirim" : "Medya kaynak önceliği",
                notifications ? "Yeni uygulamalar genel ayarı izler. Windows'ta bırakılan uygulamaların önceki başlık tercihleri korunur. Listede bu oturumda bildirimi görülen uygulamalar yer alır."
                : "Öncelikli kaynak çalarken diğer kaynaklara karşı korunur. Hariç kaynaklar otomatik seçilmez veya duraklatılmaz; elle seçilebilir. Tarayıcı sekmeleri tek uygulamadır.");
            var source = new ComboBox { MinHeight = 34, Margin = new Thickness(0, 10, 0, 6), Foreground = ThemePalette.Text };
            IslandWindow.StyleSessionPicker(source); AutomationProperties.SetName(source, notifications ? "Bildirim uygulaması" : "Medya uygulaması");
            var mode = new ComboBox { ItemsSource = notifications ? new[] { "Genel ayarı izle", "Morf · İçeriği gizle", "Windows'ta bırak" }
                : new[] { "Normal otomatik takip", "Öncelikli kaynak", "Otomatik kontrolden hariç" }, SelectedIndex = 0, MinHeight = 34, Foreground = ThemePalette.Text };
            IslandWindow.StyleSessionPicker(mode); AutomationProperties.SetName(mode, "Uygulama tercihi");
            var notice = Text("", 11); notice.Foreground = ThemePalette.Muted; notice.Margin = new Thickness(0, 6, 0, 0);
            var saved = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            Func<SourceRules> rules = () => SourceRules.Parse(notifications ? settings.NotificationRules : settings.MediaRules);
            Func<SourceChoice[]> available = () => {
                var read = notifications ? actions.NotificationSources : actions.MediaSources;
                var entries = read == null ? new SourceChoice[0] : read();
                return entries.Concat(rules().Ids.Select(id => new SourceChoice { Id = id, Name = MediaSourceNames.Friendly(id) }))
                    .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).OrderBy(x => x.Name).ToArray();
            };
            Action refreshSources = () => {
                string selected = (source.SelectedItem as SourceChoice) == null ? null : ((SourceChoice)source.SelectedItem).Id;
                var entries = available(); source.ItemsSource = entries;
                source.SelectedItem = entries.FirstOrDefault(x => x.Id == selected) ?? entries.FirstOrDefault();
                notice.Text = entries.Length == 0 ? (notifications ? "Bir uygulamadan bildirim geldikten sonra listeyi yenileyin." : "Bir oynatıcıda medya başlatıp listeyi yenileyin.") : "";
            };
            Action<SourceRules> commit = value => { if (notifications) settings.NotificationRules = value.Text; else settings.MediaRules = value.Text; apply(true); };
            Action render = null;
            render = () => {
                saved.Children.Clear(); var current = rules(); var entries = available();
                foreach (string id in current.Ids.ToArray())
                {
                    string selected = id; var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                    var clear = Button("Kaldır", () => { var next = rules(); next.Set(selected, 0); commit(next); render(); });
                    DockPanel.SetDock(clear, Dock.Right); row.Children.Add(clear);
                    var entry = entries.FirstOrDefault(x => x.Id == id);
                    var label = Text((entry == null ? MediaSourceNames.Friendly(id) : entry.Name) + " · " + mode.Items[current.Get(id)], 11);
                    label.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(label); saved.Children.Add(row);
                }
            };
            source.DropDownOpened += (_, __) => refreshSources();
            source.SelectionChanged += (_, __) => { var selected = source.SelectedItem as SourceChoice; if (selected != null) mode.SelectedIndex = rules().Get(selected.Id); };
            content.Children.Add(source); content.Children.Add(mode);
            var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            buttons.Children.Add(Button("Uygula", () => {
                var selected = source.SelectedItem as SourceChoice; if (selected == null) { refreshSources(); return; }
                var next = rules();
                if (!next.Set(selected.Id, mode.SelectedIndex, !notifications)) { notice.Text = "Uygulama sınırına ulaşıldı. Önce bir tercihi kaldırın."; return; }
                commit(next); notice.Text = "Tercih uygulandı."; render();
            }));
            buttons.Children.Add(Button("Listeyi yenile", async () => {
                if (notifications && actions.RefreshNotifications != null) await actions.RefreshNotifications();
                refreshSources(); render();
            }));
            content.Children.Add(buttons); content.Children.Add(notice); content.Children.Add(saved);
            refreshSources(); render();
            if (target == null) Card(content); else target.Children.Add(content);
        }
    }
}
