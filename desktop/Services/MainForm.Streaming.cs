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
    public partial class MainForm : Form
    {
        private void BtnStartStop_Click(object sender, EventArgs e)
        {
            if (!isStreaming)
            {
                StartStreaming();
            }
            else
            {
                StopStreaming();
            }
        }

        private void StartStreaming()
        {
            try
            {
                foreach (Process p in Process.GetProcessesByName("ScreenServer"))
                {
                    try { p.Kill(); } catch { }
                }
            }
            catch { }

            RunAdbForward();
            LaunchAppOnDeviceOnce();

            isStreaming = true;
            btnStartStop.Text = "⏹    " + L10n.T("EKRAN PAYLAŞIMINI DURDUR", "STOP SCREEN STREAMING");
            btnStartStop.SetColors(ColorDanger, Color.White);
            heroBanner.SetStatus(L10n.T("BAĞLANTI ARANIYOR...", "SEARCHING CONNECTION..."), ColorWarning);
            UpdateStatus(L10n.T("BAĞLANTI ARANIYOR...", "SEARCHING CONNECTION..."), L10n.T("Cihazda uygulama açık olmalıdır (127.0.0.1:55555)", "Client app must be open on device (127.0.0.1:55555)"), ColorWarning);

            streamThread = new Thread(StreamingWorker);
            streamThread.IsBackground = true;
            streamThread.Priority = ThreadPriority.AboveNormal;
            streamThread.Start();

            if (AppConfig.EnableAudio)
            {
                StartAudioStreamingWorker();
            }

            UpdateTrayMenuStrings();
        }

        private void StopStreaming()
        {
            isStreaming = false;
            StopAudioStreamingWorker();
            try
            {
                if (activeClient != null)
                {
                    activeClient.Close();
                }
            }
            catch { }
            activeClient = null;

            btnStartStop.Text = "▶    " + L10n.T("EKRAN PAYLAŞIMINI BAŞLAT", "START SCREEN STREAMING");
            btnStartStop.SetColors(ColorSuccess, Color.FromArgb(11, 15, 25));
            heroBanner.SetStatus(L10n.T("BEKLENİYOR", "WAITING"), ColorTextSubtle);
            UpdateStatus(L10n.T("DURDURULDU", "STOPPED"), L10n.T("Yayını başlatmak için yukarıdaki butona tıklayın.", "Click the button above to start streaming."), ColorSuccess);

            badgeFps.Value = "0";
            badgeBandwidth.Value = "0.0 MB/s";
            badgeSize.Value = "0 KB";
            badgeLatency.Value = "<1 ms";
            UpdateTrayMenuStrings();
        }

        private void UpdateStatus(string title, string details, Color dotColor)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(delegate() { UpdateStatus(title, details, dotColor); }));
                return;
            }
            lblStatusTitle.Text = title;
            lblStatusSub.Text = details;
            lblStatusDot.ForeColor = dotColor;
        }

        private static void AttachToDefaultDesktop()
        {
            try
            {
                IntPtr hDesk = OpenDesktop("default", 0, false, 0x01FF);
                if (hDesk != IntPtr.Zero)
                {
                    SetThreadDesktop(hDesk);
                }
            }
            catch { }
        }

        private void UpdateFps(int val)
        {
            targetFps = val;
            lblFpsVal.Text = string.Format("{0} FPS", val);
        }

        private void UpdateQuality(int val)
        {
            targetQuality = val;
            string levelDesc;
            if (val <= 35) levelDesc = L10n.T("Hızlı", "Fast");
            else if (val <= 65) levelDesc = L10n.T("Dengeli", "Balanced");
            else if (val <= 85) levelDesc = L10n.T("Yüksek", "High");
            else levelDesc = L10n.T("Ultra Net", "Ultra Crisp");

            lblQualityVal.Text = string.Format(L10n.T("%{0} ({1})", "{0}% ({1})"), val, levelDesc);
        }

        private void StatsTimer_Tick(object sender, EventArgs e)
        {
            if (isStreaming && activeClient != null && activeClient.Connected)
            {
                int framesDelta = totalFramesSent - lastFramesSent;
                long curTotalBytes = Interlocked.Read(ref totalBytesSent);
                long bytesDelta = curTotalBytes - lastBytesSent;
                lastFramesSent = totalFramesSent;
                lastBytesSent = curTotalBytes;

                currentSentFps = framesDelta;
                double mbPerSec = bytesDelta / (1024.0 * 1024.0);
                int avgKb = framesDelta > 0 ? (int)(bytesDelta / (framesDelta * 1024)) : 0;

                badgeFps.Value = string.Format("{0}", currentSentFps);
                badgeBandwidth.Value = string.Format("{0:F1} MB/s", mbPerSec);
                badgeSize.Value = string.Format("{0} KB", avgKb);
                badgeLatency.Value = "<2 ms";

                lblStatusTitle.Text = L10n.T("CANLI YAYIN AKTİF", "LIVE STREAM ACTIVE");
                lblStatusSub.Text = string.Format(L10n.T("Cihaza {0} FPS hızında {1:F1} MB/s veri akışı sağlanıyor.", "Streaming to device at {0} FPS ({1:F1} MB/s)."), currentSentFps, mbPerSec);
                lblStatusDot.ForeColor = ColorSuccess;
                heroBanner.SetStatus(L10n.T("CANLI YAYINDA", "STREAMING LIVE"), ColorSuccess);
                Program.Log(string.Format("PC_STATS -> sentFps={0}, mbPerSec={1:F2}, avgKb={2}, targetFps={3}, targetQ={4}, battSaver={5}", currentSentFps, mbPerSec, avgKb, targetFps, targetQuality, enableBatterySaver));
            }
        }

        private void StreamingWorker()
        {
            try { Thread.CurrentThread.Priority = ThreadPriority.AboveNormal; } catch { }
            AttachToDefaultDesktop();

            while (isStreaming)
            {
                RunAdbForward();

                TcpClient client = null;
                while (isStreaming)
                {
                    try
                    {
                        client = new TcpClient();
                        client.NoDelay = true;
                        client.SendBufferSize = 64 * 1024;
                        client.Connect("127.0.0.1", 55555);
                        break;
                    }
                    catch
                    {
                        try { if (client != null) client.Close(); } catch { }
                        client = null;
                        Thread.Sleep(800);
                    }
                }

                if (client != null && client.Connected)
                {
                    activeClient = client;
                    pendingFpsConfig = true;
                    totalFramesSent = 0;
                    totalBytesSent = 0;
                    lastFramesSent = 0;
                    lastBytesSent = 0;
                    UpdateStatus(L10n.T("CANLI YAYIN AKTİF", "LIVE STREAM ACTIVE"), string.Format(L10n.T("Cihaza {0} FPS hızında kesintisiz aktarım başladı.", "Streaming smoothly to device at {0} FPS."), targetFps), ColorSuccess);
                    heroBanner.SetStatus(L10n.T("CANLI YAYINDA", "STREAMING LIVE"), ColorSuccess);
                    StreamToClient(client);
                    activeClient = null;
                    if (isStreaming)
                    {
                        heroBanner.SetStatus(L10n.T("BEKLENİYOR", "WAITING"), ColorWarning);
                        UpdateStatus(
                            L10n.T("CİHAZ BEKLENİYOR", "WAITING FOR DEVICE"),
                            L10n.T("Cihazda uygulama açıldığında yayın otomatik devam edecek.", "Stream will resume automatically when app is opened on device."),
                            ColorWarning
                        );
                        Thread.Sleep(1000);
                    }
                }
            }
        }

        private void StreamToClient(TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            ImageCodecInfo jpegEncoder = GetJpegEncoder();

            Thread touchThread = new Thread(delegate()
            {
                AttachToDefaultDesktop();
                try
                {
                    using (StreamReader reader = new StreamReader(stream, Encoding.ASCII))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null && isStreaming)
                        {
                            if (line.StartsWith("DEVICE_INFO"))
                            {
                                HandleDeviceInfo(line);
                            }
                            else if (line.StartsWith("FPS_STATE"))
                            {
                                HandleFpsStateFromClient(line);
                            }
                            else
                            {
                                ProcessTouchCommand(line);
                            }
                        }
                    }
                }
                catch { }
            });
            touchThread.IsBackground = true;
            touchThread.Start();

            Bitmap targetBmp = null;
            Graphics gTarget = null;
            int lastTargetW = 0;
            int lastTargetH = 0;

            MemoryStream ms = new MemoryStream(1024 * 512);

            long ticksPerSec = Stopwatch.Frequency;
            long nextFrameTick = Stopwatch.GetTimestamp();
            long lastFrameHash = 0;
            int lastMouseX = -1;
            int lastMouseY = -1;
            IntPtr lastCursor = IntPtr.Zero;
            long lastSendTicks = 0;

            try
            {
                while (client.Connected && isStreaming)
                {
                    int curTargetW, curTargetH;
                    GetStreamTargetResolution(clientTargetWidth, clientTargetHeight, out curTargetW, out curTargetH);

                    if (targetBmp == null || curTargetW != lastTargetW || curTargetH != lastTargetH)
                    {
                        if (gTarget != null) { gTarget.Dispose(); gTarget = null; }
                        if (targetBmp != null) { targetBmp.Dispose(); targetBmp = null; }

                        targetBmp = new Bitmap(curTargetW, curTargetH, PixelFormat.Format32bppRgb);
                        gTarget = Graphics.FromImage(targetBmp);
                        lastTargetW = curTargetW;
                        lastTargetH = curTargetH;
                    }

                    Rectangle physBounds = GetPhysicalMonitorBounds(selectedDeviceName);
                    Rectangle captureBounds = (physBounds.Width > 0 && physBounds.Height > 0) ? physBounds : currentScreenBounds;
                    if (captureBounds.Width <= 0 || captureBounds.Height <= 0)
                    {
                        captureBounds = Screen.PrimaryScreen.Bounds;
                    }
                    currentScreenBounds = captureBounds;

                    long t0 = Stopwatch.GetTimestamp();
                    IntPtr hdcScreen = GetDC(IntPtr.Zero);
                    IntPtr hdcDst = gTarget.GetHdc();
                    if (curTargetW == captureBounds.Width && curTargetH == captureBounds.Height)
                    {
                        BitBlt(hdcDst, 0, 0, curTargetW, curTargetH,
                               hdcScreen, captureBounds.X, captureBounds.Y,
                               0x00CC0020);
                    }
                    else
                    {
                        SetStretchBltMode(hdcDst, 3); // COLORONCOLOR
                        StretchBlt(hdcDst, 0, 0, curTargetW, curTargetH,
                                   hdcScreen, captureBounds.X, captureBounds.Y, captureBounds.Width, captureBounds.Height,
                                   0x00CC0020);
                    }

                    CURSORINFO pci;
                    pci.cbSize = Marshal.SizeOf(typeof(CURSORINFO));
                    if (GetCursorInfo(out pci) && (pci.flags & CURSOR_SHOWING) != 0)
                    {
                        if (captureBounds.Contains(pci.ptScreenPos.x, pci.ptScreenPos.y))
                        {
                            int relX = pci.ptScreenPos.x - captureBounds.X;
                            int relY = pci.ptScreenPos.y - captureBounds.Y;
                            int curTargetX = (int)((double)relX * curTargetW / captureBounds.Width);
                            int curTargetY = (int)((double)relY * curTargetH / captureBounds.Height);

                            ICONINFO ii;
                            if (GetIconInfo(pci.hCursor, out ii))
                            {
                                curTargetX -= ii.xHotspot;
                                curTargetY -= ii.yHotspot;
                                if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask);
                                if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
                            }

                            DrawIconEx(hdcDst, curTargetX, curTargetY, pci.hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL);
                        }
                    }

                    gTarget.ReleaseHdc(hdcDst);
                    ReleaseDC(IntPtr.Zero, hdcScreen);
                    long t1 = Stopwatch.GetTimestamp();

                    // 2. Akıllı Güç Tasarrufu (Yalnızca açıksa)
                    if (enableBatterySaver)
                    {
                        long curFrameHash = 17;
                        BitmapData bd = targetBmp.LockBits(new Rectangle(0, 0, curTargetW, curTargetH), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                        try
                        {
                            unsafe
                            {
                                int* ptr = (int*)bd.Scan0.ToPointer();
                                int stride = bd.Stride / 4;
                                int stepY = Math.Max(1, curTargetH / 16);
                                int stepX = Math.Max(1, curTargetW / 8);
                                for (int y = 0; y < curTargetH; y += stepY)
                                {
                                    int* row = ptr + y * stride;
                                    for (int x = 0; x < curTargetW; x += stepX)
                                    {
                                        curFrameHash = curFrameHash * 31 + *(row + x);
                                    }
                                }
                            }
                        }
                        finally
                        {
                            targetBmp.UnlockBits(bd);
                        }

                        int curMouseX = pci.ptScreenPos.x;
                        int curMouseY = pci.ptScreenPos.y;
                        IntPtr curCursor = pci.hCursor;
                        bool contentChanged = (curFrameHash != lastFrameHash) ||
                                              (curMouseX != lastMouseX) ||
                                              (curMouseY != lastMouseY) ||
                                              (curCursor != lastCursor);

                        long nowTicks = Stopwatch.GetTimestamp();
                        bool forceSend = (nowTicks - lastSendTicks) >= (ticksPerSec * 1);

                        if (!contentChanged && !forceSend)
                        {
                            long idleTargetTicks = ticksPerSec / targetFps;
                            nextFrameTick += idleTargetTicks;
                            while (true)
                            {
                                long idleCurTick = Stopwatch.GetTimestamp();
                                long remainingTicks = nextFrameTick - idleCurTick;
                                if (remainingTicks <= 0)
                                {
                                    if (idleCurTick - nextFrameTick > idleTargetTicks)
                                    {
                                        nextFrameTick = idleCurTick;
                                    }
                                    break;
                                }
                                int remainingMs = (int)((remainingTicks * 1000) / ticksPerSec);
                                if (remainingMs > 16)
                                {
                                    Thread.Sleep(1);
                                }
                                else if (remainingMs > 2)
                                {
                                    Thread.Sleep(0);
                                }
                                else
                                {
                                    Thread.SpinWait(20);
                                }
                            }
                            continue;
                        }

                        lastFrameHash = curFrameHash;
                        lastMouseX = curMouseX;
                        lastMouseY = curMouseY;
                        lastCursor = curCursor;
                        lastSendTicks = nowTicks;
                    }

                    // 3. Dinamik Kalite Ayarına Göre JPEG Sıkıştırma
                    int q = targetQuality;
                    if (q < 10) q = 10;
                    if (q > 100) q = 100;

                    EncoderParameters encoderParams = new EncoderParameters(1);
                    encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)q);

                    ms.SetLength(0);
                    targetBmp.Save(ms, jpegEncoder, encoderParams);
                    byte[] frameBytes = ms.GetBuffer();
                    int frameLength = (int)ms.Length;
                    long t2 = Stopwatch.GetTimestamp();

                    // 4. FPS Konfigürasyon Paketi (Gerekliyse)
                    if (pendingFpsConfig)
                    {
                        pendingFpsConfig = false;
                        byte[] cfgBytes = new byte[8];
                        int cmd = IPAddress.HostToNetworkOrder(-101);
                        int arg = IPAddress.HostToNetworkOrder(showFpsOnTablet ? 1 : 0);
                        Buffer.BlockCopy(BitConverter.GetBytes(cmd), 0, cfgBytes, 0, 4);
                        Buffer.BlockCopy(BitConverter.GetBytes(arg), 0, cfgBytes, 4, 4);
                        stream.Write(cfgBytes, 0, 8);
                    }

                    int networkLength = IPAddress.HostToNetworkOrder(frameLength);
                    byte[] lenBytes = BitConverter.GetBytes(networkLength);

                    stream.Write(lenBytes, 0, 4);
                    stream.Write(frameBytes, 0, frameLength);
                    stream.Flush();

                    Interlocked.Add(ref totalBytesSent, (long)(frameLength + 4));
                    totalFramesSent++;
                    long t3 = Stopwatch.GetTimestamp();

                    // 5. 1ms Hassas Zamanlayıcı
                    int curFps = targetFps;
                    if (curFps < 15) curFps = 15;
                    if (curFps > 120) curFps = 120;

                    long frameTargetTicks = ticksPerSec / curFps;
                    nextFrameTick += frameTargetTicks;

                    while (true)
                    {
                        long frameCurTick = Stopwatch.GetTimestamp();
                        long remainingTicks = nextFrameTick - frameCurTick;
                        if (remainingTicks <= 0)
                        {
                            if (frameCurTick - nextFrameTick > frameTargetTicks)
                            {
                                nextFrameTick = frameCurTick;
                            }
                            break;
                        }
                        int remainingMs = (int)((remainingTicks * 1000) / ticksPerSec);
                        if (remainingMs > 16)
                        {
                            Thread.Sleep(1);
                        }
                        else if (remainingMs > 2)
                        {
                            Thread.Sleep(0);
                        }
                        else
                        {
                            Thread.SpinWait(20);
                        }
                    }
                    long t4 = Stopwatch.GetTimestamp();

                    if (totalFramesSent % 30 == 0)
                    {
                        double grabMs = (t1 - t0) * 1000.0 / ticksPerSec;
                        double jpegMs = (t2 - t1) * 1000.0 / ticksPerSec;
                        double writeMs = (t3 - t2) * 1000.0 / ticksPerSec;
                        double paceMs = (t4 - t3) * 1000.0 / ticksPerSec;
                        Program.Log(string.Format("PC_FRAME_LOOP -> grab={0:F1}ms, jpeg={1:F1}ms, netWrite={2:F1}ms, pace={3:F1}ms, totalFrames={4}, size={5}KB", grabMs, jpegMs, writeMs, paceMs, totalFramesSent, frameLength / 1024));
                    }
                }
            }
            catch { }
            finally
            {
                if (gTarget != null) gTarget.Dispose();
                if (targetBmp != null) targetBmp.Dispose();
                ms.Dispose();
                try { client.Close(); } catch { }
            }
        }

        private void HandleDeviceInfo(string line)
        {
            try
            {
                string[] parts = line.Split(' ');
                if (parts.Length >= 4)
                {
                    int w, h, dpi;
                    if (int.TryParse(parts[1], out w) && int.TryParse(parts[2], out h) && int.TryParse(parts[3], out dpi))
                    {
                        clientTargetWidth = w;
                        clientTargetHeight = h;
                        clientTargetDpi = dpi;
                        if (parts.Length >= 5)
                        {
                            clientDeviceModel = parts[4].Replace('_', ' ');
                        }

                        this.BeginInvoke(new Action(delegate()
                        {
                            UpdateDeviceSpecsDisplay();
                            heroBanner.SetStatus(L10n.T("CANLI YAYINDA", "STREAMING LIVE"), ColorSuccess);
                            UpdateStatus(L10n.T("CİHAZ AKTİF", "DEVICE ACTIVE"), string.Format(L10n.T("Cihaz: {0} ({1}x{2}) ile bağlantı aktif.", "Connected to device: {0} ({1}x{2})."), clientDeviceModel, w, h), ColorSuccess);
                        }));

                        TrySyncVirtualDisplayResolution(w, h);
                    }
                }
            }
            catch { }
        }

        private void HandleFpsStateFromClient(string line)
        {
            try
            {
                string[] parts = line.Split(' ');
                if (parts.Length >= 2)
                {
                    bool isEnabled = (parts[1] == "1" || parts[1].Equals("true", StringComparison.OrdinalIgnoreCase));
                    this.BeginInvoke(new Action(delegate()
                    {
                        showFpsOnTablet = isEnabled;
                        AppConfig.ShowFps = isEnabled;
                        AppConfig.Save();

                        if (swShowFps != null && swShowFps.Checked != isEnabled)
                        {
                            swShowFps.Checked = isEnabled;
                        }

                        Program.Log(string.Format("FPS counter state synced from mobile client: enabled={0}", isEnabled));
                    }));
                }
            }
            catch (Exception ex)
            {
                Program.Log("HandleFpsStateFromClient error: " + ex.Message);
            }
        }

        private void ProcessTouchCommand(string line)
        {
            if (string.IsNullOrEmpty(line)) return;

            string[] parts = line.Split(' ');
            if (parts.Length == 4 && parts[0] == "TOUCH")
            {
                string action = parts[1];
                float normX, normY;
                if (float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out normX) &&
                    float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out normY))
                {
                    Rectangle bounds = currentScreenBounds;
                    int screenX = bounds.Left + (int)(normX * bounds.Width);
                    int screenY = bounds.Top + (int)(normY * bounds.Height);

                    SetCursorPos(screenX, screenY);

                    if (action == "DOWN")
                    {
                        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                    }
                    else if (action == "UP")
                    {
                        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                    }
                }
            }
        }

        private static ImageCodecInfo GetJpegEncoder()
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == ImageFormat.Jpeg.Guid)
                {
                    return codec;
                }
            }
            return null;
        }
    }
}