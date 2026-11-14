using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Widgates
{
    // Subscribed only while the mixer is open. Callbacks never block or read COM/UI.
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    internal sealed class AudioMixerWatch : IDisposable, AudioDevices.SessionEvents, AudioDevices.SessionCreated, AudioDevices.EndpointEvents, AudioDevices.DeviceEvents
    {
        private object enumerator, manager, endpoint;
        private readonly List<object> sessions = new List<object>();
        private readonly Action changed;
        private volatile bool disposed;
        private bool deviceSubscribed, sessionSubscribed, endpointSubscribed;
        internal AudioMixerWatch(string id, Action changed)
        {
            this.changed = changed; AudioDevices.Device device = null; AudioDevices.SessionList entries = null;
            try
            {
                enumerator = AudioDevices.Enumerator(); AudioDevices.Check(((AudioDevices.DeviceEnumerator)enumerator).GetDevice(id, out device));
                AudioDevices.Check(((AudioDevices.DeviceEnumerator)enumerator).RegisterEndpointNotificationCallback(this)); deviceSubscribed = true;
                endpoint = AudioDevices.Activate(device, "5CDF2C82-841E-4546-9722-0CF74078229A");
                AudioDevices.Check(((AudioDevices.EndpointVolume)endpoint).RegisterControlChangeNotify(this)); endpointSubscribed = true;
                manager = AudioDevices.Activate(device, "77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
                AudioDevices.Check(((AudioDevices.SessionManager)manager).GetSessionEnumerator(out entries));
                AudioDevices.Check(((AudioDevices.SessionManager)manager).RegisterSessionNotification(this)); sessionSubscribed = true;
                int count; AudioDevices.Check(entries.GetCount(out count));
                for (int i = 0; i < Math.Min(128, count); i++)
                {
                    object entry = null;
                    try { AudioDevices.Check(entries.GetSession(i, out entry)); AudioDevices.Check(((AudioDevices.SessionControl)entry).RegisterAudioSessionNotification(this)); sessions.Add(entry); entry = null; }
                    catch (COMException) { }
                    finally { AudioDevices.Release(entry); }
                }
            }
            catch { Dispose(); throw; }
            finally { AudioDevices.Release(entries); AudioDevices.Release(device); }
        }
        private int Signal() { if (!disposed) { try { changed(); } catch (InvalidOperationException) { } } return 0; }
        public int OnDisplayNameChanged(string name, ref Guid context) { return Signal(); }
        public int OnIconPathChanged(string path, ref Guid context) { return Signal(); }
        public int OnSimpleVolumeChanged(float level, bool muted, ref Guid context) { return Signal(); }
        public int OnChannelVolumeChanged(uint count, IntPtr levels, uint changedChannel, ref Guid context) { return Signal(); }
        public int OnGroupingParamChanged(ref Guid group, ref Guid context) { return Signal(); }
        public int OnStateChanged(int state) { return Signal(); }
        public int OnSessionDisconnected(int reason) { return Signal(); }
        public int OnSessionCreated(object session) { return Signal(); }
        public int OnNotify(IntPtr data) { return Signal(); }
        public int OnDeviceStateChanged(string id, uint state) { return Signal(); }
        public int OnDeviceAdded(string id) { return Signal(); }
        public int OnDeviceRemoved(string id) { return Signal(); }
        public int OnDefaultDeviceChanged(int flow, int role, string id) { return Signal(); }
        public int OnPropertyValueChanged(string id, AudioDevices.PropertyKey key) { return Signal(); }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            foreach (var session in sessions) { try { ((AudioDevices.SessionControl)session).UnregisterAudioSessionNotification(this); } catch (COMException) { } AudioDevices.Release(session); }
            sessions.Clear();
            try { if (sessionSubscribed) ((AudioDevices.SessionManager)manager).UnregisterSessionNotification(this); } catch (COMException) { }
            try { if (endpointSubscribed) ((AudioDevices.EndpointVolume)endpoint).UnregisterControlChangeNotify(this); } catch (COMException) { }
            try { if (deviceSubscribed) ((AudioDevices.DeviceEnumerator)enumerator).UnregisterEndpointNotificationCallback(this); } catch (COMException) { }
            AudioDevices.Release(endpoint); AudioDevices.Release(manager); AudioDevices.Release(enumerator); endpoint = manager = enumerator = null;
        }
    }
}
