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
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ScreenMonitoringApp
{
    static class Program
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

        public static void Log(string msg)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + " - " + msg + Environment.NewLine
                );
            }
            catch { }
        }

        [STAThread]
        static void Main(string[] args)
        {
            Log("App starting (Redesigned Studio UI v2)");
            try
            {
                if (!SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
                {
                    SetProcessDPIAware();
                }
            }
            catch (Exception ex)
            {
                Log("DPI exception: " + ex.Message);
                try { SetProcessDPIAware(); } catch { }
            }

            try
            {
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    Log("UnhandledException: " + e.ExceptionObject);
                    try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), e.ExceptionObject.ToString()); } catch { }
                };
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
                {
                    Log("ThreadException: " + e.Exception);
                    try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), e.Exception.ToString()); } catch { }
                };
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // Tekil kopya güvencesi: Eski MobiDesk veya TabletMonitor süreçlerini temizle
                try
                {
                    Process curProc = Process.GetCurrentProcess();
                    foreach (Process p in Process.GetProcessesByName("MobiDesk"))
                    {
                        if (p.Id != curProc.Id)
                        {
                            try { p.Kill(); } catch { }
                        }
                    }
                    foreach (Process p in Process.GetProcessesByName("TabletMonitor"))
                    {
                        if (p.Id != curProc.Id)
                        {
                            try { p.Kill(); } catch { }
                        }
                    }
                }
                catch { }

                bool autoStart = false;
                string snapshotPath = null;
                bool closeAfterSnapshot = false;
                if (args != null)
                {
                    for (int i = 0; i < args.Length; i++)
                    {
                        if (string.Equals(args[i], "--autostart", StringComparison.OrdinalIgnoreCase)) autoStart = true;
                        if (string.Equals(args[i], "--snapshot", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) snapshotPath = args[i + 1];
                        if (string.Equals(args[i], "--closeAfterSnapshot", StringComparison.OrdinalIgnoreCase)) closeAfterSnapshot = true;
                        if (string.Equals(args[i], "--device", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) MainForm.activeTargetSerial = args[i + 1];
                    }
                }

                Log("Starting MainForm autoStart=" + autoStart + ", snapshot=" + (snapshotPath ?? "none"));
                Application.Run(new MainForm(autoStart, snapshotPath, closeAfterSnapshot));
                Log("Application.Run exited normally");
            }
            catch (Exception ex)
            {
                Log("Main catch exception: " + ex);
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), ex.ToString()); } catch { }
            }
        }
    }

    public class MainForm : Form
    {
        // ═══════════════════════════════════════════════
        // DWM VE WINDOWS NATIVE API'LERİ
        // ═══════════════════════════════════════════════
        [DllImport("dwmapi.dll", PreserveSig = true)]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWA_BORDER_COLOR = 34;
        const int DWMWCP_ROUND = 2;

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_CAPTION = 0x2;

        public static void DragWindow(IntPtr handle)
        {
            ReleaseCapture();
            SendMessage(handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
        }

        [DllImport("user32.dll")]
        static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetThreadDesktop(IntPtr hDesktop);

        [DllImport("user32.dll")]
        static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        static extern bool StretchBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int nWidthSrc, int nHeightSrc, int dwRop);

        [DllImport("gdi32.dll")]
        static extern int SetStretchBltMode(IntPtr hdc, int nStretchMode);

        [DllImport("gdi32.dll")]
        static extern bool SetBrushOrgEx(IntPtr hdc, int nXOrg, int nYOrg, IntPtr lppt);

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        struct CURSORINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hCursor;
            public POINT ptScreenPos;
        }

        const int CURSOR_SHOWING = 0x00000001;

        [DllImport("user32.dll")]
        static extern bool GetCursorInfo(out CURSORINFO pci);

        [StructLayout(LayoutKind.Sequential)]
        struct ICONINFO
        {
            public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [DllImport("user32.dll")]
        static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon, int cxWidth, int cyWidth, uint istepIfAniCur, IntPtr hbrFlickerFreeDraw, uint diFlags);

        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint uMilliseconds);

        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint uMilliseconds);

        const uint DI_NORMAL = 0x0003;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion; public short dmDriverVersion; public short dmSize; public short dmDriverExtra;
            public int dmFields; public int dmPositionX; public int dmPositionY; public int dmDisplayOrientation;
            public int dmDisplayFixedOutput; public short dmColor; public short dmDuplex; public short dmYResolution;
            public short dmTTOption; public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels; public short dmBitsPerPel; public int dmPelsWidth; public int dmPelsHeight;
            public int dmDisplayFlags; public int dmDisplayFrequency; public int dmICMMethod; public int dmICMIntent;
            public int dmMediaType; public int dmDitherType; public int dmReserved1; public int dmReserved2;
            public int dmPanningWidth; public int dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        public static extern int ChangeDisplaySettingsEx(string lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

        [DllImport("gdi32.dll", CharSet = CharSet.Ansi)]
        public static extern IntPtr CreateDCA(string lpszDriver, string lpszDevice, string lpszOutput, IntPtr lpInitData);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        public static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        public static bool IsVirtualDisplay(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) return false;
            try
            {
                DISPLAY_DEVICE d = new DISPLAY_DEVICE();
                d.cb = Marshal.SizeOf(d);
                for (uint id = 0; EnumDisplayDevices(null, id, ref d, 0); id++)
                {
                    if (string.Equals(d.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(d.DeviceString) &&
                            (d.DeviceString.IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             d.DeviceString.IndexOf("VDD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             d.DeviceString.IndexOf("MTT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             d.DeviceString.IndexOf("USBMMIDD", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            return true;
                        }
                    }
                }

                DISPLAY_DEVICE m = new DISPLAY_DEVICE();
                m.cb = Marshal.SizeOf(m);
                for (uint mid = 0; EnumDisplayDevices(deviceName, mid, ref m, 0); mid++)
                {
                    if (!string.IsNullOrEmpty(m.DeviceID) &&
                        (m.DeviceID.IndexOf("MTT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         m.DeviceID.IndexOf("VDD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         m.DeviceID.IndexOf("USBMMIDD", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int left; public int top; public int right; public int bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        public static Rectangle GetPhysicalMonitorBounds(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) return Rectangle.Empty;
            Rectangle result = Rectangle.Empty;
            try
            {
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data)
                {
                    MONITORINFOEX mi = new MONITORINFOEX();
                    mi.cbSize = Marshal.SizeOf(mi);
                    if (GetMonitorInfo(hMon, ref mi))
                    {
                        if (string.Equals(mi.szDevice, deviceName, StringComparison.OrdinalIgnoreCase))
                        {
                            result = new Rectangle(mi.rcMonitor.left, mi.rcMonitor.top,
                                                   mi.rcMonitor.right - mi.rcMonitor.left,
                                                   mi.rcMonitor.bottom - mi.rcMonitor.top);
                            return false;
                        }
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
            return result;
        }

        // ═══════════════════════════════════════════════
        // ULTRA PREMIUM RENK PALETİ (Obsidian & Cyber Studio)
        // ═══════════════════════════════════════════════
        public static readonly Color ColorBg = Color.FromArgb(11, 14, 22);             // #0B0E16 - Derin obsidian
        public static readonly Color ColorCard = Color.FromArgb(17, 22, 34);           // #111622 - Kart gövdesi
        public static readonly Color ColorCardBorder = Color.FromArgb(30, 39, 60);     // #1E273C - Zarif kenarlık
        public static readonly Color ColorCardHover = Color.FromArgb(23, 30, 46);      // #171E2E
        public static readonly Color ColorPrimary = Color.FromArgb(99, 102, 241);      // #6366F1 - Parlak Indigo
        public static readonly Color ColorPrimaryDark = Color.FromArgb(79, 70, 229);   // #4F46E5
        public static readonly Color ColorAccent = Color.FromArgb(6, 182, 212);        // #06B6D4 - Elektrik Camgöbeği
        public static readonly Color ColorSuccess = Color.FromArgb(16, 185, 129);      // #10B981 - Canlı Zümrüt
        public static readonly Color ColorSuccessDark = Color.FromArgb(5, 150, 105);   // #059669
        public static readonly Color ColorDanger = Color.FromArgb(239, 68, 68);        // #EF4444 - Mercan Kırmızı
        public static readonly Color ColorDangerDark = Color.FromArgb(190, 18, 60);    // #BE123C
        public static readonly Color ColorWarning = Color.FromArgb(245, 158, 11);      // #F59E0B - Amber
        public static readonly Color ColorText = Color.FromArgb(248, 250, 252);        // #F8FAFC - Net beyaz
        public static readonly Color ColorTextMuted = Color.FromArgb(148, 163, 184);   // #94A3B8 - Açık arduvaz
        public static readonly Color ColorTextSubtle = Color.FromArgb(100, 116, 139);  // #64748B - Koyu arduvaz

        // ═══════════════════════════════════════════════
        // ÇEKİRDEK AKIŞ VE CİHAZ DURUMU
        // ═══════════════════════════════════════════════
        private volatile bool isStreaming = false;
        private Thread streamThread;
        private TcpClient activeClient;
        private Rectangle currentScreenBounds;
        private string selectedDeviceName = "";
        private int selectedScreenIndex = 0;
        private volatile int targetFps = 60;
        private volatile int targetQuality = 65;
        private volatile bool enableBatterySaver = false;
        private volatile bool showFpsOnTablet = true;
        private volatile bool pendingFpsConfig = true;
        private volatile int clientTargetWidth = 1280;
        private volatile int clientTargetHeight = 800;
        private volatile int clientTargetDpi = 160;
        private volatile string clientDeviceModel = "Android Cihazı";
        private volatile bool clientIsPhone = false;

        // Telemetri
        private System.Windows.Forms.Timer statsTimer;
        private volatile int totalFramesSent = 0;
        private long totalBytesSent = 0;
        private int lastFramesSent = 0;
        private long lastBytesSent = 0;
        private int currentSentFps = 0;

        // UI Kontrolleri
        private ModernTitleBar titleBar;
        private TabletHeroBanner heroBanner;
        private ModernSegmentedMode modeControl;
        private ModernMonitorSelector screenSelector;
        private ModernButton btnAddVirtual;
        private ModernButton btnRemoveVirtual;
        private Label lblVddHint;
        private ModernSlider tbFps;
        private Label lblFpsVal;
        private ModernSlider tbQuality;
        private Label lblQualityVal;
        private ModernSwitch swAdaptiveFps;
        private ModernSwitch swShowFps;
        private ModernSwitch swBatterySaver;
        private volatile bool enableAdaptiveFps = true;
        private ModernButton btnStartStop;

        // Dinamik USB Cihaz İzleme
        private System.Windows.Forms.Timer deviceMonitorTimer;
        private volatile bool isCheckingDevice = false;
        public static string activeTargetSerial = "";
        public static List<string> connectedSerials = new List<string>();

        // Alt Çubuk Elemanları
        private Label lblStatusDot;
        private Label lblStatusTitle;
        private Label lblStatusSub;
        private TelemetryBadge badgeFps;
        private TelemetryBadge badgeBandwidth;
        private TelemetryBadge badgeSize;
        private TelemetryBadge badgeLatency;

        protected override CreateParams CreateParams
        {
            get
            {
                const int CS_DROPSHADOW = 0x00020000;
                const int WS_MINIMIZEBOX = 0x00020000;
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                cp.Style |= WS_MINIMIZEBOX;
                return cp;
            }
        }

        private static Image cachedAppLogo = null;
        public static Image GetAppLogoImage()
        {
            if (cachedAppLogo != null) return cachedAppLogo;
            try
            {
                string pngPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_icon.png");
                if (File.Exists(pngPath))
                {
                    using (Image img = Image.FromFile(pngPath))
                    {
                        cachedAppLogo = new Bitmap(img);
                    }
                    return cachedAppLogo;
                }
                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_icon.ico");
                if (File.Exists(icoPath))
                {
                    using (Icon ico = new Icon(icoPath, 64, 64))
                    {
                        cachedAppLogo = ico.ToBitmap();
                    }
                    return cachedAppLogo;
                }
                Icon appIco = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (appIco != null)
                {
                    cachedAppLogo = appIco.ToBitmap();
                    return cachedAppLogo;
                }
            }
            catch { }
            return null;
        }

        public MainForm(bool autoStart = false, string snapshotPath = null, bool closeAfterSnapshot = false)
        {
            try { timeBeginPeriod(1); } catch { }

            try
            {
                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_icon.ico");
                if (File.Exists(icoPath))
                {
                    this.Icon = new Icon(icoPath);
                }
                else
                {
                    this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                }
            }
            catch { }

            try
            {
                foreach (Process p in Process.GetProcessesByName("ScreenServer"))
                {
                    try { p.Kill(); } catch { }
                }
            }
            catch { }

            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            InitializeStudioUI();
            ApplyModernWindowStyle();
            RefreshScreenList();
            UpdateStatus("HAZIR", "Yansıtma için aşağıdaki 'Yayını Başlat' butonuna tıklayın.", ColorSuccess);

            statsTimer = new System.Windows.Forms.Timer();
            statsTimer.Interval = 1000;
            statsTimer.Tick += StatsTimer_Tick;
            statsTimer.Start();

            this.Shown += delegate(object s, EventArgs e)
            {
                if (!string.IsNullOrEmpty(snapshotPath))
                {
                    ThreadPool.QueueUserWorkItem(delegate(object state)
                    {
                        try
                        {
                            Thread.Sleep(2200);
                            this.Invoke(new Action(delegate()
                            {
                                try
                                {
                                    using (Bitmap bmp = new Bitmap(this.ClientSize.Width, this.ClientSize.Height))
                                    {
                                        this.DrawToBitmap(bmp, new Rectangle(0, 0, this.ClientSize.Width, this.ClientSize.Height));
                                        bmp.Save(snapshotPath, ImageFormat.Png);
                                    }
                                    Program.Log("Snapshot successfully saved to: " + snapshotPath);
                                }
                                catch (Exception ex)
                                {
                                    Program.Log("Snapshot error: " + ex);
                                }
                                if (closeAfterSnapshot)
                                {
                                    this.Close();
                                }
                            }));
                        }
                        catch { }
                    });
                }
            };

            if (autoStart)
            {
                this.Load += delegate(object s, EventArgs e) { StartStreaming(); };
            }
        }

        private void ApplyModernWindowStyle()
        {
            try
            {
                IntPtr hwnd = this.Handle;
                int darkMode = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
                int cornerPref = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));
                int borderColor = (ColorCardBorder.B << 16) | (ColorCardBorder.G << 8) | ColorCardBorder.R;
                DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
            }
            catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen borderPen = new Pen(ColorCardBorder, 1.0f))
            {
                e.Graphics.DrawRectangle(borderPen, 0, 0, this.Width - 1, this.Height - 1);
            }
        }

        private void InitializeStudioUI()
        {
            this.Text = "MobiDesk Studio";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = true;
            this.BackColor = ColorBg;
            this.ForeColor = ColorText;
            this.Font = new Font("Segoe UI", 9.0f, FontStyle.Regular);

            int windowW = 780;
            int windowH = 672;
            this.ClientSize = new Size(windowW, windowH);

            // 1. MODERN ENTEGRE BAŞLIK ÇUBUĞU
            titleBar = new ModernTitleBar(this, "MobiDesk Studio", "BETA");
            titleBar.Location = new Point(0, 0);
            titleBar.Size = new Size(windowW, 42);
            this.Controls.Add(titleBar);

            // 2. HERO CİHAZ BİLGİ KARTI (DİNAMİK USB ALGILAMA)
            heroBanner = new TabletHeroBanner(
                "USB Cihazı Aranıyor...",
                "USB kablosunu bağlayın • USB Hata Ayıklama açık olmalıdır",
                "ARANIYOR",
                MainForm.ColorWarning
            );
            heroBanner.Location = new Point(18, 52);
            heroBanner.Size = new Size(windowW - 36, 58);
            heroBanner.Cursor = Cursors.Hand;
            heroBanner.Click += delegate(object s, EventArgs e)
            {
                if (connectedSerials != null && connectedSerials.Count > 1)
                {
                    int idx = connectedSerials.IndexOf(activeTargetSerial);
                    idx = (idx + 1) % connectedSerials.Count;
                    activeTargetSerial = connectedSerials[idx];
                    ThreadPool.QueueUserWorkItem(delegate { CheckConnectedUsbDevicesAsync(); });
                }
            };
            this.Controls.Add(heroBanner);

            int colY = 122;
            int colH = 404;
            int colW = (windowW - 36 - 16) / 2; // 364 px

            // ═══════════════════════════════════════════════
            // SOL PANEL: EKRAN VE YAYIN MODU
            // ═══════════════════════════════════════════════
            Panel pnlLeftCol = new Panel
            {
                Location = new Point(18, colY),
                Size = new Size(colW, colH),
                BackColor = ColorBg
            };

            // Sol Kart 1: Çalışma Modu (H=86)
            ModernCard cardMode = new ModernCard("ÇALIŞMA MODU (TOPOLOJİ)", 0, 0, colW, 86);
            modeControl = new ModernSegmentedMode(
                "🖥️ Genişlet (2. Ekran)",
                "🔄 Yinele (Ayna)",
                12, 30, colW - 24, 42
            );
            modeControl.ModeChanged += delegate(bool isExtend)
            {
                AutoSelectScreen(isExtend);
            };
            cardMode.Controls.Add(modeControl);
            pnlLeftCol.Controls.Add(cardMode);

            // Sol Kart 2: Yansıtılacak Monitör Seçimi (H=186, Y=100)
            ModernCard cardScreens = new ModernCard("YANSITILACAK MONİTÖR", 0, 100, colW, 186);
            
            ModernButton btnRefresh = new ModernButton("🔄 Yenile", colW - 86, 7, 74, 22, Color.FromArgb(28, 36, 54), ColorTextMuted, 5);
            btnRefresh.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            btnRefresh.Click += delegate(object s, EventArgs e) { RefreshScreenList(); };
            cardScreens.Controls.Add(btnRefresh);

            screenSelector = new ModernMonitorSelector(12, 34, colW - 24, 140);
            screenSelector.SelectionChanged += delegate(int index, Screen scr, bool isVirt)
            {
                selectedScreenIndex = index;
                if (scr != null)
                {
                    selectedDeviceName = scr.DeviceName;
                    Rectangle phys = GetPhysicalMonitorBounds(selectedDeviceName);
                    currentScreenBounds = (phys.Width > 0 && phys.Height > 0) ? phys : scr.Bounds;
                }
            };
            cardScreens.Controls.Add(screenSelector);
            pnlLeftCol.Controls.Add(cardScreens);

            // Sol Kart 3: Sanal Ekran Sürücüsü (VDD) (H=104, Y=300)
            ModernCard cardVirtual = new ModernCard("SANAL 3. EKRAN YÖNETİMİ (VDD)", 0, 300, colW, 104);

            lblVddHint = new Label
            {
                Text = "Bağlı cihaza özel 1:1 piksel netliğinde sanal ekran sağlar.",
                Font = new Font("Segoe UI", 7.5f),
                ForeColor = ColorTextSubtle,
                BackColor = ColorCard,
                Location = new Point(14, 28),
                Size = new Size(colW - 28, 18)
            };
            cardVirtual.Controls.Add(lblVddHint);

            int btnHalfW = (colW - 24 - 8) / 2;
            btnAddVirtual = new ModernButton("➕ Sanal Ekran Ayarla", 12, 50, btnHalfW, 40, ColorPrimary, Color.White, 8);
            btnAddVirtual.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            btnAddVirtual.Click += BtnAddVirtual_Click;

            btnRemoveVirtual = new ModernButton("🗑️ Sürücüyü Kaldır", 12 + btnHalfW + 8, 50, btnHalfW, 40, Color.FromArgb(32, 24, 32), Color.FromArgb(244, 114, 182), 8, Color.FromArgb(70, 30, 50));
            btnRemoveVirtual.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            btnRemoveVirtual.Click += BtnRemoveVirtual_Click;

            cardVirtual.Controls.Add(btnAddVirtual);
            cardVirtual.Controls.Add(btnRemoveVirtual);
            pnlLeftCol.Controls.Add(cardVirtual);

            this.Controls.Add(pnlLeftCol);

            // ═══════════════════════════════════════════════
            // SAĞ PANEL: PERFORMANS VE MOTOR AYARLARI
            // ═══════════════════════════════════════════════
            Panel pnlRightCol = new Panel
            {
                Location = new Point(18 + colW + 16, colY),
                Size = new Size(colW, colH),
                BackColor = ColorBg
            };

            // Sağ Kart 1: Performans & Hız (H=192, Y=0)
            ModernCard cardPerf = new ModernCard("AKIŞ VE HIZ LİMİTİ", 0, 0, colW, 192);

            // FPS Satırı
            Label lblFpsTitle = new Label
            {
                Text = "Hedef Kare Hızı",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = ColorTextMuted,
                BackColor = ColorCard,
                Location = new Point(12, 26),
                AutoSize = true
            };
            lblFpsVal = new Label
            {
                Text = "60 FPS",
                Font = new Font("Segoe UI", 9.0f, FontStyle.Bold),
                ForeColor = ColorSuccess,
                BackColor = ColorCard,
                Location = new Point(colW - 90, 25),
                Size = new Size(78, 18),
                TextAlign = ContentAlignment.MiddleRight
            };
            cardPerf.Controls.Add(lblFpsTitle);
            cardPerf.Controls.Add(lblFpsVal);

            tbFps = new ModernSlider(15, 75, 60, ColorSuccess, Color.FromArgb(6, 182, 212))
            {
                Location = new Point(12, 48),
                Size = new Size(colW - 24, 20)
            };
            tbFps.ValueChanged += delegate(object s, EventArgs e) { UpdateFps(tbFps.Value); };
            cardPerf.Controls.Add(tbFps);

            // FPS Hızlı Butonlar
            int[] fpsPresets = new int[] { 30, 45, 60, 75 };
            int chipW = (colW - 24 - 9) / 4;
            for (int i = 0; i < fpsPresets.Length; i++)
            {
                int val = fpsPresets[i];
                QuickChip chip = new QuickChip(val.ToString() + " FPS", 12 + i * (chipW + 3), 74, chipW, 22);
                chip.Click += delegate(object s, EventArgs e) { tbFps.Value = val; };
                cardPerf.Controls.Add(chip);
            }

            // Kalite Satırı
            Label lblQualityTitle = new Label
            {
                Text = "Görüntü Netliği Kalite",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = ColorTextMuted,
                BackColor = ColorCard,
                Location = new Point(12, 108),
                AutoSize = true
            };
            lblQualityVal = new Label
            {
                Text = "%65 (Dengeli)",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = ColorAccent,
                BackColor = ColorCard,
                Location = new Point(colW - 120, 107),
                Size = new Size(108, 18),
                TextAlign = ContentAlignment.MiddleRight
            };
            cardPerf.Controls.Add(lblQualityTitle);
            cardPerf.Controls.Add(lblQualityVal);

            tbQuality = new ModernSlider(15, 100, 65, ColorPrimary, Color.FromArgb(168, 85, 247))
            {
                Location = new Point(12, 130),
                Size = new Size(colW - 24, 20)
            };
            tbQuality.ValueChanged += delegate(object s, EventArgs e) { UpdateQuality(tbQuality.Value); };
            cardPerf.Controls.Add(tbQuality);

            // Kalite Hızlı Butonlar
            string[] qLabels = new string[] { "%40", "%65", "%85", "%100" };
            int[] qPresets = new int[] { 40, 65, 85, 100 };
            for (int i = 0; i < qPresets.Length; i++)
            {
                int val = qPresets[i];
                QuickChip chip = new QuickChip(qLabels[i], 12 + i * (chipW + 3), 156, chipW, 22);
                chip.Click += delegate(object s, EventArgs e) { tbQuality.Value = val; };
                cardPerf.Controls.Add(chip);
            }

            pnlRightCol.Controls.Add(cardPerf);

            // Sağ Kart 2: Sistem ve Motor Seçenekleri (H=198, Y=206)
            ModernCard cardOpts = new ModernCard("SİSTEM VE OPTİMİZASYONLAR", 0, 206, colW, 198);

            swAdaptiveFps = new ModernSwitch(
                "⚡ Akıllı 60 FPS Performans Modu",
                "Yüksek pikselli cihazları 60 FPS'e dengeler (Kapalıyken 1:1)",
                12, 28, colW - 24, 48
            );
            swAdaptiveFps.Checked = true;
            swAdaptiveFps.CheckedChanged += delegate(object s, EventArgs e)
            {
                enableAdaptiveFps = swAdaptiveFps.Checked;
                UpdateDeviceSpecsDisplay();
            };
            cardOpts.Controls.Add(swAdaptiveFps);

            swShowFps = new ModernSwitch(
                "Cihazda Canlı FPS Sayacı Göster",
                "Sol üst köşede yeşil donanım OSD sayacı gösterir",
                12, 82, colW - 24, 48
            );
            swShowFps.Checked = true;
            swShowFps.CheckedChanged += delegate(object s, EventArgs e)
            {
                showFpsOnTablet = swShowFps.Checked;
                pendingFpsConfig = true;
            };
            cardOpts.Controls.Add(swShowFps);

            swBatterySaver = new ModernSwitch(
                "Akıllı Güç ve Veri Tasarrufu",
                "Statik ekranda gereksiz veri aktarımını durdurur",
                12, 136, colW - 24, 48
            );
            swBatterySaver.Checked = false;
            swBatterySaver.CheckedChanged += delegate(object s, EventArgs e)
            {
                enableBatterySaver = swBatterySaver.Checked;
            };
            cardOpts.Controls.Add(swBatterySaver);

            pnlRightCol.Controls.Add(cardOpts);

            this.Controls.Add(pnlRightCol);

            // ═══════════════════════════════════════════════
            // ALT KOMUTA VE CANLI TELEMETRİ GÜVERTESİ
            // ═══════════════════════════════════════════════
            int bottomY = colY + colH + 14; // 540

            // Büyük Aksiyon Butonu
            btnStartStop = new ModernButton(
                "▶    EKRAN PAYLAŞIMINI BAŞLAT",
                18, bottomY, windowW - 36, 50,
                ColorSuccess, Color.FromArgb(11, 15, 25), 10
            );
            btnStartStop.Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
            btnStartStop.Click += BtnStartStop_Click;
            this.Controls.Add(btnStartStop);

            // Canlı Telemetri Çubuğu
            RoundedPanel pnlBottomBar = new RoundedPanel(windowW - 36, 50, 10, ColorCard, ColorCardBorder);
            pnlBottomBar.Location = new Point(18, bottomY + 56);

            lblStatusDot = new Label
            {
                Text = "●",
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = ColorSuccess,
                BackColor = ColorCard,
                Location = new Point(12, 7),
                AutoSize = true
            };
            pnlBottomBar.Controls.Add(lblStatusDot);

            lblStatusTitle = new Label
            {
                Text = "HAZIR",
                Font = new Font("Segoe UI", 9.0f, FontStyle.Bold),
                ForeColor = ColorText,
                BackColor = ColorCard,
                Location = new Point(32, 8),
                AutoSize = true
            };
            pnlBottomBar.Controls.Add(lblStatusTitle);

            lblStatusSub = new Label
            {
                Text = "Tablete aktarımı başlatmak için yukarıdaki butona tıklayın.",
                Font = new Font("Segoe UI", 7.5f),
                ForeColor = ColorTextMuted,
                BackColor = ColorCard,
                Location = new Point(33, 28),
                Size = new Size(330, 16)
            };
            pnlBottomBar.Controls.Add(lblStatusSub);

            // Sağ Telemetri Rozetleri
            badgeLatency = new TelemetryBadge("⏱ Gecikme", "<1 ms", (windowW - 36) - 86, 11, 74, 28);
            badgeSize = new TelemetryBadge("📦 Boyut", "0 KB", (windowW - 36) - 176, 11, 82, 28);
            badgeBandwidth = new TelemetryBadge("📶 Veri", "0.0 MB/s", (windowW - 36) - 276, 11, 92, 28);
            badgeFps = new TelemetryBadge("⚡ FPS", "0", (windowW - 36) - 356, 11, 72, 28);

            pnlBottomBar.Controls.Add(badgeLatency);
            pnlBottomBar.Controls.Add(badgeSize);
            pnlBottomBar.Controls.Add(badgeBandwidth);
            pnlBottomBar.Controls.Add(badgeFps);

            this.Controls.Add(pnlBottomBar);

            // Dinamik USB Cihaz İzleme Zamanlayıcısını Başlat
            deviceMonitorTimer = new System.Windows.Forms.Timer();
            deviceMonitorTimer.Interval = 2500;
            deviceMonitorTimer.Tick += delegate(object s, EventArgs e) { CheckConnectedUsbDevicesAsync(); };
            deviceMonitorTimer.Start();
            CheckConnectedUsbDevicesAsync();

            this.FormClosing += MainForm_FormClosing;
        }

        // ═══════════════════════════════════════════════
        // EKRAN YÖNETİMİ VE MOD SEÇİMİ
        // ═══════════════════════════════════════════════
        private void RefreshScreenList()
        {
            Screen[] screens = Screen.AllScreens;
            List<MonitorItemData> list = new List<MonitorItemData>();
            int defaultIndex = 0;
            bool foundVirtual = false;

            for (int i = 0; i < screens.Length; i++)
            {
                Screen s = screens[i];
                Rectangle phys = GetPhysicalMonitorBounds(s.DeviceName);
                int w = (phys.Width > 0) ? phys.Width : s.Bounds.Width;
                int h = (phys.Height > 0) ? phys.Height : s.Bounds.Height;

                bool isVirtual = IsVirtualDisplay(s.DeviceName);
                string tag;
                if (isVirtual)
                {
                    tag = "★ SANAL MOBIDESK";
                    if (modeControl != null && modeControl.IsExtend)
                    {
                        defaultIndex = i;
                        foundVirtual = true;
                    }
                }
                else if (s.Primary)
                {
                    tag = "ANA MONİTÖR";
                }
                else
                {
                    tag = string.Format("MONİTÖR {0}", i + 1);
                    if (modeControl != null && modeControl.IsExtend && !foundVirtual && defaultIndex == 0)
                    {
                        defaultIndex = i;
                    }
                }

                list.Add(new MonitorItemData
                {
                    Index = i,
                    Screen = s,
                    DeviceName = s.DeviceName,
                    Resolution = string.Format("{0}x{1}", w, h),
                    Tag = tag,
                    IsVirtual = isVirtual
                });
            }

            screenSelector.SetMonitors(list, defaultIndex);
        }

        private void AutoSelectScreen(bool extendMode)
        {
            Screen[] screens = Screen.AllScreens;
            if (screens.Length == 0) return;

            if (!extendMode)
            {
                for (int i = 0; i < screens.Length; i++)
                {
                    if (screens[i].Primary)
                    {
                        screenSelector.SelectedIndex = i;
                        return;
                    }
                }
            }
            else
            {
                // 1. Sanal VDD Ekranı
                for (int i = 0; i < screens.Length; i++)
                {
                    if (IsVirtualDisplay(screens[i].DeviceName))
                    {
                        screenSelector.SelectedIndex = i;
                        return;
                    }
                }
                // 2. 1280x800 Harici Ekran
                for (int i = 0; i < screens.Length; i++)
                {
                    if (!screens[i].Primary && screens[i].Bounds.Width == 1280 && screens[i].Bounds.Height == 800)
                    {
                        screenSelector.SelectedIndex = i;
                        return;
                    }
                }
                // 3. Herhangi bir harici ekran
                for (int i = 0; i < screens.Length; i++)
                {
                    if (!screens[i].Primary)
                    {
                        screenSelector.SelectedIndex = i;
                        return;
                    }
                }
            }
        }

        private void BtnAddVirtual_Click(object sender, EventArgs e)
        {
            string scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sanal_Ekran_Ekle.bat");
            if (File.Exists(scriptPath))
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = scriptPath,
                        Verb = "runas",
                        UseShellExecute = true
                    };
                    Process p = Process.Start(psi);
                    p.WaitForExit();
                    Thread.Sleep(1000);
                    RefreshScreenList();
                    MessageBox.Show(this, "Sanal ekran 1280x800 çözünürlüğünde başarıyla ayarlandı!\nArtık cihazınız 1:1 piksel netliğinde çalışacaktır.", "Sanal Ekran", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "İşlem iptal edildi veya hata: " + ex.Message, "Bilgi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void BtnRemoveVirtual_Click(object sender, EventArgs e)
        {
            string scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sanal_Ekran_Kaldir.bat");
            if (File.Exists(scriptPath))
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = scriptPath,
                        Verb = "runas",
                        UseShellExecute = true
                    };
                    Process p = Process.Start(psi);
                    p.WaitForExit();
                    Thread.Sleep(1000);
                    RefreshScreenList();
                    MessageBox.Show(this, "Sanal ekran devre dışı bırakıldı.", "Sanal Ekran", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "İşlem iptal edildi veya hata: " + ex.Message, "Bilgi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

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

            isStreaming = true;
            btnStartStop.Text = "⏹    EKRAN PAYLAŞIMINI DURDUR";
            btnStartStop.SetColors(ColorDanger, Color.White);
            heroBanner.SetStatus("BAĞLANTI ARANIYOR...", ColorWarning);
            UpdateStatus("BAĞLANTI ARANIYOR...", "Cihazda uygulama açık olmalıdır (127.0.0.1:55555)", ColorWarning);

            streamThread = new Thread(StreamingWorker);
            streamThread.IsBackground = true;
            streamThread.Start();
        }

        private void StopStreaming()
        {
            isStreaming = false;
            try
            {
                if (activeClient != null)
                {
                    activeClient.Close();
                }
            }
            catch { }
            activeClient = null;

            btnStartStop.Text = "▶    EKRAN PAYLAŞIMINI BAŞLAT";
            btnStartStop.SetColors(ColorSuccess, Color.FromArgb(11, 15, 25));
            heroBanner.SetStatus("BEKLENİYOR", ColorTextSubtle);
            UpdateStatus("DURDURULDU", "Yayını başlatmak için yukarıdaki butona tıklayın.", ColorSuccess);

            badgeFps.Value = "0";
            badgeBandwidth.Value = "0.0 MB/s";
            badgeSize.Value = "0 KB";
            badgeLatency.Value = "<1 ms";
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

        private static string FindAdbPath()
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

        private static void RunAdbForward()
        {
            string adbPath = FindAdbPath();
            try
            {
                List<string> devices = new List<string>();
                ProcessStartInfo psiList = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = "devices",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using (Process p = Process.Start(psiList))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(2000);
                    string[] lines = output.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string line in lines)
                    {
                        string trimmed = line.Trim();
                        if (!trimmed.StartsWith("List") && (trimmed.Contains("\tdevice") || trimmed.EndsWith("device")))
                        {
                            string dev = trimmed.Split(new char[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                            devices.Add(dev);
                        }
                    }
                }

                string targetDevice = null;
                if (!string.IsNullOrEmpty(activeTargetSerial))
                {
                    targetDevice = activeTargetSerial;
                }
                else if (devices.Count == 1)
                {
                    targetDevice = devices[0];
                }
                else if (devices.Count > 1)
                {
                    foreach (string dev in devices)
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
                                    targetDevice = dev;
                                    break;
                                }
                            }
                        }
                        catch { }
                    }
                    if (targetDevice == null) targetDevice = devices[0];
                }

                string forwardArgs = !string.IsNullOrEmpty(targetDevice)
                    ? string.Format("-s {0} forward tcp:55555 tcp:55555", targetDevice)
                    : "forward tcp:55555 tcp:55555";

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = forwardArgs,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit(2000);
                }

                // Uygulama telefonda açık değilse otomatik başlat
                try
                {
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
                ApplyDeviceState(null, "none", "USB Cihazı Bekleniyor...", "Cihazı USB kablosuyla bağlayın • USB Hata Ayıklama açık olmalıdır", 0, 0, 0);
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
                        "Android Cihaz (Yetki Bekleniyor)",
                        "Cihaz ekranında çıkan 'USB Hata Ayıklamaya İzin Ver' uyarısını onaylayın.",
                        0, 0, 0);
                }
                else
                {
                    ApplyDeviceState(null, "none",
                        "USB Cihazı Bekleniyor...",
                        "Cihazı USB kablosuyla bağlayın • USB Hata Ayıklama açık olmalıdır",
                        0, 0, 0);
                }
                return;
            }

            connectedSerials = authorizedDevices;

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
                ? string.Format("{0}x{1} ⚡ 60 FPS (Yerel {2}x{3})", streamW, streamH, clientTargetWidth, clientTargetHeight)
                : string.Format("{0}x{1} [1:1 Yerel]", clientTargetWidth, clientTargetHeight);

            string specs = string.Format("{0} • {1} DPI • USB ADB Köprüsü (Hazır)", modeTag, clientTargetDpi);
            heroBanner.SetDevice(clientDeviceModel, specs, clientIsPhone);

            if (lblVddHint != null)
            {
                lblVddHint.Text = string.Format("Cihaza ({0}x{1}) özel 1:1 piksel netliğinde sanal ekran sağlar.", clientTargetWidth, clientTargetHeight);
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
                heroBanner.SetStatus("CİHAZ YOK", ColorTextSubtle);
                if (!isStreaming)
                {
                    UpdateStatus("CİHAZ BEKLENİYOR", "USB kablosunu bağlayın veya USB Hata Ayıklama modunu açın.", ColorTextSubtle);
                }
            }
            else if (state == "unauthorized")
            {
                heroBanner.SetStatus("YETKİ GEREKLİ", ColorWarning);
                if (!isStreaming)
                {
                    UpdateStatus("YETKİ BEKLENİYOR", "Cihaz ekranındaki onay uyarısını kabul edin.", ColorWarning);
                }
            }
            else if (state == "device")
            {
                if (isStreaming)
                {
                    heroBanner.SetStatus("CANLI YAYINDA", ColorSuccess);
                }
                else
                {
                    if (connectedSerials != null && connectedSerials.Count > 1)
                    {
                        int devIdx = Math.Max(0, connectedSerials.IndexOf(activeTargetSerial));
                        heroBanner.SetStatus(string.Format("CİHAZ {0}/{1} (DEĞİŞTİR)", devIdx + 1, connectedSerials.Count), ColorAccent);
                    }
                    else
                    {
                        heroBanner.SetStatus("USB BAĞLANDI", ColorAccent);
                    }
                    UpdateStatus("HAZIR", "Cihaz algılandı. Yayını başlatmak için aşağıdaki butona tıklayın.", ColorSuccess);
                }
            }

            if (lblVddHint != null && width > 0 && height > 0)
            {
                lblVddHint.Text = string.Format("Cihaza ({0}x{1}) özel 1:1 piksel netliğinde sanal ekran sağlar.", width, height);
            }
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
            if (val <= 35) levelDesc = "Hızlı";
            else if (val <= 65) levelDesc = "Dengeli";
            else if (val <= 85) levelDesc = "Yüksek";
            else levelDesc = "Ultra Net";

            lblQualityVal.Text = string.Format("%{0} ({1})", val, levelDesc);
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

                lblStatusTitle.Text = "CANLI YAYIN AKTİF";
                lblStatusSub.Text = string.Format("Cihaza {0} FPS hızında {1:F1} MB/s veri akışı sağlanıyor.", currentSentFps, mbPerSec);
                lblStatusDot.ForeColor = ColorSuccess;
                heroBanner.SetStatus("CANLI YAYINDA", ColorSuccess);
                Program.Log(string.Format("PC_STATS -> sentFps={0}, mbPerSec={1:F2}, avgKb={2}, targetFps={3}, targetQ={4}, battSaver={5}", currentSentFps, mbPerSec, avgKb, targetFps, targetQuality, enableBatterySaver));
            }
        }

        private void StreamingWorker()
        {
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
                        client.SendBufferSize = 1024 * 1024;
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
                    UpdateStatus("CANLI YAYIN AKTİF", string.Format("Cihaza {0} FPS hızında kesintisiz aktarım başladı.", targetFps), ColorSuccess);
                    heroBanner.SetStatus("CANLI YAYINDA", ColorSuccess);
                    StreamToClient(client);
                    activeClient = null;
                    if (isStreaming)
                    {
                        heroBanner.SetStatus("YENİDEN BAĞLANILIYOR", ColorWarning);
                        UpdateStatus("BAĞLANTI KESİLDİ", "Cihaza otomatik tekrar bağlanılıyor...", ColorWarning);
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
                    SetStretchBltMode(hdcDst, 3); // COLORONCOLOR

                    StretchBlt(hdcDst, 0, 0, curTargetW, curTargetH,
                               hdcScreen, captureBounds.X, captureBounds.Y, captureBounds.Width, captureBounds.Height,
                               0x00CC0020);

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
                                if (remainingMs > 3)
                                {
                                    Thread.Sleep(1);
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
                        if (remainingMs > 3)
                        {
                            Thread.Sleep(1);
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
                            heroBanner.SetStatus("CANLI YAYINDA", ColorSuccess);
                            UpdateStatus("CİHAZ AKTİF", string.Format("Cihaz: {0} ({1}x{2}) ile bağlantı aktif.", clientDeviceModel, w, h), ColorSuccess);
                        }));

                        TrySyncVirtualDisplayResolution(w, h);
                    }
                }
            }
            catch { }
        }

        private void TrySyncVirtualDisplayResolution(int w, int h)
        {
            try
            {
                Screen[] screens = Screen.AllScreens;
                if (selectedScreenIndex >= 0 && selectedScreenIndex < screens.Length)
                {
                    Screen s = screens[selectedScreenIndex];
                    if (IsVirtualDisplay(s.DeviceName))
                    {
                        if (s.Bounds.Width != w || s.Bounds.Height != h)
                        {
                            DEVMODE dm = new DEVMODE();
                            dm.dmSize = (short)Marshal.SizeOf(dm);
                            dm.dmPelsWidth = w;
                            dm.dmPelsHeight = h;
                            dm.dmFields = 0x00080000 | 0x00100000;

                            int res = ChangeDisplaySettingsEx(s.DeviceName, ref dm, IntPtr.Zero, 0x00000001, IntPtr.Zero);
                            if (res == 0)
                            {
                                this.BeginInvoke(new Action(delegate()
                                {
                                    RefreshScreenList();
                                }));
                            }
                        }
                    }
                }
            }
            catch { }
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

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            isStreaming = false;
            try { if (deviceMonitorTimer != null) { deviceMonitorTimer.Stop(); deviceMonitorTimer.Dispose(); } } catch { }
            try { statsTimer.Stop(); statsTimer.Dispose(); } catch { }
            try
            {
                if (activeClient != null)
                {
                    activeClient.Close();
                }
            }
            catch { }
            try { timeEndPeriod(1); } catch { }
        }
    }

    // ═══════════════════════════════════════════════
    // ÖZEL GRAFİK KONTROLLERİ VE BİLEŞENLERİ (SIFIR LEKE, %100 OPAK)
    // ═══════════════════════════════════════════════

    public class ModernTitleBar : Panel
    {
        private Form parentForm;
        private string appTitle;
        private string appBadge;
        private TitleBarButton btnMin;
        private TitleBarButton btnClose;

        public ModernTitleBar(Form parent, string title, string badge)
        {
            this.parentForm = parent;
            this.appTitle = title;
            this.appBadge = badge;
            this.BackColor = MainForm.ColorBg;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

            btnMin = new TitleBarButton(false) { Size = new Size(42, 32) };
            btnMin.Click += delegate(object s, EventArgs e) { parentForm.WindowState = FormWindowState.Minimized; };

            btnClose = new TitleBarButton(true) { Size = new Size(42, 32) };
            btnClose.Click += delegate(object s, EventArgs e) { parentForm.Close(); };

            this.Controls.Add(btnMin);
            this.Controls.Add(btnClose);

            this.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    MainForm.DragWindow(parentForm.Handle);
                }
            };
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            if (btnClose != null && btnMin != null)
            {
                int btnY = (this.Height - 32) / 2;
                btnClose.Location = new Point(this.Width - 44, btnY);
                btnMin.Location = new Point(this.Width - 88, btnY);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (SolidBrush bgBrush = new SolidBrush(MainForm.ColorBg))
            {
                g.FillRectangle(bgBrush, this.ClientRectangle);
            }

            // Alt ayrım çizgisi
            using (Pen p = new Pen(Color.FromArgb(24, 32, 48), 1.0f))
            {
                g.DrawLine(p, 0, this.Height - 1, this.Width, this.Height - 1);
            }

            // App Icon
            int iconY = (this.Height - 24) / 2;
            Rectangle iconBox = new Rectangle(14, iconY, 24, 24);
            Image logo = MainForm.GetAppLogoImage();
            if (logo != null)
            {
                InterpolationMode prevMode = g.InterpolationMode;
                SmoothingMode prevSmooth = g.SmoothingMode;
                PixelOffsetMode prevOffset = g.PixelOffsetMode;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                using (GraphicsPath ip = RoundedPanel.CreateRoundedRect(iconBox, 5))
                {
                    GraphicsState state = g.Save();
                    g.SetClip(ip);
                    g.DrawImage(logo, iconBox);
                    g.Restore(state);
                }

                g.InterpolationMode = prevMode;
                g.SmoothingMode = prevSmooth;
                g.PixelOffsetMode = prevOffset;
            }
            else
            {
                using (GraphicsPath ip = RoundedPanel.CreateRoundedRect(iconBox, 6))
                using (LinearGradientBrush lgb = new LinearGradientBrush(iconBox, MainForm.ColorPrimary, MainForm.ColorAccent, 45f))
                {
                    g.FillPath(lgb, ip);
                }
                using (Font mf = new Font("Segoe UI", 9.5f, FontStyle.Bold))
                {
                    TextRenderer.DrawText(g, "M", mf, iconBox, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }

            // Başlık Metni
            using (Font titleFont = new Font("Segoe UI", 9.5f, FontStyle.Bold))
            using (SolidBrush b = new SolidBrush(MainForm.ColorText))
            {
                int titleY = (this.Height - 18) / 2;
                g.DrawString(appTitle, titleFont, b, 44, titleY);
            }

            // Sürüm Rozeti (BETA)
            using (Font titleFont = new Font("Segoe UI", 9.5f, FontStyle.Bold))
            using (Font bFont = new Font("Segoe UI", 7.0f, FontStyle.Bold))
            {
                int badgeX = 44 + (int)g.MeasureString(appTitle, titleFont).Width + 8;
                int badgeW = Math.Max(42, (int)g.MeasureString(appBadge, bFont).Width + 14);
                int badgeY = (this.Height - 16) / 2;
                Rectangle badgeRect = new Rectangle(badgeX, badgeY, badgeW, 16);
                using (GraphicsPath bp = RoundedPanel.CreateRoundedRect(badgeRect, 4))
                {
                    using (SolidBrush bgB = new SolidBrush(Color.FromArgb(28, 36, 56)))
                    {
                        g.FillPath(bgB, bp);
                    }
                    using (Pen bPen = new Pen(Color.FromArgb(45, 58, 90), 1.0f))
                    {
                        g.DrawPath(bPen, bp);
                    }
                }
                TextRenderer.DrawText(g, appBadge, bFont, badgeRect, MainForm.ColorAccent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }

    public class TitleBarButton : Control
    {
        private bool isCloseBtn;
        private bool isHovered = false;
        private bool isPressed = false;

        public TitleBarButton(bool isClose)
        {
            this.isCloseBtn = isClose;
            this.BackColor = MainForm.ColorBg;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            this.Cursor = Cursors.Default;

            this.MouseEnter += delegate(object s, EventArgs e) { isHovered = true; Invalidate(); };
            this.MouseLeave += delegate(object s, EventArgs e) { isHovered = false; isPressed = false; Invalidate(); };
            this.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { isPressed = true; Invalidate(); } };
            this.MouseUp += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { isPressed = false; Invalidate(); } };
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            using (SolidBrush b = new SolidBrush(MainForm.ColorBg))
            {
                pevent.Graphics.FillRectangle(b, this.ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (SolidBrush b = new SolidBrush(MainForm.ColorBg))
            {
                g.FillRectangle(b, this.ClientRectangle);
            }

            Color bg = Color.Transparent;
            Color fg = MainForm.ColorTextMuted;

            if (isCloseBtn)
            {
                if (isPressed) { bg = Color.FromArgb(185, 28, 28); fg = Color.White; }
                else if (isHovered) { bg = Color.FromArgb(220, 38, 38); fg = Color.White; }
            }
            else
            {
                if (isPressed) { bg = Color.FromArgb(38, 48, 70); fg = Color.White; }
                else if (isHovered) { bg = Color.FromArgb(28, 36, 54); fg = Color.White; }
            }

            if (bg != Color.Transparent)
            {
                Rectangle r = new Rectangle(0, 0, this.Width, this.Height);
                using (GraphicsPath gp = RoundedPanel.CreateRoundedRect(r, 6))
                {
                    using (SolidBrush b = new SolidBrush(bg))
                    {
                        g.FillPath(b, gp);
                    }
                }
            }

            int cx = this.Width / 2;
            int cy = this.Height / 2;
            using (Pen pen = new Pen(fg, 1.4f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                if (isCloseBtn)
                {
                    g.DrawLine(pen, cx - 4, cy - 4, cx + 4, cy + 4);
                    g.DrawLine(pen, cx - 4, cy + 4, cx + 4, cy - 4);
                }
                else
                {
                    g.DrawLine(pen, cx - 5, cy, cx + 5, cy);
                }
            }
        }
    }

    public class TabletHeroBanner : Panel
    {
        private string deviceName;
        private string specs;
        private string statusText;
        private Color statusColor;
        private bool isPortrait = false;

        public TabletHeroBanner(string name, string specs, string status, Color? statusColor = null)
        {
            this.deviceName = name;
            this.specs = specs;
            this.statusText = status;
            this.statusColor = statusColor.HasValue ? statusColor.Value : MainForm.ColorTextSubtle;
            this.DoubleBuffered = true;
            this.BackColor = MainForm.ColorBg;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        public void SetDevice(string name, string specs, bool portrait = false)
        {
            this.deviceName = name;
            this.specs = specs;
            this.isPortrait = portrait;
            this.Invalidate();
        }

        public void SetStatus(string status, Color color)
        {
            this.statusText = status;
            this.statusColor = color;
            this.Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            using (SolidBrush b = new SolidBrush(MainForm.ColorBg))
            {
                pevent.Graphics.FillRectangle(b, this.ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (SolidBrush clearBrush = new SolidBrush(MainForm.ColorBg))
            {
                g.FillRectangle(clearBrush, this.ClientRectangle);
            }

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);

            // Banner arka planı ve degrade ışık vurumu
            using (GraphicsPath path = RoundedPanel.CreateRoundedRect(rect, 10))
            {
                using (LinearGradientBrush lgb = new LinearGradientBrush(rect, Color.FromArgb(20, 26, 40), Color.FromArgb(14, 18, 28), 90f))
                {
                    g.FillPath(lgb, path);
                }
                using (Pen borderPen = new Pen(MainForm.ColorCardBorder, 1.2f))
                {
                    g.DrawPath(borderPen, path);
                }
            }

            // Sol Vektör Cihaz Çizimi (Telefon veya Tablet)
            if (isPortrait)
            {
                int phY = (this.Height - 34) / 2;
                Rectangle phoneRect = new Rectangle(16, phY, 18, 34);
                using (GraphicsPath pp = RoundedPanel.CreateRoundedRect(phoneRect, 4))
                {
                    using (Pen pPhone = new Pen(MainForm.ColorAccent, 1.5f))
                    {
                        g.DrawPath(pPhone, pp);
                    }
                }
                using (SolidBrush bSc = new SolidBrush(Color.FromArgb(30, 40, 60)))
                {
                    g.FillRectangle(bSc, 19, phY + 4, 12, 23);
                }
                using (SolidBrush bDot = new SolidBrush(MainForm.ColorAccent))
                {
                    g.FillRectangle(bDot, 22, phY + 1, 6, 2);
                    g.FillRectangle(bDot, 22, phY + 29, 6, 2);
                }
            }
            else
            {
                int tabY = (this.Height - 28) / 2;
                Rectangle tabRect = new Rectangle(14, tabY, 30, 28);
                using (GraphicsPath tp = RoundedPanel.CreateRoundedRect(tabRect, 4))
                {
                    using (Pen pTab = new Pen(MainForm.ColorAccent, 1.5f))
                    {
                        g.DrawPath(pTab, tp);
                    }
                }
                using (SolidBrush bSc = new SolidBrush(Color.FromArgb(30, 40, 60)))
                {
                    g.FillRectangle(bSc, 17, tabY + 3, 20, 22);
                }
                using (SolidBrush bDot = new SolidBrush(MainForm.ColorAccent))
                {
                    g.FillEllipse(bDot, 39, tabY + 12, 3, 3);
                }
            }

            // Cihaz Adı
            using (Font titleFont = new Font("Segoe UI", 10.0f, FontStyle.Bold))
            using (SolidBrush bTitle = new SolidBrush(MainForm.ColorText))
            {
                g.DrawString(deviceName, titleFont, bTitle, 54, 10);
            }

            // Özellikler ve Bağlantı Alt Metni
            using (Font subFont = new Font("Segoe UI", 8.2f))
            using (SolidBrush bSub = new SolidBrush(MainForm.ColorTextMuted))
            {
                g.DrawString(specs, subFont, bSub, 54, 32);
            }

            // Sağ Durum Rozeti (Pill)
            using (Font pillFont = new Font("Segoe UI", 7.5f, FontStyle.Bold))
            {
                Size sz = TextRenderer.MeasureText(g, statusText, pillFont);
                int pillW = Math.Max(120, sz.Width + 34);
                int pillH = 26;
                Rectangle pillRect = new Rectangle(this.Width - pillW - 14, (this.Height - pillH) / 2, pillW, pillH);

                using (GraphicsPath pillPath = RoundedPanel.CreateRoundedRect(pillRect, pillH / 2))
                {
                    using (SolidBrush pillBg = new SolidBrush(Color.FromArgb(24, 30, 46)))
                    {
                        g.FillPath(pillBg, pillPath);
                    }
                    using (Pen pillBorder = new Pen(Color.FromArgb(36, 48, 72), 1.0f))
                    {
                        g.DrawPath(pillBorder, pillPath);
                    }
                }

                // Rozet İçi Canlı Nokta
                using (SolidBrush dotBrush = new SolidBrush(statusColor))
                {
                    g.FillEllipse(dotBrush, pillRect.X + 11, pillRect.Y + 9, 8, 8);
                }

                // Rozet Metni
                Rectangle txtRect = new Rectangle(pillRect.X + 23, pillRect.Y, pillRect.Width - 26, pillRect.Height);
                TextRenderer.DrawText(g, statusText, pillFont, txtRect, statusColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }
        }
    }

    public class ModernCard : Panel
    {
        private string headerTitle;

        public ModernCard(string title, int x, int y, int w, int h)
        {
            this.headerTitle = title;
            this.Location = new Point(x, y);
            this.Size = new Size(w, h);
            this.BackColor = MainForm.ColorCard;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            using (SolidBrush b = new SolidBrush(MainForm.ColorBg))
            {
                pevent.Graphics.FillRectangle(b, this.ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (SolidBrush clearBrush = new SolidBrush(MainForm.ColorBg))
            {
                g.FillRectangle(clearBrush, this.ClientRectangle);
            }

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            using (GraphicsPath path = RoundedPanel.CreateRoundedRect(rect, 10))
            {
                using (SolidBrush b = new SolidBrush(MainForm.ColorCard))
                {
                    g.FillPath(b, path);
                }
                using (Pen p = new Pen(MainForm.ColorCardBorder, 1.0f))
                {
                    g.DrawPath(p, path);
                }
            }

            if (!string.IsNullOrEmpty(headerTitle))
            {
                using (Font hFont = new Font("Segoe UI", 7.5f, FontStyle.Bold))
                using (SolidBrush hBrush = new SolidBrush(MainForm.ColorTextMuted))
                {
                    g.DrawString(headerTitle, hFont, hBrush, 12, 10);
                }
            }
        }
    }

    public class ModernSegmentedMode : Control
    {
        private string text1;
        private string text2;
        private bool isExtend = true;
        private int hoveredIndex = -1;

        public bool IsExtend
        {
            get { return isExtend; }
            set { isExtend = value; Invalidate(); }
        }

        public delegate void ModeChangedHandler(bool isExtendMode);
        public event ModeChangedHandler ModeChanged;

        public ModernSegmentedMode(string t1, string t2, int x, int y, int w, int h)
        {
            this.text1 = t1;
            this.text2 = t2;
            this.Location = new Point(x, y);
            this.Size = new Size(w, h);
            this.BackColor = MainForm.ColorCard;
            this.Cursor = Cursors.Hand;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

            this.MouseMove += delegate(object s, MouseEventArgs e)
            {
                int newHov = (e.X < this.Width / 2) ? 0 : 1;
                if (newHov != hoveredIndex)
                {
                    hoveredIndex = newHov;
                    Invalidate();
                }
            };
            this.MouseLeave += delegate(object s, EventArgs e)
            {
                hoveredIndex = -1;
                Invalidate();
            };
            this.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    bool newExtend = (e.X < this.Width / 2);
                    if (newExtend != isExtend)
                    {
                        isExtend = newExtend;
                        Invalidate();
                        if (ModeChanged != null) ModeChanged(isExtend);
                    }
                }
            };
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            using (SolidBrush b = new SolidBrush(MainForm.ColorCard))
            {
                pevent.Graphics.FillRectangle(b, this.ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (SolidBrush cb = new SolidBrush(MainForm.ColorCard))
            {
                g.FillRectangle(cb, this.ClientRectangle);
            }

            // Dış Kaplama Tabanı
            Rectangle outer = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            using (GraphicsPath op = RoundedPanel.CreateRoundedRect(outer, 8))
            {
                using (SolidBrush ob = new SolidBrush(Color.FromArgb(14, 18, 28)))
                {
                    g.FillPath(ob, op);
                }
                using (Pen open = new Pen(MainForm.ColorCardBorder, 1.0f))
                {
                    g.DrawPath(open, op);
                }
            }

            int gap = 3;
            int segW = (this.Width - gap * 3) / 2;
            int segH = this.Height - gap * 2;

            // 1. Sekme (Genişlet)
            Rectangle r1 = new Rectangle(gap, gap, segW, segH);
            DrawSegment(g, r1, text1, isExtend, hoveredIndex == 0);

            // 2. Sekme (Yinele)
            Rectangle r2 = new Rectangle(gap * 2 + segW, gap, segW, segH);
            DrawSegment(g, r2, text2, !isExtend, hoveredIndex == 1);
        }

        private void DrawSegment(Graphics g, Rectangle r, string text, bool active, bool hovered)
        {
            using (GraphicsPath p = RoundedPanel.CreateRoundedRect(r, 6))
            {
                if (active)
                {
                    using (LinearGradientBrush lgb = new LinearGradientBrush(r, MainForm.ColorPrimary, MainForm.ColorPrimaryDark, 90f))
                    {
                        g.FillPath(lgb, p);
                    }
                    using (Pen pen = new Pen(Color.FromArgb(129, 140, 248), 1.2f))
                    {
                        g.DrawPath(pen, p);
                    }
                }
                else
                {
                    if (hovered)
                    {
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(24, 32, 48)))
                        {
                            g.FillPath(b, p);
                        }
                    }
                }
            }

            Color fg = active ? Color.White : (hovered ? MainForm.ColorText : MainForm.ColorTextMuted);
            using (Font f = new Font("Segoe UI", 8.5f, active ? FontStyle.Bold : FontStyle.Regular))
            {
                TextRenderer.DrawText(g, text, f, r, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }
    }

    public class MonitorItemData
    {
        public int Index;
        public Screen Screen;
        public string DeviceName;
        public string Resolution;
        public string Tag;
        public bool IsVirtual;
    }

    public class ModernMonitorSelector : Panel
    {
        private List<MonitorItemData> monitors = new List<MonitorItemData>();
        private int selectedIdx = 0;

        public delegate void SelectionChangedHandler(int index, Screen scr, bool isVirtual);
        public event SelectionChangedHandler SelectionChanged;

        public int SelectedIndex
        {
            get { return selectedIdx; }
            set
            {
                if (value >= 0 && value < monitors.Count && selectedIdx != value)
                {
                    selectedIdx = value;
                    RefreshCards();
                    if (SelectionChanged != null && monitors.Count > selectedIdx)
                    {
                        SelectionChanged(selectedIdx, monitors[selectedIdx].Screen, monitors[selectedIdx].IsVirtual);
                    }
                }
            }
        }

        public ModernMonitorSelector(int x, int y, int w, int h)
        {
            this.Location = new Point(x, y);
            this.Size = new Size(w, h);
            this.AutoScroll = true;
            this.BackColor = MainForm.ColorCard;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            using (SolidBrush b = new SolidBrush(MainForm.ColorCard))
            {
                pevent.Graphics.FillRectangle(b, this.ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(MainForm.ColorCard))
            {
                e.Graphics.FillRectangle(b, this.ClientRectangle);
            }
        }

        public void SetMonitors(List<MonitorItemData> items, int defaultIndex)
        {
            this.monitors = items ?? new List<MonitorItemData>();
            this.selectedIdx = (defaultIndex >= 0 && defaultIndex < monitors.Count) ? defaultIndex : 0;
            RefreshCards();

            if (monitors.Count > selectedIdx && SelectionChanged != null)
            {
                SelectionChanged(selectedIdx, monitors[selectedIdx].Screen, monitors[selectedIdx].IsVirtual);
            }
        }

        private void RefreshCards()
        {
            this.Controls.Clear();
            this.AutoScroll = (monitors.Count > 3);
            int curY = 0;
            int cardH = (monitors.Count >= 3) ? 40 : 52;
            int gap = (monitors.Count >= 3) ? 5 : 8;
            int itemW = (monitors.Count > 3) ? (this.Width - 18) : this.Width;

            Control selectedCtrl = null;
            for (int i = 0; i < monitors.Count; i++)
            {
                MonitorItemData data = monitors[i];
                bool isSelected = (i == selectedIdx);

                MonitorCardItem card = new MonitorCardItem(data, isSelected, 0, curY, itemW, cardH);
                int cardIdx = i;
                card.Click += delegate(object s, EventArgs e)
                {
                    this.SelectedIndex = cardIdx;
                };
                this.Controls.Add(card);
                if (isSelected) selectedCtrl = card;
                curY += cardH + gap;
            }
            if (selectedCtrl != null && monitors.Count > 3)
            {
                try { this.ScrollControlIntoView(selectedCtrl); } catch { }
            }
        }
    }

    public class MonitorCardItem : Control
    {
        private MonitorItemData data;
        private bool isSelected;
        private bool isHovered = false;

        public MonitorCardItem(MonitorItemData data, bool selected, int x, int y, int w, int h)
        {
            this.data = data;
            this.isSelected = selected;
            this.Location = new Point(x, y);
            this.Size = new Size(w, h);
            this.BackColor = MainForm.ColorCard;
            this.Cursor = Cursors.Hand;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

            this.MouseEnter += delegate(object s, EventArgs e) { isHovered = true; Invalidate(); };
            this.MouseLeave += delegate(object s, EventArgs e) { isHovered = false; Invalidate(); };
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            using (SolidBrush b = new SolidBrush(MainForm.ColorCard))
            {
                pevent.Graphics.FillRectangle(b, this.ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (SolidBrush cb = new SolidBrush(MainForm.ColorCard))
            {
                g.FillRectangle(cb, this.ClientRectangle);
            }

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);

            Color bg = isSelected ? Color.FromArgb(26, 34, 52) : (isHovered ? Color.FromArgb(20, 26, 40) : Color.FromArgb(14, 18, 28));
            Color border = isSelected ? MainForm.ColorPrimary : (isHovered ? Color.FromArgb(46, 58, 86) : MainForm.ColorCardBorder);

            using (GraphicsPath p = RoundedPanel.CreateRoundedRect(rect, 8))
            {
                using (SolidBrush b = new SolidBrush(bg))
                {
                    g.FillPath(b, p);
                }
                using (Pen pen = new Pen(border, isSelected ? 1.6f : 1.0f))
                {
                    g.DrawPath(pen, p);
                }
            }

            // Sol Seçim Çubuğu
            if (isSelected)
            {
                using (SolidBrush barB = new SolidBrush(MainForm.ColorPrimary))
                {
                    g.FillRectangle(barB, 0, 8, 4, this.Height - 16);
                }
            }

            // Monitör Vektör Simgesi
            int monY = (this.Height - 15) / 2 - 1;
            Rectangle monR = new Rectangle(12, monY, 20, 15);
            using (Pen pMon = new Pen(isSelected ? MainForm.ColorAccent : MainForm.ColorTextMuted, 1.4f))
            {
                g.DrawRectangle(pMon, monR);
                g.DrawLine(pMon, monR.Left + 5, monR.Bottom + 4, monR.Right - 5, monR.Bottom + 4);
                g.DrawLine(pMon, monR.Left + 10, monR.Bottom, monR.Left + 10, monR.Bottom + 4);
            }

            // Ekran Adı ve Çözünürlük
            string devLabel = data.DeviceName;
            if (devLabel.Length > 16) devLabel = devLabel.Substring(0, 16);
            string line1 = string.Format("{0}  •  {1}", devLabel, data.Resolution);

            int textY = (this.Height <= 38) ? 2 : ((this.Height <= 46) ? 4 : 7);
            using (Font f1 = new Font("Segoe UI", (this.Height <= 38) ? 8.0f : 8.5f, FontStyle.Bold))
            using (SolidBrush b1 = new SolidBrush(isSelected ? Color.White : MainForm.ColorText))
            {
                g.DrawString(line1, f1, b1, 40, textY);
            }

            // Alt Rozet Metni
            int tagY = (this.Height <= 38) ? 18 : ((this.Height <= 46) ? 22 : 26);
            int tagH = (this.Height <= 38) ? 13 : 14;
            using (Font f2 = new Font("Segoe UI", 6.8f, FontStyle.Bold))
            {
                Color tagBg = data.IsVirtual ? Color.FromArgb(46, 16, 70) : (data.Screen.Primary ? Color.FromArgb(16, 36, 68) : Color.FromArgb(26, 34, 48));
                Color tagFg = data.IsVirtual ? Color.FromArgb(216, 180, 254) : (data.Screen.Primary ? Color.FromArgb(147, 197, 253) : MainForm.ColorTextMuted);

                int tagW = (int)g.MeasureString(data.Tag, f2).Width + 10;
                Rectangle tagR = new Rectangle(40, tagY, tagW, tagH);
                using (GraphicsPath tp = RoundedPanel.CreateRoundedRect(tagR, 3))
                {
                    using (SolidBrush tb = new SolidBrush(tagBg))
                    {
                        g.FillPath(tb, tp);
                    }
                }
                TextRenderer.DrawText(g, data.Tag, f2, tagR, tagFg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            // Sağ Seçim Göstergesi (Radio / Check)
            int dotX = this.Width - 22;
            int dotY = (this.Height - 14) / 2;
            Rectangle dotR = new Rectangle(dotX, dotY, 14, 14);

            using (Pen dotPen = new Pen(isSelected ? MainForm.ColorPrimary : Color.FromArgb(50, 60, 84), 1.5f))
            {
                g.DrawEllipse(dotPen, dotR);
            }
            if (isSelected)
            {
                using (SolidBrush dotFill = new SolidBrush(MainForm.ColorPrimary))
                {
                    g.FillEllipse(dotFill, dotX + 3, dotY + 3, 8, 8);
                }
            }
        }
    }

    public class ModernSlider : Control
    {
        private int minimum;
        private int maximum;
        private int val;
        private bool isDragging = false;
        private bool isHovered = false;
        private Color colorStart;
        private Color colorEnd;

        public event EventHandler ValueChanged;

        public int Value
        {
            get { return val; }
            set
            {
                int clamped = Math.Max(minimum, Math.Min(maximum, value));
                if (clamped != val)
                {
                    val = clamped;
                    Invalidate();
                    if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
                }
            }
        }

        public ModernSlider(int min, int max, int defaultVal, Color cStart, Color cEnd)
        {
            this.minimum = min;
            this.maximum = max;
            this.val = defaultVal;
            this.colorStart = cStart;
            this.colorEnd = cEnd;
            this.BackColor = MainForm.ColorCard;
            this.Cursor = Cursors.Hand;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

            this.MouseEnter += delegate(object s, EventArgs e) { isHovered = true; Invalidate(); };
            this.MouseLeave += delegate(object s, EventArgs e) { isHovered = false; Invalidate(); };
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            using (SolidBrush b = new SolidBrush(MainForm.ColorCard))
            {
                pevent.Graphics.FillRectangle(b, this.ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (SolidBrush b = new SolidBrush(MainForm.ColorCard))
            {
                g.FillRectangle(b, this.ClientRectangle);
            }

            int pad = 8;
            int trackH = 5;
            int trackY = (this.Height - trackH) / 2;
            int trackW = this.Width - pad * 2;
            if (trackW <= 0) return;

            // Arka İz
            Rectangle bgRect = new Rectangle(pad, trackY, trackW, trackH);
            using (GraphicsPath bp = RoundedPanel.CreateRoundedRect(bgRect, 2))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(28, 36, 52)))
                {
                    g.FillPath(b, bp);
                }
            }

            // Dolu İz
            float pct = (maximum > minimum) ? (float)(val - minimum) / (maximum - minimum) : 0f;
            int fillW = (int)(trackW * pct);
            if (fillW > 0)
            {
                Rectangle fillR = new Rectangle(pad, trackY, fillW, trackH);
                using (GraphicsPath fp = RoundedPanel.CreateRoundedRect(fillR, 2))
                {
                    using (LinearGradientBrush lgb = new LinearGradientBrush(bgRect, colorStart, colorEnd, 0f))
                    {
                        g.FillPath(lgb, fp);
                    }
                }
            }

            // Yuvarlak Tutamaç
            int thumbRadius = isHovered || isDragging ? 8 : 7;
            int thumbX = pad + (int)(trackW * pct);
            int thumbY = this.Height / 2;
            Rectangle thumbRect = new Rectangle(thumbX - thumbRadius, thumbY - thumbRadius, thumbRadius * 2, thumbRadius * 2);

            using (SolidBrush tb = new SolidBrush(Color.White))
            {
                g.FillEllipse(tb, thumbRect);
            }
            using (Pen tp = new Pen(colorStart, 2.5f))
            {
                g.DrawEllipse(tp, thumbRect);
            }
        }

        private void UpdateMouse(int mouseX)
        {
            int pad = 8;
            int trackW = this.Width - pad * 2;
            if (trackW <= 0) return;

            float ratio = (float)(mouseX - pad) / trackW;
            ratio = Math.Max(0f, Math.Min(1f, ratio));
            int newVal = minimum + (int)Math.Round(ratio * (maximum - minimum));
            if (newVal != val)
            {
                val = newVal;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                isDragging = true;
                this.Capture = true;
                UpdateMouse(e.X);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (isDragging) UpdateMouse(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (isDragging)
            {
                isDragging = false;
                this.Capture = false;
            }
        }
    }

    public class QuickChip : Control
    {
        private bool isHovered = false;

        public QuickChip(string text, int x, int y, int w, int h)
        {
            this.Text = text;
            this.Location = new Point(x, y);
            this.Size = new Size(w, h);
            this.BackColor = MainForm.ColorCard;
            this.Cursor = Cursors.Hand;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

            this.MouseEnter += delegate(object s, EventArgs e) { isHovered = true; Invalidate(); };
            this.MouseLeave += delegate(object s, EventArgs e) { isHovered = false; Invalidate(); };
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            using (SolidBrush b = new SolidBrush(MainForm.ColorCard))
            {
                pevent.Graphics.FillRectangle(b, this.ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (SolidBrush b = new SolidBrush(MainForm.ColorCard))
            {
                g.FillRectangle(b, this.ClientRectangle);
            }

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            Color bg = isHovered ? Color.FromArgb(32, 42, 64) : Color.FromArgb(20, 26, 40);
            Color border = isHovered ? MainForm.ColorPrimary : Color.FromArgb(36, 46, 70);

            using (GraphicsPath p = RoundedPanel.CreateRoundedRect(rect, 4))
            {
                using (SolidBrush b = new SolidBrush(bg))
                {
                    g.FillPath(b, p);
                }
                using (Pen pen = new Pen(border, 1.0f))
                {
                    g.DrawPath(pen, p);
                }
            }

            Color fg = isHovered ? Color.White : MainForm.ColorTextMuted;
            using (Font f = new Font("Segoe UI", 7.0f, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, this.Text, f, rect, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }

    public class ModernSwitch : Control
    {
        private string title;
        private string subtitle;
        private bool isChecked = false;
        private bool isHovered = false;

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return isChecked; }
            set
            {
                if (isChecked != value)
                {
                    isChecked = value;
                    Invalidate();
                    if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
                }
            }
        }

        public ModernSwitch(string title, string subtitle, int x, int y, int w, int h)
        {
            this.title = title;
            this.subtitle = subtitle;
            this.Location = new Point(x, y);
            this.Size = new Size(w, h);
            this.BackColor = MainForm.ColorCard;
            this.Cursor = Cursors.Hand;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

            this.MouseEnter += delegate(object s, EventArgs e) { isHovered = true; Invalidate(); };
            this.MouseLeave += delegate(object s, EventArgs e) { isHovered = false; Invalidate(); };
            this.Click += delegate(object s, EventArgs e) { this.Checked = !this.Checked; };
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            using (SolidBrush b = new SolidBrush(MainForm.ColorCard))
            {
                pevent.Graphics.FillRectangle(b, this.ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (SolidBrush cb = new SolidBrush(MainForm.ColorCard))
            {
                g.FillRectangle(cb, this.ClientRectangle);
            }

            // iOS / Fluent tarzı anahtar hapı
            int swW = 38;
            int swH = 20;
            int swY = (this.Height - swH) / 2;
            Rectangle swRect = new Rectangle(0, swY, swW, swH);

            Color swBg = isChecked ? MainForm.ColorPrimary : Color.FromArgb(28, 36, 52);
            using (GraphicsPath p = RoundedPanel.CreateRoundedRect(swRect, swH / 2))
            {
                using (SolidBrush b = new SolidBrush(swBg))
                {
                    g.FillPath(b, p);
                }
                using (Pen pen = new Pen(isChecked ? Color.FromArgb(129, 140, 248) : Color.FromArgb(46, 58, 80), 1.0f))
                {
                    g.DrawPath(pen, p);
                }
            }

            // Kayar Düğme
            int knobD = 14;
            int knobX = isChecked ? (swW - knobD - 3) : 3;
            int knobY = swY + (swH - knobD) / 2;
            using (SolidBrush kb = new SolidBrush(Color.White))
            {
                g.FillEllipse(kb, knobX, knobY, knobD, knobD);
            }

            // Başlık ve Alt Bilgi (Dikeyde Ortalanmış)
            int txtX = swW + 12;
            int txtY = Math.Max(2, (this.Height - 34) / 2);
            Rectangle titleR = new Rectangle(txtX, txtY, this.Width - txtX, 17);
            using (Font fTitle = new Font("Segoe UI", 8.5f, FontStyle.Bold))
            {
                Color fg = isHovered ? Color.White : MainForm.ColorText;
                TextRenderer.DrawText(g, title, fTitle, titleR, fg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            Rectangle subR = new Rectangle(txtX, txtY + 17, this.Width - txtX, 15);
            using (Font fSub = new Font("Segoe UI", 7.5f))
            {
                TextRenderer.DrawText(g, subtitle, fSub, subR, MainForm.ColorTextMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }
    }

    public class TelemetryBadge : Control
    {
        private string label;
        private string val;

        public string Value
        {
            get { return val; }
            set { val = value; Invalidate(); }
        }

        public TelemetryBadge(string lbl, string initialVal, int x, int y, int w, int h)
        {
            this.label = lbl;
            this.val = initialVal;
            this.Location = new Point(x, y);
            this.Size = new Size(w, h);
            this.BackColor = MainForm.ColorCard;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            using (SolidBrush b = new SolidBrush(MainForm.ColorCard))
            {
                pevent.Graphics.FillRectangle(b, this.ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (SolidBrush cb = new SolidBrush(MainForm.ColorCard))
            {
                g.FillRectangle(cb, this.ClientRectangle);
            }

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            using (GraphicsPath p = RoundedPanel.CreateRoundedRect(rect, 6))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(14, 18, 28)))
                {
                    g.FillPath(b, p);
                }
                using (Pen pen = new Pen(Color.FromArgb(32, 42, 64), 1.0f))
                {
                    g.DrawPath(pen, p);
                }
            }

            Rectangle topR = new Rectangle(0, 2, this.Width, 11);
            using (Font fL = new Font("Segoe UI", 6.5f, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, label, fL, topR, MainForm.ColorTextSubtle, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            Rectangle botR = new Rectangle(0, 13, this.Width, 13);
            using (Font fV = new Font("Segoe UI", 7.5f, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, val, fV, botR, MainForm.ColorAccent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }

    public class ModernButton : Button
    {
        private Color baseColor;
        private Color textColor;
        private Color borderColor = Color.Empty;
        private bool isHovered = false;
        private bool isPressed = false;
        private int cornerRadius = 8;

        public ModernButton(string text, int x, int y, int w, int h, Color bg, Color fg, int radius = 8, Color? border = null)
        {
            this.Text = text;
            this.Location = new Point(x, y);
            this.Size = new Size(w, h);
            this.baseColor = bg;
            this.textColor = fg;
            this.cornerRadius = radius;
            if (border.HasValue) this.borderColor = border.Value;
            this.FlatStyle = FlatStyle.Flat;
            this.FlatAppearance.BorderSize = 0;
            this.Cursor = Cursors.Hand;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            this.MouseEnter += delegate(object s, EventArgs e) { isHovered = true; this.Invalidate(); };
            this.MouseLeave += delegate(object s, EventArgs e) { isHovered = false; isPressed = false; this.Invalidate(); };
            this.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { isPressed = true; this.Invalidate(); } };
            this.MouseUp += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { isPressed = false; this.Invalidate(); } };
        }

        public void SetColors(Color bg, Color fg, Color? border = null)
        {
            this.baseColor = bg;
            this.textColor = fg;
            if (border.HasValue) this.borderColor = border.Value;
            this.Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            // Ebeveyn rengiyle temizle
            Color pBg = (this.Parent != null) ? this.Parent.BackColor : MainForm.ColorCard;
            using (SolidBrush b = new SolidBrush(pBg))
            {
                pevent.Graphics.FillRectangle(b, this.ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color pBg = (this.Parent != null) ? this.Parent.BackColor : MainForm.ColorCard;
            using (SolidBrush b = new SolidBrush(pBg))
            {
                g.FillRectangle(b, this.ClientRectangle);
            }

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            Color drawBg = baseColor;
            if (isPressed)
            {
                drawBg = ControlPaint.Dark(baseColor, 0.10f);
            }
            else if (isHovered)
            {
                drawBg = ControlPaint.Light(baseColor, 0.15f);
            }

            using (GraphicsPath path = RoundedPanel.CreateRoundedRect(rect, cornerRadius))
            {
                using (SolidBrush brush = new SolidBrush(drawBg))
                {
                    g.FillPath(brush, path);
                }

                Color borderCol = borderColor.IsEmpty ? drawBg : (isHovered ? ControlPaint.Light(borderColor, 0.2f) : borderColor);
                using (Pen pen = new Pen(borderCol, 1.0f))
                {
                    g.DrawPath(pen, path);
                }
            }

            TextRenderer.DrawText(g, this.Text, this.Font, rect, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    public class RoundedPanel : Panel
    {
        private int cornerRadius;
        private Color bgColor;
        private Color borderColor;

        public RoundedPanel(int w, int h, int radius, Color bg, Color border)
        {
            this.Size = new Size(w, h);
            this.cornerRadius = radius;
            this.bgColor = bg;
            this.borderColor = border;
            this.DoubleBuffered = true;
            this.BackColor = bg;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            using (SolidBrush b = new SolidBrush(MainForm.ColorBg))
            {
                pevent.Graphics.FillRectangle(b, this.ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using (SolidBrush clearBrush = new SolidBrush(MainForm.ColorBg))
            {
                e.Graphics.FillRectangle(clearBrush, this.ClientRectangle);
            }

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            using (GraphicsPath path = CreateRoundedRect(rect, cornerRadius))
            {
                using (SolidBrush brush = new SolidBrush(bgColor))
                {
                    e.Graphics.FillPath(brush, path);
                }
                using (Pen pen = new Pen(borderColor, 1.2f))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }
        }

        public static GraphicsPath CreateRoundedRect(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            if (d <= 0)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
