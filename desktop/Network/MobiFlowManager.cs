using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ScreenMonitoringApp
{
    public class MobiFlowManager
    {
        public static readonly string DownloadsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "MobiDesk");

        private volatile bool isRunning = false;
        private readonly Dictionary<string, MobiFlowDeviceSession> activeSessions = new Dictionary<string, MobiFlowDeviceSession>(StringComparer.OrdinalIgnoreCase);
        private readonly object sessionsLock = new object();

        public event Action<FlowMessageItem> MessageReceived;
        public event Action<FlowMessageItem> FileProgress;
        public event Action<NotificationCardItem> NotificationReceived;
        public event Action<string, string> NotificationRemoved;
        public event Action<string, bool> DeviceConnectionChanged;
        public event Action<bool> ConnectionChanged;

        public MobiFlowManager()
        {
            try
            {
                if (!Directory.Exists(DownloadsFolder))
                {
                    Directory.CreateDirectory(DownloadsFolder);
                }
            }
            catch { }
        }

        public void Start()
        {
            if (isRunning) return;
            isRunning = true;
            lock (sessionsLock)
            {
                if (activeSessions.Count == 0)
                {
                    MobiFlowDeviceSession fallback = new MobiFlowDeviceSession(this, "default", "Android", MobiFlowProtocol.PORT);
                    activeSessions["default"] = fallback;
                    fallback.Start();
                }
            }
        }

        public void Stop()
        {
            isRunning = false;
            lock (sessionsLock)
            {
                foreach (var s in activeSessions.Values)
                {
                    try { s.Stop(); } catch { }
                }
                activeSessions.Clear();
            }
            if (ConnectionChanged != null) ConnectionChanged(false);
        }

        public bool IsConnected(string serial = null)
        {
            lock (sessionsLock)
            {
                if (string.IsNullOrEmpty(serial))
                {
                    foreach (var s in activeSessions.Values)
                        if (s.IsConnected) return true;
                    return false;
                }
                MobiFlowDeviceSession sDev;
                if (activeSessions.TryGetValue(serial, out sDev))
                    return sDev.IsConnected;
                return false;
            }
        }

        public void UpdateDevices(List<ConnectedDeviceItem> devices)
        {
            if (!isRunning) isRunning = true;
            lock (sessionsLock)
            {
                if (devices != null && devices.Count > 0 && activeSessions.ContainsKey("default"))
                {
                    activeSessions["default"].Stop();
                    activeSessions.Remove("default");
                }

                HashSet<string> currentSerials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (devices != null)
                {
                    foreach (var dev in devices)
                    {
                        currentSerials.Add(dev.Serial);
                        MobiFlowDeviceSession session;
                        if (!activeSessions.TryGetValue(dev.Serial, out session))
                        {
                            session = new MobiFlowDeviceSession(this, dev.Serial, dev.FriendlyName, dev.FlowPort);
                            activeSessions[dev.Serial] = session;
                            session.Start();
                        }
                        else
                        {
                            session.FriendlyName = dev.FriendlyName;
                            if (session.Port != dev.FlowPort)
                            {
                                session.Stop();
                                session.Port = dev.FlowPort;
                                session.Start();
                            }
                        }
                    }
                }

                List<string> toRemove = new List<string>();
                foreach (var kvp in activeSessions)
                {
                    if (kvp.Key != "default" && !currentSerials.Contains(kvp.Key))
                    {
                        toRemove.Add(kvp.Key);
                        kvp.Value.Stop();
                    }
                }
                foreach (string s in toRemove) activeSessions.Remove(s);
            }
        }

        private MobiFlowDeviceSession GetSession(string targetSerial)
        {
            lock (sessionsLock)
            {
                if (!string.IsNullOrEmpty(targetSerial))
                {
                    MobiFlowDeviceSession s;
                    if (activeSessions.TryGetValue(targetSerial, out s)) return s;
                }
                foreach (var s in activeSessions.Values)
                {
                    if (s.IsConnected) return s;
                }
                foreach (var s in activeSessions.Values) return s;
                return null;
            }
        }

        public FlowMessageItem SendTextMessage(string text, string targetSerial = null)
        {
            MobiFlowDeviceSession s = GetSession(targetSerial);
            if (s != null) return s.SendTextMessage(text);
            return null;
        }

        public FlowMessageItem SendFile(string filePath, string targetSerial = null, Action<int> onProgress = null)
        {
            MobiFlowDeviceSession s = GetSession(targetSerial);
            if (s != null) return s.SendFile(filePath, onProgress);
            return null;
        }

        public void DismissNotification(string pkg, int id, string key, string deviceSerial = null)
        {
            MobiFlowDeviceSession s = GetSession(deviceSerial);
            if (s != null) s.DismissNotification(pkg, id, key);
        }

        internal void DispatchMessageReceived(FlowMessageItem item)
        {
            if (MessageReceived != null) MessageReceived(item);
        }

        internal void DispatchFileProgress(FlowMessageItem item)
        {
            if (FileProgress != null) FileProgress(item);
        }

        internal void DispatchNotificationReceived(NotificationCardItem notif)
        {
            if (NotificationReceived != null) NotificationReceived(notif);
        }

        internal void DispatchNotificationRemoved(string key, string serial)
        {
            if (NotificationRemoved != null) NotificationRemoved(key, serial);
        }

        internal void NotifyDeviceConnectionChanged(string serial, bool connected)
        {
            if (DeviceConnectionChanged != null) DeviceConnectionChanged(serial, connected);
            if (ConnectionChanged != null) ConnectionChanged(IsConnected());
        }
    }

}