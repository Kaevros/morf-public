namespace Widgates
{
    // One owner for panel interaction state; geometry and native visibility stay in the window/placement layer.
    internal sealed class PanelState
    {
        internal bool Expanded, Latched, Persistent;
        private bool wasExpanded, wasLatched;
        internal bool SettingsOpen { get; private set; }
        internal void SuspendForSettings() { wasExpanded = Expanded; wasLatched = Latched; SettingsOpen = true; Expanded = Latched = false; }
        internal void RestoreAfterSettings(bool allowed)
        {
            SettingsOpen = false;
            Expanded = allowed && (wasExpanded || Persistent); Latched = allowed && (wasLatched || Persistent);
        }
        internal bool CanHover(bool suppressed, bool hidden) { return !SettingsOpen && !suppressed && !hidden; }
    }
}
