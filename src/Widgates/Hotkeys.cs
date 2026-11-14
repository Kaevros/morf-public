using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Widgates
{
    internal sealed class HotkeyBinding
    {
        internal uint Modifiers, Key;
        internal string Text;
        internal static bool TryParse(string text, out HotkeyBinding binding)
        {
            binding = new HotkeyBinding { Text = "" };
            if (string.IsNullOrWhiteSpace(text)) return true;
            string[] parts = text.Split('+').Select(part => part.Trim()).ToArray();
            uint modifiers = 0, key = 0; string keyName = null;
            foreach (string part in parts)
            {
                string value = part.ToUpperInvariant(); uint modifier = value == "CTRL" ? 2u : value == "ALT" ? 1u : value == "SHIFT" ? 4u : 0;
                if (modifier != 0) { if ((modifiers & modifier) != 0) return false; modifiers |= modifier; continue; }
                if (key != 0) return false;
                int number;
                if (value.Length == 1 && ((value[0] >= 'A' && value[0] <= 'Z') || (value[0] >= '0' && value[0] <= '9'))) { key = value[0]; keyName = value; }
                else if (value.StartsWith("F") && int.TryParse(value.Substring(1), out number) && number >= 1 && number <= 24 && number != 12)
                { key = (uint)(0x70 + number - 1); keyName = "F" + number; }
                else return false;
            }
            if (key == 0 || (key < 0x70 && (modifiers & 3) == 0)) return false;
            binding.Modifiers = modifiers; binding.Key = key;
            binding.Text = ((modifiers & 2) != 0 ? "Ctrl+" : "") + ((modifiers & 1) != 0 ? "Alt+" : "") + ((modifiers & 4) != 0 ? "Shift+" : "") + keyName;
            return true;
        }
    }

    internal interface IHotkeyRegistry { bool Register(int id, HotkeyBinding binding); void Unregister(int id); }

    internal sealed class HotkeySet : IDisposable
    {
        private readonly IHotkeyRegistry registry;
        private HotkeyBinding[] current = new HotkeyBinding[4];
        internal string Failure { get; private set; }
        internal bool PanelRegistered { get { return current[0] != null && current[0].Key != 0; } }
        internal HotkeySet(IHotkeyRegistry registry) { this.registry = registry; }
        internal bool Apply(string[] texts)
        {
            var next = new HotkeyBinding[4];
            if (texts.Length != 4) return false;
            for (int i = 0; i < 4; i++) if (!HotkeyBinding.TryParse(texts[i], out next[i]) || (i == 0 && next[i].Key == 0))
            { Failure = "Geçersiz kısayol. Ctrl/Alt/Shift + harf, sayı veya F tuşu kullanın. Panel kısayolu boş bırakılamaz; F12 sisteme ayrılmıştır."; return false; }
            if (next.Where(item => item.Key != 0).GroupBy(item => item.Text).Any(group => group.Count() > 1))
            { Failure = "Aynı kısayol birden fazla işleme atanamaz."; return false; }
            var previous = (HotkeyBinding[])current.Clone(); var changed = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                if (current[i] != null && current[i].Text == next[i].Text) continue;
                changed.Add(i);
                if (current[i] != null && current[i].Key != 0) registry.Unregister(i + 1);
                current[i] = null;
            }
            foreach (int i in changed)
            {
                if (next[i].Key != 0 && !registry.Register(i + 1, next[i]))
                {
                    foreach (int restoring in changed)
                    {
                        if (current[restoring] != null && current[restoring].Key != 0) registry.Unregister(restoring + 1);
                        current[restoring] = null;
                    }
                    foreach (int restoring in changed)
                    {
                        current[restoring] = previous[restoring];
                        if (current[restoring] != null && current[restoring].Key != 0 && !registry.Register(restoring + 1, current[restoring])) current[restoring] = null;
                    }
                    Failure = "Kısayol başka bir uygulama tarafından kullanılıyor. Önceki atamalar geri yüklenmeye çalışıldı; kayıt durumu tanılamada görülebilir.";
                    return false;
                }
                current[i] = next[i];
            }
            Failure = ""; return true;
        }
        internal string Status { get { return string.Join("\n", Enumerable.Range(0, 4).Select(i => new[] { "Panel", "Oynat/duraklat", "Önceki", "Sonraki" }[i] + ": " +
            (current[i] == null ? "Kaydedilemedi" : current[i].Key == 0 ? "Kapalı" : current[i].Text))); } }
        public void Dispose() { for (int i = 0; i < 4; i++) if (current[i] != null && current[i].Key != 0) registry.Unregister(i + 1); }
    }

    internal sealed class WindowsHotkeyRegistry : IHotkeyRegistry
    {
        private readonly IntPtr window;
        internal WindowsHotkeyRegistry(IntPtr window) { this.window = window; }
        public bool Register(int id, HotkeyBinding binding) { return RegisterHotKey(window, id, binding.Modifiers | 0x4000, binding.Key); }
        public void Unregister(int id) { UnregisterHotKey(window, id); }
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    }
}
