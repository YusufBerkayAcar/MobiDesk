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
        internal static string FindAdbPath()
        {
            string localAdb = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adb.exe");
            if (File.Exists(localAdb)) return localAdb;

            string subAdb = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "platform-tools", "adb.exe");
            if (File.Exists(subAdb)) return subAdb;

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
            {
                string sdkAdb = Path.Combine(localAppData, "Android", "Sdk", "platform-tools", "adb.exe");
                if (File.Exists(sdkAdb)) return sdkAdb;
            }

            return "adb";
        }

        public static void RunAdbForwardPort(string adbPath, string serial, int localPort, int remotePort)
        {
            try
            {
                string args = !string.IsNullOrEmpty(serial)
                    ? string.Format("-s {0} forward tcp:{1} tcp:{2}", serial, localPort, remotePort)
                    : string.Format("forward tcp:{0} tcp:{1}", localPort, remotePort);

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit(1000);
                }
            }
            catch { }
        }

        public static HashSet<string> GetActiveAdbForwards(string adbPath)
        {
            HashSet<string> forwards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = "forward --list",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using (Process p = Process.Start(psi))
                {
                    string outStr = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(1000);
                    if (!string.IsNullOrEmpty(outStr))
                    {
                        string[] lines = outStr.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string l in lines)
                        {
                            string[] parts = l.Trim().Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 3)
                            {
                                string ser = parts[0];
                                string loc = parts[1].Replace("tcp:", "");
                                string rem = parts[2].Replace("tcp:", "");
                                forwards.Add(string.Format("{0}:{1}:{2}", ser, loc, rem));
                            }
                        }
                    }
                }
            }
            catch { }
            return forwards;
        }

        private static readonly Dictionary<string, int> deviceFlowPortMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private static void RunAdbForward()
        {
            string adbPath = FindAdbPath();
            try
            {
                string targetDevice = !string.IsNullOrEmpty(activeTargetSerial) ? activeTargetSerial : null;
                RunAdbForwardPort(adbPath, targetDevice, 55555, 55555);
            }
            catch { }
        }

        private static void LaunchAppOnDeviceOnce()
        {
            try
            {
                string adbPath = FindAdbPath();
                string targetDevice = !string.IsNullOrEmpty(activeTargetSerial) ? activeTargetSerial : null;
                ProcessStartInfo psiLaunch = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = !string.IsNullOrEmpty(targetDevice)
                        ? string.Format("-s {0} shell am start -n com.example.screenmonitoring/.MainActivity", targetDevice)
                        : "shell am start -n com.example.screenmonitoring/.MainActivity",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (Process pLaunch = Process.Start(psiLaunch))
                {
                    pLaunch.WaitForExit(1000);
                }
            }
            catch { }
        }

        // ═══════════════════════════════════════════════
        // DİNAMİK USB CİHAZ ALGILAMA VE BİLGİ TOPLAMA
        // ═══════════════════════════════════════════════
        private void CheckConnectedUsbDevicesAsync()
        {
            if (isCheckingDevice) return;
            isCheckingDevice = true;

            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                try
                {
                    DetectAndApplyUsbDevice();
                }
                catch (Exception ex)
                {
                    Program.Log("CheckConnectedUsbDevicesAsync error: " + ex.Message);
                }
                finally
                {
                    isCheckingDevice = false;
                }
            });
        }

        private void DetectAndApplyUsbDevice()
        {
            string adbPath = FindAdbPath();
            if (string.IsNullOrEmpty(adbPath)) return;

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = adbPath,
                Arguments = "devices -l",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            string output = "";
            try
            {
                using (Process p = Process.Start(psi))
                {
                    output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(2000);
                }
            }
            catch { }

            if (string.IsNullOrEmpty(output))
            {
                ApplyDeviceState(null, "none",
                    L10n.T("USB Cihazı Bekleniyor...", "Waiting for USB Device..."),
                    L10n.T("Cihazı USB kablosuyla bağlayın • USB Hata Ayıklama açık olmalıdır", "Connect device via USB • USB Debugging must be enabled"),
                    0, 0, 0);
                return;
            }

            string[] lines = output.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> authorizedDevices = new List<string>();
            List<string> unauthorizedDevices = new List<string>();

            foreach (string line in lines)
            {
                string tr = line.Trim();
                if (tr.StartsWith("List") || string.IsNullOrEmpty(tr)) continue;

                string[] tokens = tr.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length >= 2)
                {
                    string serial = tokens[0];
                    string status = tokens[1].ToLower();
                    if (status == "device") authorizedDevices.Add(serial);
                    else if (status == "unauthorized") unauthorizedDevices.Add(serial);
                }
            }

            if (authorizedDevices.Count == 0)
            {
                if (unauthorizedDevices.Count > 0)
                {
                    ApplyDeviceState(unauthorizedDevices[0], "unauthorized",
                        L10n.T("Android Cihaz (Yetki Bekleniyor)", "Android Device (Auth Required)"),
                        L10n.T("Cihaz ekranında çıkan 'USB Hata Ayıklamaya İzin Ver' uyarısını onaylayın.", "Accept 'Allow USB Debugging' prompt on device screen."),
                        0, 0, 0);
                }
                else
                {
                    ApplyDeviceState(null, "none",
                        L10n.T("USB Cihazı Bekleniyor...", "Waiting for USB Device..."),
                        L10n.T("Cihazı USB kablosuyla bağlayın • USB Hata Ayıklama açık olmalıdır", "Connect device via USB • USB Debugging must be enabled"),
                        0, 0, 0);
                }
                activeForwardCache.Clear();
                deviceFlowPortMap.Clear();
                OnConnectedDevicesChanged(new List<ConnectedDeviceItem>());
                return;
            }

            connectedSerials = authorizedDevices;

            // Remove disconnected devices from deviceFlowPortMap
            List<string> removedSerials = new List<string>();
            foreach (string sMap in deviceFlowPortMap.Keys)
            {
                bool exists = false;
                for (int d = 0; d < authorizedDevices.Count; d++)
                {
                    if (string.Equals(authorizedDevices[d], sMap, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists) removedSerials.Add(sMap);
            }
            for (int r = 0; r < removedSerials.Count; r++) deviceFlowPortMap.Remove(removedSerials[r]);

            // Query actual live ADB forwards from daemon
            HashSet<string> activeForwards = GetActiveAdbForwards(adbPath);

            // Her bağlı cihaz için ayrı stabil port forward tanımla (55557, 55558, ...)
            List<ConnectedDeviceItem> connectedItems = new List<ConnectedDeviceItem>();
            for (int i = 0; i < authorizedDevices.Count; i++)
            {
                string ser = authorizedDevices[i];
                string devModel = ser;
                foreach (string l in lines)
                {
                    if (l.Contains(ser))
                    {
                        int mIdx = l.IndexOf("model:");
                        if (mIdx >= 0)
                        {
                            string rem = l.Substring(mIdx + 6).Split(' ')[0].Trim();
                            if (!string.IsNullOrEmpty(rem)) devModel = rem;
                        }
                        break;
                    }
                }

                string friendly = devModel;
                if (devModel.IndexOf("VIA", StringComparison.OrdinalIgnoreCase) >= 0) friendly = "Casper " + devModel;
                else if (devModel.StartsWith("SM-", StringComparison.OrdinalIgnoreCase) || devModel.StartsWith("SM_", StringComparison.OrdinalIgnoreCase)) friendly = "Samsung " + devModel;

                int flowPort;
                if (!deviceFlowPortMap.TryGetValue(ser, out flowPort))
                {
                    flowPort = 55557;
                    while (deviceFlowPortMap.ContainsValue(flowPort))
                    {
                        flowPort++;
                    }
                    deviceFlowPortMap[ser] = flowPort;
                }

                string fwdKey = string.Format("{0}:{1}:{2}", ser, flowPort, 55557);
                if (!activeForwards.Contains(fwdKey))
                {
                    RunAdbForwardPort(adbPath, ser, flowPort, 55557);
                    activeForwards.Add(fwdKey);
                }

                ConnectedDeviceItem cItem = new ConnectedDeviceItem
                {
                    Serial = ser,
                    Model = devModel,
                    FriendlyName = friendly,
                    FlowPort = flowPort
                };
                connectedItems.Add(cItem);
            }

            OnConnectedDevicesChanged(connectedItems);

            string targetSerial = (!string.IsNullOrEmpty(activeTargetSerial) && authorizedDevices.Contains(activeTargetSerial))
                ? activeTargetSerial
                : null;

            if (targetSerial == null)
            {
                if (authorizedDevices.Count > 1)
                {
                    foreach (string dev in authorizedDevices)
                    {
                        try
                        {
                            ProcessStartInfo psiCheck = new ProcessStartInfo
                            {
                                FileName = adbPath,
                                Arguments = string.Format("-s {0} shell pm list packages com.example.screenmonitoring", dev),
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                CreateNoWindow = true
                            };
                            using (Process pCheck = Process.Start(psiCheck))
                            {
                                string outCheck = pCheck.StandardOutput.ReadToEnd();
                                pCheck.WaitForExit(1000);
                                if (outCheck.Contains("com.example.screenmonitoring"))
                                {
                                    targetSerial = dev;
                                    break;
                                }
                            }
                        }
                        catch { }
                    }
                }
                if (targetSerial == null) targetSerial = authorizedDevices[0];
            }

            activeTargetSerial = targetSerial;
            string fwdMainKey = string.Format("{0}:{1}:{2}", targetSerial, 55555, 55555);
            if (!activeForwards.Contains(fwdMainKey))
            {
                RunAdbForwardPort(adbPath, targetSerial, 55555, 55555);
                activeForwards.Add(fwdMainKey);
            }

            // Özellikleri sorgula
            string cmd = "getprop ro.product.manufacturer; echo ---; getprop ro.product.model; echo ---; getprop ro.build.version.release; echo ---; wm size; echo ---; wm density; echo ---; pm list packages com.example.screenmonitoring; echo ---; getprop ro.build.characteristics";
            ProcessStartInfo psiProps = new ProcessStartInfo
            {
                FileName = adbPath,
                Arguments = string.Format("-s {0} shell \"{1}\"", targetSerial, cmd),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            string propsOut = "";
            try
            {
                using (Process pProps = Process.Start(psiProps))
                {
                    propsOut = pProps.StandardOutput.ReadToEnd();
                    pProps.WaitForExit(2500);
                }
            }
            catch { }

            string manufacturer = "";
            string model = "";
            string release = "";
            int width = 1280;
            int height = 800;
            int density = 160;
            bool hasApp = false;

            if (!string.IsNullOrEmpty(propsOut))
            {
                string[] sections = propsOut.Split(new string[] { "---" }, StringSplitOptions.None);
                if (sections.Length >= 1) manufacturer = sections[0].Trim();
                if (sections.Length >= 2) model = sections[1].Trim().Replace('_', ' ');
                if (sections.Length >= 3) release = sections[2].Trim();
                if (sections.Length >= 4)
                {
                    string sizeStr = sections[3];
                    int idxX = sizeStr.IndexOf('x');
                    if (idxX > 0)
                    {
                        int lastDelim = sizeStr.LastIndexOfAny(new char[] { ':', ' ' });
                        string dims = (lastDelim >= 0) ? sizeStr.Substring(lastDelim + 1) : sizeStr;
                        string[] parts = dims.Split('x');
                        if (parts.Length == 2)
                        {
                            int rawW = 1280, rawH = 800;
                            int.TryParse(parts[0].Trim(), out rawW);
                            int.TryParse(parts[1].Trim(), out rawH);
                            width = Math.Max(rawW, rawH);
                            height = Math.Min(rawW, rawH);
                        }
                    }
                }
                if (sections.Length >= 5)
                {
                    string densStr = sections[4];
                    int idxD = densStr.LastIndexOfAny(new char[] { ':', ' ' });
                    if (idxD >= 0)
                    {
                        int.TryParse(densStr.Substring(idxD + 1).Trim(), out density);
                    }
                }
                if (sections.Length >= 6)
                {
                    hasApp = sections[5].Contains("com.example.screenmonitoring");
                }
                if (sections.Length >= 7)
                {
                    string chars = sections[6].Trim().ToLower();
                    clientIsPhone = chars.Contains("phone") || (!chars.Contains("tablet") && density >= 300);
                }
                else
                {
                    clientIsPhone = (density >= 300);
                }
            }

            string cleanMan = "";
            if (!string.IsNullOrEmpty(manufacturer))
            {
                cleanMan = char.ToUpper(manufacturer[0]) + (manufacturer.Length > 1 ? manufacturer.Substring(1).ToLower() : "");
            }

            string displayTitle = "";
            if (!string.IsNullOrEmpty(model))
            {
                if (!string.IsNullOrEmpty(cleanMan) && model.IndexOf(cleanMan, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    displayTitle = cleanMan + " " + model;
                }
                else
                {
                    displayTitle = model;
                }
            }
            else
            {
                displayTitle = string.IsNullOrEmpty(cleanMan) ? "Android Cihaz" : cleanMan;
            }

            if (!string.IsNullOrEmpty(release))
            {
                displayTitle += " • Android " + release;
            }

            string specs = string.Format("{0}x{1} • {2} DPI • USB ADB Köprüsü (Hazır)", width, height, density);
            if (!hasApp)
            {
                specs = string.Format("{0}x{1} • USB Bağlandı (Uygulama Açılmalı)", width, height);
            }

            ApplyDeviceState(targetSerial, "device", displayTitle, specs, width, height, density);
        }

        private void GetStreamTargetResolution(int nativeW, int nativeH, out int streamW, out int streamH)
        {
            streamW = nativeW > 0 ? nativeW : 1280;
            streamH = nativeH > 0 ? nativeH : 800;

            if (!enableAdaptiveFps)
            {
                return; // 1:1 Tam Yerel Çözünürlük (Ölçekleme Kapalı)
            }

            // Evrensel Dinamik Piksel Bütçeleme (60 FPS Sweet Spot ~1.15 Megapiksel)
            long totalPixels = (long)streamW * streamH;
            const long MAX_60FPS_PIXELS = 1150000;

            if (totalPixels > MAX_60FPS_PIXELS)
            {
                double scale = Math.Sqrt((double)MAX_60FPS_PIXELS / totalPixels);
                int scaledW = (int)Math.Round(streamW * scale);
                int scaledH = (int)Math.Round(streamH * scale);

                // GDI / JPEG DCT 8x8 blok uyumu için çift sayıya hizala
                scaledW = (scaledW / 2) * 2;
                scaledH = (scaledH / 2) * 2;

                if (scaledW > 320 && scaledH > 240)
                {
                    streamW = scaledW;
                    streamH = scaledH;
                }
            }
        }

        private void UpdateDeviceSpecsDisplay()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(UpdateDeviceSpecsDisplay));
                return;
            }

            if (clientTargetWidth <= 0 || clientTargetHeight <= 0) return;

            int streamW, streamH;
            GetStreamTargetResolution(clientTargetWidth, clientTargetHeight, out streamW, out streamH);

            string modeTag = (enableAdaptiveFps && (streamW != clientTargetWidth || streamH != clientTargetHeight))
                ? string.Format(L10n.T("{0}x{1} ⚡ 60 FPS (Yerel {2}x{3})", "{0}x{1} ⚡ 60 FPS (Native {2}x{3})"), streamW, streamH, clientTargetWidth, clientTargetHeight)
                : string.Format(L10n.T("{0}x{1} [1:1 Yerel]", "{0}x{1} [1:1 Native]"), clientTargetWidth, clientTargetHeight);

            string specs = string.Format(L10n.T("{0} • {1} DPI • USB ADB Köprüsü (Hazır)", "{0} • {1} DPI • USB ADB Bridge (Ready)"), modeTag, clientTargetDpi);
            heroBanner.SetDevice(clientDeviceModel, specs, clientIsPhone);

            if (isStreaming)
            {
                heroBanner.SetStatus(L10n.T("CANLI YAYINDA", "STREAMING LIVE"), ColorSuccess);
            }
            else if (connectedSerials != null && connectedSerials.Count > 1)
            {
                int devIdx = Math.Max(0, connectedSerials.IndexOf(activeTargetSerial));
                heroBanner.SetStatus(string.Format(L10n.T("CİHAZ {0}/{1} (DEĞİŞTİR)", "DEVICE {0}/{1} (SWITCH)"), devIdx + 1, connectedSerials.Count), ColorAccent);
            }
            else
            {
                heroBanner.SetStatus(L10n.T("USB BAĞLANDI", "USB CONNECTED"), ColorAccent);
            }

            if (lblVddHint != null)
            {
                lblVddHint.Text = L10n.T("Bağlı cihaza özel 1:1 piksel netliğinde sanal ekran sağlar.", "Provides a 1:1 pixel-perfect virtual external display.");
            }
        }

        private void ApplyDeviceState(string serial, string state, string title, string specs, int width, int height, int dpi)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(delegate()
                {
                    ApplyDeviceState(serial, state, title, specs, width, height, dpi);
                }));
                return;
            }

            if (width > 0 && height > 0)
            {
                clientTargetWidth = width;
                clientTargetHeight = height;
                if (dpi > 0) clientTargetDpi = dpi;
                clientDeviceModel = title;
                UpdateDeviceSpecsDisplay();
            }
            else
            {
                heroBanner.SetDevice(title, specs, clientIsPhone);
            }

            if (state == "none")
            {
                heroBanner.SetStatus(L10n.T("CİHAZ YOK", "NO DEVICE"), ColorTextSubtle);
                if (!isStreaming)
                {
                    UpdateStatus(L10n.T("CİHAZ BEKLENİYOR", "WAITING FOR DEVICE"), L10n.T("USB kablosunu bağlayın veya USB Hata Ayıklama modunu açın.", "Connect USB cable or enable USB Debugging."), ColorTextSubtle);
                }
            }
            else if (state == "unauthorized")
            {
                heroBanner.SetStatus(L10n.T("YETKİ GEREKLİ", "AUTH REQUIRED"), ColorWarning);
                if (!isStreaming)
                {
                    UpdateStatus(L10n.T("YETKİ BEKLENİYOR", "AWAITING AUTH"), L10n.T("Cihaz ekranındaki onay uyarısını kabul edin.", "Accept authorization prompt on device screen."), ColorWarning);
                }
            }
            else if (state == "device")
            {
                if (isStreaming)
                {
                    heroBanner.SetStatus(L10n.T("CANLI YAYINDA", "STREAMING LIVE"), ColorSuccess);
                }
                else
                {
                    if (connectedSerials != null && connectedSerials.Count > 1)
                    {
                        int devIdx = Math.Max(0, connectedSerials.IndexOf(activeTargetSerial));
                        heroBanner.SetStatus(string.Format(L10n.T("CİHAZ {0}/{1} (DEĞİŞTİR)", "DEVICE {0}/{1} (SWITCH)"), devIdx + 1, connectedSerials.Count), ColorAccent);
                    }
                    else
                    {
                        heroBanner.SetStatus(L10n.T("USB BAĞLANDI", "USB CONNECTED"), ColorAccent);
                    }
                    UpdateStatus(L10n.T("HAZIR", "READY"), L10n.T("Cihaz algılandı. Yayını başlatmak için aşağıdaki butona tıklayın.", "Device detected. Click the button below to start streaming."), ColorSuccess);
                }
            }

            if (lblVddHint != null)
            {
                lblVddHint.Text = L10n.T("Bağlı cihaza özel 1:1 piksel netliğinde sanal ekran sağlar.", "Provides a 1:1 pixel-perfect virtual external display.");
            }
        }
    }
}