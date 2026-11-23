namespace Widgates
{
    // One owner for panel interaction state; geometry and native visibility stay in the window/placement layer.
    internal sealed class PanelState
    {
        internal bool Expanded, Latched, Persistent;
        internal bool SettingsOpen { get; private set; }
        internal void BeginSettings() { SettingsOpen = true; }
        internal void EndSettings(bool allowed)
        {
            SettingsOpen = false;
            if (!allowed) Expanded = Latched = false;
        }
        internal bool CanHover(bool suppressed, bool hidden) { return !suppressed && !hidden; }
    }
}
