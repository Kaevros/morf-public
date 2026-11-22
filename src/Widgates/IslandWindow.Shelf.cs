using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace Widgates
{
    internal sealed partial class IslandWindow
    {
        private ComboBox shelfFilter, shelfGroup;
        private Button shelfUndo, shelfLeft, shelfRight, shelfAssign;
        private Button shelfRecovery;
        private void InitializeShelfTools()
        {
            var filters = new Grid { Margin = new Thickness(0, 4, 0, 6) };
            filters.ColumnDefinitions.Add(new ColumnDefinition()); filters.ColumnDefinitions.Add(new ColumnDefinition());
            shelfFilter = new ComboBox { ItemsSource = new[] { "Tümü", "Genel", "Grup 1", "Grup 2", "Grup 3" }, SelectedIndex = 0, MinHeight = 30, Margin = new Thickness(0, 0, 6, 0), Foreground = ThemePalette.Text };
            shelfGroup = new ComboBox { ItemsSource = new[] { "Genel", "Grup 1", "Grup 2", "Grup 3" }, SelectedIndex = 0, MinHeight = 30, Foreground = ThemePalette.Text };
            StyleSessionPicker(shelfFilter); StyleSessionPicker(shelfGroup);
            AutomationProperties.SetName(shelfFilter, "Rafta gösterilen grup"); AutomationProperties.SetName(shelfGroup, "Seçili dosyaların hedef grubu");
            shelfFilter.SelectionChanged += (_, __) => ApplyShelfFilter();
            foreach (var picker in new[] { shelfFilter, shelfGroup })
            {
                picker.DropDownOpened += (_, __) => collapse.Stop();
                picker.DropDownClosed += (_, __) => { if (!pinned && !IsPointerOverIsland) collapse.Start(); };
            }
            filters.Children.Add(shelfFilter); Grid.SetColumn(shelfGroup, 1); filters.Children.Add(shelfGroup); shelfSection.Children.Add(filters);
            var tools = new WrapPanel();
            shelfLeft = CreateActionButton("←", () => MoveShelf(-1), "Seçili öğeleri sola al", "Seçili dosyaları sola sırala");
            shelfRight = CreateActionButton("→", () => MoveShelf(1), "Seçili öğeleri sağa al", "Seçili dosyaları sağa sırala");
            shelfAssign = CreateActionButton("Gruba ata", () => { shelf.Assign(SelectedPaths(), (string)shelfGroup.SelectedItem); ApplyShelfFilter(); }, "Seçili öğelerin raf grubunu değiştir", "Seçili dosyaları gruba ata");
            shelfUndo = CreateActionButton("Geri al", UndoShelfRemoval, "Son raftan kaldırmayı geri al · Ctrl+Z", "Son raftan kaldırmayı geri al");
            foreach (var button in new[] { shelfLeft, shelfRight, shelfAssign, shelfUndo }) tools.Children.Add(button);
            shelfSection.Children.Add(tools);
            shelfRecovery = CreateActionButton("Raftan kaldırmayı geri al", UndoShelfRemoval, "Son kaldırmayı geri al · Ctrl+Z", "Boş rafta son kaldırmayı geri al");
            shelfRecovery.Visibility = Visibility.Collapsed; shelfRecovery.Margin = new Thickness(0, 8, 0, 0); panel.Children.Add(shelfRecovery);
            files.PreviewKeyDown += (_, e) => {
                if (e.Key == Key.Delete) { RemoveFiles(); e.Handled = true; }
                else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C) { CopyFiles(); e.Handled = true; }
                else if (Keyboard.Modifiers == ModifierKeys.Alt && (e.Key == Key.Left || e.Key == Key.Right)) { MoveShelf(e.Key == Key.Left ? -1 : 1); e.Handled = true; }
                else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z) { shelfUndo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; }
            };
        }
        private void UndoShelfRemoval()
        {
            if (shelf.UndoRemove()) { UpdateShelf(); shelfNotice.Text = "Raftan kaldırma geri alındı. Orijinal dosyalar değiştirilmedi."; }
            else shelfNotice.Text = "Geri almak için rafta yer açın.";
        }
        private void MoveShelf(int direction)
        {
            var selected = SelectedPaths(); shelf.Move(selected, direction, shelfFilter.SelectedIndex == 0 ? null : (string)shelfFilter.SelectedItem); UpdateShelf();
            foreach (ListBoxItem item in files.Items) item.IsSelected = selected.Contains((string)item.Tag, StringComparer.OrdinalIgnoreCase);
        }
        private void ApplyShelfFilter()
        {
            string group = shelfFilter == null ? "Tümü" : (string)shelfFilter.SelectedItem;
            foreach (ListBoxItem item in files.Items)
            {
                bool visible = group == "Tümü" || shelf.Group((string)item.Tag) == group;
                item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                if (!visible) item.IsSelected = false;
            }
            UpdateShelfButtons();
        }
    }
}
