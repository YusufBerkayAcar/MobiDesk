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
    public class MobiFlowDeviceSession
    {
        public string Serial { get; set; }
        public string FriendlyName { get; set; }
        public int Port { get; set; }
        public volatile bool IsConnected = false;

        private MobiFlowManager manager;
        private volatile bool isRunning = true;
        private Thread clientThread;
        private TcpClient activeClient;
        private NetworkStream activeStream;
        private readonly object sendLock = new object();

        private readonly Dictionary<string, FileStream> activeReceivingFiles = new Dictionary<string, FileStream>();
        private readonly Dictionary<string, FlowMessageItem> activeFileItems = new Dictionary<string, FlowMessageItem>();

        public MobiFlowDeviceSession(MobiFlowManager mgr, string serial, string friendlyName, int port)
        {
            this.manager = mgr;
            this.Serial = serial;
            this.FriendlyName = friendlyName;
            this.Port = port;
        }

        public void Start()
        {
            lock (this)
            {
                if (isRunning && clientThread != null && clientThread.IsAlive) return;
                isRunning = true;
                if (clientThread != null && clientThread.IsAlive)
                {
                    try { clientThread.Interrupt(); clientThread.Join(500); } catch { }
                }
                clientThread = new Thread(ClientWorkerLoop);
                clientThread.IsBackground = true;
                clientThread.Start();
            }
        }

        public void Stop()
        {
            bool wasConn;
            lock (this)
            {
                isRunning = false;
                wasConn = IsConnected;
                IsConnected = false;
                lock (sendLock)
                {
                    try { if (activeStream != null) activeStream.Close(); } catch { }
                    try { if (activeClient != null) activeClient.Close(); } catch { }
                    activeStream = null;
                    activeClient = null;
                }
                try { if (clientThread != null && clientThread.IsAlive) clientThread.Interrupt(); } catch { }
            }
            if (wasConn)
            {
                manager.NotifyDeviceConnectionChanged(Serial, false);
            }
        }

        private void ClientWorkerLoop()
        {
            while (isRunning)
            {
                TcpClient client = null;
                try
                {
                    client = new TcpClient();
                    client.NoDelay = true;
                    client.SendBufferSize = 64 * 1024;
                    client.ReceiveBufferSize = 64 * 1024;
                    client.Connect("127.0.0.1", Port);

                    lock (sendLock)
                    {
                        activeClient = client;
                        activeStream = client.GetStream();
                    }

                    // Handshake ping: Android will respond with PONG or active notifications.
                    SendRawPacket(MobiFlowProtocol.TYPE_PING, new byte[0]);

                    client.ReceiveTimeout = 6000;
                    BinaryReader br = new BinaryReader(activeStream);

                    while (isRunning && client.Connected)
                    {
                        int m0;
                        try
                        {
                            m0 = br.ReadByte();
                        }
                        catch (IOException ioEx)
                        {
                            SocketException sockEx = ioEx.InnerException as SocketException;
                            if (sockEx != null && sockEx.SocketErrorCode == SocketError.TimedOut)
                            {
                                // 6 seconds idle timeout - send keepalive PING to ensure ADB bridge is alive
                                if (!SendRawPacket(MobiFlowProtocol.TYPE_PING, new byte[0]))
                                {
                                    break;
                                }
                                continue;
                            }
                            throw;
                        }

                        int m1 = br.ReadByte();
                        if (m0 != 0x4D || m1 != 0x46)
                        {
                            continue;
                        }

                        if (!IsConnected)
                        {
                            IsConnected = true;
                            manager.NotifyDeviceConnectionChanged(Serial, true);
                            Program.Log("Connected & verified MobiFlow on port " + Port + " for " + Serial);
                        }

                        short type = IPAddress.NetworkToHostOrder(br.ReadInt16());
                        int len = IPAddress.NetworkToHostOrder(br.ReadInt32());
                        if (len < 0 || len > 100 * 1024 * 1024) break;

                        byte[] payload = new byte[len];
                        int totalRead = 0;
                        while (totalRead < len)
                        {
                            int r = br.Read(payload, totalRead, len - totalRead);
                            if (r <= 0) throw new IOException("Disconnected while reading payload");
                            totalRead += r;
                        }

                        ProcessIncomingPacket(type, payload);
                    }
                }
                catch
                {
                    // Disconnected or connection refused
                }
                finally
                {
                    bool wasConn = IsConnected;
                    lock (sendLock)
                    {
                        try { if (client != null) client.Close(); } catch { }
                        if (activeClient == client)
                        {
                            activeClient = null;
                            activeStream = null;
                        }
                    }
                    IsConnected = false;
                    if (wasConn)
                    {
                        manager.NotifyDeviceConnectionChanged(Serial, false);
                        Program.Log("Disconnected from MobiFlow on port " + Port + " for " + Serial);
                    }
                }

                if (isRunning)
                {
                    try { Thread.Sleep(1500); } catch (ThreadInterruptedException) { }
                }
            }
        }

        private void ProcessIncomingPacket(short type, byte[] payload)
        {
            try
            {
                if (type == MobiFlowProtocol.TYPE_TEXT)
                {
                    string json = Encoding.UTF8.GetString(payload);
                    Program.Log("PC received TYPE_TEXT from " + this.Serial + " (" + this.FriendlyName + "): " + json);
                    Dictionary<string, string> dict = MiniJson.Parse(json);
                    FlowMessageItem item = new FlowMessageItem();
                    item.Id = dict.ContainsKey("id") ? dict["id"] : Guid.NewGuid().ToString();
                    item.Sender = dict.ContainsKey("sender") ? dict["sender"] : "mobile";
                    item.Text = dict.ContainsKey("text") ? dict["text"] : json;
                    item.Timestamp = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
                    item.DeviceSerial = this.Serial;
                    item.DeviceName = this.FriendlyName;
                    manager.DispatchMessageReceived(item);
                }
                else if (type == MobiFlowProtocol.TYPE_FILE_START)
                {
                    string json = Encoding.UTF8.GetString(payload);
                    Dictionary<string, string> dict = MiniJson.Parse(json);
                    string transferId = dict.ContainsKey("transferId") ? dict["transferId"] : Guid.NewGuid().ToString();
                    string rawFileName = dict.ContainsKey("fileName") ? dict["fileName"] : "dosya";
                    long fileSize = 0;
                    if (dict.ContainsKey("fileSize")) long.TryParse(dict["fileSize"], out fileSize);

                    if (!Directory.Exists(MobiFlowManager.DownloadsFolder))
                    {
                        try { Directory.CreateDirectory(MobiFlowManager.DownloadsFolder); } catch { }
                    }

                    // Windows dosya sistemi için geçerli ve güvenli dosya adı
                    string safeName = Path.GetFileName(rawFileName);
                    foreach (char c in Path.GetInvalidFileNameChars())
                    {
                        safeName = safeName.Replace(c, '_');
                    }
                    if (string.IsNullOrEmpty(safeName.Trim())) safeName = "dosya_" + DateTime.Now.Ticks + ".png";

                    // Dosya çakışmalarını ve kilitlenmelerini önle
                    string ext = Path.GetExtension(safeName);
                    string baseName = Path.GetFileNameWithoutExtension(safeName);
                    string targetPath = Path.Combine(MobiFlowManager.DownloadsFolder, safeName);
                    int counter = 1;
                    while (File.Exists(targetPath))
                    {
                        try
                        {
                            using (FileStream test = new FileStream(targetPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                            {
                                break;
                            }
                        }
                        catch
                        {
                            targetPath = Path.Combine(MobiFlowManager.DownloadsFolder, string.Format("{0}_{1}{2}", baseName, counter++, ext));
                        }
                    }

                    try
                    {
                        FileStream fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                        activeReceivingFiles[transferId] = fs;

                        FlowMessageItem item = new FlowMessageItem();
                        item.Id = transferId;
                        item.Sender = "mobile";
                        item.IsFile = true;
                        item.FileName = Path.GetFileName(targetPath);
                        item.FileSize = fileSize;
                        item.FilePath = targetPath;
                        item.Progress = 0;
                        item.Timestamp = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
                        item.DeviceSerial = this.Serial;
                        item.DeviceName = this.FriendlyName;
                        activeFileItems[transferId] = item;

                        manager.DispatchMessageReceived(item);
                    }
                    catch (Exception ex)
                    {
                        Program.Log("TYPE_FILE_START open error: " + ex.Message);
                    }
                }
                else if (type == MobiFlowProtocol.TYPE_FILE_CHUNK)
                {
                    using (MemoryStream ms = new MemoryStream(payload))
                    using (BinaryReader br = new BinaryReader(ms))
                    {
                        string transferId = MobiFlowProtocol.ReadNetUTF(br);
                        long offset = MobiFlowProtocol.ReadNetInt64(br);
                        int chunkLen = MobiFlowProtocol.ReadNetInt32(br);
                        byte[] chunk = br.ReadBytes(chunkLen);

                        FileStream fs;
                        if (activeReceivingFiles.TryGetValue(transferId, out fs))
                        {
                            try
                            {
                                if (fs.Position != offset)
                                {
                                    fs.Seek(offset, SeekOrigin.Begin);
                                }
                                fs.Write(chunk, 0, chunk.Length);
                                fs.Flush();
                            }
                            catch (Exception ex)
                            {
                                Program.Log("TYPE_FILE_CHUNK write error: " + ex.Message);
                            }

                            FlowMessageItem item;
                            if (activeFileItems.TryGetValue(transferId, out item) && item.FileSize > 0)
                            {
                                long totalSoFar = offset + chunkLen;
                                item.Progress = (int)Math.Min(99, (totalSoFar * 100) / item.FileSize);
                                manager.DispatchFileProgress(item);
                            }
                        }
                    }
                }
                else if (type == MobiFlowProtocol.TYPE_FILE_END)
                {
                    string json = Encoding.UTF8.GetString(payload);
                    Dictionary<string, string> dict = MiniJson.Parse(json);
                    string transferId = dict.ContainsKey("transferId") ? dict["transferId"] : "";

                    FileStream fs;
                    if (activeReceivingFiles.TryGetValue(transferId, out fs))
                    {
                        try
                        {
                            fs.Flush();
                            fs.Close();
                        }
                        catch { }
                        activeReceivingFiles.Remove(transferId);
                    }

                    FlowMessageItem item;
                    if (activeFileItems.TryGetValue(transferId, out item))
                    {
                        item.Progress = 100;
                        activeFileItems.Remove(transferId);
                        manager.DispatchFileProgress(item);
                    }
                }
                else if (type == MobiFlowProtocol.TYPE_NOTIFICATION)
                {
                    string json = Encoding.UTF8.GetString(payload);
                    Dictionary<string, string> dict = MiniJson.Parse(json);
                    NotificationCardItem notif = new NotificationCardItem();
                    int id = 0;
                    if (dict.ContainsKey("id")) int.TryParse(dict["id"], out id);
                    notif.Id = id;
                    notif.Key = dict.ContainsKey("key") ? dict["key"] : "";
                    notif.PackageName = dict.ContainsKey("pkg") ? dict["pkg"] : "";
                    string rawApp = dict.ContainsKey("app") ? dict["app"] : notif.PackageName;
                    notif.AppName = NotificationHelper.ResolveAppName(rawApp, notif.PackageName);
                    notif.Title = dict.ContainsKey("title") ? dict["title"] : "";
                    notif.Text = dict.ContainsKey("text") ? dict["text"] : "";
                    notif.IconBase64 = dict.ContainsKey("icon") ? dict["icon"] : null;
                    notif.Timestamp = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
                    notif.DeviceSerial = this.Serial;
                    notif.DeviceName = this.FriendlyName;

                    if (!string.IsNullOrEmpty(notif.IconBase64))
                    {
                        try
                        {
                            byte[] imgBytes = Convert.FromBase64String(notif.IconBase64);
                            using (MemoryStream ms = new MemoryStream(imgBytes))
                            using (Bitmap temp = new Bitmap(ms))
                            {
                                notif.CachedIcon = new Bitmap(temp);
                            }
                        }
                        catch { }
                    }

                    manager.DispatchNotificationReceived(notif);
                }
                else if (type == MobiFlowProtocol.TYPE_NOTIFICATION_REMOVE)
                {
                    string str = Encoding.UTF8.GetString(payload);
                    string[] parts = str.Split('\t');
                    string key = (parts.Length >= 3) ? parts[2] : (parts.Length >= 1 ? parts[0] : "");
                    manager.DispatchNotificationRemoved(key, this.Serial);
                }
                else if (type == MobiFlowProtocol.TYPE_PING)
                {
                    SendRawPacket(MobiFlowProtocol.TYPE_PONG, new byte[0]);
                }
                else if (type == MobiFlowProtocol.TYPE_PONG)
                {
                    // Heartbeat PONG received from Android device
                }
            }
            catch (Exception ex)
            {
                Program.Log("Session ProcessIncomingPacket error: " + ex.Message);
            }
        }

        public FlowMessageItem SendTextMessage(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string id = Guid.NewGuid().ToString();
            long time = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
            string json = string.Format("{{\"id\":\"{0}\",\"sender\":\"pc\",\"text\":\"{1}\",\"timestamp\":{2}}}",
                id, MiniJson.Escape(text), time);

            FlowMessageItem item = new FlowMessageItem();
            item.Id = id;
            item.Sender = "pc";
            item.Text = text;
            item.Timestamp = time;
            item.DeviceSerial = this.Serial;
            item.DeviceName = this.FriendlyName;

            bool sent = SendRawPacket(MobiFlowProtocol.TYPE_TEXT, Encoding.UTF8.GetBytes(json));
            if (!sent)
            {
                Program.Log("Warning: MobiFlow message not sent (device " + this.Serial + " not connected)");
            }
            manager.DispatchMessageReceived(item);
            return item;
        }

        public FlowMessageItem SendFile(string filePath, Action<int> onProgress = null)
        {
            if (!File.Exists(filePath)) return null;
            string transferId = Guid.NewGuid().ToString();
            FileInfo fi = new FileInfo(filePath);
            string fileName = fi.Name;
            long fileSize = fi.Length;

            FlowMessageItem item = new FlowMessageItem();
            item.Id = transferId;
            item.Sender = "pc";
            item.IsFile = true;
            item.FileName = fileName;
            item.FileSize = fileSize;
            item.FilePath = filePath;
            item.Progress = 0;
            item.Timestamp = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
            item.DeviceSerial = this.Serial;
            item.DeviceName = this.FriendlyName;

            manager.DispatchMessageReceived(item);

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    // 1. FILE_START
                    string startJson = string.Format("{{\"transferId\":\"{0}\",\"fileName\":\"{1}\",\"fileSize\":{2}}}",
                        transferId, MiniJson.Escape(fileName), fileSize);
                    SendRawPacket(MobiFlowProtocol.TYPE_FILE_START, Encoding.UTF8.GetBytes(startJson));

                    // 2. FILE_CHUNKS (64 KB)
                    byte[] buffer = new byte[64 * 1024];
                    using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        long offset = 0;
                        int read;
                        while ((read = fs.Read(buffer, 0, buffer.Length)) > 0 && isRunning)
                        {
                            using (MemoryStream ms = new MemoryStream())
                            using (BinaryWriter bw = new BinaryWriter(ms))
                            {
                                MobiFlowProtocol.WriteNetUTF(bw, transferId);
                                MobiFlowProtocol.WriteNetInt64(bw, offset);
                                MobiFlowProtocol.WriteNetInt32(bw, read);
                                bw.Write(buffer, 0, read);
                                bw.Flush();

                                SendRawPacket(MobiFlowProtocol.TYPE_FILE_CHUNK, ms.ToArray());
                            }
                            offset += read;
                            int pct = (int)Math.Min(99, (offset * 100) / fileSize);
                            item.Progress = pct;
                            manager.DispatchFileProgress(item);
                            if (onProgress != null) onProgress(pct);
                        }
                    }

                    // 3. FILE_END
                    string endJson = string.Format("{{\"transferId\":\"{0}\",\"status\":\"OK\"}}", transferId);
                    SendRawPacket(MobiFlowProtocol.TYPE_FILE_END, Encoding.UTF8.GetBytes(endJson));

                    item.Progress = 100;
                    manager.DispatchFileProgress(item);
                    if (onProgress != null) onProgress(100);
                }
                catch (Exception ex)
                {
                    Program.Log("SendFile error: " + ex.Message);
                }
            });

            return item;
        }

        public void DismissNotification(string pkg, int id, string key)
        {
            string str = string.Format("{0}\t{1}\t{2}", pkg, id, key);
            SendRawPacket(MobiFlowProtocol.TYPE_DISMISS_NOTIFICATION, Encoding.UTF8.GetBytes(str));
        }

        public bool SendRawPacket(short type, byte[] payload)
        {
            lock (sendLock)
            {
                if (activeStream == null || activeClient == null || !activeClient.Connected)
                    return false;

                try
                {
                    byte[] header = new byte[8];
                    header[0] = MobiFlowProtocol.MAGIC[0];
                    header[1] = MobiFlowProtocol.MAGIC[1];

                    short netType = IPAddress.HostToNetworkOrder(type);
                    header[2] = (byte)(netType & 0xFF);
                    header[3] = (byte)((netType >> 8) & 0xFF);

                    int netLen = IPAddress.HostToNetworkOrder(payload != null ? payload.Length : 0);
                    header[4] = (byte)(netLen & 0xFF);
                    header[5] = (byte)((netLen >> 8) & 0xFF);
                    header[6] = (byte)((netLen >> 16) & 0xFF);
                    header[7] = (byte)((netLen >> 24) & 0xFF);

                    activeStream.Write(header, 0, 8);
                    if (payload != null && payload.Length > 0)
                    {
                        activeStream.Write(payload, 0, payload.Length);
                    }
                    activeStream.Flush();
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

}