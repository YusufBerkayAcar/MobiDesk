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
        public static EventWaitHandle AppWakeupEvent = null;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern int RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool ChangeWindowMessageFilter(int message, uint dwFlag);
        public const uint MSGFLT_ADD = 1;

        public static readonly int WM_SHOW_MOBIDESK = RegisterWindowMessage("MOBIDESK_SHOW_INSTANCE_MESSAGE");

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr ProcessId);

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

        public const int SW_RESTORE = 9;

        public static void ForceForegroundWindow(IntPtr hWnd)
        {
            try
            {
                ShowWindow(hWnd, 9 /* SW_RESTORE */);
                ShowWindow(hWnd, 5 /* SW_SHOW */);
                keybd_event(0x12 /* VK_MENU */, 0, 0, 0);
                keybd_event(0x12 /* VK_MENU */, 0, 2 /* KEYEVENTF_KEYUP */, 0);
                BringWindowToTop(hWnd);
                SetForegroundWindow(hWnd);
            }
            catch { }
        }

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
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;

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
        static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

        [DllImport("gdi32.dll")]
        static extern bool StretchBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int nWidthSrc, int nHeightSrc, int dwRop);

        [DllImport("gdi32.dll")]
        static extern int SetStretchBltMode(IntPtr hdc, int nStretchMode);

        [DllImport("gdi32.dll")]
        static extern bool SetBrushOrgEx(IntPtr hdc, int nXOrg, int nYOrg, IntPtr lppt);


        const int CURSOR_SHOWING = 0x00000001;

        [DllImport("user32.dll")]
        static extern bool GetCursorInfo(out CURSORINFO pci);


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


        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        public static extern int ChangeDisplaySettingsEx(string lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

        [DllImport("gdi32.dll", CharSet = CharSet.Ansi)]
        public static extern IntPtr CreateDCA(string lpszDriver, string lpszDevice, string lpszOutput, IntPtr lpInitData);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        public static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);


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


        // ULTRA PREMIUM RENK PALETI (Obsidian & Cyber Studio)
        // CyberObsidianTheme yönlendirmeleri ile tam geriye dönük uyumluluk
        public static readonly Color ColorBg          = CyberObsidianTheme.Bg;
        public static readonly Color ColorCard        = CyberObsidianTheme.Card;
        public static readonly Color ColorCardBorder  = CyberObsidianTheme.CardBorder;
        public static readonly Color ColorCardHover   = CyberObsidianTheme.CardHover;
        public static readonly Color ColorPrimary     = CyberObsidianTheme.Primary;
        public static readonly Color ColorPrimaryDark = CyberObsidianTheme.PrimaryDark;
        public static readonly Color ColorAccent      = CyberObsidianTheme.Accent;
        public static readonly Color Success          = CyberObsidianTheme.Success;
        public static readonly Color ColorSuccess     = CyberObsidianTheme.Success;
        public static readonly Color ColorSuccessDark = CyberObsidianTheme.SuccessDark;
        public static readonly Color ColorDanger      = CyberObsidianTheme.Danger;
        public static readonly Color ColorDangerDark  = CyberObsidianTheme.DangerDark;
        public static readonly Color Warning          = CyberObsidianTheme.Warning;
        public static readonly Color ColorWarning     = CyberObsidianTheme.Warning;
        public static readonly Color ColorText        = CyberObsidianTheme.Text;
        public static readonly Color ColorTextMuted   = CyberObsidianTheme.TextMuted;
        public static readonly Color ColorTextSubtle  = CyberObsidianTheme.TextSubtle;
        // ═══════════════════════════════════════════════
        private volatile bool isStreaming = false;
        private Thread streamThread;
        private TcpClient activeClient;
        private Rectangle currentScreenBounds;
        private string selectedDeviceName = "";
        private int selectedScreenIndex = -1;
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
        private ModernCard cardMode;
        private ModernSegmentedMode modeControl;
        internal ModernCard cardScreens;
        private ModernMonitorSelector screenSelector;
        private ModernButton btnRefresh;
        internal ModernCard cardVirtual;
        private ModernButton btnAddVirtual;
        private ModernButton btnRemoveVirtual;
        private ModernButton btnVirtualInfo;
        internal ModernCard cardAudio;
        private ModernSwitch swEnableAudio;
        private ModernButton btnAddAudio;
        private ModernButton btnRemoveAudio;
        private ModernButton btnAudioInfo;
        private Thread audioStreamThread;
        private volatile bool isAudioStreaming = false;
        private ToolTip vddToolTip;
        private Label lblVddHint;
        private ModernCard cardPerf;
        private Label lblFpsTitle;
        private ModernSlider tbFps;
        private Label lblFpsVal;
        private Label lblQualityTitle;
        private ModernSlider tbQuality;
        private Label lblQualityVal;
        internal ModernCard cardOpts;
        internal ModernSwitch swAdaptiveFps;
        internal ModernSwitch swShowFps;
        internal ModernSwitch swBatterySaver;
        internal ModernSwitch swStartup;
        private volatile bool enableAdaptiveFps = true;
        private bool startMinimized = false;
        private bool hasShownInitial = false;
        internal ModernButton btnStartStop;

        // Dinamik USB Cihaz İzleme
        private System.Windows.Forms.Timer deviceMonitorTimer;
        private volatile bool isCheckingDevice = false;
        public static string activeTargetSerial = "";
        public static List<string> connectedSerials = new List<string>();
        private static readonly HashSet<string> activeForwardCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Alt Çubuk Elemanları
        private Label lblStatusDot;
        private Label lblStatusTitle;
        private Label lblStatusSub;
        private TelemetryBadge badgeFps;
        private TelemetryBadge badgeBandwidth;
        private TelemetryBadge badgeSize;
        private TelemetryBadge badgeLatency;

        // Düzen Konteynerleri ve Çipler
        internal DoubleBufferedPanel pnlLeftCol;
        internal DoubleBufferedPanel pnlRightCol;
        internal RoundedPanel pnlBottomBar;
        private List<QuickChip> fpsChips = new List<QuickChip>();
        private List<QuickChip> qualityChips = new List<QuickChip>();

        // MobiFlow & Bildirimler (Opera Flow & Notification Mirror)
        private ModernStudioTabs tabSelector;
        private MobiFlowPanel pnlFlow;
        private MobiNotificationsPanel pnlNotifications;
        private MobiFlowManager flowManager;
        private int currentTabIndex = 0;

        // Sistem Tepsisi (System Tray / Gizli Simgeler)
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private ToolStripMenuItem trayItemShow;
        private ToolStripMenuItem trayItemToggleStream;
        private ToolStripSeparator traySeparator1;
        private ToolStripMenuItem trayItemExit;
        internal bool reallyExit = false;
        private bool hasShownTrayBalloon = false;

        protected override CreateParams CreateParams
        {
            get
            {
                const int CS_DROPSHADOW = 0x00020000;
                const int WS_MINIMIZEBOX = 0x00020000;
                const int WS_MAXIMIZEBOX = 0x00010000;
                const int WS_CLIPCHILDREN = 0x02000000;
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                cp.Style |= WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_CLIPCHILDREN;
                return cp;
            }
        }

        private static Image cachedAppLogo = null;
        public static Image GetAppLogoImage()
        {
            if (cachedAppLogo != null) return cachedAppLogo;
            try
            {
                string pngPath = MobiPathResolver.ResolveAsset("app_icon.png");
                if (File.Exists(pngPath))
                {
                    using (Image img = Image.FromFile(pngPath))
                    {
                        cachedAppLogo = new Bitmap(img);
                    }
                    return cachedAppLogo;
                }
                string icoPath = MobiPathResolver.ResolveAsset("app_icon.ico");
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

        public MainForm(bool autoStart = false, string snapshotPath = null, bool closeAfterSnapshot = false, int initialTab = 0, bool startMinimized = false)
        {
            this.startMinimized = startMinimized;
            try { timeBeginPeriod(1); } catch { }

            try
            {
                string icoPath = MobiPathResolver.ResolveAsset("app_icon.ico");
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
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            this.MinimumSize = new Size(860, 680);
            try { ChangeWindowMessageFilter(WM_SHOW_MOBIDESK, MSGFLT_ADD); } catch { }

            if (AppWakeupEvent != null)
            {
                Thread wakeupThread = new Thread(delegate()
                {
                    while (!reallyExit)
                    {
                        try
                        {
                            if (AppWakeupEvent.WaitOne())
                            {
                                if (reallyExit) break;
                                this.BeginInvoke(new Action(delegate()
                                {
                                    RestoreFromTray();
                                }));
                            }
                        }
                        catch { }
                    }
                });
                wakeupThread.IsBackground = true;
                wakeupThread.Start();
            }

            AppConfig.Load();
            targetFps = AppConfig.Fps;
            targetQuality = AppConfig.Quality;
            enableAdaptiveFps = AppConfig.AdaptiveFps;
            showFpsOnTablet = AppConfig.ShowFps;
            enableBatterySaver = AppConfig.BatterySaver;

            Program.Log("MainForm ctor 1: config loaded");
            // MobiFlow & Bildirim Senkronizasyon Yöneticisi
            try
            {
                flowManager = new MobiFlowManager();
                flowManager.MessageReceived += FlowManager_MessageReceived;
                flowManager.FileProgress += FlowManager_FileProgress;
                flowManager.NotificationReceived += FlowManager_NotificationReceived;
                flowManager.NotificationRemoved += FlowManager_NotificationRemoved;
                flowManager.DeviceConnectionChanged += FlowManager_DeviceConnectionChanged;
                flowManager.ConnectionChanged += FlowManager_ConnectionChanged;
                Program.Log("MainForm ctor 2: flowManager created");
            }
            catch (Exception ex)
            {
                Program.Log("flowManager init error: " + ex.Message);
            }

            Program.Log("MainForm ctor 3: calling InitializeStudioUI");
            InitializeStudioUI();
            Program.Log("MainForm ctor 4: calling ApplyModernWindowStyle");
            ApplyModernWindowStyle();
            Program.Log("MainForm ctor 5: calling RefreshScreenList");
            RefreshScreenList();
            Program.Log("MainForm ctor 6: calling ApplyLanguage");
            ApplyLanguage();
            Program.Log("MainForm ctor 7: calling InitializeTrayIcon");
            InitializeTrayIcon();
            Program.Log("MainForm ctor 8: ctor finished");

            statsTimer = new System.Windows.Forms.Timer();
            statsTimer.Interval = 1000;
            statsTimer.Tick += StatsTimer_Tick;
            statsTimer.Start();

            this.Shown += delegate(object s, EventArgs e)
            {
                Program.Log("MainForm.Shown fired");
                if (startMinimized)
                {
                    this.WindowState = FormWindowState.Minimized;
                    this.Hide();
                    this.ShowInTaskbar = false;
                    if (trayIcon != null)
                    {
                        trayIcon.Visible = true;
                        ThreadPool.QueueUserWorkItem(delegate
                        {
                            Thread.Sleep(1500);
                            try
                            {
                                if (trayIcon != null)
                                {
                                    trayIcon.ShowBalloonTip(
                                        2500,
                                        "MobiDesk",
                                        L10n.T(
                                            "MobiDesk arka planda çalışıyor. USB ile bağlı telefonunuzun bildirimleri aktarılmaya hazır.",
                                            "MobiDesk is running in background. Plugged-in phone notifications ready to sync."
                                        ),
                                        ToolTipIcon.Info
                                    );
                                }
                            }
                            catch { }
                        });
                    }
                }
                else
                {
                    try
                    {
                        this.BringToFront();
                        this.Activate();
                        ForceForegroundWindow(this.Handle);
                    }
                    catch { }
                }

                if (flowManager != null)
                {
                    try { flowManager.Start(); } catch { }
                }

                if (initialTab > 0 && tabSelector != null)
                {
                    tabSelector.SelectedIndex = initialTab;
                }

                if ((Program.MockTestNotifications || AppConfig.MockNotifications) && pnlNotifications != null)
                {
                    pnlNotifications.LoadMockNotifications();
                    if (tabSelector != null && initialTab != 2) tabSelector.NotificationBadge = 9;
                }

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
                                    reallyExit = true;
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

        protected override void SetVisibleCore(bool value)
        {
            if (startMinimized && !hasShownInitial)
            {
                value = false;
                if (!this.IsHandleCreated) CreateHandle();
            }
            base.SetVisibleCore(value);
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

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutStudioUI();
            this.Invalidate(true);
        }

        protected override void WndProc(ref Message m)
        {
            if (WM_SHOW_MOBIDESK != 0 && m.Msg == WM_SHOW_MOBIDESK)
            {
                RestoreFromTray();
                m.Result = (IntPtr)1;
                return;
            }

            const int WM_GETMINMAXINFO = 0x0024;
            const int WM_NCHITTEST = 0x0084;
            const int WM_DPICHANGED = 0x02E0;
            const int WM_ERASEBKGND = 0x0014;
            const int WM_EXITSIZEMOVE = 0x0232;

            if (m.Msg == WM_ERASEBKGND)
            {
                m.Result = (IntPtr)1;
                return;
            }

            if (m.Msg == WM_EXITSIZEMOVE)
            {
                LayoutStudioUI();
                if (this.WindowState == FormWindowState.Normal)
                {
                    AppConfig.WindowWidth = this.ClientSize.Width;
                    AppConfig.WindowHeight = this.ClientSize.Height;
                    AppConfig.Save();
                }
                this.Invalidate(true);
                this.Update();
            }

            if (m.Msg == WM_GETMINMAXINFO)
            {
                try
                {
                    MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(m.LParam, typeof(MINMAXINFO));
                    mmi.ptMinTrackSize.x = 860;
                    mmi.ptMinTrackSize.y = 680;
                    Marshal.StructureToPtr(mmi, m.LParam, true);
                    m.Result = IntPtr.Zero;
                    return;
                }
                catch { }
            }

            if (m.Msg == WM_DPICHANGED)
            {
                try
                {
                    RECT suggested = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
                    int newW = Math.Max(860, suggested.right - suggested.left);
                    int newH = Math.Max(680, suggested.bottom - suggested.top);
                    SetWindowPos(this.Handle, IntPtr.Zero, suggested.left, suggested.top, newW, newH, SWP_NOZORDER | SWP_NOACTIVATE);
                    LayoutStudioUI();
                }
                catch { }
                m.Result = IntPtr.Zero;
                return;
            }

            if (m.Msg == WM_NCHITTEST && this.WindowState == FormWindowState.Normal)
            {
                base.WndProc(ref m);
                if (m.Result == (IntPtr)1 /* HTCLIENT */)
                {
                    short x = (short)(m.LParam.ToInt32() & 0xFFFF);
                    short y = (short)((m.LParam.ToInt32() >> 16) & 0xFFFF);
                    Point clientPoint = this.PointToClient(new Point(x, y));

                    int grip = 7;
                    bool left = clientPoint.X < grip;
                    bool right = clientPoint.X >= this.ClientSize.Width - grip;
                    bool top = clientPoint.Y < grip;
                    bool bottom = clientPoint.Y >= this.ClientSize.Height - grip;

                    if (top && left) { m.Result = (IntPtr)13; return; }     // HTTOPLEFT
                    if (top && right) { m.Result = (IntPtr)14; return; }    // HTTOPRIGHT
                    if (bottom && left) { m.Result = (IntPtr)16; return; }  // HTBOTTOMLEFT
                    if (bottom && right) { m.Result = (IntPtr)17; return; } // HTBOTTOMRIGHT
                    if (left) { m.Result = (IntPtr)10; return; }            // HTLEFT
                    if (right) { m.Result = (IntPtr)11; return; }           // HTRIGHT
                    if (top) { m.Result = (IntPtr)12; return; }             // HTTOP
                    if (bottom) { m.Result = (IntPtr)15; return; }          // HTBOTTOM
                }
                return;
            }

            base.WndProc(ref m);
        }

        public void SetLanguage(bool english)
        {
            L10n.IsEnglish = english;
            L10n.SaveConfig();
            ApplyLanguage();
        }

        private void ApplyLanguage()
        {
            if (titleBar != null) titleBar.SetLanguage(L10n.IsEnglish);

            if (cardMode != null) cardMode.Title = L10n.T("ÇALIŞMA MODU (TOPOLOJİ)", "OPERATING MODE (TOPOLOGY)");
            if (modeControl != null)
            {
                modeControl.UpdateLabels(
                    L10n.T("🖥️ Genişlet (2. Ekran)", "🖥️ Extend (2nd Screen)"),
                    L10n.T("🔄 Yinele (Ayna)", "🔄 Duplicate (Mirror)")
                );
            }

            if (cardScreens != null) cardScreens.Title = L10n.T("YANSITILACAK MONİTÖR", "TARGET DISPLAY");
            if (btnRefresh != null) btnRefresh.Text = L10n.T("🔄 Yenile", "🔄 Refresh");
            if (screenSelector != null) screenSelector.UpdateLanguageTags();

            if (cardVirtual != null)
            {
                cardVirtual.Title = L10n.T("SANAL 3. EKRAN YÖNETİMİ (VDD)", "VIRTUAL DISPLAY MANAGEMENT (VDD)");
                if (lblVddHint != null)
                {
                    lblVddHint.Text = L10n.T("Bağlı cihaza özel 1:1 piksel netliğinde sanal ekran sağlar.", "Provides a 1:1 pixel-perfect virtual external display.");
                }
                if (btnVirtualInfo != null)
                {
                    btnVirtualInfo.Text = L10n.T("ℹ️ Bilgi", "ℹ️ Info");
                    if (vddToolTip != null)
                    {
                        vddToolTip.SetToolTip(btnVirtualInfo, L10n.T("Sanal ekran konumu Windows Görüntü Ayarları'ndan düzenlenebilir. (Açmak için tıklayın)", "Virtual display position can be arranged in Windows Display Settings. (Click to open)"));
                    }
                }
            }
            if (btnAddVirtual != null) btnAddVirtual.Text = L10n.T("➕ Sanal Ekran Ayarla", "➕ Setup Virtual Display");
            if (btnRemoveVirtual != null) btnRemoveVirtual.Text = L10n.T("🗑️ Sürücüyü Kaldır", "🗑️ Uninstall Driver");

            if (cardAudio != null) cardAudio.Title = L10n.T("SES YAYINI (MOBIDESK AUDIO)", "AUDIO STREAMING (MOBIDESK AUDIO)");
            if (btnAudioInfo != null) btnAudioInfo.Text = L10n.T("ℹ️ Bilgi", "ℹ️ Info");
            if (swEnableAudio != null)
            {
                swEnableAudio.UpdateText(
                    L10n.T("Sesi Tablete İlet", "Stream Audio to Tablet"),
                    L10n.T("Windows sesini USB üzerinden tablete aktarır", "Streams Windows PC audio to tablet via USB/ADB")
                );
            }
            if (btnAddAudio != null) btnAddAudio.Text = "➕ " + L10n.T("Sanal Ses Ayarla", "Setup Virtual Audio");
            if (btnRemoveAudio != null) btnRemoveAudio.Text = "🗑️ " + L10n.T("Sürücüyü Kaldır", "Remove Driver");

            if (cardPerf != null) cardPerf.Title = L10n.T("AKIŞ VE HIZ LİMİTİ", "STREAM & SPEED LIMITS");
            if (lblFpsTitle != null) lblFpsTitle.Text = L10n.T("Hedef Kare Hızı", "Target Frame Rate");
            if (lblQualityTitle != null) lblQualityTitle.Text = L10n.T("Görüntü Netliği Kalite", "Image Clarity (Quality)");
            if (tbQuality != null) UpdateQuality(tbQuality.Value);

            if (cardOpts != null) cardOpts.Title = L10n.T("SİSTEM VE OPTİMİZASYONLAR", "SYSTEM & OPTIMIZATIONS");
            if (swAdaptiveFps != null)
            {
                swAdaptiveFps.UpdateText(
                    L10n.T("⚡ Akıllı 60 FPS Performans Modu", "⚡ Smart 60 FPS Performance Mode"),
                    L10n.T("Yüksek pikselli cihazları 60 FPS'e dengeler (Kapalıyken 1:1)", "Balances high-res devices for 60 FPS (1:1 when off)")
                );
            }
            if (swShowFps != null)
            {
                swShowFps.UpdateText(
                    L10n.T("Cihazda Canlı FPS Sayacı Göster", "Show Live FPS Counter on Device"),
                    L10n.T("Sol üst köşede yeşil donanım OSD sayacı gösterir", "Displays green on-screen OSD telemetry counter")
                );
            }
            if (swBatterySaver != null)
            {
                swBatterySaver.UpdateText(
                    L10n.T("Akıllı Güç ve Veri Tasarrufu", "Smart Power & Data Saver"),
                    L10n.T("Statik ekranda gereksiz veri aktarımını durdurur", "Pauses transmission when screen is static")
                );
            }
            if (swStartup != null)
            {
                swStartup.UpdateText(
                    "🪟 " + L10n.T("Windows ile Birlikte Başlat", "Start with Windows"),
                    L10n.T("PC açılışında arka planda sessizce başlar, bildirimleri yakalar", "Starts silently in tray on boot to capture notifications")
                );
            }

            if (btnStartStop != null)
            {
                if (isStreaming)
                {
                    btnStartStop.Text = "⏹    " + L10n.T("EKRAN PAYLAŞIMINI DURDUR", "STOP SCREEN STREAMING");
                }
                else
                {
                    btnStartStop.Text = "▶    " + L10n.T("EKRAN PAYLAŞIMINI BAŞLAT", "START SCREEN STREAMING");
                }
            }

            if (badgeLatency != null) badgeLatency.Label = L10n.T("⏱ Gecikme", "⏱ Latency");
            if (badgeSize != null) badgeSize.Label = L10n.T("📦 Boyut", "📦 Size");
            if (badgeBandwidth != null) badgeBandwidth.Label = L10n.T("📶 Veri", "📶 Data");
            if (badgeFps != null) badgeFps.Label = "⚡ FPS";

            if (!isStreaming)
            {
                if (!string.IsNullOrEmpty(activeTargetSerial))
                {
                    UpdateStatus(
                        L10n.T("HAZIR", "READY"),
                        L10n.T("Cihaz algılandı. Yayını başlatmak için aşağıdaki butona tıklayın.", "Device detected. Click the button below to start streaming."),
                        ColorSuccess
                    );
                }
                else
                {
                    UpdateStatus(
                        L10n.T("HAZIR", "READY"),
                        L10n.T("Yansıtma için aşağıdaki 'Yayını Başlat' butonuna tıklayın.", "Click 'Start Screen Streaming' button below to begin."),
                        ColorSuccess
                    );
                }
            }

            if (heroBanner != null)
            {
                if (clientTargetWidth <= 0 || clientTargetHeight <= 0)
                {
                    heroBanner.SetDevice(
                        L10n.T("USB Cihazı Aranıyor...", "Searching for USB Device..."),
                        L10n.T("USB kablosunu bağlayın • USB Hata Ayıklama açık olmalıdır", "Connect USB cable • USB Debugging must be enabled")
                    );
                    heroBanner.SetStatus(L10n.T("ARANIYOR", "SEARCHING"), ColorWarning);
                }
                else
                {
                    UpdateDeviceSpecsDisplay();
                }
            }

            UpdateTrayMenuStrings();

            if (tabSelector != null) tabSelector.UpdateLanguage();
            if (pnlFlow != null) pnlFlow.ApplyLanguage();
            if (pnlNotifications != null) pnlNotifications.ApplyLanguage();
        }

        // ═══════════════════════════════════════════════
        // EKRAN YÖNETİMİ VE MOD SEÇİMİ
        // ═══════════════════════════════════════════════
        // MONİTÖR YÖNETİMİ VE EKRAN LİSTESİ
        // ═══════════════════════════════════════════════
        private void RefreshScreenList()
        {
            Screen[] screens = Screen.AllScreens;
            List<MonitorItemData> list = new List<MonitorItemData>();
            bool isExtend = (modeControl != null) ? modeControl.IsExtend : AppConfig.IsExtendMode;

            int defaultIndex = -1;
            int virtualIndex = -1;
            int primaryIndex = -1;
            int secondaryIndex = -1;

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
                    tag = L10n.T("★ SANAL MOBIDESK", "★ VIRTUAL MOBIDESK");
                    if (virtualIndex < 0) virtualIndex = i;
                }
                else if (s.Primary)
                {
                    tag = L10n.T("ANA MONİTÖR", "PRIMARY MONITOR");
                    if (primaryIndex < 0) primaryIndex = i;
                }
                else
                {
                    tag = string.Format(L10n.T("MONİTÖR {0}", "MONITOR {0}"), i + 1);
                    if (secondaryIndex < 0) secondaryIndex = i;
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

            // Seçim Belirleme Mantığı:
            if (isExtend)
            {
                // Genişlet (2. Ekran) Modunda: Öncelikle varsa sanal ekranı seç
                if (virtualIndex >= 0)
                {
                    defaultIndex = virtualIndex;
                }
                else if (secondaryIndex >= 0)
                {
                    defaultIndex = secondaryIndex;
                }
                else
                {
                    defaultIndex = (primaryIndex >= 0) ? primaryIndex : 0;
                }
            }
            else
            {
                // Yinele (Ayna) Modunda: Ana monitörü seç
                if (primaryIndex >= 0)
                {
                    defaultIndex = primaryIndex;
                }
                else
                {
                    defaultIndex = 0;
                }
            }

            if (defaultIndex < 0 || defaultIndex >= screens.Length)
            {
                defaultIndex = 0;
            }

            if (screenSelector != null)
            {
                screenSelector.SetMonitors(list, defaultIndex);
            }
        }

        private void AutoSelectScreen(bool extendMode)
        {
            Screen[] screens = Screen.AllScreens;
            if (screens.Length == 0 || screenSelector == null) return;

            if (!extendMode)
            {
                // Yinele (Ayna) Modu: Ana ekranı seç
                for (int i = 0; i < screens.Length; i++)
                {
                    if (screens[i].Primary)
                    {
                        screenSelector.SelectedIndex = i;
                        return;
                    }
                }
                screenSelector.SelectedIndex = 0;
            }
            else
            {
                // Genişlet (2. Ekran) Modu:
                // 1. Öncelik: Sanal Ekran (MobiDesk VDD)
                for (int i = 0; i < screens.Length; i++)
                {
                    if (IsVirtualDisplay(screens[i].DeviceName))
                    {
                        screenSelector.SelectedIndex = i;
                        return;
                    }
                }
                // 2. Öncelik: Varsa ikinci fiziksel ekran
                for (int i = 0; i < screens.Length; i++)
                {
                    if (!screens[i].Primary)
                    {
                        screenSelector.SelectedIndex = i;
                        return;
                    }
                }
                // 3. Son çare
                screenSelector.SelectedIndex = 0;
            }
        }

        private void InitializeTrayIcon()
        {
            try
            {
                trayMenu = new ContextMenuStrip();
                trayMenu.Renderer = new DarkMenuRenderer();
                trayMenu.ShowImageMargin = false;
                trayMenu.ShowCheckMargin = false;
                trayMenu.BackColor = Color.FromArgb(17, 24, 39);
                trayMenu.ForeColor = Color.FromArgb(248, 250, 252);
                trayMenu.Font = new Font("Segoe UI", 9f);

                trayItemShow = new ToolStripMenuItem();
                trayItemShow.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
                trayItemShow.ForeColor = Color.White;
                trayItemShow.Click += delegate(object s, EventArgs e) { RestoreFromTray(); };

                trayItemToggleStream = new ToolStripMenuItem();
                trayItemToggleStream.ForeColor = Color.FromArgb(226, 232, 240);
                trayItemToggleStream.Click += delegate(object s, EventArgs e)
                {
                    if (isStreaming) StopStreaming();
                    else StartStreaming();
                };

                traySeparator1 = new ToolStripSeparator();

                trayItemExit = new ToolStripMenuItem();
                trayItemExit.ForeColor = Color.FromArgb(248, 113, 113);
                trayItemExit.Click += delegate(object s, EventArgs e)
                {
                    reallyExit = true;
                    this.Close();
                };

                trayMenu.Items.Add(trayItemShow);
                trayMenu.Items.Add(trayItemToggleStream);
                trayMenu.Items.Add(traySeparator1);
                trayMenu.Items.Add(trayItemExit);

                trayIcon = new NotifyIcon();
                trayIcon.Icon = this.Icon ?? Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                trayIcon.ContextMenuStrip = trayMenu;
                trayIcon.Visible = true;

                trayIcon.MouseClick += delegate(object s, MouseEventArgs e)
                {
                    if (e.Button == MouseButtons.Left)
                    {
                        RestoreFromTray();
                    }
                };

                trayIcon.DoubleClick += delegate(object s, EventArgs e)
                {
                    RestoreFromTray();
                };

                trayIcon.BalloonTipClicked += delegate(object s, EventArgs e)
                {
                    RestoreFromTray();
                    SwitchStudioTab(2);
                };

                UpdateTrayMenuStrings();
            }
            catch (Exception ex)
            {
                Program.Log("InitializeTrayIcon error: " + ex);
            }
        }

        public void RestoreFromTray()
        {
            if (this.InvokeRequired)
            {
                try { this.BeginInvoke(new Action(RestoreFromTray)); } catch { }
                return;
            }
            hasShownInitial = true;
            startMinimized = false;
            this.ShowInTaskbar = true;
            this.WindowState = FormWindowState.Normal;
            if (!this.Visible)
            {
                this.Visible = true;
                this.Show();
            }
            this.ShowInTaskbar = true;
            try
            {
                ShowWindow(this.Handle, 9 /* SW_RESTORE */);
                ShowWindow(this.Handle, 5 /* SW_SHOW */);
                ForceForegroundWindow(this.Handle);
            }
            catch { }
            this.Activate();
            this.BringToFront();
            LayoutStudioUI();
            this.Invalidate(true);
        }

        private void UpdateTrayMenuStrings()
        {
            if (this.InvokeRequired)
            {
                try { this.BeginInvoke(new Action(UpdateTrayMenuStrings)); } catch { }
                return;
            }

            if (trayItemShow != null)
            {
                trayItemShow.Text = L10n.T("  MobiDesk Studio'yu Göster", "  Show MobiDesk Studio");
            }
            if (trayItemToggleStream != null)
            {
                trayItemToggleStream.Text = isStreaming
                    ? L10n.T("⏹  Yayını Durdur", "⏹  Stop Streaming")
                    : L10n.T("▶  Yayını Başlat", "▶  Start Streaming");
            }
            if (trayItemExit != null)
            {
                trayItemExit.Text = L10n.T("❌  Çıkış", "❌  Exit");
            }
            if (trayIcon != null)
            {
                string statusText = isStreaming ? L10n.T("Canlı Yayında", "Streaming Live") : L10n.T("Hazır", "Ready");
                string tip = "MobiDesk Studio - " + statusText;
                if (tip.Length > 63) tip = tip.Substring(0, 63);
                trayIcon.Text = tip;
            }
        }

        private void ShowTrayNotificationOnce()
        {
            if (trayIcon != null && !hasShownTrayBalloon)
            {
                hasShownTrayBalloon = true;
                trayIcon.ShowBalloonTip(
                    2500,
                    "MobiDesk Studio",
                    L10n.T(
                        "MobiDesk arka planda çalışmaya devam ediyor.\nSimgeye tıklayarak açabilir veya sağ tıklayarak kapatabilirsiniz.",
                        "MobiDesk is running in the background.\nClick the icon to open or right-click to exit."
                    ),
                    ToolTipIcon.Info
                );
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                if (this.WindowState == FormWindowState.Normal)
                {
                    AppConfig.WindowWidth = this.ClientSize.Width;
                    AppConfig.WindowHeight = this.ClientSize.Height;
                }
                AppConfig.Fps = targetFps;
                AppConfig.Quality = targetQuality;
                AppConfig.AdaptiveFps = enableAdaptiveFps;
                AppConfig.ShowFps = showFpsOnTablet;
                AppConfig.BatterySaver = enableBatterySaver;
                if (modeControl != null) AppConfig.IsExtendMode = modeControl.IsExtend;
                if (!string.IsNullOrEmpty(selectedDeviceName)) AppConfig.LastMonitorDevice = selectedDeviceName;
                AppConfig.Save();
            }
            catch { }

            // Yalnızca aktif yayın yapılıyorsa VEYA Windows başlangıç modu açıksa arka planda (tepside) çalışmaya devam et
            if (!reallyExit && e.CloseReason == CloseReason.UserClosing && (isStreaming || WindowsStartupManager.IsStartupEnabled()))
            {
                e.Cancel = true;
                this.Hide();
                ShowTrayNotificationOnce();
                return;
            }

            isStreaming = false;
            try { if (AppWakeupEvent != null) { AppWakeupEvent.Set(); } } catch { }
            try { if (trayIcon != null) { trayIcon.Visible = false; trayIcon.Dispose(); } } catch { }
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
            try { if (flowManager != null) flowManager.Stop(); } catch { }
            try { timeEndPeriod(1); } catch { }
        }

        private void SwitchStudioTab(int index)
        {
            currentTabIndex = index;
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(delegate { SwitchStudioTab(index); }));
                return;
            }

            bool isScreenTab = (index == 0);
            bool isFlowTab = (index == 1);
            bool isNotifTab = (index == 2);

            if (pnlLeftCol != null) pnlLeftCol.Visible = isScreenTab;
            if (pnlRightCol != null) pnlRightCol.Visible = isScreenTab;
            if (btnStartStop != null) btnStartStop.Visible = isScreenTab;
            if (pnlBottomBar != null) pnlBottomBar.Visible = isScreenTab;

            if (pnlFlow != null)
            {
                pnlFlow.Visible = isFlowTab;
                if (isFlowTab)
                {
                    pnlFlow.BringToFront();
                    pnlFlow.UpdateLayout();
                    pnlFlow.UpdateStatusDisplay();
                }
            }
            if (pnlNotifications != null)
            {
                pnlNotifications.Visible = isNotifTab;
                if (isNotifTab)
                {
                    pnlNotifications.BringToFront();
                    pnlNotifications.UpdateLayout();
                    pnlNotifications.UpdateCount();
                    pnlNotifications.UpdateStatusDisplay();
                }
            }

            if (isNotifTab && tabSelector != null)
            {
                tabSelector.NotificationBadge = 0;
            }
            if (isFlowTab && tabSelector != null)
            {
                tabSelector.FlowBadge = 0;
            }

            this.Invalidate(true);
        }

        private void SafeInvoke(Action act)
        {
            try
            {
                if (this.IsDisposed) return;
                if (!this.IsHandleCreated)
                {
                    this.HandleCreated += delegate { SafeInvoke(act); };
                    return;
                }
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(act);
                }
                else
                {
                    act();
                }
            }
            catch { }
        }

        private void FlowManager_MessageReceived(FlowMessageItem msg)
        {
            SafeInvoke(delegate
            {
                if (pnlFlow != null) pnlFlow.AddMessage(msg);
                if (currentTabIndex != 1 && tabSelector != null) tabSelector.FlowBadge++;
            });
        }

        private void FlowManager_FileProgress(FlowMessageItem msg)
        {
            SafeInvoke(delegate
            {
                if (pnlFlow != null) pnlFlow.UpdateFileProgress(msg);
            });
        }

        private void FlowManager_NotificationReceived(NotificationCardItem item)
        {
            SafeInvoke(delegate
            {
                if (pnlNotifications != null) pnlNotifications.AddNotification(item);
                if (currentTabIndex != 2 && tabSelector != null) tabSelector.NotificationBadge++;

                // Masaüstü bildirim balonu (Pencere odakta değilse veya tepsideyse göster)
                bool isForeground = (this.Visible && this.WindowState != FormWindowState.Minimized && Form.ActiveForm == this);
                if (!isForeground && trayIcon != null && item != null)
                {
                    try
                    {
                        string notifTitle = string.IsNullOrEmpty(item.AppName) ? item.Title : (item.AppName + " • " + item.Title);
                        if (string.IsNullOrEmpty(notifTitle)) notifTitle = "MobiDesk Bildirim";
                        string notifBody = string.IsNullOrEmpty(item.Text) ? "Yeni bildirim alındı" : item.Text;
                        if (notifBody.Length > 200) notifBody = notifBody.Substring(0, 197) + "...";
                        trayIcon.ShowBalloonTip(4000, notifTitle, notifBody, ToolTipIcon.Info);
                    }
                    catch { }
                }
            });
        }

        private void FlowManager_NotificationRemoved(string key, string serial)
        {
            SafeInvoke(delegate
            {
                if (pnlNotifications != null) pnlNotifications.RemoveNotification(key);
            });
        }

        private void FlowManager_DeviceConnectionChanged(string serial, bool connected)
        {
            SafeInvoke(delegate
            {
                if (pnlFlow != null) pnlFlow.UpdateDeviceConnection(serial, connected);
                if (pnlNotifications != null) pnlNotifications.UpdateDeviceConnection(serial, connected);
            });
        }

        private void FlowManager_ConnectionChanged(bool connected)
        {
            SafeInvoke(delegate
            {
                if (pnlFlow != null)
                {
                    if (string.IsNullOrEmpty(pnlFlow.CurrentSelectedSerial))
                        pnlFlow.SetConnected(connected);
                    else
                        pnlFlow.SetConnected(flowManager != null && flowManager.IsConnected(pnlFlow.CurrentSelectedSerial));
                }
                if (pnlNotifications != null)
                {
                    if (string.IsNullOrEmpty(pnlNotifications.currentFilterSerial))
                        pnlNotifications.SetConnected(connected);
                    else
                        pnlNotifications.SetConnected(flowManager != null && flowManager.IsConnected(pnlNotifications.currentFilterSerial));
                }
            });
        }

        private void OnConnectedDevicesChanged(List<ConnectedDeviceItem> devices)
        {
            SafeInvoke(delegate
            {
                if (flowManager != null) flowManager.UpdateDevices(devices);
                if (pnlFlow != null) pnlFlow.UpdateDeviceList(devices);
                if (pnlNotifications != null) pnlNotifications.UpdateDeviceList(devices);
            });
        }
    }

}