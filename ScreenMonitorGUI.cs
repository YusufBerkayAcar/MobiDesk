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

[assembly: AssemblyTitle("MobiDesk")]
[assembly: AssemblyDescription("MobiDesk - High-Performance Display & Mobile Continuity Ecosystem")]
[assembly: AssemblyProduct("MobiDesk")]
[assembly: AssemblyCompany("MobiDesk")]
[assembly: AssemblyCopyright("Copyright © 2026")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

namespace ScreenMonitoringApp
{
    static class Program
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private const int ASFW_ANY = -1;

        private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);

        private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

        private static Mutex appMutex = null;
        private static EventWaitHandle wakeupEvent = null;

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

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_POWER_THROTTLING_STATE
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        private const int ProcessPowerThrottling = 4;
        private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
        private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessInformation(
            IntPtr hProcess,
            int processInformationClass,
            ref PROCESS_POWER_THROTTLING_STATE processInformation,
            uint processInformationSize);

        public static void DisableProcessPowerThrottling()
        {
            try
            {
                PROCESS_POWER_THROTTLING_STATE state = new PROCESS_POWER_THROTTLING_STATE();
                state.Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION;
                state.ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED;
                state.StateMask = 0;
                SetProcessInformation(Process.GetCurrentProcess().Handle, ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf(state));
            }
            catch { }
        }

        public static bool MockTestNotifications = false;

        [STAThread]
        static void Main(string[] args)
        {
            Log("App starting (Redesigned Studio UI v2)");
            try
            {
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
                DisableProcessPowerThrottling();
            }
            catch { }

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
                Log("Step 1: DPI aware");
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                bool autoStart = false;
                bool startMinimized = false;
                string snapshotPath = null;
                bool closeAfterSnapshot = false;
                int initialTab = 0;
                bool runTests = false;
                if (args != null)
                {
                    for (int i = 0; i < args.Length; i++)
                    {
                        if (string.Equals(args[i], "--test", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(args[i], "--tests", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(args[i], "--run-tests", StringComparison.OrdinalIgnoreCase)) runTests = true;
                        if (string.Equals(args[i], "--autostart", StringComparison.OrdinalIgnoreCase)) autoStart = true;
                        if (string.Equals(args[i], "--minimized", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(args[i], "--tray", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(args[i], "--startup", StringComparison.OrdinalIgnoreCase)) startMinimized = true;
                        if (string.Equals(args[i], "--snapshot", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) snapshotPath = args[i + 1];
                        if (string.Equals(args[i], "--closeAfterSnapshot", StringComparison.OrdinalIgnoreCase)) closeAfterSnapshot = true;
                        if (string.Equals(args[i], "--tab", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) int.TryParse(args[i + 1], out initialTab);
                        if (string.Equals(args[i], "--device", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) MainForm.activeTargetSerial = args[i + 1];
                        if (string.Equals(args[i], "--mock", StringComparison.OrdinalIgnoreCase)) MockTestNotifications = true;
                    }
                }

                if (runTests)
                {
                    int testCode = MobiTestSuite.RunAll();
                    Environment.Exit(testCode);
                    return;
                }

                bool isSnapshot = !string.IsNullOrEmpty(snapshotPath);
                const string MUTEX_NAME = @"Local\MobiDesk_SingleInstance_Mutex_v1";
                const string EVENT_NAME = @"Local\MobiDesk_Wakeup_Event_v1";
                const string MSG_NAME = "MOBIDESK_SHOW_INSTANCE_MESSAGE";

                Process cur = Process.GetCurrentProcess();
                List<Process> othersList = new List<Process>();
                try
                {
                    foreach (Process p in Process.GetProcessesByName("MobiDesk"))
                    {
                        if (p.Id != cur.Id) othersList.Add(p);
                    }
                }
                catch { }
                try
                {
                    if (!cur.ProcessName.Equals("MobiDesk", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (Process p in Process.GetProcessesByName(cur.ProcessName))
                        {
                            if (p.Id != cur.Id)
                            {
                                bool exists = false;
                                for (int i = 0; i < othersList.Count; i++)
                                {
                                    if (othersList[i].Id == p.Id) { exists = true; break; }
                                }
                                if (!exists) othersList.Add(p);
                            }
                        }
                    }
                }
                catch { }

                bool createdNewMutex = false;
                if (!isSnapshot)
                {
                    try
                    {
                        appMutex = new Mutex(true, MUTEX_NAME, out createdNewMutex);
                        if (!createdNewMutex)
                        {
                            try
                            {
                                if (appMutex.WaitOne(0, false))
                                {
                                    createdNewMutex = true;
                                }
                            }
                            catch (AbandonedMutexException)
                            {
                                createdNewMutex = true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log("Mutex check exception: " + ex.Message);
                    }

                    if (!createdNewMutex || othersList.Count > 0)
                    {
                        Log("MobiDesk is already running (Mutex=" + createdNewMutex + ", OtherProcs=" + othersList.Count + "). Signaling existing instance to restore window...");
                        try { AllowSetForegroundWindow(ASFW_ANY); } catch { }

                        foreach (Process p in othersList)
                        {
                            try { AllowSetForegroundWindow(p.Id); } catch { }
                        }

                        // 1. İsimlendirilmiş uyanma sinyalini gönder (Wakeup Event)
                        try
                        {
                            using (EventWaitHandle existingEvent = EventWaitHandle.OpenExisting(EVENT_NAME))
                            {
                                existingEvent.Set();
                                Log("Wakeup event signaled.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log("OpenExisting EventWaitHandle exception: " + ex.Message);
                        }

                        // 2. Kayıtlı pencere mesajını yayınla (Broadcast Window Message)
                        try
                        {
                            int msgId = RegisterWindowMessage(MSG_NAME);
                            if (msgId != 0)
                            {
                                PostMessage(HWND_BROADCAST, msgId, IntPtr.Zero, IntPtr.Zero);
                                Log("Broadcast window message sent.");
                            }
                        }
                        catch { }

                        // 3. Win32 EnumWindows ile mevcut sürecin gizli/küçültülmüş pencerelerini bulup doğrudan geri yükle
                        try
                        {
                            EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
                            {
                                uint procId;
                                GetWindowThreadProcessId(hWnd, out procId);
                                if (procId != (uint)cur.Id)
                                {
                                    foreach (Process p in othersList)
                                    {
                                        if (p.Id == (int)procId)
                                        {
                                            StringBuilder sb = new StringBuilder(256);
                                            GetWindowText(hWnd, sb, 256);
                                            string wTitle = sb.ToString();
                                            if (!string.IsNullOrEmpty(wTitle) && wTitle.Contains("MobiDesk"))
                                            {
                                                ShowWindow(hWnd, 9 /* SW_RESTORE */);
                                                ShowWindow(hWnd, 5 /* SW_SHOW */);
                                                SetForegroundWindow(hWnd);
                                                Log("Restored window handle " + hWnd + " for PID " + p.Id);
                                            }
                                        }
                                    }
                                }
                                return true;
                            }, IntPtr.Zero);
                        }
                        catch (Exception ex)
                        {
                            Log("EnumWindows restore exception: " + ex.Message);
                        }

                        // 4. Mevcut sürecin yanıt verip vermediğini denetle
                        bool anyAlive = false;
                        foreach (Process p in othersList)
                        {
                            try
                            {
                                if (p.Responding)
                                {
                                    anyAlive = true;
                                    Log("Existing active MobiDesk instance (PID " + p.Id + ") is responsive.");
                                }
                                else
                                {
                                    Log("Existing MobiDesk process (PID " + p.Id + ") is NOT responding.");
                                }
                            }
                            catch { }
                        }

                        if (anyAlive)
                        {
                            Log("Existing active MobiDesk instance signaled. Exiting launcher instance.");
                            return;
                        }

                        // Yalnızca süreçler gerçekten kilitlenmiş/yanıt vermiyorsa zombi temizliği yap
                        if (othersList.Count > 0)
                        {
                            Log("Existing processes were unresponsive. Terminating zombie processes and launching fresh instance...");
                            foreach (Process p in othersList)
                            {
                                try { p.Kill(); p.WaitForExit(1000); } catch { }
                            }
                        }
                    }

                    try
                    {
                        wakeupEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EVENT_NAME);
                        MainForm.AppWakeupEvent = wakeupEvent;
                    }
                    catch (Exception ex)
                    {
                        Log("Create EventWaitHandle exception: " + ex.Message);
                    }
                }

                try
                {
                    Log("Starting MainForm autoStart=" + autoStart + ", snapshot=" + (snapshotPath ?? "none") + ", tab=" + initialTab + ", minimized=" + startMinimized);
                    Application.Run(new MainForm(autoStart, snapshotPath, closeAfterSnapshot, initialTab, startMinimized));
                    Log("Application.Run exited normally");
                }
                finally
                {
                    if (wakeupEvent != null)
                    {
                        try { wakeupEvent.Close(); } catch { }
                    }
                    if (appMutex != null && createdNewMutex)
                    {
                        try
                        {
                            appMutex.ReleaseMutex();
                            appMutex.Close();
                        }
                        catch { }
                    }
                }
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
        // SINGLE INSTANCE & FOREGROUND WAKEUP API'LERİ
        // ═══════════════════════════════════════════════
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

        public const int SW_RESTORE = 9;

        public static void ForceForegroundWindow(IntPtr hWnd)
        {
            try
            {
                ShowWindow(hWnd, SW_RESTORE);
                IntPtr foreHwnd = GetForegroundWindow();
                uint foreThread = GetWindowThreadProcessId(foreHwnd, IntPtr.Zero);
                uint curThread = GetCurrentThreadId();

                if (foreThread != curThread && foreThread != 0)
                {
                    AttachThreadInput(curThread, foreThread, true);
                    BringWindowToTop(hWnd);
                    SetForegroundWindow(hWnd);
                    AttachThreadInput(curThread, foreThread, false);
                }
                else
                {
                    BringWindowToTop(hWnd);
                    SetForegroundWindow(hWnd);
                }
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

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

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
        private ModernCard cardScreens;
        private ModernMonitorSelector screenSelector;
        private ModernButton btnRefresh;
        private ModernCard cardVirtual;
        private ModernButton btnAddVirtual;
        private ModernButton btnRemoveVirtual;
        private ModernButton btnVirtualInfo;
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

        public MainForm(bool autoStart = false, string snapshotPath = null, bool closeAfterSnapshot = false, int initialTab = 0, bool startMinimized = false)
        {
            this.startMinimized = startMinimized;
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

        private void InitializeStudioUI()
        {
            this.Text = "MobiDesk Studio";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = true;
            this.BackColor = ColorBg;
            this.ForeColor = ColorText;
            this.Font = new Font("Segoe UI", 9.0f, FontStyle.Regular);

            int windowW = Math.Max(860, AppConfig.WindowWidth);
            int windowH = Math.Max(680, AppConfig.WindowHeight);
            this.MinimumSize = new Size(860, 680);
            this.ClientSize = new Size(windowW, windowH);

            // 1. MODERN ENTEGRE BAŞLIK ÇUBUĞU
            titleBar = new ModernTitleBar(this, "MobiDesk Studio", "BETA");
            titleBar.Location = new Point(0, 0);
            titleBar.Size = new Size(windowW, 42);
            titleBar.LanguageChanged += delegate(bool english)
            {
                SetLanguage(english);
            };
            this.Controls.Add(titleBar);

            // 2. HERO CİHAZ BİLGİ KARTI (DİNAMİK USB ALGILAMA)
            heroBanner = new TabletHeroBanner(
                L10n.T("USB Cihazı Aranıyor...", "Searching for USB Device..."),
                L10n.T("USB kablosunu bağlayın • USB Hata Ayıklama açık olmalıdır", "Connect USB cable • USB Debugging must be enabled"),
                L10n.T("ARANIYOR", "SEARCHING"),
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

            // 2.5. MODERN SEKME ÇUBUĞU (EKRAN / FLOW / BİLDİRİMLER)
            tabSelector = new ModernStudioTabs(18, 116, windowW - 36, 36);
            tabSelector.SelectedTabChanged += delegate(int tabIndex)
            {
                SwitchStudioTab(tabIndex);
            };
            this.Controls.Add(tabSelector);

            int colY = 158;
            int bottomSpace = 118;
            int colH = Math.Max(396, windowH - colY - bottomSpace);
            int colW = Math.Max(350, (windowW - 36 - 16) / 2);

            // ═══════════════════════════════════════════════
            // SOL PANEL: EKRAN VE YAYIN MODU
            // ═══════════════════════════════════════════════
            pnlLeftCol = new DoubleBufferedPanel
            {
                Location = new Point(18, colY),
                Size = new Size(colW, colH),
                BackColor = ColorBg
            };

            // Sol Kart 1: Çalışma Modu (H=84)
            cardMode = new ModernCard("ÇALIŞMA MODU (TOPOLOJİ)", 0, 0, colW, 84);
            modeControl = new ModernSegmentedMode(
                "🖥️ Genişlet (2. Ekran)",
                "🔄 Yinele (Ayna)",
                12, 28, colW - 24, 44
            );
            modeControl.IsExtend = AppConfig.IsExtendMode;
            modeControl.ModeChanged += delegate(bool isExtend)
            {
                AppConfig.IsExtendMode = isExtend;
                AppConfig.Save();
                AutoSelectScreen(isExtend);
            };
            cardMode.Controls.Add(modeControl);
            pnlLeftCol.Controls.Add(cardMode);

            // Sol Kart 3: Sanal Ekran Sürücüsü (VDD) (H=104, alta kilitli)
            int cardVirtH = 104;
            cardVirtual = new ModernCard("SANAL 3. EKRAN YÖNETİMİ (VDD)", 0, colH - cardVirtH, colW, cardVirtH);

            btnVirtualInfo = new ModernButton(L10n.T("ℹ️ Bilgi", "ℹ️ Info"), colW - 74, 7, 62, 22, Color.FromArgb(28, 36, 54), ColorAccent, 5);
            btnVirtualInfo.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            btnVirtualInfo.Click += BtnVirtualInfo_Click;
            vddToolTip = new ToolTip();
            vddToolTip.SetToolTip(btnVirtualInfo, L10n.T("Sanal ekran konumu Windows Görüntü Ayarları'ndan düzenlenebilir. (Açmak için tıklayın)", "Virtual display position can be arranged in Windows Display Settings. (Click to open)"));
            cardVirtual.Controls.Add(btnVirtualInfo);

            lblVddHint = new Label
            {
                Text = L10n.T("Bağlı cihaza özel 1:1 piksel netliğinde sanal ekran sağlar.", "Provides a 1:1 pixel-perfect virtual external display."),
                Font = new Font("Segoe UI", 7.5f),
                ForeColor = ColorTextSubtle,
                BackColor = ColorCard,
                Location = new Point(14, 28),
                Size = new Size(colW - 28, 18)
            };
            cardVirtual.Controls.Add(lblVddHint);

            int btnHalfW = (colW - 24 - 8) / 2;
            btnAddVirtual = new ModernButton("➕ Sanal Ekran Ayarla", 12, 48, btnHalfW, 42, ColorPrimary, Color.White, 8);
            btnAddVirtual.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            btnAddVirtual.Click += BtnAddVirtual_Click;

            btnRemoveVirtual = new ModernButton("🗑️ Sürücüyü Kaldır", 12 + btnHalfW + 8, 48, btnHalfW, 42, Color.FromArgb(32, 24, 32), Color.FromArgb(244, 114, 182), 8, Color.FromArgb(70, 30, 50));
            btnRemoveVirtual.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            btnRemoveVirtual.Click += BtnRemoveVirtual_Click;

            cardVirtual.Controls.Add(btnAddVirtual);
            cardVirtual.Controls.Add(btnRemoveVirtual);
            pnlLeftCol.Controls.Add(cardVirtual);

            // Sol Kart 2: Yansıtılacak Monitör Seçimi (Orta dinamik alan)
            int screensY = 94;
            int screensH = Math.Max(120, (colH - cardVirtH - 10) - screensY);
            cardScreens = new ModernCard("YANSITILACAK MONİTÖR", 0, screensY, colW, screensH);
            
            btnRefresh = new ModernButton("🔄 Yenile", colW - 86, 7, 74, 22, Color.FromArgb(28, 36, 54), ColorTextMuted, 5);
            btnRefresh.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            btnRefresh.Click += delegate(object s, EventArgs e) { RefreshScreenList(); };
            cardScreens.Controls.Add(btnRefresh);

            screenSelector = new ModernMonitorSelector(12, 34, colW - 24, Math.Max(60, screensH - 42));
            screenSelector.SelectionChanged += delegate(int index, Screen scr, bool isVirt)
            {
                selectedScreenIndex = index;
                if (scr != null)
                {
                    selectedDeviceName = scr.DeviceName;
                    AppConfig.LastMonitorDevice = selectedDeviceName;
                    AppConfig.Save();
                    Rectangle phys = GetPhysicalMonitorBounds(selectedDeviceName);
                    currentScreenBounds = (phys.Width > 0 && phys.Height > 0) ? phys : scr.Bounds;
                }
            };
            cardScreens.Controls.Add(screenSelector);
            pnlLeftCol.Controls.Add(cardScreens);

            this.Controls.Add(pnlLeftCol);

            // ═══════════════════════════════════════════════
            // SAĞ PANEL: PERFORMANS VE MOTOR AYARLARI
            // ═══════════════════════════════════════════════
            pnlRightCol = new DoubleBufferedPanel
            {
                Location = new Point(18 + colW + 16, colY),
                Size = new Size(colW, colH),
                BackColor = ColorBg
            };

            // Sağ Kart 1: Performans & Hız (H=172, Y=0)
            cardPerf = new ModernCard("AKIŞ VE HIZ LİMİTİ", 0, 0, colW, 172);

            // FPS Satırı
            lblFpsTitle = new Label
            {
                Text = "Hedef Kare Hızı",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = ColorTextMuted,
                BackColor = ColorCard,
                Location = new Point(12, 25),
                AutoSize = true
            };
            lblFpsVal = new Label
            {
                Text = "60 FPS",
                Font = new Font("Segoe UI", 9.0f, FontStyle.Bold),
                ForeColor = ColorSuccess,
                BackColor = ColorCard,
                Location = new Point(colW - 90, 24),
                Size = new Size(78, 18),
                TextAlign = ContentAlignment.MiddleRight
            };
            cardPerf.Controls.Add(lblFpsTitle);
            cardPerf.Controls.Add(lblFpsVal);

            tbFps = new ModernSlider(15, 75, AppConfig.Fps, ColorSuccess, Color.FromArgb(6, 182, 212))
            {
                Location = new Point(12, 45),
                Size = new Size(colW - 24, 18)
            };
            tbFps.ValueChanged += delegate(object s, EventArgs e) { UpdateFps(tbFps.Value); };
            tbFps.MouseUp += delegate(object s, MouseEventArgs e)
            {
                AppConfig.Fps = tbFps.Value;
                AppConfig.Save();
            };
            cardPerf.Controls.Add(tbFps);

            // FPS Hızlı Butonlar
            fpsChips.Clear();
            int[] fpsPresets = new int[] { 30, 45, 60, 75 };
            int chipW = (colW - 24 - 9) / 4;
            for (int i = 0; i < fpsPresets.Length; i++)
            {
                int val = fpsPresets[i];
                QuickChip chip = new QuickChip(val.ToString() + " FPS", 12 + i * (chipW + 3), 67, chipW, 22);
                chip.Click += delegate(object s, EventArgs e)
                {
                    tbFps.Value = val;
                    AppConfig.Fps = val;
                    AppConfig.Save();
                };
                cardPerf.Controls.Add(chip);
                fpsChips.Add(chip);
            }

            // Kalite Satırı
            lblQualityTitle = new Label
            {
                Text = "Görüntü Netliği Kalite",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = ColorTextMuted,
                BackColor = ColorCard,
                Location = new Point(12, 96),
                AutoSize = true
            };
            lblQualityVal = new Label
            {
                Text = "%65 (Dengeli)",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = ColorAccent,
                BackColor = ColorCard,
                Location = new Point(colW - 120, 95),
                Size = new Size(108, 18),
                TextAlign = ContentAlignment.MiddleRight
            };
            cardPerf.Controls.Add(lblQualityTitle);
            cardPerf.Controls.Add(lblQualityVal);

            tbQuality = new ModernSlider(15, 100, AppConfig.Quality, ColorPrimary, Color.FromArgb(168, 85, 247))
            {
                Location = new Point(12, 116),
                Size = new Size(colW - 24, 18)
            };
            tbQuality.ValueChanged += delegate(object s, EventArgs e) { UpdateQuality(tbQuality.Value); };
            tbQuality.MouseUp += delegate(object s, MouseEventArgs e)
            {
                AppConfig.Quality = tbQuality.Value;
                AppConfig.Save();
            };
            cardPerf.Controls.Add(tbQuality);

            // Kalite Hızlı Butonlar
            qualityChips.Clear();
            string[] qLabels = new string[] { "%40", "%65", "%85", "%100" };
            int[] qPresets = new int[] { 40, 65, 85, 100 };
            for (int i = 0; i < qPresets.Length; i++)
            {
                int val = qPresets[i];
                QuickChip chip = new QuickChip(qLabels[i], 12 + i * (chipW + 3), 138, chipW, 22);
                chip.Click += delegate(object s, EventArgs e)
                {
                    tbQuality.Value = val;
                    AppConfig.Quality = val;
                    AppConfig.Save();
                };
                cardPerf.Controls.Add(chip);
                qualityChips.Add(chip);
            }

            pnlRightCol.Controls.Add(cardPerf);

            // Sağ Kart 2: Sistem ve Motor Seçenekleri (H=214, Y=182)
            int optsY = 182;
            int optsH = Math.Max(214, colH - optsY);
            cardOpts = new ModernCard("SİSTEM VE OPTİMİZASYONLAR", 0, optsY, colW, optsH);

            swAdaptiveFps = new ModernSwitch(
                "⚡ " + L10n.T("Akıllı 60 FPS Performans Modu", "Smart 60 FPS Performance Mode"),
                L10n.T("Yüksek pikselli cihazları 60 FPS'e dengeler (Kapalıyken 1:1)", "Balances high-res devices for 60 FPS (1:1 when off)"),
                12, 26, colW - 24, 40
            );
            swAdaptiveFps.Checked = AppConfig.AdaptiveFps;
            swAdaptiveFps.CheckedChanged += delegate(object s, EventArgs e)
            {
                enableAdaptiveFps = swAdaptiveFps.Checked;
                AppConfig.AdaptiveFps = swAdaptiveFps.Checked;
                AppConfig.Save();
                UpdateDeviceSpecsDisplay();
            };
            cardOpts.Controls.Add(swAdaptiveFps);

            swShowFps = new ModernSwitch(
                L10n.T("Cihazda Canlı FPS Sayacı Göster", "Show Live FPS Counter on Device"),
                L10n.T("Sol üst köşede yeşil donanım OSD sayacı gösterir", "Displays green on-screen OSD telemetry counter"),
                12, 70, colW - 24, 40
            );
            swShowFps.Checked = AppConfig.ShowFps;
            swShowFps.CheckedChanged += delegate(object s, EventArgs e)
            {
                showFpsOnTablet = swShowFps.Checked;
                AppConfig.ShowFps = swShowFps.Checked;
                AppConfig.Save();
                pendingFpsConfig = true;
            };
            cardOpts.Controls.Add(swShowFps);

            swBatterySaver = new ModernSwitch(
                L10n.T("Akıllı Güç ve Veri Tasarrufu", "Smart Power & Data Saver"),
                L10n.T("Statik ekranda gereksiz veri aktarımını durdurur", "Pauses transmission when screen is static"),
                12, 114, colW - 24, 40
            );
            swBatterySaver.Checked = AppConfig.BatterySaver;
            swBatterySaver.CheckedChanged += delegate(object s, EventArgs e)
            {
                enableBatterySaver = swBatterySaver.Checked;
                AppConfig.BatterySaver = swBatterySaver.Checked;
                AppConfig.Save();
            };
            cardOpts.Controls.Add(swBatterySaver);

            swStartup = new ModernSwitch(
                "🪟 " + L10n.T("Windows ile Birlikte Başlat", "Start with Windows"),
                L10n.T("PC açılışında arka planda sessizce başlar, bildirimleri yakalar", "Starts silently in tray on boot to capture notifications"),
                12, 158, colW - 24, 40
            );
            swStartup.Checked = WindowsStartupManager.IsStartupEnabled();
            swStartup.CheckedChanged += delegate(object s, EventArgs e)
            {
                WindowsStartupManager.SetStartup(swStartup.Checked);
                if (pnlNotifications != null) pnlNotifications.UpdateStartupState();
            };
            cardOpts.Controls.Add(swStartup);

            pnlRightCol.Controls.Add(cardOpts);

            UpdateFps(AppConfig.Fps);
            UpdateQuality(AppConfig.Quality);

            this.Controls.Add(pnlRightCol);

            // ═══════════════════════════════════════════════
            // ALT KOMUTA VE CANLI TELEMETRİ GÜVERTESİ
            // ═══════════════════════════════════════════════
            int bottomY = colY + colH + 10;

            // Büyük Aksiyon Butonu
            btnStartStop = new ModernButton(
                "▶    EKRAN PAYLAŞIMINI BAŞLAT",
                18, bottomY, windowW - 36, 46,
                ColorSuccess, Color.FromArgb(11, 15, 25), 10
            );
            btnStartStop.Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
            btnStartStop.Click += BtnStartStop_Click;
            this.Controls.Add(btnStartStop);

            // Canlı Telemetri Çubuğu
            pnlBottomBar = new RoundedPanel(windowW - 36, 46, 10, ColorCard, ColorCardBorder);
            pnlBottomBar.Location = new Point(18, bottomY + 52);

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

            // 5. MOBIFLOW VE BİLDİRİMLER PANELLERİ
            pnlFlow = new MobiFlowPanel(18, 158, windowW - 36, windowH - 158 - 18, flowManager);
            pnlFlow.Visible = false;
            this.Controls.Add(pnlFlow);

            pnlNotifications = new MobiNotificationsPanel(18, 158, windowW - 36, windowH - 158 - 18, flowManager);
            pnlNotifications.Visible = false;
            this.Controls.Add(pnlNotifications);

            // Yerleşim düzenini dinamik boyutlara hizala
            LayoutStudioUI();

            // Dinamik USB Cihaz İzleme Zamanlayıcısını Başlat
            deviceMonitorTimer = new System.Windows.Forms.Timer();
            deviceMonitorTimer.Interval = 2500;
            deviceMonitorTimer.Tick += delegate(object s, EventArgs e) { CheckConnectedUsbDevicesAsync(); };
            deviceMonitorTimer.Start();
            CheckConnectedUsbDevicesAsync();

            this.FormClosing += MainForm_FormClosing;
        }

        private void LayoutStudioUI()
        {
            if (titleBar == null || heroBanner == null || pnlLeftCol == null || pnlRightCol == null || btnStartStop == null || pnlBottomBar == null)
                return;

            this.SuspendLayout();
            pnlLeftCol.SuspendLayout();
            pnlRightCol.SuspendLayout();

            int w = this.ClientSize.Width;
            int h = this.ClientSize.Height;

            // 1. Başlık Çubuğu
            titleBar.Location = new Point(0, 0);
            titleBar.Size = new Size(w, 42);

            // 2. Hero Cihaz Bannerı
            heroBanner.Location = new Point(18, 52);
            heroBanner.Size = new Size(w - 36, 58);

            // 2.5 Sekme Çubuğu
            if (tabSelector != null)
            {
                tabSelector.Location = new Point(18, 116);
                tabSelector.Size = new Size(w - 36, 36);
            }

            // 3. İki Sütunlu Kart Izgarası
            int colY = 158;
            int bottomSpace = 118; // btnStartStop(46) + pnlBottomBar(46) + boşluklar
            int colH = Math.Max(396, h - colY - bottomSpace);
            int colW = Math.Max(350, (w - 36 - 16) / 2);

            // Sol Panel
            pnlLeftCol.Location = new Point(18, colY);
            pnlLeftCol.Size = new Size(colW, colH);

            if (cardMode != null)
            {
                cardMode.Location = new Point(0, 0);
                cardMode.Size = new Size(colW, 84);
                if (modeControl != null)
                {
                    modeControl.Location = new Point(12, 28);
                    modeControl.Size = new Size(colW - 24, 44);
                }
            }

            int cardVirtH = 104;
            if (cardVirtual != null)
            {
                cardVirtual.Location = new Point(0, colH - cardVirtH);
                cardVirtual.Size = new Size(colW, cardVirtH);
                if (btnVirtualInfo != null)
                {
                    btnVirtualInfo.Location = new Point(colW - 74, 7);
                }
                if (lblVddHint != null)
                {
                    lblVddHint.Size = new Size(colW - 28, 18);
                }
                int btnHalfW = (colW - 24 - 8) / 2;
                if (btnAddVirtual != null)
                {
                    btnAddVirtual.Location = new Point(12, 48);
                    btnAddVirtual.Size = new Size(btnHalfW, 42);
                }
                if (btnRemoveVirtual != null)
                {
                    btnRemoveVirtual.Location = new Point(12 + btnHalfW + 8, 48);
                    btnRemoveVirtual.Size = new Size(btnHalfW, 42);
                }
            }

            if (cardScreens != null)
            {
                int screensY = 94;
                int screensH = Math.Max(120, (colH - cardVirtH - 10) - screensY);
                cardScreens.Location = new Point(0, screensY);
                cardScreens.Size = new Size(colW, screensH);
                if (btnRefresh != null)
                {
                    btnRefresh.Location = new Point(colW - 86, 7);
                }
                if (screenSelector != null)
                {
                    screenSelector.Location = new Point(12, 34);
                    screenSelector.Size = new Size(colW - 24, Math.Max(60, screensH - 42));
                }
            }

            // Sağ Panel
            pnlRightCol.Location = new Point(18 + colW + 16, colY);
            pnlRightCol.Size = new Size(colW, colH);

            // Flow & Bildirim Panelleri (Tam Boyut)
            if (pnlFlow != null)
            {
                pnlFlow.Location = new Point(18, colY);
                pnlFlow.Size = new Size(w - 36, h - colY - 18);
            }
            if (pnlNotifications != null)
            {
                pnlNotifications.Location = new Point(18, colY);
                pnlNotifications.Size = new Size(w - 36, h - colY - 18);
            }

            if (cardPerf != null)
            {
                cardPerf.Location = new Point(0, 0);
                cardPerf.Size = new Size(colW, 172);

                if (lblFpsVal != null) lblFpsVal.Location = new Point(colW - 90, 24);
                if (tbFps != null)
                {
                    tbFps.Location = new Point(12, 45);
                    tbFps.Size = new Size(colW - 24, 18);
                }

                int chipW = (colW - 24 - 9) / 4;
                if (fpsChips != null)
                {
                    for (int i = 0; i < fpsChips.Count; i++)
                    {
                        fpsChips[i].Location = new Point(12 + i * (chipW + 3), 67);
                        fpsChips[i].Size = new Size(chipW, 22);
                    }
                }

                if (lblQualityVal != null) lblQualityVal.Location = new Point(colW - 120, 95);
                if (tbQuality != null)
                {
                    tbQuality.Location = new Point(12, 116);
                    tbQuality.Size = new Size(colW - 24, 18);
                }

                if (qualityChips != null)
                {
                    for (int i = 0; i < qualityChips.Count; i++)
                    {
                        qualityChips[i].Location = new Point(12 + i * (chipW + 3), 138);
                        qualityChips[i].Size = new Size(chipW, 22);
                    }
                }
            }

            if (cardOpts != null)
            {
                int optsY = 182;
                int optsH = Math.Max(214, colH - optsY);
                cardOpts.Location = new Point(0, optsY);
                cardOpts.Size = new Size(colW, optsH);

                if (swAdaptiveFps != null)
                {
                    swAdaptiveFps.Location = new Point(12, 26);
                    swAdaptiveFps.Size = new Size(colW - 24, 40);
                }
                if (swShowFps != null)
                {
                    swShowFps.Location = new Point(12, 70);
                    swShowFps.Size = new Size(colW - 24, 40);
                }
                if (swBatterySaver != null)
                {
                    swBatterySaver.Location = new Point(12, 114);
                    swBatterySaver.Size = new Size(colW - 24, 40);
                }
                if (swStartup != null)
                {
                    swStartup.Location = new Point(12, 158);
                    swStartup.Size = new Size(colW - 24, 40);
                }
            }

            // 4. Alt Komuta ve Canlı Telemetri Güvertesi
            int bottomY = colY + colH + 10;
            btnStartStop.Location = new Point(18, bottomY);
            btnStartStop.Size = new Size(w - 36, 46);

            pnlBottomBar.Location = new Point(18, bottomY + 52);
            pnlBottomBar.Size = new Size(w - 36, 46);

            int barW = w - 36;
            if (badgeLatency != null) badgeLatency.Location = new Point(barW - 86, 11);
            if (badgeSize != null) badgeSize.Location = new Point(barW - 176, 11);
            if (badgeBandwidth != null) badgeBandwidth.Location = new Point(barW - 276, 11);
            if (badgeFps != null) badgeFps.Location = new Point(barW - 356, 11);

            if (lblStatusSub != null)
            {
                lblStatusSub.Size = new Size(Math.Max(200, barW - 380), 16);
            }

            pnlLeftCol.ResumeLayout(false);
            pnlRightCol.ResumeLayout(false);
            this.ResumeLayout(false);

            heroBanner.Invalidate();
            pnlLeftCol.Invalidate(true);
            pnlRightCol.Invalidate(true);
            pnlBottomBar.Invalidate(true);
            this.Invalidate(true);
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
        private void RefreshScreenList()
        {
            Screen[] screens = Screen.AllScreens;
            List<MonitorItemData> list = new List<MonitorItemData>();
            int defaultIndex = (selectedScreenIndex >= 0 && selectedScreenIndex < screens.Length) ? selectedScreenIndex : -1;
            if (defaultIndex < 0 && !string.IsNullOrEmpty(AppConfig.LastMonitorDevice))
            {
                for (int i = 0; i < screens.Length; i++)
                {
                    if (string.Equals(screens[i].DeviceName, AppConfig.LastMonitorDevice, StringComparison.OrdinalIgnoreCase))
                    {
                        defaultIndex = i;
                        break;
                    }
                }
            }
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
                    tag = L10n.T("★ SANAL MOBIDESK", "★ VIRTUAL MOBIDESK");
                    if (defaultIndex < 0 && modeControl != null && modeControl.IsExtend)
                    {
                        defaultIndex = i;
                        foundVirtual = true;
                    }
                }
                else if (s.Primary)
                {
                    tag = L10n.T("ANA MONİTÖR", "PRIMARY MONITOR");
                }
                else
                {
                    tag = string.Format(L10n.T("MONİTÖR {0}", "MONITOR {0}"), i + 1);
                    if (defaultIndex < 0 && modeControl != null && modeControl.IsExtend && !foundVirtual)
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

            if (defaultIndex < 0 || defaultIndex >= screens.Length)
            {
                defaultIndex = 0;
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
                // 2. Harici Ekran
                for (int i = 0; i < screens.Length; i++)
                {
                    if (!screens[i].Primary)
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
            if (!File.Exists(scriptPath))
            {
                MessageBox.Show(this,
                    L10n.T("Sanal_Ekran_Ekle.bat dosyası bulunamadı!", "Sanal_Ekran_Ekle.bat file not found!"),
                    L10n.T("Hata", "Error"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string oldText = btnAddVirtual != null ? btnAddVirtual.Text : "";
            try
            {
                if (btnAddVirtual != null)
                {
                    btnAddVirtual.Enabled = false;
                    btnAddVirtual.Text = L10n.T("⏳ Ekleniyor...", "⏳ Adding...");
                    btnAddVirtual.Refresh();
                }

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c \"\"" + scriptPath + "\" --silent\"",
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = true
                };

                Process p = Process.Start(psi);
                if (p != null)
                {
                    p.WaitForExit();
                }

                Thread.Sleep(1200);
                RefreshScreenList();

                using (VirtualDisplayInfoModal modal = new VirtualDisplayInfoModal(VirtualModalMode.InstallSuccess))
                {
                    modal.ShowDialog(this);
                }
            }
            catch (System.ComponentModel.Win32Exception wEx)
            {
                if (wEx.NativeErrorCode != 1223)
                {
                    MessageBox.Show(this,
                        L10n.T("İşlem hatası: ", "Operation error: ") + wEx.Message,
                        L10n.T("Hata", "Error"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    L10n.T("İşlem iptal edildi veya hata: ", "Operation cancelled or error: ") + ex.Message,
                    L10n.T("Bilgi", "Info"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                if (btnAddVirtual != null)
                {
                    btnAddVirtual.Enabled = true;
                    btnAddVirtual.Text = oldText;
                }
            }
        }

        private void BtnVirtualInfo_Click(object sender, EventArgs e)
        {
            using (VirtualDisplayInfoModal modal = new VirtualDisplayInfoModal(VirtualModalMode.GeneralInfo))
            {
                modal.ShowDialog(this);
            }
        }

        private void BtnRemoveVirtual_Click(object sender, EventArgs e)
        {
            string scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sanal_Ekran_Kaldir.bat");
            if (!File.Exists(scriptPath))
            {
                MessageBox.Show(this,
                    L10n.T("Sanal_Ekran_Kaldir.bat dosyası bulunamadı!", "Sanal_Ekran_Kaldir.bat file not found!"),
                    L10n.T("Hata", "Error"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string oldText = btnRemoveVirtual != null ? btnRemoveVirtual.Text : "";
            try
            {
                if (btnRemoveVirtual != null)
                {
                    btnRemoveVirtual.Enabled = false;
                    btnRemoveVirtual.Text = L10n.T("⏳ Kaldırılıyor...", "⏳ Removing...");
                    btnRemoveVirtual.Refresh();
                }

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c \"\"" + scriptPath + "\" --silent\"",
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = true
                };

                Process p = Process.Start(psi);
                if (p != null)
                {
                    p.WaitForExit();
                }

                Thread.Sleep(1000);
                RefreshScreenList();

                using (VirtualDisplayInfoModal modal = new VirtualDisplayInfoModal(VirtualModalMode.RemoveSuccess))
                {
                    modal.ShowDialog(this);
                }
            }
            catch (System.ComponentModel.Win32Exception wEx)
            {
                if (wEx.NativeErrorCode != 1223)
                {
                    MessageBox.Show(this,
                        L10n.T("İşlem hatası: ", "Operation error: ") + wEx.Message,
                        L10n.T("Hata", "Error"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    L10n.T("İşlem iptal edildi veya hata: ", "Operation cancelled or error: ") + ex.Message,
                    L10n.T("Bilgi", "Info"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                if (btnRemoveVirtual != null)
                {
                    btnRemoveVirtual.Enabled = true;
                    btnRemoveVirtual.Text = oldText;
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
            UpdateTrayMenuStrings();
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

        public static void RunAdbForwardPort(string adbPath, string serial, int localPort, int remotePort)
        {
            try
            {
                string args = !string.IsNullOrEmpty(serial)
                    ? string.Format("-s {0} forward tcp:{1} tcp:{2}", serial, localPort, remotePort)
                    : string.Format("forward tcp:{1} tcp:{2}", localPort, remotePort);

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
                OnConnectedDevicesChanged(new List<ConnectedDeviceItem>());
                return;
            }

            connectedSerials = authorizedDevices;

            // Her bağlı cihaz için ayrı port forward tanımla (55556, 55557, ...)
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

                int flowPort = 55556 + i;
                RunAdbForwardPort(adbPath, ser, flowPort, 55556);

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
            RunAdbForwardPort(adbPath, targetSerial, 55555, 55555);

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
                this.TopMost = true;
                this.TopMost = false;
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

            if (pnlFlow != null) pnlFlow.Visible = isFlowTab;
            if (pnlNotifications != null) pnlNotifications.Visible = isNotifTab;

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
                if (pnlFlow != null) pnlFlow.SetConnected(connected);
                if (pnlNotifications != null) pnlNotifications.SetConnected(connected);
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

    // ═══════════════════════════════════════════════
    // ÖZEL GRAFİK KONTROLLERİ VE BİLEŞENLERİ (SIFIR LEKE, %100 OPAK)
    // ═══════════════════════════════════════════════

    public enum TitleBtnType
    {
        Minimize,
        Maximize,
        Close
    }

    public class ModernTitleBar : Panel
    {
        private Form parentForm;
        private string appTitle;
        private string appBadge;
        private TitleBarButton btnMin;
        private TitleBarButton btnMax;
        private TitleBarButton btnClose;
        private LanguageToggle langToggle;

        public event Action<bool> LanguageChanged;

        public void SetLanguage(bool isEnglish)
        {
            if (langToggle != null)
            {
                langToggle.IsEnglish = isEnglish;
            }
        }

        public ModernTitleBar(Form parent, string title, string badge)
        {
            this.parentForm = parent;
            this.appTitle = title;
            this.appBadge = badge;
            this.BackColor = MainForm.ColorBg;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            langToggle = new LanguageToggle();
            langToggle.IsEnglish = L10n.IsEnglish;
            langToggle.LanguageChanged += delegate(bool english)
            {
                if (LanguageChanged != null)
                {
                    LanguageChanged(english);
                }
            };
            this.Controls.Add(langToggle);

            btnMin = new TitleBarButton(TitleBtnType.Minimize, parentForm) { Size = new Size(42, 32) };
            btnMin.Click += delegate(object s, EventArgs e) { parentForm.WindowState = FormWindowState.Minimized; };

            btnMax = new TitleBarButton(TitleBtnType.Maximize, parentForm) { Size = new Size(42, 32) };
            btnMax.Click += delegate(object s, EventArgs e)
            {
                if (parentForm.WindowState == FormWindowState.Maximized)
                    parentForm.WindowState = FormWindowState.Normal;
                else
                    parentForm.WindowState = FormWindowState.Maximized;
            };

            btnClose = new TitleBarButton(TitleBtnType.Close, parentForm) { Size = new Size(42, 32) };
            btnClose.Click += delegate(object s, EventArgs e) { parentForm.Close(); };

            this.Controls.Add(btnMin);
            this.Controls.Add(btnMax);
            this.Controls.Add(btnClose);

            this.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && e.Clicks == 1)
                {
                    MainForm.DragWindow(parentForm.Handle);
                }
            };

            this.DoubleClick += delegate(object s, EventArgs e)
            {
                if (parentForm != null)
                {
                    parentForm.WindowState = (parentForm.WindowState == FormWindowState.Maximized)
                        ? FormWindowState.Normal
                        : FormWindowState.Maximized;
                }
            };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x02000000; // WS_CLIPCHILDREN
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x0084;
            const int HTTRANSPARENT = -1;
            if (m.Msg == WM_NCHITTEST && parentForm != null && parentForm.WindowState == FormWindowState.Normal)
            {
                short x = (short)(m.LParam.ToInt32() & 0xFFFF);
                short y = (short)((m.LParam.ToInt32() >> 16) & 0xFFFF);
                Point pt = this.PointToClient(new Point(x, y));
                if (pt.Y < 7 || pt.X < 7 || pt.X >= this.Width - 7)
                {
                    m.Result = (IntPtr)HTTRANSPARENT;
                    return;
                }
            }
            base.WndProc(ref m);
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            if (btnClose != null && btnMax != null && btnMin != null)
            {
                int btnY = (this.Height - 32) / 2;
                btnClose.Location = new Point(this.Width - 44, btnY);
                btnMax.Location = new Point(this.Width - 88, btnY);
                btnMin.Location = new Point(this.Width - 132, btnY);
                btnMax.Invalidate();
            }
            if (langToggle != null)
            {
                int langY = (this.Height - 22) / 2;
                langToggle.Location = new Point(this.Width - 206, langY);
            }
            this.Invalidate();
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
        private TitleBtnType btnType;
        private Form parentForm;
        private bool isHovered = false;
        private bool isPressed = false;

        public TitleBarButton(TitleBtnType type, Form parent = null)
        {
            this.btnType = type;
            this.parentForm = parent;
            this.BackColor = MainForm.ColorBg;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.Cursor = Cursors.Default;

            this.MouseEnter += delegate(object s, EventArgs e) { isHovered = true; Invalidate(); };
            this.MouseLeave += delegate(object s, EventArgs e) { isHovered = false; isPressed = false; Invalidate(); };
            this.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { isPressed = true; Invalidate(); } };
            this.MouseUp += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { isPressed = false; Invalidate(); } };
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
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

            using (SolidBrush b = new SolidBrush(MainForm.ColorBg))
            {
                g.FillRectangle(b, this.ClientRectangle);
            }

            Color bg = Color.Transparent;
            Color fg = MainForm.ColorTextMuted;

            if (btnType == TitleBtnType.Close)
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
                if (btnType == TitleBtnType.Close)
                {
                    g.DrawLine(pen, cx - 4, cy - 4, cx + 4, cy + 4);
                    g.DrawLine(pen, cx - 4, cy + 4, cx + 4, cy - 4);
                }
                else if (btnType == TitleBtnType.Minimize)
                {
                    g.DrawLine(pen, cx - 5, cy, cx + 5, cy);
                }
                else if (btnType == TitleBtnType.Maximize)
                {
                    bool isMaximized = (parentForm != null && parentForm.WindowState == FormWindowState.Maximized);
                    if (isMaximized)
                    {
                        // Restore icon: iki iç içe pencere
                        Rectangle r1 = new Rectangle(cx - 3, cy - 5, 8, 7);
                        Rectangle r2 = new Rectangle(cx - 5, cy - 3, 8, 7);
                        g.DrawRectangle(pen, r1);
                        using (SolidBrush fillBg = new SolidBrush(bg != Color.Transparent ? bg : MainForm.ColorBg))
                        {
                            g.FillRectangle(fillBg, r2.X + 1, r2.Y + 1, r2.Width - 1, r2.Height - 1);
                        }
                        g.DrawRectangle(pen, r2);
                    }
                    else
                    {
                        // Maximize icon: tek zarif kare
                        Rectangle r = new Rectangle(cx - 5, cy - 5, 10, 10);
                        g.DrawRectangle(pen, r);
                    }
                }
            }
        }
    }

    public class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.UpdateStyles();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x02000000; // WS_CLIPCHILDREN
                return cp;
            }
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            this.Invalidate();
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
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x02000000; // WS_CLIPCHILDREN
                return cp;
            }
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            this.Invalidate();
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

        public string Title
        {
            get { return headerTitle; }
            set { headerTitle = value; this.Invalidate(); }
        }

        public ModernCard(string title, int x, int y, int w, int h)
        {
            this.headerTitle = title;
            this.Location = new Point(x, y);
            this.Size = new Size(w, h);
            this.BackColor = MainForm.ColorCard;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x02000000; // WS_CLIPCHILDREN
                return cp;
            }
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            this.Invalidate(true);
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

        public void UpdateLabels(string t1, string t2)
        {
            this.text1 = t1;
            this.text2 = t2;
            Invalidate();
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
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

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

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            this.Invalidate();
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
            using (Font f = new Font("Segoe UI Emoji", 8.5f, active ? FontStyle.Bold : FontStyle.Regular))
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
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x02000000; // WS_CLIPCHILDREN
                return cp;
            }
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

        public void UpdateLanguageTags()
        {
            if (monitors == null) return;
            for (int i = 0; i < monitors.Count; i++)
            {
                MonitorItemData m = monitors[i];
                if (m.IsVirtual)
                {
                    m.Tag = L10n.T("★ SANAL MOBIDESK", "★ VIRTUAL MOBIDESK");
                }
                else if (m.Screen != null && m.Screen.Primary)
                {
                    m.Tag = L10n.T("ANA MONİTÖR", "PRIMARY MONITOR");
                }
                else
                {
                    m.Tag = string.Format(L10n.T("MONİTÖR {0}", "MONITOR {0}"), i + 1);
                }
            }
            RefreshCards();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RefreshCards();
            this.Invalidate(true);
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
            this.Invalidate(true);
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
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            this.MouseEnter += delegate(object s, EventArgs e) { isHovered = true; Invalidate(); };
            this.MouseLeave += delegate(object s, EventArgs e) { isHovered = false; Invalidate(); };
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            this.Invalidate();
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
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            this.MouseEnter += delegate(object s, EventArgs e) { isHovered = true; Invalidate(); };
            this.MouseLeave += delegate(object s, EventArgs e) { isHovered = false; Invalidate(); };
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            this.Invalidate();
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
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            this.MouseEnter += delegate(object s, EventArgs e) { isHovered = true; Invalidate(); };
            this.MouseLeave += delegate(object s, EventArgs e) { isHovered = false; Invalidate(); };
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            this.Invalidate();
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

        public void UpdateText(string t, string sub)
        {
            this.title = t;
            this.subtitle = sub;
            Invalidate();
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
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            this.MouseEnter += delegate(object s, EventArgs e) { isHovered = true; Invalidate(); };
            this.MouseLeave += delegate(object s, EventArgs e) { isHovered = false; Invalidate(); };
            this.Click += delegate(object s, EventArgs e) { this.Checked = !this.Checked; };
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            this.Invalidate();
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

        public string Label
        {
            get { return label; }
            set { label = value; Invalidate(); }
        }

        public TelemetryBadge(string lbl, string initialVal, int x, int y, int w, int h)
        {
            this.label = lbl;
            this.val = initialVal;
            this.Location = new Point(x, y);
            this.Size = new Size(w, h);
            this.BackColor = MainForm.ColorCard;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            this.Invalidate();
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
            this.Font = new Font("Segoe UI Emoji", 8.5f, FontStyle.Bold);
            this.Cursor = Cursors.Hand;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            this.MouseEnter += delegate(object s, EventArgs e) { isHovered = true; this.Invalidate(); };
            this.MouseLeave += delegate(object s, EventArgs e) { isHovered = false; isPressed = false; this.Invalidate(); };
            this.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { isPressed = true; this.Invalidate(); } };
            this.MouseUp += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { isPressed = false; this.Invalidate(); } };
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            this.Invalidate();
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
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x02000000; // WS_CLIPCHILDREN
                return cp;
            }
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            this.Invalidate(true);
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

    public class ModernDarkScrollBar : Control
    {
        private int minimum = 0;
        private int maximum = 100;
        private int val = 0;
        private int smallChange = 40;
        private int largeChange = 100;

        private bool isHovered = false;
        private bool isDragging = false;
        private int dragStartY = 0;
        private int dragStartVal = 0;

        public event EventHandler ValueChanged;

        public ModernDarkScrollBar()
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint |
                          ControlStyles.UserPaint |
                          ControlStyles.OptimizedDoubleBuffer |
                          ControlStyles.ResizeRedraw |
                          ControlStyles.SupportsTransparentBackColor, true);
            this.DoubleBuffered = true;
            this.Width = 10;
            this.BackColor = MainForm.ColorBg;
            this.Cursor = Cursors.Default;
        }

        public int Minimum
        {
            get { return minimum; }
            set { minimum = value; Invalidate(); }
        }

        public int Maximum
        {
            get { return maximum; }
            set
            {
                maximum = value;
                this.Value = val;
                Invalidate();
            }
        }

        public int SmallChange
        {
            get { return smallChange; }
            set { smallChange = value; }
        }

        public int LargeChange
        {
            get { return largeChange; }
            set
            {
                largeChange = value;
                this.Value = val;
                Invalidate();
            }
        }

        public int Value
        {
            get { return val; }
            set
            {
                int maxPossible = Math.Max(minimum, maximum - largeChange);
                int clamped = Math.Max(minimum, Math.Min(maxPossible, value));
                if (val != clamped)
                {
                    val = clamped;
                    Invalidate();
                    if (ValueChanged != null)
                    {
                        ValueChanged(this, EventArgs.Empty);
                    }
                }
            }
        }

        private Rectangle GetThumbRect()
        {
            int trackH = this.Height;
            if (trackH <= 0) return Rectangle.Empty;

            int totalRange = Math.Max(1, maximum - minimum);
            int viewRange = Math.Min(totalRange, Math.Max(1, largeChange));
            float ratio = (float)viewRange / (float)totalRange;
            int thumbH = Math.Max(32, (int)(trackH * ratio));
            if (thumbH > trackH) thumbH = trackH;

            int maxVal = Math.Max(1, maximum - largeChange - minimum);
            int curOffset = val - minimum;
            int trackTravel = trackH - thumbH;
            int thumbY = (trackTravel > 0) ? (int)((float)curOffset / maxVal * trackTravel) : 0;
            thumbY = Math.Max(0, Math.Min(trackTravel, thumbY));

            return new Rectangle(1, thumbY, this.Width - 2, thumbH);
        }

        internal Rectangle GetThumbRectForTest()
        {
            return GetThumbRect();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            isHovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!isDragging)
            {
                isHovered = false;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                Rectangle thumbRect = GetThumbRect();
                if (thumbRect.Contains(e.Location))
                {
                    isDragging = true;
                    this.Capture = true;
                    dragStartY = e.Y;
                    dragStartVal = val;
                }
                else
                {
                    if (e.Y < thumbRect.Top)
                    {
                        this.Value -= largeChange;
                    }
                    else if (e.Y > thumbRect.Bottom)
                    {
                        this.Value += largeChange;
                    }
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (isDragging)
            {
                Rectangle thumbRect = GetThumbRect();
                int trackTravel = this.Height - thumbRect.Height;
                if (trackTravel > 0)
                {
                    int maxVal = Math.Max(1, maximum - largeChange - minimum);
                    int deltaY = e.Y - dragStartY;
                    int deltaVal = (int)((float)deltaY / trackTravel * maxVal);
                    this.Value = dragStartVal + deltaVal;
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (isDragging)
            {
                this.Capture = false;
                isDragging = false;
                Point cursorP = this.PointToClient(Cursor.Position);
                isHovered = this.ClientRectangle.Contains(cursorP);
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // Arka plan / Track: İnce koyu ray
            int trackW = 4;
            int trackX = (this.Width - trackW) / 2;
            using (Brush trackBrush = new SolidBrush(Color.FromArgb(24, 33, 50)))
            {
                using (GraphicsPath trackPath = RoundedPanel.CreateRoundedRect(new Rectangle(trackX, 2, trackW, Math.Max(1, this.Height - 4)), 2))
                {
                    g.FillPath(trackBrush, trackPath);
                }
            }

            // Thumb: Modern hap (pill) biçiminde
            Rectangle thumbRect = GetThumbRect();
            if (thumbRect.Width > 0 && thumbRect.Height > 0)
            {
                Color thumbColor;
                if (isDragging)
                {
                    thumbColor = Color.FromArgb(59, 130, 246); // MobiDesk Royal Blue
                }
                else if (isHovered)
                {
                    thumbColor = Color.FromArgb(100, 116, 139); // Slate-500 hover
                }
                else
                {
                    thumbColor = Color.FromArgb(71, 85, 105);   // Slate-600 visible dark
                }

                int pillW = 6;
                int pillX = (this.Width - pillW) / 2;
                Rectangle pillRect = new Rectangle(pillX, thumbRect.Y, pillW, thumbRect.Height);
                int radius = pillW / 2;

                using (Brush thumbBrush = new SolidBrush(thumbColor))
                {
                    using (GraphicsPath pillPath = RoundedPanel.CreateRoundedRect(pillRect, radius))
                    {
                        g.FillPath(thumbBrush, pillPath);
                    }
                }
            }
        }
    }

    public class LanguageToggle : Control
    {
        private bool isEnglish = false;
        private int hoveredIndex = -1;

        public event Action<bool> LanguageChanged;

        public bool IsEnglish
        {
            get { return isEnglish; }
            set
            {
                if (isEnglish != value)
                {
                    isEnglish = value;
                    Invalidate();
                }
            }
        }

        public LanguageToggle()
        {
            this.Size = new Size(64, 22);
            this.BackColor = MainForm.ColorBg;
            this.Cursor = Cursors.Hand;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

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
                    bool newEn = (e.X >= this.Width / 2);
                    if (newEn != isEnglish)
                    {
                        isEnglish = newEn;
                        Invalidate();
                        if (LanguageChanged != null)
                        {
                            LanguageChanged(isEnglish);
                        }
                    }
                }
            };
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
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

            using (SolidBrush b = new SolidBrush(MainForm.ColorBg))
            {
                g.FillRectangle(b, this.ClientRectangle);
            }

            Rectangle r = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            using (GraphicsPath p = RoundedPanel.CreateRoundedRect(r, 6))
            {
                using (SolidBrush bgPill = new SolidBrush(Color.FromArgb(20, 26, 40)))
                {
                    g.FillPath(bgPill, p);
                }
                using (Pen penBorder = new Pen(Color.FromArgb(42, 54, 76), 1.0f))
                {
                    g.DrawPath(penBorder, p);
                }
            }

            int halfW = this.Width / 2;
            Rectangle activeRect = isEnglish
                ? new Rectangle(halfW + 1, 2, halfW - 3, this.Height - 4)
                : new Rectangle(2, 2, halfW - 3, this.Height - 4);

            using (GraphicsPath activePath = RoundedPanel.CreateRoundedRect(activeRect, 4))
            {
                using (SolidBrush ab = new SolidBrush(MainForm.ColorPrimary))
                {
                    g.FillPath(ab, activePath);
                }
            }

            using (Font f = new Font("Segoe UI", 7.5f, FontStyle.Bold))
            {
                // TR
                Rectangle rTr = new Rectangle(0, 0, halfW, this.Height);
                Color trColor = !isEnglish ? Color.White : (hoveredIndex == 0 ? Color.FromArgb(226, 232, 240) : Color.FromArgb(148, 163, 184));
                using (SolidBrush sb = new SolidBrush(trColor))
                {
                    StringFormat sf = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };
                    g.DrawString("TR", f, sb, rTr, sf);
                }

                // EN
                Rectangle rEn = new Rectangle(halfW, 0, halfW, this.Height);
                Color enColor = isEnglish ? Color.White : (hoveredIndex == 1 ? Color.FromArgb(226, 232, 240) : Color.FromArgb(148, 163, 184));
                using (SolidBrush sb = new SolidBrush(enColor))
                {
                    StringFormat sf = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };
                    g.DrawString("EN", f, sb, rEn, sf);
                }
            }
        }
    }

    public class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkMenuColorTable()) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected)
            {
                Rectangle r = new Rectangle(3, 1, e.Item.Width - 6, e.Item.Height - 2);
                using (GraphicsPath path = RoundedPanel.CreateRoundedRect(r, 4))
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(37, 99, 235)))
                    {
                        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        e.Graphics.FillPath(b, path);
                    }
                }
            }
            else
            {
                Rectangle r = new Rectangle(0, 0, e.Item.Width, e.Item.Height);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(17, 24, 39)))
                {
                    e.Graphics.FillRectangle(b, r);
                }
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            Rectangle r = new Rectangle(8, e.Item.Height / 2, e.Item.Width - 16, 1);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(42, 54, 76)))
            {
                e.Graphics.FillRectangle(b, r);
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            Rectangle r = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            using (Pen p = new Pen(Color.FromArgb(42, 54, 76), 1))
            {
                e.Graphics.DrawRectangle(p, r);
            }
        }
    }

    public class DarkMenuColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(17, 24, 39); } }
        public override Color MenuBorder { get { return Color.FromArgb(42, 54, 76); } }
        public override Color MenuItemBorder { get { return Color.Transparent; } }
        public override Color MenuItemSelected { get { return Color.FromArgb(37, 99, 235); } }
        public override Color MenuStripGradientBegin { get { return Color.FromArgb(17, 24, 39); } }
        public override Color MenuStripGradientEnd { get { return Color.FromArgb(17, 24, 39); } }
        public override Color ImageMarginGradientBegin { get { return Color.FromArgb(17, 24, 39); } }
        public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(17, 24, 39); } }
        public override Color ImageMarginGradientEnd { get { return Color.FromArgb(17, 24, 39); } }
    }

    public class ModernComboBox : Control
    {
        private readonly List<object> items = new List<object>();
        private int selectedIndex = -1;
        private bool isHovered = false;
        private bool isDroppedDown = false;
        private ContextMenuStrip dropDownMenu;

        public event EventHandler SelectedIndexChanged;

        public class ItemCollection : System.Collections.IEnumerable
        {
            private readonly ModernComboBox owner;
            public ItemCollection(ModernComboBox owner) { this.owner = owner; }

            public int Count { get { return owner.items.Count; } }

            public object this[int index]
            {
                get { return owner.items[index]; }
                set { owner.items[index] = value; owner.Invalidate(); }
            }

            public int Add(object item)
            {
                int idx = owner.items.Count;
                owner.items.Add(item);
                if (owner.selectedIndex < 0) owner.SelectedIndex = 0;
                owner.Invalidate();
                return idx;
            }

            public void Clear()
            {
                owner.items.Clear();
                owner.selectedIndex = -1;
                owner.Invalidate();
            }

            public bool Contains(object item) { return owner.items.Contains(item); }
            public int IndexOf(object item) { return owner.items.IndexOf(item); }
            public void Insert(int index, object item) { owner.items.Insert(index, item); owner.Invalidate(); }
            public void Remove(object item) { owner.items.Remove(item); owner.Invalidate(); }
            public void RemoveAt(int index) { owner.items.RemoveAt(index); owner.Invalidate(); }
            public System.Collections.IEnumerator GetEnumerator() { return owner.items.GetEnumerator(); }
        }

        private ItemCollection itemsCollection;
        public ItemCollection Items { get { return itemsCollection; } }

        public int SelectedIndex
        {
            get { return selectedIndex; }
            set
            {
                if (selectedIndex != value)
                {
                    if (value >= 0 && value < items.Count)
                        selectedIndex = value;
                    else
                        selectedIndex = -1;

                    this.Invalidate();
                    if (SelectedIndexChanged != null)
                    {
                        SelectedIndexChanged(this, EventArgs.Empty);
                    }
                }
            }
        }

        public object SelectedItem
        {
            get
            {
                if (selectedIndex >= 0 && selectedIndex < items.Count)
                    return items[selectedIndex];
                return null;
            }
            set
            {
                int idx = items.IndexOf(value);
                SelectedIndex = idx;
            }
        }

        public string SelectedText
        {
            get
            {
                object item = SelectedItem;
                return item != null ? item.ToString() : string.Empty;
            }
        }

        public ModernComboBox()
        {
            this.itemsCollection = new ItemCollection(this);
            this.Size = new Size(210, 28);
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            this.BackColor = Color.FromArgb(20, 26, 40);
            this.ForeColor = MainForm.ColorText;
            this.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            this.Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            isHovered = true;
            this.Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            isHovered = false;
            this.Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            this.Focus();
            ShowDropDown();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Down)
            {
                if (SelectedIndex < items.Count - 1) SelectedIndex++;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Up)
            {
                if (SelectedIndex > 0) SelectedIndex--;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                ShowDropDown();
                e.Handled = true;
            }
        }

        public void ShowDropDown()
        {
            if (items.Count == 0) return;

            if (dropDownMenu != null)
            {
                dropDownMenu.Dispose();
                dropDownMenu = null;
            }

            dropDownMenu = new ContextMenuStrip();
            dropDownMenu.Renderer = new DarkMenuRenderer();
            dropDownMenu.BackColor = Color.FromArgb(17, 24, 39);
            dropDownMenu.ShowImageMargin = false;
            dropDownMenu.Font = this.Font;
            dropDownMenu.MinimumSize = new Size(this.Width, 0);

            for (int i = 0; i < items.Count; i++)
            {
                int itemIndex = i;
                object itemObj = items[i];
                string itemText = itemObj != null ? itemObj.ToString() : "";

                ToolStripMenuItem menuItem = new ToolStripMenuItem(itemText);
                menuItem.AutoSize = false;
                menuItem.Height = 28;
                menuItem.Width = Math.Max(this.Width, 240);
                menuItem.ForeColor = (itemIndex == selectedIndex) ? MainForm.ColorAccent : MainForm.ColorText;
                menuItem.Font = (itemIndex == selectedIndex) ? new Font(this.Font, FontStyle.Bold) : this.Font;

                menuItem.Click += delegate
                {
                    this.SelectedIndex = itemIndex;
                };

                dropDownMenu.Items.Add(menuItem);
            }

            isDroppedDown = true;
            this.Invalidate();

            dropDownMenu.Closed += delegate
            {
                isDroppedDown = false;
                this.Invalidate();
            };

            dropDownMenu.Show(this, new Point(0, this.Height + 2));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color pBg = (this.Parent != null) ? this.Parent.BackColor : MainForm.ColorCard;
            using (SolidBrush b = new SolidBrush(pBg))
            {
                g.FillRectangle(b, this.ClientRectangle);
            }

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);

            Color bg = (isHovered || isDroppedDown || this.Focused) ? Color.FromArgb(26, 35, 52) : Color.FromArgb(20, 26, 40);
            using (GraphicsPath path = RoundedPanel.CreateRoundedRect(rect, 6))
            {
                using (SolidBrush b = new SolidBrush(bg))
                {
                    g.FillPath(b, path);
                }

                Color borderCol = (isHovered || isDroppedDown || this.Focused) ? MainForm.ColorPrimary : MainForm.ColorCardBorder;
                using (Pen p = new Pen(borderCol, 1.2f))
                {
                    g.DrawPath(p, path);
                }
            }

            int btnW = 26;
            Rectangle btnRect = new Rectangle(this.Width - btnW, 0, btnW, this.Height);
            int cx = btnRect.X + (btnRect.Width / 2);
            int cy = btnRect.Y + (btnRect.Height / 2);

            Color chevronColor = (isHovered || isDroppedDown) ? MainForm.ColorAccent : MainForm.ColorTextMuted;
            using (Pen cp = new Pen(chevronColor, 1.8f))
            {
                cp.StartCap = LineCap.Round;
                cp.EndCap = LineCap.Round;
                cp.LineJoin = LineJoin.Round;

                if (isDroppedDown)
                {
                    Point[] pts = new Point[]
                    {
                        new Point(cx - 4, cy + 2),
                        new Point(cx, cy - 2),
                        new Point(cx + 4, cy + 2)
                    };
                    g.DrawLines(cp, pts);
                }
                else
                {
                    Point[] pts = new Point[]
                    {
                        new Point(cx - 4, cy - 2),
                        new Point(cx, cy + 2),
                        new Point(cx + 4, cy - 2)
                    };
                    g.DrawLines(cp, pts);
                }
            }

            string text = SelectedText;
            if (!string.IsNullOrEmpty(text))
            {
                Rectangle textRect = new Rectangle(10, 0, this.Width - btnW - 10, this.Height);
                TextRenderer.DrawText(g, text, this.Font, textRect, MainForm.ColorText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }

    public enum VirtualModalMode
    {
        GeneralInfo,
        InstallSuccess,
        RemoveSuccess
    }

    public class VirtualDisplayInfoModal : Form
    {
        private VirtualModalMode mode;

        public VirtualDisplayInfoModal() : this(VirtualModalMode.GeneralInfo)
        {
        }

        public VirtualDisplayInfoModal(VirtualModalMode mode)
        {
            this.mode = mode;
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterParent;
            int modalW = (mode == VirtualModalMode.RemoveSuccess) ? 500 : 540;
            int modalH = (mode == VirtualModalMode.RemoveSuccess) ? 260 : 446;
            this.Size = new Size(modalW, modalH);
            this.BackColor = MainForm.ColorBg;
            this.ShowInTaskbar = false;
            this.DoubleBuffered = true;
            this.KeyPreview = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            this.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    this.Close();
                }
                else if (e.KeyCode == Keys.Enter)
                {
                    if (this.mode != VirtualModalMode.RemoveSuccess)
                    {
                        OpenDisplaySettings();
                    }
                    else
                    {
                        this.Close();
                    }
                }
            };

            this.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && e.Y < 60)
                {
                    MainForm.DragWindow(this.Handle);
                }
            };

            InitializeModalUI();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            using (GraphicsPath path = RoundedPanel.CreateRoundedRect(new Rectangle(0, 0, this.Width, this.Height), 12))
            {
                this.Region = new Region(path);
            }
            this.Invalidate();
        }

        private void OpenDisplaySettings()
        {
            try
            {
                Process.Start("ms-settings:display");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, L10n.T("Hata", "Error"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            this.Close();
        }

        private void InitializeModalUI()
        {
            // Kapatma butonu (✕)
            Button btnClose = new Button
            {
                Text = "✕",
                Font = new Font("Segoe UI", 10f, FontStyle.Regular),
                ForeColor = MainForm.ColorTextMuted,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(32, 32),
                Location = new Point(this.Width - 44, 14),
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.MouseEnter += delegate { btnClose.ForeColor = MainForm.ColorDanger; };
            btnClose.MouseLeave += delegate { btnClose.ForeColor = MainForm.ColorTextMuted; };
            btnClose.Click += delegate { this.Close(); };
            this.Controls.Add(btnClose);

            // İkon Rozeti
            Color badgeBorder = mode == VirtualModalMode.InstallSuccess ? MainForm.ColorSuccess :
                                (mode == VirtualModalMode.RemoveSuccess ? Color.FromArgb(244, 114, 182) : MainForm.ColorAccent);
            Color badgeBg = mode == VirtualModalMode.InstallSuccess ? Color.FromArgb(16, 36, 26) :
                            (mode == VirtualModalMode.RemoveSuccess ? Color.FromArgb(36, 20, 30) : Color.FromArgb(24, 32, 48));
            string badgeChar = (mode == VirtualModalMode.InstallSuccess || mode == VirtualModalMode.RemoveSuccess) ? "✓" : "ℹ";

            Panel pnlIcon = new Panel
            {
                Size = new Size(38, 38),
                Location = new Point(20, 16),
                BackColor = badgeBg
            };
            pnlIcon.Paint += delegate(object s, PaintEventArgs pe)
            {
                Graphics g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, pnlIcon.Width - 1, pnlIcon.Height - 1);
                using (GraphicsPath gp = RoundedPanel.CreateRoundedRect(r, 8))
                {
                    using (SolidBrush b = new SolidBrush(badgeBg)) g.FillPath(b, gp);
                    using (Pen p = new Pen(badgeBorder, 1.2f)) g.DrawPath(p, gp);
                }
                using (Font f = new Font("Segoe UI", 13f, FontStyle.Bold))
                {
                    TextRenderer.DrawText(g, badgeChar, f, r, badgeBorder, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            };
            this.Controls.Add(pnlIcon);

            // Başlık & Alt Başlık
            string titleText = mode == VirtualModalMode.InstallSuccess
                ? L10n.T("Sanal Ekran Başarıyla Eklendi!", "Virtual Display Added Successfully!")
                : (mode == VirtualModalMode.RemoveSuccess
                    ? L10n.T("Sanal Ekran Devre Dışı Bırakıldı", "Virtual Display Disabled")
                    : L10n.T("Sanal Ekran Düzeni & Konumlandırma", "Virtual Display Layout & Positioning"));

            string subText = mode == VirtualModalMode.InstallSuccess
                ? L10n.T("Cihazınız artık 1:1 piksel netliğinde harici ekran olarak hazır", "Your device is now ready as a crisp 1:1 pixel external display")
                : (mode == VirtualModalMode.RemoveSuccess
                    ? L10n.T("Sanal monitör sürücüsü başarıyla sistemden kaldırıldı", "Virtual monitor driver has been disabled successfully")
                    : L10n.T("Windows Görüntü Ayarları üzerinden kolayca yönetilebilir", "Easily configured via Windows Display Settings"));

            Label lblTitle = new Label
            {
                Text = titleText,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = MainForm.ColorText,
                Location = new Point(68, 16),
                Size = new Size(this.Width - 120, 22),
                BackColor = Color.Transparent,
                UseMnemonic = false
            };
            lblTitle.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) MainForm.DragWindow(this.Handle); };
            this.Controls.Add(lblTitle);

            Label lblSubtitle = new Label
            {
                Text = subText,
                Font = new Font("Segoe UI", 7.8f),
                ForeColor = MainForm.ColorTextSubtle,
                Location = new Point(68, 38),
                Size = new Size(this.Width - 120, 18),
                BackColor = Color.Transparent,
                UseMnemonic = false
            };
            lblSubtitle.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) MainForm.DragWindow(this.Handle); };
            this.Controls.Add(lblSubtitle);

            // Ana İçerik Kartı
            int cardH = (mode == VirtualModalMode.RemoveSuccess) ? 104 : 300;
            Panel pnlCard = new Panel
            {
                Location = new Point(20, 68),
                Size = new Size(this.Width - 40, cardH),
                BackColor = MainForm.ColorCard
            };
            pnlCard.Paint += delegate(object s, PaintEventArgs pe)
            {
                Graphics g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, pnlCard.Width - 1, pnlCard.Height - 1);
                using (GraphicsPath gp = RoundedPanel.CreateRoundedRect(r, 10))
                {
                    using (SolidBrush b = new SolidBrush(MainForm.ColorCard)) g.FillPath(b, gp);
                    using (Pen p = new Pen(MainForm.ColorCardBorder, 1.0f)) g.DrawPath(p, gp);
                }
            };

            int cardInnerW = pnlCard.Width - 24;

            if (mode == VirtualModalMode.RemoveSuccess)
            {
                AddInfoItem(pnlCard, 3,
                    L10n.T("Sistem Varsayılanına Döndürüldü", "Restored to System Defaults"),
                    L10n.T("Sanal monitör sürücüsü sistemden kaldırıldı. Ekran listeniz varsayılan donanım monitörlerinize güncellendi.",
                           "Virtual monitor driver was removed. Your display list has been restored to default hardware monitors."),
                    12, 10, cardInnerW);
            }
            else if (mode == VirtualModalMode.InstallSuccess)
            {
                int itemY = 12;
                AddInfoItem(pnlCard, 0,
                    L10n.T("Ekran Konumunu Belirleyin", "Position Your Display"),
                    L10n.T("Sanal ekran ana monitörünüzün yanına eklendi. Fare imlecinizin geçiş yönünü ayarlamak için Windows Görüntü Ayarları'ndan sürükleyebilirsiniz.",
                           "Virtual display added next to your monitor. You can drag and position it in Windows Display Settings to choose mouse transit direction."),
                    12, itemY, cardInnerW);
                itemY += 94;

                AddInfoItem(pnlCard, 1,
                    L10n.T("Ölçek ve Netlik (DPI)", "Scaling & Sharpness (DPI)"),
                    L10n.T("Tablette simgeleri büyütmek için %125, masaüstünüzü tam 1:1 piksel netliğinde genişletmek için %100 ölçeğini seçebilirsiniz.",
                           "Choose 125% for larger tablet elements or 100% for crisp 1:1 pixel precision."),
                    12, itemY, cardInnerW);
                itemY += 94;

                AddInfoItem(pnlCard, 2,
                    L10n.T("Yayını Başlatmaya Hazır", "Ready for Streaming"),
                    L10n.T("Ekran listesinden yeni sanal monitörü seçip 'Yayını Başlat' butonuna basarak tabletinizi ikinci ekran olarak kullanabilirsiniz.",
                           "Select the new virtual monitor from the screen list and click 'Start Streaming' to use your tablet as a second screen."),
                    12, itemY, cardInnerW);
            }
            else
            {
                int itemY = 12;
                AddInfoItem(pnlCard, 0,
                    L10n.T("Ekran Konumu ve Fare Geçiş Yönü", "Screen Position & Mouse Transition"),
                    L10n.T("Sanal ekranı ana monitörünüzün sağına, soluna veya üstüne serbestçe taşıyabilirsiniz. Fare imleciniz ekranlar arasında belirlediğiniz bu yönde akıcı şekilde geçer.",
                           "Move the virtual display to the right, left, or top of your primary monitor. Mouse cursor seamlessly passes between screens in this direction."),
                    12, itemY, cardInnerW);
                itemY += 94;

                AddInfoItem(pnlCard, 1,
                    L10n.T("Ölçek ve Yazı Boyutu (DPI)", "Scaling & Font Size (DPI)"),
                    L10n.T("Tablette simgeleri ve pencereleri büyütmek için %125, masaüstünüzü tam 1:1 piksel netliğinde genişletmek için %100 ölçeğini seçebilirsiniz.",
                           "Select 125% to make icons and windows larger on tablet, or 100% for crisp 1:1 pixel precision."),
                    12, itemY, cardInnerW);
                itemY += 94;

                AddInfoItem(pnlCard, 2,
                    L10n.T("Akıllı Çözünürlük Uyumu", "Smart Resolution Adaptation"),
                    L10n.T("MobiDesk cihazınız bağlandığında çözünürlüğü cihazınıza göre otomatik eşitler. Dilerseniz Windows ayarlarından çözünürlüğü de değiştirebilirsiniz.",
                           "MobiDesk automatically adapts resolution when your device connects. You can also adjust it in Windows Display Settings anytime."),
                    12, itemY, cardInnerW);
            }

            this.Controls.Add(pnlCard);

            // Alt Butonlar
            int btnH = 38;
            int btnY = this.Height - 56;

            if (mode == VirtualModalMode.RemoveSuccess)
            {
                ModernButton btnOk = new ModernButton(
                    L10n.T("Tamam", "OK"),
                    this.Width - 20 - 130, btnY, 130, btnH,
                    Color.FromArgb(32, 24, 32),
                    Color.FromArgb(244, 114, 182),
                    8,
                    Color.FromArgb(70, 30, 50)
                );
                btnOk.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                btnOk.Click += delegate { this.Close(); };
                this.Controls.Add(btnOk);
            }
            else
            {
                ModernButton btnCancel = new ModernButton(
                    L10n.T("Kapat", "Close"),
                    20, btnY, 110, btnH,
                    Color.FromArgb(20, 26, 38),
                    MainForm.ColorTextMuted,
                    8,
                    MainForm.ColorCardBorder
                );
                btnCancel.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                btnCancel.Click += delegate { this.Close(); };
                this.Controls.Add(btnCancel);

                Color btnSettingsCol = mode == VirtualModalMode.InstallSuccess ? MainForm.ColorSuccess : MainForm.ColorPrimary;
                Color btnSettingsDark = mode == VirtualModalMode.InstallSuccess ? Color.FromArgb(14, 150, 105) : MainForm.ColorPrimaryDark;

                ModernButton btnOpenSettings = new ModernButton(
                    L10n.T("🚀 Windows Ayarlarını Aç", "🚀 Open Windows Settings"),
                    this.Width - 20 - 240, btnY, 240, btnH,
                    btnSettingsCol,
                    Color.White,
                    8,
                    btnSettingsDark
                );
                btnOpenSettings.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                btnOpenSettings.Click += delegate { OpenDisplaySettings(); };
                this.Controls.Add(btnOpenSettings);
            }
        }

        private void AddInfoItem(Control parent, int iconType, string title, string desc, int x, int y, int w)
        {
            Panel itemPnl = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(w, 84),
                BackColor = Color.FromArgb(13, 17, 26)
            };
            itemPnl.Paint += delegate(object s, PaintEventArgs pe)
            {
                Graphics g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, itemPnl.Width - 1, itemPnl.Height - 1);
                using (GraphicsPath gp = RoundedPanel.CreateRoundedRect(r, 8))
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(13, 17, 26))) g.FillPath(b, gp);
                    using (Pen p = new Pen(Color.FromArgb(32, 42, 64), 1.0f)) g.DrawPath(p, gp);
                }

                // Vektör İkon Yuvası
                Rectangle icoR = new Rectangle(12, 14, 28, 28);
                using (GraphicsPath ip = RoundedPanel.CreateRoundedRect(icoR, 6))
                {
                    using (SolidBrush ib = new SolidBrush(Color.FromArgb(22, 30, 46))) g.FillPath(ib, ip);
                    using (Pen ipen = new Pen(Color.FromArgb(40, 54, 82), 1.0f)) g.DrawPath(ipen, ip);
                }

                if (iconType == 0) // Monitör Konum & Geçiş Vektörü
                {
                    using (Pen pMon = new Pen(MainForm.ColorAccent, 1.4f))
                    {
                        g.DrawRectangle(pMon, 16, 20, 9, 8);
                        g.DrawRectangle(pMon, 27, 20, 8, 8);
                        g.DrawLine(pMon, 22, 29, 25, 29);
                    }
                    using (Pen pArr = new Pen(Color.White, 1.2f))
                    {
                        g.DrawLine(pArr, 23, 24, 27, 24);
                    }
                }
                else if (iconType == 1) // Ölçek & Boyutlandırma Vektörü
                {
                    using (Pen pScale = new Pen(MainForm.ColorPrimary, 1.4f))
                    {
                        g.DrawRectangle(pScale, 17, 19, 18, 14);
                        g.DrawLine(pScale, 21, 26, 31, 26);
                        g.DrawLine(pScale, 26, 22, 26, 30);
                    }
                }
                else if (iconType == 2) // Çözünürlük & Hız (Şimşek Vektörü)
                {
                    using (SolidBrush bBolt = new SolidBrush(MainForm.ColorSuccess))
                    {
                        Point[] bolt = new Point[]
                        {
                            new Point(27, 17),
                            new Point(20, 27),
                            new Point(25, 27),
                            new Point(23, 37),
                            new Point(31, 25),
                            new Point(25, 25)
                        };
                        g.FillPolygon(bBolt, bolt);
                    }
                }
                else // iconType == 3: Tamam / Kaldırıldı (Onay / Rozet Vektörü)
                {
                    using (Pen pCheck = new Pen(Color.FromArgb(244, 114, 182), 2.0f))
                    {
                        pCheck.StartCap = LineCap.Round;
                        pCheck.EndCap = LineCap.Round;
                        g.DrawLine(pCheck, 18, 28, 23, 33);
                        g.DrawLine(pCheck, 23, 33, 33, 21);
                    }
                }
            };

            Label lblT = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = MainForm.ColorText,
                Location = new Point(48, 8),
                Size = new Size(w - 58, 18),
                BackColor = Color.Transparent,
                UseMnemonic = false
            };
            itemPnl.Controls.Add(lblT);

            Label lblD = new Label
            {
                Text = desc,
                Font = new Font("Segoe UI", 7.5f),
                ForeColor = MainForm.ColorTextMuted,
                Location = new Point(48, 28),
                Size = new Size(w - 58, 48),
                BackColor = Color.Transparent,
                UseMnemonic = false
            };
            itemPnl.Controls.Add(lblD);

            parent.Controls.Add(itemPnl);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle r = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            using (GraphicsPath gp = RoundedPanel.CreateRoundedRect(r, 12))
            {
                Color borderCol = mode == VirtualModalMode.InstallSuccess ? MainForm.ColorSuccess :
                                  (mode == VirtualModalMode.RemoveSuccess ? Color.FromArgb(244, 114, 182) : MainForm.ColorPrimary);
                using (Pen p = new Pen(borderCol, 1.5f))
                {
                    g.DrawPath(p, gp);
                }
            }
        }
    }

    public static class AppConfig
    {
        private static string ConfigFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mobidesk.cfg");
        private static readonly object fileLock = new object();

        public static string Lang = "tr";
        public static int Fps = 60;
        public static int Quality = 65;
        public static bool AdaptiveFps = true;
        public static bool ShowFps = true;
        public static bool BatterySaver = false;
        public static bool IsExtendMode = true;
        public static string LastMonitorDevice = "";
        public static int WindowWidth = 860;
        public static int WindowHeight = 680;
        public static bool MockNotifications = false;

        public static void Load()
        {
            lock (fileLock)
            {
                try
                {
                    if (File.Exists(ConfigFile))
                    {
                        string[] lines = File.ReadAllLines(ConfigFile);
                        foreach (string rawLine in lines)
                        {
                            string line = rawLine.Trim();
                            if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                            int eqIdx = line.IndexOf('=');
                            if (eqIdx <= 0) continue;

                            string key = line.Substring(0, eqIdx).Trim().ToLowerInvariant();
                            string val = line.Substring(eqIdx + 1).Trim();

                            switch (key)
                            {
                                case "lang":
                                    Lang = val.ToLowerInvariant();
                                    break;
                                case "fps":
                                    int fpsVal;
                                    if (int.TryParse(val, out fpsVal))
                                        Fps = Math.Max(15, Math.Min(75, fpsVal));
                                    break;
                                case "quality":
                                    int qVal;
                                    if (int.TryParse(val, out qVal))
                                        Quality = Math.Max(15, Math.Min(100, qVal));
                                    break;
                                case "adaptive_fps":
                                    bool adaptVal;
                                    if (bool.TryParse(val, out adaptVal))
                                        AdaptiveFps = adaptVal;
                                    break;
                                case "show_fps":
                                    bool showFpsVal;
                                    if (bool.TryParse(val, out showFpsVal))
                                        ShowFps = showFpsVal;
                                    break;
                                case "battery_saver":
                                    bool battVal;
                                    if (bool.TryParse(val, out battVal))
                                        BatterySaver = battVal;
                                    break;
                                case "mock":
                                case "mock_notifications":
                                    bool mockVal;
                                    if (bool.TryParse(val, out mockVal))
                                        MockNotifications = mockVal;
                                    break;
                                case "is_extend_mode":
                                case "mode":
                                    if (val.Equals("mirror", StringComparison.OrdinalIgnoreCase) || val.Equals("duplicate", StringComparison.OrdinalIgnoreCase) || val.Equals("false", StringComparison.OrdinalIgnoreCase))
                                        IsExtendMode = false;
                                    else
                                        IsExtendMode = true;
                                    break;
                                case "last_monitor_device":
                                case "monitor":
                                    LastMonitorDevice = val;
                                    break;
                                case "window_w":
                                    int w;
                                    if (int.TryParse(val, out w))
                                        WindowWidth = Math.Max(860, w);
                                    break;
                                case "window_h":
                                    int h;
                                    if (int.TryParse(val, out h))
                                        WindowHeight = Math.Max(680, h);
                                    break;
                            }
                        }
                    }
                }
                catch { }
            }
        }

        public static void Save()
        {
            lock (fileLock)
            {
                try
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("lang=" + (Lang ?? "tr"));
                    sb.AppendLine("fps=" + Fps);
                    sb.AppendLine("quality=" + Quality);
                    sb.AppendLine("adaptive_fps=" + (AdaptiveFps ? "true" : "false"));
                    sb.AppendLine("show_fps=" + (ShowFps ? "true" : "false"));
                    sb.AppendLine("battery_saver=" + (BatterySaver ? "true" : "false"));
                    sb.AppendLine("mode=" + (IsExtendMode ? "extend" : "mirror"));
                    sb.AppendLine("monitor=" + (LastMonitorDevice ?? ""));
                    sb.AppendLine("window_w=" + WindowWidth);
                    sb.AppendLine("window_h=" + WindowHeight);

                    File.WriteAllText(ConfigFile, sb.ToString());
                }
                catch { }
            }
        }
    }

    public static class L10n
    {
        public static bool IsEnglish
        {
            get { return string.Equals(AppConfig.Lang, "en", StringComparison.OrdinalIgnoreCase); }
            set
            {
                AppConfig.Lang = value ? "en" : "tr";
                AppConfig.Save();
            }
        }

        public static void LoadConfig()
        {
            AppConfig.Load();
        }

        public static void SaveConfig()
        {
            AppConfig.Save();
        }

        public static string T(string tr, string en)
        {
            return IsEnglish ? en : tr;
        }
    }

    public static class WindowsStartupManager
    {
        private const string RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string APP_NAME = "MobiDesk";

        public static bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RUN_KEY, false))
                {
                    if (key != null)
                    {
                        object val = key.GetValue(APP_NAME);
                        return val != null;
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Log("IsStartupEnabled error: " + ex.Message);
            }
            return false;
        }

        public static bool SetStartup(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RUN_KEY, true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            string exePath = Application.ExecutablePath;
                            string cmd = "\"" + exePath + "\" --minimized";
                            key.SetValue(APP_NAME, cmd);
                            Program.Log("Startup enabled in registry: " + cmd);
                        }
                        else
                        {
                            key.DeleteValue(APP_NAME, false);
                            Program.Log("Startup disabled in registry");
                        }
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Log("SetStartup error: " + ex.Message);
            }
            return false;
        }
    }

    // ═══════════════════════════════════════════════
    // MOBIDESK FLOW & BİLDİRİM SENKRONİZASYON PROTOKOLÜ (C# 5 UYUMLU)
    // ═══════════════════════════════════════════════

    public static class MobiFlowProtocol
    {
        public const int PORT = 55556;
        public static readonly byte[] MAGIC = new byte[] { 0x4D, 0x46 }; // "MF"

        public const short TYPE_TEXT = 0x0001;
        public const short TYPE_FILE_START = 0x0002;
        public const short TYPE_FILE_CHUNK = 0x0003;
        public const short TYPE_FILE_END = 0x0004;
        public const short TYPE_NOTIFICATION = 0x0005;
        public const short TYPE_NOTIFICATION_REMOVE = 0x0006;
        public const short TYPE_PING = 0x0007;
        public const short TYPE_PONG = 0x0008;
        public const short TYPE_DISMISS_NOTIFICATION = 0x0009;

        public static void WriteNetUTF(BinaryWriter bw, string s)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(s ?? "");
            bw.Write(IPAddress.HostToNetworkOrder((short)bytes.Length));
            bw.Write(bytes);
        }

        public static string ReadNetUTF(BinaryReader br)
        {
            short len = IPAddress.NetworkToHostOrder(br.ReadInt16());
            if (len <= 0) return "";
            byte[] bytes = br.ReadBytes(len);
            return Encoding.UTF8.GetString(bytes);
        }

        public static void WriteNetInt64(BinaryWriter bw, long val)
        {
            bw.Write(IPAddress.HostToNetworkOrder(val));
        }

        public static long ReadNetInt64(BinaryReader br)
        {
            return IPAddress.NetworkToHostOrder(br.ReadInt64());
        }

        public static void WriteNetInt32(BinaryWriter bw, int val)
        {
            bw.Write(IPAddress.HostToNetworkOrder(val));
        }

        public static int ReadNetInt32(BinaryReader br)
        {
            return IPAddress.NetworkToHostOrder(br.ReadInt32());
        }
    }

    public class FlowMessageItem
    {
        public string Id { get; set; }
        public string Sender { get; set; } // "pc" or "mobile"
        public string Text { get; set; }
        public long Timestamp { get; set; }
        public bool IsFile { get; set; }
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public string FilePath { get; set; }
        public int Progress { get; set; }
        public string DeviceSerial { get; set; }
        public string DeviceName { get; set; }

        public FlowMessageItem()
        {
            Progress = 100;
        }
    }

    public class NotificationCardItem
    {
        public int Id { get; set; }
        public string Key { get; set; }
        public string PackageName { get; set; }
        public string AppName { get; set; }
        public string Title { get; set; }
        public string Text { get; set; }
        public long Timestamp { get; set; }
        public string IconBase64 { get; set; }
        public Image CachedIcon { get; set; }
        public string DeviceSerial { get; set; }
        public string DeviceName { get; set; }
    }

    public class ConnectedDeviceItem
    {
        public string Serial { get; set; }
        public string Model { get; set; }
        public string FriendlyName { get; set; }
        public int FlowPort { get; set; }

        public override string ToString()
        {
            return FriendlyName + " (" + Serial + ")";
        }
    }

    public static class MiniJson
    {
        public static Dictionary<string, string> Parse(string json)
        {
            Dictionary<string, string> dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(json)) return dict;
            System.Text.RegularExpressions.Regex regex = new System.Text.RegularExpressions.Regex("\"(?<key>[^\"]+)\"\\s*:\\s*(\"(?<val>[^\"]*)\"|(?<val>[^,}\\]]+))");
            System.Text.RegularExpressions.MatchCollection matches = regex.Matches(json);
            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                dict[m.Groups["key"].Value] = m.Groups["val"].Value.Trim();
            }
            return dict;
        }

        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        }
    }

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
            if (clientThread != null && clientThread.IsAlive) return;
            isRunning = true;
            clientThread = new Thread(ClientWorkerLoop);
            clientThread.IsBackground = true;
            clientThread.Start();
        }

        public void Stop()
        {
            isRunning = false;
            lock (sendLock)
            {
                try { if (activeStream != null) activeStream.Close(); } catch { }
                try { if (activeClient != null) activeClient.Close(); } catch { }
                activeStream = null;
                activeClient = null;
            }
            IsConnected = false;
            manager.NotifyDeviceConnectionChanged(Serial, false);
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

                    IsConnected = true;
                    manager.NotifyDeviceConnectionChanged(Serial, true);
                    Program.Log("Connected to MobiFlow on port " + Port + " for " + Serial);

                    BinaryReader br = new BinaryReader(activeStream);
                    while (isRunning && client.Connected)
                    {
                        int m0 = br.ReadByte();
                        int m1 = br.ReadByte();
                        if (m0 != 0x4D || m1 != 0x46)
                        {
                            continue;
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
                    manager.NotifyDeviceConnectionChanged(Serial, false);
                }

                if (isRunning) Thread.Sleep(1500);
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
                    string fileName = dict.ContainsKey("fileName") ? dict["fileName"] : "dosya";
                    long fileSize = 0;
                    if (dict.ContainsKey("fileSize")) long.TryParse(dict["fileSize"], out fileSize);

                    string targetPath = Path.Combine(MobiFlowManager.DownloadsFolder, fileName);
                    FileStream fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                    activeReceivingFiles[transferId] = fs;

                    FlowMessageItem item = new FlowMessageItem();
                    item.Id = transferId;
                    item.Sender = "mobile";
                    item.IsFile = true;
                    item.FileName = fileName;
                    item.FileSize = fileSize;
                    item.FilePath = targetPath;
                    item.Progress = 0;
                    item.Timestamp = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
                    item.DeviceSerial = this.Serial;
                    item.DeviceName = this.FriendlyName;
                    activeFileItems[transferId] = item;

                    manager.DispatchMessageReceived(item);
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
                            fs.Write(chunk, 0, chunk.Length);
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
                        fs.Flush();
                        fs.Close();
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
                    notif.AppName = dict.ContainsKey("app") ? dict["app"] : notif.PackageName;
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

            SendRawPacket(MobiFlowProtocol.TYPE_TEXT, Encoding.UTF8.GetBytes(json));
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

    public class ModernStudioTabs : Panel
    {
        private int selectedIndex = 0;
        public event Action<int> SelectedTabChanged;

        private int flowBadge = 0;
        private int notificationBadge = 0;

        public int FlowBadge
        {
            get { return flowBadge; }
            set { flowBadge = value; this.Invalidate(); }
        }

        public int NotificationBadge
        {
            get { return notificationBadge; }
            set { notificationBadge = value; this.Invalidate(); }
        }

        public int SelectedIndex
        {
            get { return selectedIndex; }
            set
            {
                if (selectedIndex != value)
                {
                    selectedIndex = value;
                    this.Invalidate();
                    if (SelectedTabChanged != null) SelectedTabChanged(selectedIndex);
                }
            }
        }

        private string[] tabTitles = new string[]
        {
            "🖥️ İkinci Ekran",
            "💬 MobiFlow",
            "🔔 Bildirimler"
        };

        public void UpdateLanguage()
        {
            tabTitles = new string[]
            {
                L10n.T("🖥️ İkinci Ekran", "🖥️ Second Screen"),
                "💬 MobiFlow",
                L10n.T("🔔 Bildirimler", "🔔 Notifications")
            };
            this.Invalidate();
        }

        private int hoveredIndex = -1;

        public ModernStudioTabs(int x, int y, int width, int height)
        {
            this.Location = new Point(x, y);
            this.Size = new Size(width, height);
            this.BackColor = MainForm.ColorBg;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            UpdateLanguage();

            this.MouseMove += delegate(object s, MouseEventArgs e)
            {
                int tabW = this.Width / 3;
                int hIdx = Math.Min(2, Math.Max(0, e.X / tabW));
                if (hoveredIndex != hIdx)
                {
                    hoveredIndex = hIdx;
                    this.Cursor = Cursors.Hand;
                    this.Invalidate();
                }
            };

            this.MouseLeave += delegate(object s, EventArgs e)
            {
                hoveredIndex = -1;
                this.Cursor = Cursors.Default;
                this.Invalidate();
            };

            this.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    int tabW = this.Width / 3;
                    SelectedIndex = Math.Min(2, Math.Max(0, e.X / tabW));
                }
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            int tabW = this.Width / 3;
            int tabH = this.Height;

            // Arka plan rayı (Track)
            using (GraphicsPath trackPath = CreateRoundedRectanglePath(new Rectangle(0, 0, this.Width - 1, tabH - 1), 8))
            using (SolidBrush trackBrush = new SolidBrush(Color.FromArgb(18, 24, 38)))
            using (Pen trackPen = new Pen(MainForm.ColorCardBorder, 1.0f))
            {
                g.FillPath(trackBrush, trackPath);
                g.DrawPath(trackPen, trackPath);
            }

            for (int i = 0; i < 3; i++)
            {
                Rectangle tabRect = new Rectangle(i * tabW + 2, 2, tabW - 4, tabH - 4);
                bool isSelected = (i == selectedIndex);
                bool isHovered = (i == hoveredIndex && !isSelected);

                if (isSelected)
                {
                    using (GraphicsPath selPath = CreateRoundedRectanglePath(tabRect, 6))
                    using (LinearGradientBrush selBrush = new LinearGradientBrush(tabRect, MainForm.ColorPrimary, MainForm.ColorPrimaryDark, 90f))
                    {
                        g.FillPath(selBrush, selPath);
                    }
                }
                else if (isHovered)
                {
                    using (GraphicsPath hovPath = CreateRoundedRectanglePath(tabRect, 6))
                    using (SolidBrush hovBrush = new SolidBrush(Color.FromArgb(28, 38, 58)))
                    {
                        g.FillPath(hovBrush, hovPath);
                    }
                }

                // Metin ve Rozetler
                string title = tabTitles[i];
                if (i == 1 && flowBadge > 0)
                {
                    title += " (" + flowBadge + ")";
                }
                else if (i == 2 && notificationBadge > 0)
                {
                    title += " (" + notificationBadge + ")";
                }

                Color textColor = isSelected ? Color.White : (isHovered ? MainForm.ColorText : MainForm.ColorTextMuted);
                using (Font font = new Font("Segoe UI Emoji", 9.0f, isSelected ? FontStyle.Bold : FontStyle.Regular))
                using (SolidBrush textBrush = new SolidBrush(textColor))
                using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    g.DrawString(title, font, textBrush, tabRect, sf);
                }
            }
        }

        private GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    public class MobiFlowPanel : DoubleBufferedPanel
    {
        private MobiFlowManager flowManager;
        private Panel pnlHeader;
        private Panel pnlBottom;
        internal Panel pnlFlowViewport;
        internal DoubleBufferedPanel pnlMessages;
        internal ModernDarkScrollBar vScrollBar;
        private TextBox txtInput;
        private ModernButton btnSend;
        private ModernButton btnSendFile;
        private ModernButton btnOpenFolder;
        private Label lblTitle;
        private Label lblStatusDot;
        private Label lblStatusText;
        private Label lblDeviceLabel;
        private ModernComboBox cbDeviceSelect;

        private readonly List<FlowMessageItem> messageHistory = new List<FlowMessageItem>();
        private readonly List<ConnectedDeviceItem> knownDevices = new List<ConnectedDeviceItem>();
        private string currentSelectedSerial = null;

        public string SelectedDeviceSerial
        {
            get { return currentSelectedSerial; }
        }

        public MobiFlowPanel(int x, int y, int width, int height, MobiFlowManager manager)
        {
            this.flowManager = manager;
            this.Location = new Point(x, y);
            this.Size = new Size(width, height);
            this.BackColor = MainForm.ColorBg;
            this.AllowDrop = true;

            InitializeUI();
            this.Resize += delegate { UpdateLayout(); };
            UpdateLayout();
        }

        private void InitializeUI()
        {
            this.Controls.Clear();
            int w = this.Width;
            int h = this.Height;

            // 1. Üst Başlık & Eylemler
            pnlHeader = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(w, 42),
                BackColor = MainForm.ColorCard
            };

            lblTitle = new Label
            {
                Text = L10n.T("💬 MobiDesk Flow", "💬 MobiDesk Flow"),
                Font = new Font("Segoe UI", 10.0f, FontStyle.Bold),
                ForeColor = MainForm.ColorText,
                Location = new Point(12, 10),
                AutoSize = true
            };
            pnlHeader.Controls.Add(lblTitle);

            lblStatusDot = new Label
            {
                Text = "●",
                Font = new Font("Segoe UI", 10.0f, FontStyle.Bold),
                ForeColor = MainForm.ColorTextMuted,
                Location = new Point(lblTitle.Right + 8, 10),
                AutoSize = true
            };
            pnlHeader.Controls.Add(lblStatusDot);

            lblStatusText = new Label
            {
                Text = L10n.T("Bekleniyor", "Waiting"),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = MainForm.ColorTextMuted,
                Location = new Point(lblStatusDot.Right + 2, 12),
                AutoSize = true
            };
            pnlHeader.Controls.Add(lblStatusText);

            lblDeviceLabel = new Label
            {
                Text = L10n.T("Cihaz:", "Device:"),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = MainForm.ColorTextMuted,
                Location = new Point(lblStatusText.Right + 14, 12),
                AutoSize = true
            };
            pnlHeader.Controls.Add(lblDeviceLabel);

            cbDeviceSelect = new ModernComboBox
            {
                Location = new Point(lblDeviceLabel.Right + 8, 7),
                Size = new Size(220, 28)
            };
            cbDeviceSelect.SelectedIndexChanged += delegate
            {
                ConnectedDeviceItem sel = cbDeviceSelect.SelectedItem as ConnectedDeviceItem;
                currentSelectedSerial = sel != null ? sel.Serial : null;
                UpdateStatusDisplay();
                RebuildMessagesList();
            };
            pnlHeader.Controls.Add(cbDeviceSelect);

            btnOpenFolder = new ModernButton(L10n.T("📂 İndirilenler", "📂 Downloads"), w - 140, 7, 130, 28, Color.FromArgb(28, 38, 58), MainForm.ColorText, 6);
            btnOpenFolder.Font = new Font("Segoe UI", 8.0f, FontStyle.Bold);
            btnOpenFolder.Click += delegate
            {
                try { Process.Start("explorer.exe", MobiFlowManager.DownloadsFolder); } catch { }
            };
            pnlHeader.Controls.Add(btnOpenFolder);
            this.Controls.Add(pnlHeader);

            // 2. Alt Mesaj Yazma ve Dosya Gönderme Çubuğu
            int bottomH = 48;
            pnlBottom = new Panel
            {
                Location = new Point(0, h - bottomH),
                Size = new Size(w, bottomH),
                BackColor = MainForm.ColorCard
            };

            btnSendFile = new ModernButton(L10n.T("📎 Dosya Gönder", "📎 Send File"), 10, 8, 120, 32, Color.FromArgb(28, 38, 58), MainForm.ColorText, 6);
            btnSendFile.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            btnSendFile.Click += delegate
            {
                using (OpenFileDialog ofd = new OpenFileDialog { Title = L10n.T("MobiDesk Flow - Gönderilecek Dosyayı Seçin", "MobiDesk Flow - Select File to Send"), Multiselect = true })
                {
                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        foreach (string file in ofd.FileNames)
                        {
                            flowManager.SendFile(file, currentSelectedSerial);
                        }
                    }
                }
            };
            pnlBottom.Controls.Add(btnSendFile);

            btnSend = new ModernButton(L10n.T("➤ Gönder", "➤ Send"), w - 90, 8, 80, 32, MainForm.ColorPrimary, Color.White, 6);
            btnSend.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            btnSend.Click += delegate { SendCurrentInput(); };
            pnlBottom.Controls.Add(btnSend);

            txtInput = new TextBox
            {
                Location = new Point(140, 11),
                Size = new Size(Math.Max(100, w - 240), 26),
                BackColor = MainForm.ColorBg,
                ForeColor = MainForm.ColorText,
                Font = new Font("Segoe UI", 10.0f, FontStyle.Regular),
                BorderStyle = BorderStyle.FixedSingle
            };
            txtInput.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    SendCurrentInput();
                }
            };
            pnlBottom.Controls.Add(txtInput);
            this.Controls.Add(pnlBottom);

            UpdateHeaderPositions();

            // 3. Mesaj Görünüm Alanı (Viewport + Content)
            int viewportH = Math.Max(50, h - 44 - bottomH - 2);
            pnlFlowViewport = new Panel
            {
                Location = new Point(0, 44),
                Size = new Size(w, viewportH),
                AutoScroll = false,
                BackColor = MainForm.ColorBg
            };

            pnlMessages = new DoubleBufferedPanel
            {
                Location = new Point(0, 0),
                Size = new Size(w, viewportH),
                BackColor = MainForm.ColorBg,
                AllowDrop = true
            };
            pnlFlowViewport.Controls.Add(pnlMessages);
            this.Controls.Add(pnlFlowViewport);

            // 4. Modern Koyu Kaydırma Çubuğu (Custom Dark ScrollBar)
            vScrollBar = new ModernDarkScrollBar
            {
                Location = new Point(w - 14, 48),
                Size = new Size(10, Math.Max(20, viewportH - 8)),
                Visible = false
            };
            vScrollBar.ValueChanged += delegate
            {
                pnlMessages.Top = -vScrollBar.Value;
            };
            this.Controls.Add(vScrollBar);
            vScrollBar.BringToFront();

            // Fare Tekerleği Desteği (Mouse Wheel)
            AttachWheel(pnlFlowViewport);
            AttachWheel(pnlMessages);

            // Sürükle-Bırak Olayları
            SetupDragAndDrop(this);
            SetupDragAndDrop(pnlFlowViewport);
            SetupDragAndDrop(pnlMessages);
        }

        private void AttachWheel(Control c)
        {
            if (c == null) return;
            c.MouseWheel += delegate(object s, MouseEventArgs e)
            {
                if (vScrollBar != null && vScrollBar.Visible)
                {
                    int step = (e.Delta < 0) ? 60 : -60;
                    vScrollBar.Value += step;
                }
            };
            foreach (Control child in c.Controls)
            {
                AttachWheel(child);
            }
        }

        public void UpdateDeviceList(List<ConnectedDeviceItem> devices)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(delegate { UpdateDeviceList(devices); }));
                return;
            }

            knownDevices.Clear();
            if (devices != null) knownDevices.AddRange(devices);

            string previousSerial = currentSelectedSerial;
            cbDeviceSelect.Items.Clear();

            int selectedIndexToRestore = -1;
            for (int i = 0; i < knownDevices.Count; i++)
            {
                cbDeviceSelect.Items.Add(knownDevices[i]);
                if (string.Equals(knownDevices[i].Serial, previousSerial, StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndexToRestore = i;
                }
            }

            if (cbDeviceSelect.Items.Count > 0)
            {
                cbDeviceSelect.SelectedIndex = (selectedIndexToRestore >= 0) ? selectedIndexToRestore : 0;
                ConnectedDeviceItem sel = cbDeviceSelect.SelectedItem as ConnectedDeviceItem;
                currentSelectedSerial = sel != null ? sel.Serial : null;
            }
            else
            {
                currentSelectedSerial = null;
            }

            UpdateStatusDisplay();
        }

        public void UpdateDeviceConnection(string serial, bool connected)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(delegate { UpdateDeviceConnection(serial, connected); }));
                return;
            }

            if (string.Equals(currentSelectedSerial, serial, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(currentSelectedSerial))
            {
                SetConnected(connected);
            }
        }

        private void UpdateStatusDisplay()
        {
            bool isConn = flowManager != null && flowManager.IsConnected(currentSelectedSerial);
            SetConnected(isConn);
        }

        private void UpdateLayout()
        {
            int curW = this.ClientSize.Width;
            int curH = this.ClientSize.Height;
            if (curW <= 0 || curH <= 0) return;

            int headerH = 42;
            int bottomH = 48;

            if (pnlHeader != null)
            {
                pnlHeader.Location = new Point(0, 0);
                pnlHeader.Size = new Size(curW, headerH);
                if (btnOpenFolder != null)
                {
                    btnOpenFolder.Location = new Point(curW - 140, 7);
                }
            }

            if (pnlBottom != null)
            {
                pnlBottom.Location = new Point(0, curH - bottomH);
                pnlBottom.Size = new Size(curW, bottomH);

                if (btnSendFile != null)
                {
                    btnSendFile.Location = new Point(10, 8);
                }

                if (btnSend != null)
                {
                    btnSend.Location = new Point(curW - 90, 8);
                }

                if (txtInput != null)
                {
                    int inputX = 140;
                    int inputW = Math.Max(100, curW - inputX - 100);
                    txtInput.Location = new Point(inputX, 11);
                    txtInput.Size = new Size(inputW, 26);
                }
            }

            int scrollBarW = 10;
            int viewportY = headerH + 2;
            int viewportH = Math.Max(50, curH - headerH - bottomH - 4);

            if (vScrollBar != null)
            {
                vScrollBar.Location = new Point(curW - scrollBarW - 2, viewportY + 2);
                vScrollBar.Size = new Size(scrollBarW, Math.Max(20, viewportH - 4));
            }

            if (pnlFlowViewport != null)
            {
                int vpW = curW - (vScrollBar != null && vScrollBar.Visible ? (scrollBarW + 4) : 0);
                pnlFlowViewport.Location = new Point(0, viewportY);
                pnlFlowViewport.Size = new Size(vpW, viewportH);
            }

            UpdateScrollBounds(false);
        }

        public void UpdateScrollBounds(bool scrollToBottom)
        {
            if (pnlFlowViewport == null || pnlMessages == null) return;

            int viewportH = pnlFlowViewport.ClientSize.Height;
            if (viewportH <= 0) return;

            int lastY = 10;
            foreach (Control c in pnlMessages.Controls)
            {
                if (c.Bottom > lastY)
                {
                    lastY = c.Bottom;
                }
            }
            int totalContentH = Math.Max(viewportH, lastY + 16);
            pnlMessages.Height = totalContentH;

            bool needsScroll = (pnlMessages.Controls.Count > 0 && lastY + 16 > viewportH);
            if (vScrollBar != null)
            {
                vScrollBar.Visible = needsScroll;
            }

            int scrollBarW = 10;
            int vpW = this.ClientSize.Width - (vScrollBar != null && vScrollBar.Visible ? (scrollBarW + 4) : 0);
            pnlFlowViewport.Size = new Size(vpW, viewportH);
            pnlMessages.Width = vpW;

            // PC tarafı mesaj balonlarını (sağa yaslı) güncel viewport genişliğine uyarla
            foreach (Control c in pnlMessages.Controls)
            {
                bool isMe = string.Equals(c.AccessibleName, "ME", StringComparison.OrdinalIgnoreCase);
                if (isMe)
                {
                    c.Location = new Point(Math.Max(12, vpW - c.Width - 14), c.Top);
                }
            }

            if (vScrollBar != null && vScrollBar.Visible)
            {
                vScrollBar.Minimum = 0;
                vScrollBar.Maximum = totalContentH;
                vScrollBar.LargeChange = viewportH;
                int maxVal = Math.Max(0, totalContentH - viewportH);
                if (scrollToBottom)
                {
                    vScrollBar.Value = maxVal;
                }
                else
                {
                    if (vScrollBar.Value > maxVal) vScrollBar.Value = maxVal;
                }
                pnlMessages.Top = -vScrollBar.Value;
            }
            else
            {
                if (vScrollBar != null) vScrollBar.Value = 0;
                pnlMessages.Top = 0;
            }
        }

        private void SetupDragAndDrop(Control c)
        {
            c.AllowDrop = true;
            c.DragEnter += delegate(object s, DragEventArgs e)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Effect = DragDropEffects.Copy;
                }
            };
            c.DragDrop += delegate(object s, DragEventArgs e)
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null)
                {
                    foreach (string f in files)
                    {
                        flowManager.SendFile(f, currentSelectedSerial);
                    }
                }
            };
        }

        private void SendCurrentInput()
        {
            string text = txtInput.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;
            txtInput.Text = "";
            flowManager.SendTextMessage(text, currentSelectedSerial);
        }

        public void ApplyLanguage()
        {
            if (lblTitle != null) lblTitle.Text = L10n.T("💬 MobiDesk Flow", "💬 MobiDesk Flow");
            if (lblDeviceLabel != null) lblDeviceLabel.Text = L10n.T("Cihaz:", "Device:");
            if (btnOpenFolder != null) btnOpenFolder.Text = L10n.T("📂 İndirilenler", "📂 Downloads");
            if (btnSendFile != null) btnSendFile.Text = L10n.T("📎 Dosya Gönder", "📎 Send File");
            if (btnSend != null) btnSend.Text = L10n.T("➤ Gönder", "➤ Send");

            bool isConn = flowManager != null && flowManager.IsConnected(currentSelectedSerial);
            SetConnected(isConn);
            UpdateHeaderPositions();
            RebuildMessagesList();
        }

        private void UpdateHeaderPositions()
        {
            if (lblTitle == null || lblStatusDot == null || lblStatusText == null || lblDeviceLabel == null || cbDeviceSelect == null) return;
            lblStatusDot.Location = new Point(lblTitle.Right + 8, 10);
            lblStatusText.Location = new Point(lblStatusDot.Right + 2, 12);
            lblDeviceLabel.Location = new Point(lblStatusText.Right + 14, 12);
            cbDeviceSelect.Location = new Point(lblDeviceLabel.Right + 8, 7);
        }

        public void SetConnected(bool connected)
        {
            if (lblStatusDot != null)
            {
                lblStatusDot.ForeColor = connected ? MainForm.ColorSuccess : MainForm.ColorTextMuted;
                lblStatusText.Text = connected ? L10n.T("Bağlandı", "Connected") : L10n.T("Bekleniyor", "Waiting");
                lblStatusText.ForeColor = connected ? MainForm.ColorSuccess : MainForm.ColorTextMuted;
                UpdateHeaderPositions();
            }
        }

        public void AddMessage(FlowMessageItem item)
        {
            if (pnlMessages == null) return;
            messageHistory.Add(item);

            // Gelen mesaj başka bir cihazdan ise otomatik olarak o cihaza odaklan
            if (item.Sender == "mobile" && !string.IsNullOrEmpty(item.DeviceSerial))
            {
                if (!string.Equals(currentSelectedSerial, item.DeviceSerial, StringComparison.OrdinalIgnoreCase))
                {
                    for (int i = 0; i < knownDevices.Count; i++)
                    {
                        if (string.Equals(knownDevices[i].Serial, item.DeviceSerial, StringComparison.OrdinalIgnoreCase))
                        {
                            cbDeviceSelect.SelectedIndex = i;
                            currentSelectedSerial = item.DeviceSerial;
                            UpdateStatusDisplay();
                            RebuildMessagesList();
                            return;
                        }
                    }
                }
            }

            if (ShouldShowMessage(item))
            {
                RenderMessageBubble(item);
            }
        }

        private bool ShouldShowMessage(FlowMessageItem item)
        {
            if (string.IsNullOrEmpty(currentSelectedSerial)) return true;
            if (string.Equals(item.DeviceSerial, currentSelectedSerial, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private void RebuildMessagesList()
        {
            if (pnlMessages == null) return;
            pnlMessages.SuspendLayout();
            pnlMessages.Controls.Clear();

            foreach (var m in messageHistory)
            {
                if (ShouldShowMessage(m))
                {
                    RenderMessageBubble(m, false);
                }
            }
            pnlMessages.ResumeLayout(true);
            UpdateScrollBounds(true);
        }

        private void RenderMessageBubble(FlowMessageItem item)
        {
            RenderMessageBubble(item, true);
        }

        private void RenderMessageBubble(FlowMessageItem item, bool updateScroll)
        {
            bool isMe = (item.Sender == "pc");
            int curW = pnlFlowViewport != null ? pnlFlowViewport.ClientSize.Width : (this.ClientSize.Width - 14);
            if (curW <= 0) curW = 400;
            int panelW = curW - 24;
            int bubbleMaxW = Math.Max(240, (int)(panelW * 0.70f));

            Panel bubble = new Panel();
            bubble.AccessibleName = isMe ? "ME" : "OTHER";
            bubble.BackColor = isMe ? Color.FromArgb(29, 78, 216) : MainForm.ColorCard;
            bubble.Padding = new Padding(10, 8, 10, 8);
            bubble.Tag = item.Id;

            int curY = 6;

            if (knownDevices.Count > 1 && !string.IsNullOrEmpty(item.DeviceName))
            {
                Label lblDevBadge = new Label
                {
                    Text = (isMe ? "PC ➔ " : "") + item.DeviceName,
                    Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                    ForeColor = isMe ? Color.FromArgb(191, 219, 254) : MainForm.ColorAccent,
                    Location = new Point(10, curY),
                    AutoSize = true
                };
                bubble.Controls.Add(lblDevBadge);
                curY += 16;
            }

            if (item.IsFile)
            {
                Label lblFileName = new Label
                {
                    Text = "📁 " + (item.FileName ?? L10n.T("Dosya", "File")),
                    Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                    ForeColor = Color.White,
                    Location = new Point(10, curY),
                    AutoSize = true
                };
                bubble.Controls.Add(lblFileName);
                curY += 20;

                long sizeKb = item.FileSize / 1024;
                string sizeStr = (sizeKb > 1024) ? string.Format("{0:F1} MB", sizeKb / 1024.0) : sizeKb + " KB";
                Label lblSize = new Label
                {
                    Text = (item.Progress < 100) ? string.Format(L10n.T("Aktarılıyor: %{0} • {1}", "Transferring: %{0} • {1}"), item.Progress, sizeStr) : (L10n.T("Tamamlandı • ", "Completed • ") + sizeStr),
                    Font = new Font("Segoe UI", 8.0f, FontStyle.Regular),
                    ForeColor = MainForm.ColorTextMuted,
                    Location = new Point(10, curY),
                    AutoSize = true,
                    Tag = "FILE_STATUS_LABEL"
                };
                bubble.Controls.Add(lblSize);
                curY += 20;

                if (!string.IsNullOrEmpty(item.FilePath))
                {
                    LinkLabel lnkOpen = new LinkLabel
                    {
                        Text = L10n.T("📂 Klasörde Göster", "📂 Show in Folder"),
                        Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                        LinkColor = Color.FromArgb(56, 189, 248),
                        Location = new Point(10, curY),
                        AutoSize = true
                    };
                    string target = item.FilePath;
                    lnkOpen.LinkClicked += delegate
                    {
                        try { Process.Start("explorer.exe", "/select,\"" + target + "\""); } catch { }
                    };
                    bubble.Controls.Add(lnkOpen);
                    curY += 22;
                }
            }
            else
            {
                Label lblText = new Label
                {
                    Text = item.Text,
                    Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                    ForeColor = Color.White,
                    Location = new Point(10, curY),
                    MaximumSize = new Size(bubbleMaxW - 20, 0),
                    AutoSize = true
                };
                bubble.Controls.Add(lblText);
                curY += lblText.Height + 4;

                if (item.Text != null && (item.Text.StartsWith("http://") || item.Text.StartsWith("https://")))
                {
                    LinkLabel lnkUrl = new LinkLabel
                    {
                        Text = L10n.T("🌐 Tarayıcıda Aç", "🌐 Open in Browser"),
                        Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                        LinkColor = Color.FromArgb(56, 189, 248),
                        Location = new Point(10, curY),
                        AutoSize = true
                    };
                    string url = item.Text;
                    lnkUrl.LinkClicked += delegate { try { Process.Start(url); } catch { } };
                    bubble.Controls.Add(lnkUrl);
                    curY += 20;
                }
            }

            Label lblTime = new Label
            {
                Text = DateTime.Now.ToString("HH:mm"),
                Font = new Font("Segoe UI", 7.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(160, 203, 213, 225),
                Location = new Point(10, curY),
                AutoSize = true
            };
            bubble.Controls.Add(lblTime);
            curY += 18;

            bubble.Size = new Size(bubbleMaxW, curY + 4);

            int lastY = 10;
            if (pnlMessages.Controls.Count > 0)
            {
                Control lastC = pnlMessages.Controls[pnlMessages.Controls.Count - 1];
                lastY = lastC.Bottom + 10;
            }

            int bubbleX = isMe ? Math.Max(12, curW - bubble.Width - 14) : 12;
            bubble.Location = new Point(bubbleX, lastY);

            AttachWheel(bubble);
            SetupDragAndDrop(bubble);
            pnlMessages.Controls.Add(bubble);

            if (updateScroll)
            {
                UpdateScrollBounds(true);
            }
        }

        public void UpdateFileProgress(FlowMessageItem item)
        {
            if (pnlMessages == null) return;
            foreach (Control c in pnlMessages.Controls)
            {
                if (c is Panel && string.Equals(c.Tag as string, item.Id))
                {
                    foreach (Control child in c.Controls)
                    {
                        if (child is Label && string.Equals(child.Tag as string, "FILE_STATUS_LABEL"))
                        {
                            long sizeKb = item.FileSize / 1024;
                            string sizeStr = (sizeKb > 1024) ? string.Format("{0:F1} MB", sizeKb / 1024.0) : sizeKb + " KB";
                            child.Text = (item.Progress < 100)
                                ? string.Format(L10n.T("Aktarılıyor: %{0} • {1}", "Transferring: %{0} • {1}"), item.Progress, sizeStr)
                                : (L10n.T("Tamamlandı • ", "Completed • ") + sizeStr);
                        }
                    }
                }
            }
        }
    }

    public class NotificationCardControl : DoubleBufferedPanel
    {
        private bool isHovered = false;
        private static readonly Color BgColor = Color.FromArgb(17, 24, 39);      // #111827
        private static readonly Color BorderNormal = Color.FromArgb(30, 41, 59); // #1E293B
        private static readonly Color BorderHover = Color.FromArgb(59, 130, 246); // #3B82F6

        public NotificationCardControl()
        {
            this.DoubleBuffered = true;
            this.BackColor = BgColor;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.MouseEnter += delegate { isHovered = true; this.Invalidate(); };
            this.MouseLeave += delegate { isHovered = false; this.Invalidate(); };
        }

        public void SetHover(bool h)
        {
            if (isHovered != h)
            {
                isHovered = h;
                this.Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            using (GraphicsPath path = RoundedPanel.CreateRoundedRect(rect, 8))
            {
                using (SolidBrush b = new SolidBrush(BgColor))
                {
                    g.FillPath(b, path);
                }
                Color bCol = isHovered ? BorderHover : BorderNormal;
                using (Pen p = new Pen(bCol, 1.2f))
                {
                    g.DrawPath(p, path);
                }
            }
        }
    }

    public class MobiNotificationsPanel : DoubleBufferedPanel
    {
        internal MobiFlowManager flowManager;
        internal Panel pnlHeader;
        internal ModernButton btnClearAll;
        internal ModernButton btnStartupToggle;
        internal Panel pnlNotifsViewport;
        internal DoubleBufferedPanel pnlNotifsContent;
        internal ModernDarkScrollBar vScrollBar;
        internal Label lblTitle;
        internal Label lblCount;
        internal Label lblStatusDot;
        internal Label lblStatusText;
        internal Label lblEmptyHint;
        internal ModernComboBox cbDeviceFilter;

        internal readonly Dictionary<string, Control> cardMap = new Dictionary<string, Control>();
        internal readonly Dictionary<string, NotificationCardItem> notifItems = new Dictionary<string, NotificationCardItem>();
        internal readonly List<ConnectedDeviceItem> knownDevices = new List<ConnectedDeviceItem>();
        internal string currentFilterSerial = null;

        public MobiNotificationsPanel(int x, int y, int width, int height, MobiFlowManager manager)
        {
            this.flowManager = manager;
            this.Location = new Point(x, y);
            this.Size = new Size(width, height);
            this.BackColor = MainForm.ColorBg;

            InitializeUI();
            this.Resize += delegate { UpdateLayout(); };
            UpdateLayout();
        }

        private void InitializeUI()
        {
            this.Controls.Clear();
            int w = this.Width;
            int h = this.Height;

            // 1. Üst Başlık & Eylemler
            pnlHeader = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(w, 42),
                BackColor = MainForm.ColorCard
            };

            lblTitle = new Label
            {
                Text = L10n.T("🔔 Bildirimler", "🔔 Notifications"),
                Font = new Font("Segoe UI", 10.0f, FontStyle.Bold),
                ForeColor = MainForm.ColorText,
                Location = new Point(12, 10),
                AutoSize = true
            };
            pnlHeader.Controls.Add(lblTitle);

            lblCount = new Label
            {
                Text = "(0)",
                Font = new Font("Segoe UI", 9.0f, FontStyle.Regular),
                ForeColor = MainForm.ColorTextMuted,
                Location = new Point(lblTitle.Right + 8, 11),
                AutoSize = true
            };
            pnlHeader.Controls.Add(lblCount);

            lblStatusDot = new Label
            {
                Text = "●",
                Font = new Font("Segoe UI", 10.0f, FontStyle.Bold),
                ForeColor = MainForm.ColorTextMuted,
                Location = new Point(lblCount.Right + 8, 10),
                AutoSize = true
            };
            pnlHeader.Controls.Add(lblStatusDot);

            lblStatusText = new Label
            {
                Text = L10n.T("Bekleniyor", "Waiting"),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = MainForm.ColorTextMuted,
                Location = new Point(lblStatusDot.Right + 2, 12),
                AutoSize = true
            };
            pnlHeader.Controls.Add(lblStatusText);

            cbDeviceFilter = new ModernComboBox
            {
                Location = new Point(lblStatusText.Right + 12, 7),
                Size = new Size(160, 28)
            };
            cbDeviceFilter.Items.Add(L10n.T("🌐 Tüm Cihazlar", "🌐 All Devices"));
            cbDeviceFilter.SelectedIndex = 0;
            cbDeviceFilter.SelectedIndexChanged += delegate
            {
                if (cbDeviceFilter.SelectedIndex <= 0)
                {
                    currentFilterSerial = null;
                }
                else
                {
                    ConnectedDeviceItem sel = cbDeviceFilter.SelectedItem as ConnectedDeviceItem;
                    currentFilterSerial = sel != null ? sel.Serial : null;
                }
                ApplyFilter();
            };
            pnlHeader.Controls.Add(cbDeviceFilter);

            btnStartupToggle = new ModernButton("", w - 280, 7, 145, 28, Color.FromArgb(28, 38, 58), MainForm.ColorText, 6);
            btnStartupToggle.Font = new Font("Segoe UI", 8.0f, FontStyle.Bold);
            UpdateStartupButtonText();
            btnStartupToggle.Click += delegate
            {
                bool newState = !WindowsStartupManager.IsStartupEnabled();
                WindowsStartupManager.SetStartup(newState);
                UpdateStartupButtonText();
            };
            pnlHeader.Controls.Add(btnStartupToggle);

            btnClearAll = new ModernButton(L10n.T("🗑️ Tümünü Temizle", "🗑️ Clear All"), w - 122, 7, 114, 28, Color.FromArgb(28, 38, 58), MainForm.ColorText, 6);
            btnClearAll.Font = new Font("Segoe UI", 8.0f, FontStyle.Bold);
            btnClearAll.Click += delegate
            {
                cardMap.Clear();
                notifItems.Clear();
                List<Control> toRemove = new List<Control>();
                foreach (Control c in pnlNotifsContent.Controls)
                {
                    if (c != lblEmptyHint) toRemove.Add(c);
                }
                foreach (Control c in toRemove)
                {
                    pnlNotifsContent.Controls.Remove(c);
                    c.Dispose();
                }
                RearrangeCards();
                UpdateCount();
            };
            pnlHeader.Controls.Add(btnClearAll);
            this.Controls.Add(pnlHeader);

            // 2. Bildirimler Görünüm Alanı (Viewport + Content)
            pnlNotifsViewport = new Panel
            {
                Location = new Point(0, 44),
                Size = new Size(w, Math.Max(50, h - 46)),
                AutoScroll = false,
                BackColor = MainForm.ColorBg
            };

            pnlNotifsContent = new DoubleBufferedPanel
            {
                Location = new Point(0, 0),
                Size = new Size(w, Math.Max(50, h - 46)),
                BackColor = MainForm.ColorBg
            };
            pnlNotifsViewport.Controls.Add(pnlNotifsContent);
            this.Controls.Add(pnlNotifsViewport);

            // 3. Modern Koyu Kaydırma Çubuğu (Custom Dark ScrollBar)
            vScrollBar = new ModernDarkScrollBar
            {
                Location = new Point(w - 14, 48),
                Size = new Size(10, Math.Max(50, h - 54)),
                Visible = false
            };
            vScrollBar.ValueChanged += delegate
            {
                pnlNotifsContent.Top = -vScrollBar.Value;
            };
            this.Controls.Add(vScrollBar);
            vScrollBar.BringToFront();

            // Fare Tekerleği Desteği (Mouse Wheel)
            AttachWheel(pnlNotifsViewport);
            AttachWheel(pnlNotifsContent);

            // Boş Bildirim İpucu
            lblEmptyHint = new Label
            {
                Text = L10n.T(
                    "🔔 Henüz yeni bir bildirim yok\n\nTelefonunuz USB ile şarj olurken MobiDesk kapalı olsa bile gelen bildirimler buraya aktarılır.\n(Cihazınızda MobiDesk Bildirim İzninin açık olduğundan emin olun.)",
                    "🔔 No new notifications yet\n\nIncoming notifications mirror here even if MobiDesk is closed while charging over USB.\n(Make sure MobiDesk Notification Access is enabled on your device.)"
                ),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                ForeColor = MainForm.ColorTextMuted,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(Math.Max(200, w - 40), 120),
                Location = new Point(20, 60),
                Visible = true
            };
            pnlNotifsContent.Controls.Add(lblEmptyHint);
            lblEmptyHint.BringToFront();

            UpdateHeaderPositions();
        }

        private void AttachWheel(Control c)
        {
            if (c == null) return;
            c.MouseWheel += delegate(object s, MouseEventArgs e)
            {
                if (vScrollBar != null && vScrollBar.Visible)
                {
                    int step = (e.Delta < 0) ? 60 : -60;
                    vScrollBar.Value += step;
                }
            };
            foreach (Control child in c.Controls)
            {
                AttachWheel(child);
            }
        }

        public void ApplyLanguage()
        {
            if (lblTitle != null) lblTitle.Text = L10n.T("🔔 Bildirimler", "🔔 Notifications");
            if (btnClearAll != null) btnClearAll.Text = L10n.T("🗑️ Tümünü Temizle", "🗑️ Clear All");
            UpdateStartupButtonText();
            if (lblEmptyHint != null)
            {
                lblEmptyHint.Text = L10n.T(
                    "🔔 Henüz yeni bir bildirim yok\n\nTelefonunuz USB ile şarj olurken MobiDesk kapalı olsa bile gelen bildirimler buraya aktarılır.\n(Cihazınızda MobiDesk Bildirim İzninin açık olduğundan emin olun.)",
                    "🔔 No new notifications yet\n\nIncoming notifications mirror here even if MobiDesk is closed while charging over USB.\n(Make sure MobiDesk Notification Access is enabled on your device.)"
                );
            }
            if (cbDeviceFilter != null && cbDeviceFilter.Items.Count > 0)
            {
                cbDeviceFilter.Items[0] = L10n.T("🌐 Tüm Cihazlar", "🌐 All Devices");
                cbDeviceFilter.Invalidate();
            }
            foreach (var card in cardMap.Values)
            {
                foreach (Control child in card.Controls)
                {
                    if (child is ModernButton && string.Equals(child.Tag as string, "BTN_DISMISS"))
                    {
                        child.Text = L10n.T("✕ Kapat", "✕ Dismiss");
                    }
                }
            }
            bool isConn = flowManager != null && flowManager.IsConnected();
            SetConnected(isConn);
            UpdateCount();
            UpdateHeaderPositions();
        }

        private void UpdateHeaderPositions()
        {
            if (pnlHeader == null || lblTitle == null || lblCount == null || lblStatusDot == null || lblStatusText == null || cbDeviceFilter == null || btnClearAll == null || btnStartupToggle == null) return;

            int curW = pnlHeader.ClientSize.Width;
            int rightMargin = 12;

            btnClearAll.Location = new Point(curW - btnClearAll.Width - rightMargin, 7);
            btnStartupToggle.Location = new Point(btnClearAll.Left - btnStartupToggle.Width - 8, 7);

            int maxLeftX = btnStartupToggle.Left - 12;

            int leftX = 12;
            lblTitle.Location = new Point(leftX, 10);
            leftX += lblTitle.Width + 6;

            lblCount.Location = new Point(leftX, 11);
            leftX += lblCount.Width + 6;

            lblStatusDot.Location = new Point(leftX, 10);
            leftX += lblStatusDot.Width + 2;

            lblStatusText.Location = new Point(leftX, 12);
            leftX += lblStatusText.Width + 10;

            int availFilterW = maxLeftX - leftX;
            if (availFilterW >= 130)
            {
                cbDeviceFilter.Visible = true;
                cbDeviceFilter.Location = new Point(leftX, 7);
                cbDeviceFilter.Width = Math.Min(170, availFilterW);
            }
            else if (availFilterW >= 80)
            {
                cbDeviceFilter.Visible = true;
                cbDeviceFilter.Location = new Point(leftX, 7);
                cbDeviceFilter.Width = availFilterW;
            }
            else
            {
                cbDeviceFilter.Visible = false;
            }
        }

        public void UpdateDeviceList(List<ConnectedDeviceItem> devices)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(delegate { UpdateDeviceList(devices); }));
                return;
            }

            knownDevices.Clear();
            if (devices != null) knownDevices.AddRange(devices);

            string prevFilter = currentFilterSerial;
            cbDeviceFilter.Items.Clear();
            cbDeviceFilter.Items.Add(L10n.T("🌐 Tüm Cihazlar", "🌐 All Devices"));

            int restoreIdx = 0;
            for (int i = 0; i < knownDevices.Count; i++)
            {
                cbDeviceFilter.Items.Add(knownDevices[i]);
                if (string.Equals(knownDevices[i].Serial, prevFilter, StringComparison.OrdinalIgnoreCase))
                {
                    restoreIdx = i + 1;
                }
            }

            cbDeviceFilter.SelectedIndex = restoreIdx;
            ApplyFilter();
        }

        public void UpdateDeviceConnection(string serial, bool connected)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(delegate { UpdateDeviceConnection(serial, connected); }));
                return;
            }

            if (flowManager != null)
            {
                SetConnected(flowManager.IsConnected());
            }
        }

        private void ApplyFilter()
        {
            if (pnlNotifsContent == null) return;
            pnlNotifsContent.SuspendLayout();
            foreach (var kvp in cardMap)
            {
                string key = kvp.Key;
                Control card = kvp.Value;
                NotificationCardItem item;
                if (notifItems.TryGetValue(key, out item))
                {
                    bool visible = string.IsNullOrEmpty(currentFilterSerial) ||
                                   string.Equals(item.DeviceSerial, currentFilterSerial, StringComparison.OrdinalIgnoreCase);
                    card.Visible = visible;
                }
            }
            RearrangeCards();
            pnlNotifsContent.ResumeLayout(true);
            UpdateCount();
        }

        private void UpdateLayout()
        {
            int curW = this.ClientSize.Width;
            int curH = this.ClientSize.Height;
            if (curW <= 0 || curH <= 0) return;

            int headerH = 42;
            if (pnlHeader != null)
            {
                pnlHeader.Location = new Point(0, 0);
                pnlHeader.Size = new Size(curW, headerH);
                UpdateHeaderPositions();
            }

            int scrollBarW = 10;
            if (vScrollBar != null)
            {
                vScrollBar.Location = new Point(curW - scrollBarW - 2, headerH + 6);
                vScrollBar.Size = new Size(scrollBarW, Math.Max(50, curH - headerH - 12));
            }

            if (pnlNotifsViewport != null)
            {
                int vpW = curW - (vScrollBar != null && vScrollBar.Visible ? (scrollBarW + 4) : 0);
                pnlNotifsViewport.Location = new Point(0, headerH + 2);
                pnlNotifsViewport.Size = new Size(vpW, Math.Max(50, curH - headerH - 4));
                if (lblEmptyHint != null)
                {
                    lblEmptyHint.Size = new Size(Math.Max(200, vpW - 40), 120);
                }
            }

            RearrangeCards();
        }

        public void UpdateStartupState()
        {
            UpdateStartupButtonText();
        }

        private void UpdateStartupButtonText()
        {
            if (btnStartupToggle == null) return;
            bool enabled = WindowsStartupManager.IsStartupEnabled();
            btnStartupToggle.Text = enabled
                ? L10n.T("🪟 Başlangıç: Açık", "🪟 Startup: On")
                : L10n.T("🪟 Başlangıç: Kapalı", "🪟 Startup: Off");
            btnStartupToggle.ForeColor = enabled
                ? MainForm.ColorSuccess
                : MainForm.ColorTextMuted;
        }

        public void SetConnected(bool connected)
        {
            if (lblStatusDot != null)
            {
                lblStatusDot.ForeColor = connected ? MainForm.ColorSuccess : MainForm.ColorTextMuted;
                lblStatusText.Text = connected ? L10n.T("Bağlandı", "Connected") : L10n.T("Bekleniyor", "Waiting");
                lblStatusText.ForeColor = connected ? MainForm.ColorSuccess : MainForm.ColorTextMuted;
                UpdateHeaderPositions();
            }
        }

        public void LoadMockNotifications()
        {
            string[] senders = new string[] { "WhatsApp", "Instagram", "Gmail", "Telegram", "Ziraat Mobil", "YouTube", "Twitter / X", "Slack", "Messages" };
            for (int mi = 0; mi < senders.Length; mi++)
            {
                AddNotification(new NotificationCardItem
                {
                    Id = 3000 + mi,
                    Key = "mock_key_" + mi,
                    PackageName = "com.mock." + senders[mi].ToLower().Replace(" ", ""),
                    AppName = senders[mi],
                    DeviceName = "Samsung SM_M315F",
                    Title = senders[mi] + " - Yeni Bildirim #" + (mi + 1),
                    Text = "Örnek bildirim içeriği ve detaylı mesaj metni burada gösterilir. (" + (mi + 1) + ")",
                    Timestamp = DateTime.Now.Ticks
                });
            }
        }

        public void AddNotification(NotificationCardItem item)
        {
            if (pnlNotifsContent == null || pnlNotifsViewport == null) return;
            if (lblEmptyHint != null) lblEmptyHint.Visible = false;

            if (cardMap.ContainsKey(item.Key))
            {
                RemoveNotification(item.Key);
            }

            notifItems[item.Key] = item;

            int viewportW = pnlNotifsViewport.ClientSize.Width;
            int scrollOffset = (vScrollBar != null && vScrollBar.Visible) ? 22 : 0;
            int cardW = Math.Max(200, viewportW - 24 - scrollOffset);

            NotificationCardControl card = new NotificationCardControl
            {
                Size = new Size(cardW, 72),
                Tag = item
            };

            // İkon
            PictureBox pbIcon = new PictureBox
            {
                Size = new Size(36, 36),
                Location = new Point(14, 18),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            if (item.CachedIcon != null)
            {
                pbIcon.Image = item.CachedIcon;
            }
            else
            {
                Bitmap bmp = new Bitmap(36, 36);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.FromArgb(28, 38, 58));
                    using (Font f = new Font("Segoe UI Emoji", 12.0f, FontStyle.Regular))
                    {
                        g.DrawString("🔔", f, Brushes.White, 4, 4);
                    }
                }
                pbIcon.Image = bmp;
            }
            card.Controls.Add(pbIcon);

            // Bilgi ve metinler
            int textW = Math.Max(80, cardW - 58 - 74 - 16);
            string devTag = !string.IsNullOrEmpty(item.DeviceName) ? ("  •  [" + item.DeviceName + "]") : "";

            Label lblApp = new Label
            {
                Text = item.AppName + devTag + "  •  " + DateTime.Now.ToString("HH:mm"),
                Font = new Font("Segoe UI", 8.0f, FontStyle.Bold),
                ForeColor = MainForm.ColorAccent,
                Location = new Point(58, 8),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            card.Controls.Add(lblApp);

            Label lblTitle = new Label
            {
                Text = item.Title,
                Font = new Font("Segoe UI", 9.0f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(58, 26),
                Size = new Size(textW, 18),
                AutoEllipsis = true,
                Tag = "LBL_TITLE",
                BackColor = Color.Transparent
            };
            card.Controls.Add(lblTitle);

            Label lblBody = new Label
            {
                Text = item.Text,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = MainForm.ColorTextMuted,
                Location = new Point(58, 46),
                Size = new Size(textW, 18),
                AutoEllipsis = true,
                Tag = "LBL_BODY",
                BackColor = Color.Transparent
            };
            card.Controls.Add(lblBody);

            // Kapat (Dismiss) Butonu
            ModernButton btnDismiss = new ModernButton(L10n.T("✕ Kapat", "✕ Dismiss"), cardW - 74, 22, 64, 28, Color.FromArgb(28, 38, 58), MainForm.ColorTextMuted, 5);
            btnDismiss.Font = new Font("Segoe UI", 8.0f, FontStyle.Bold);
            btnDismiss.Tag = "BTN_DISMISS";
            btnDismiss.Click += delegate
            {
                if (flowManager != null)
                {
                    try { flowManager.DismissNotification(item.PackageName, item.Id, item.Key, item.DeviceSerial); } catch { }
                }
                RemoveNotification(item.Key);
            };
            card.Controls.Add(btnDismiss);

            // Çocuklar üzerinde fare dolaşırken kartın ışıldaması
            foreach (Control child in card.Controls)
            {
                if (child != btnDismiss)
                {
                    child.MouseEnter += delegate { card.SetHover(true); };
                    child.MouseLeave += delegate
                    {
                        Point p = card.PointToClient(Cursor.Position);
                        if (!card.ClientRectangle.Contains(p)) card.SetHover(false);
                    };
                }
            }

            AttachWheel(card);

            cardMap[item.Key] = card;

            bool isVisible = string.IsNullOrEmpty(currentFilterSerial) ||
                             string.Equals(item.DeviceSerial, currentFilterSerial, StringComparison.OrdinalIgnoreCase);
            card.Visible = isVisible;

            pnlNotifsContent.Controls.Add(card);
            RearrangeCards();
            UpdateCount();
        }

        public void RemoveNotification(string key)
        {
            if (pnlNotifsContent == null) return;
            Control c;
            if (cardMap.TryGetValue(key, out c))
            {
                cardMap.Remove(key);
                notifItems.Remove(key);
                pnlNotifsContent.Controls.Remove(c);
                c.Dispose();
                RearrangeCards();
                UpdateCount();
            }
        }

        private void RearrangeCards()
        {
            if (pnlNotifsViewport == null || pnlNotifsContent == null) return;

            int viewportH = pnlNotifsViewport.ClientSize.Height;
            if (viewportH <= 0) return;

            int visibleCount = 0;
            int estimatedTotalH = 10;
            foreach (Control c in pnlNotifsContent.Controls)
            {
                if (c == lblEmptyHint || !c.Visible) continue;
                estimatedTotalH += c.Height + 8;
                visibleCount++;
            }

            bool needsScroll = (estimatedTotalH > viewportH);
            if (vScrollBar != null)
            {
                vScrollBar.Visible = (visibleCount > 0 && needsScroll);
            }

            int scrollBarW = 10;
            int vpW = this.ClientSize.Width - (vScrollBar != null && vScrollBar.Visible ? (scrollBarW + 4) : 0);
            pnlNotifsViewport.Size = new Size(vpW, Math.Max(50, this.ClientSize.Height - 44 - 4));
            pnlNotifsContent.Width = vpW;

            int targetCardW = Math.Max(200, vpW - 24);

            int curY = 10;
            foreach (Control c in pnlNotifsContent.Controls)
            {
                if (c == lblEmptyHint || !c.Visible) continue;

                c.Location = new Point(12, curY);
                c.Width = targetCardW;

                // Kartın içindeki kontrolleri dinamik yeniden konumlandır
                foreach (Control child in c.Controls)
                {
                    if (child is ModernButton && string.Equals(child.Tag as string, "BTN_DISMISS"))
                    {
                        child.Location = new Point(targetCardW - child.Width - 12, (c.Height - child.Height) / 2);
                    }
                    else if (child is Label)
                    {
                        string tag = child.Tag as string;
                        if (tag == "LBL_TITLE" || tag == "LBL_BODY")
                        {
                            int textW = Math.Max(80, targetCardW - 58 - 74 - 16);
                            child.Width = textW;
                        }
                    }
                }

                curY += c.Height + 8;
            }

            int totalContentH = Math.Max(viewportH, curY + 12);
            pnlNotifsContent.Height = totalContentH;

            if (vScrollBar != null && vScrollBar.Visible)
            {
                vScrollBar.Minimum = 0;
                vScrollBar.Maximum = totalContentH;
                vScrollBar.LargeChange = viewportH;
                int maxVal = Math.Max(0, totalContentH - viewportH);
                if (vScrollBar.Value > maxVal) vScrollBar.Value = maxVal;
                pnlNotifsContent.Top = -vScrollBar.Value;
            }
            else
            {
                if (vScrollBar != null) vScrollBar.Value = 0;
                pnlNotifsContent.Top = 0;
            }

            if (lblEmptyHint != null)
            {
                lblEmptyHint.Visible = (visibleCount == 0);
                lblEmptyHint.Location = new Point(20, 60);
                lblEmptyHint.Size = new Size(Math.Max(200, vpW - 40), 120);
            }
        }

        private void UpdateCount()
        {
            if (lblCount != null)
            {
                int count = 0;
                foreach (var c in cardMap.Values)
                {
                    if (c.Visible) count++;
                }
                lblCount.Text = string.Format("({0})", count);
                UpdateHeaderPositions();
            }
        }
    }

    public static class MobiTestSuite
    {
        private static int totalTests = 0;
        private static int passedTests = 0;
        private static int failedTests = 0;
        private static readonly StringBuilder report = new StringBuilder();

        private static void Assert(bool condition, string testName, string details = "")
        {
            totalTests++;
            if (condition)
            {
                passedTests++;
                string line = string.Format("[PASS] {0}{1}", testName, string.IsNullOrEmpty(details) ? "" : " (" + details + ")");
                report.AppendLine(line);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(line);
                Console.ResetColor();
            }
            else
            {
                failedTests++;
                string line = string.Format("[FAIL] {0}{1}", testName, string.IsNullOrEmpty(details) ? "" : " (" + details + ")");
                report.AppendLine(line);
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(line);
                Console.ResetColor();
            }
        }

        public static int RunAll()
        {
            report.AppendLine("═══════════════════════════════════════════════════════════");
            report.AppendLine("   MOBIDESK COMPREHENSIVE AUTOMATED TEST SUITE   ");
            report.AppendLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine(report.ToString());

            try { TestModernDarkScrollBar(); } catch (Exception ex) { Assert(false, "TestModernDarkScrollBar Exception", ex.ToString()); }
            try { TestFormMinimumSize(); } catch (Exception ex) { Assert(false, "TestFormMinimumSize Exception", ex.ToString()); }
            try { TestHeaderAntiCollision(); } catch (Exception ex) { Assert(false, "TestHeaderAntiCollision Exception", ex.ToString()); }
            try { TestNotificationsResponsiveCards(); } catch (Exception ex) { Assert(false, "TestNotificationsResponsiveCards Exception", ex.ToString()); }
            try { TestScrollBarVisibilityAndScrolling(); } catch (Exception ex) { Assert(false, "TestScrollBarVisibilityAndScrolling Exception", ex.ToString()); }
            try { TestNotificationDismissAndClearAll(); } catch (Exception ex) { Assert(false, "TestNotificationDismissAndClearAll Exception", ex.ToString()); }
            try { TestDeviceFiltering(); } catch (Exception ex) { Assert(false, "TestDeviceFiltering Exception", ex.ToString()); }
            try { TestFlowPanelScrollBar(); } catch (Exception ex) { Assert(false, "TestFlowPanelScrollBar Exception", ex.ToString()); }

            report.AppendLine("═══════════════════════════════════════════════════════════");
            string summary = string.Format("RESULTS: Total={0}, Passed={1}, Failed={2}", totalTests, passedTests, failedTests);
            report.AppendLine(summary);
            report.AppendLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine(summary);

            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_results.log");
                File.WriteAllText(logPath, report.ToString());
            }
            catch { }

            return failedTests == 0 ? 0 : 1;
        }

        private static void TestModernDarkScrollBar()
        {
            using (ModernDarkScrollBar sb = new ModernDarkScrollBar())
            {
                sb.Size = new Size(10, 400);

                Assert(sb.Minimum == 0 && sb.Maximum == 100 && sb.Value == 0,
                    "ScrollBar: Initial default bounds and value",
                    string.Format("Min={0}, Max={1}, Val={2}", sb.Minimum, sb.Maximum, sb.Value));

                Assert(sb.Width == 10, "ScrollBar: Slim 10px width");

                // Clamp below minimum
                sb.Value = -50;
                Assert(sb.Value == 0, "ScrollBar: Negative value clamped to minimum (0)");

                // Clamp above maximum - largeChange
                sb.Maximum = 500;
                sb.LargeChange = 100;
                sb.Value = 1000;
                Assert(sb.Value == 400, "ScrollBar: Clamped to (Maximum - LargeChange)",
                    string.Format("Val={0}, Expected=400", sb.Value));

                // ValueChanged event firing
                bool changedFired = false;
                sb.ValueChanged += delegate { changedFired = true; };
                sb.Value = 250;
                Assert(changedFired && sb.Value == 250, "ScrollBar: ValueChanged fired on valid change");

                changedFired = false;
                sb.Value = 250;
                Assert(!changedFired, "ScrollBar: ValueChanged NOT fired when value is unchanged");

                // Maximum update auto-clamp
                sb.Value = 200;
                sb.Maximum = 250; // maxPossible = 250 - 100 = 150
                Assert(sb.Value == 150, "ScrollBar: Maximum reduction auto-clamps Value",
                    string.Format("Val={0}, Expected=150", sb.Value));

                // LargeChange update auto-clamp
                sb.Value = 100;
                sb.LargeChange = 200; // maxPossible = 250 - 200 = 50
                Assert(sb.Value == 50, "ScrollBar: LargeChange increase auto-clamps Value",
                    string.Format("Val={0}, Expected=50", sb.Value));

                // Thumb rectangle geometry
                Rectangle tr = sb.GetThumbRectForTest();
                Assert(tr.Height >= 32, "ScrollBar: Thumb minimum pill height >= 32px",
                    string.Format("Height={0}", tr.Height));
                Assert(tr.Bottom <= sb.Height, "ScrollBar: Thumb rect stays inside control bounds",
                    string.Format("Bottom={0}, SbHeight={1}", tr.Bottom, sb.Height));
            }
        }

        private static void TestFormMinimumSize()
        {
            using (MainForm f = new MainForm(false, null, false, 0, false))
            {
                f.reallyExit = true;
                Assert(f.MinimumSize.Width >= 860, "Form: MinimumSize Width >= 860",
                    string.Format("Width={0}", f.MinimumSize.Width));
                Assert(f.MinimumSize.Height >= 680, "Form: MinimumSize Height >= 680",
                    string.Format("Height={0}", f.MinimumSize.Height));

                f.ClientSize = new Size(860, 680);

                Assert(f.cardOpts != null && f.pnlRightCol != null, "Layout: cardOpts and pnlRightCol exist");
                if (f.cardOpts != null && f.pnlRightCol != null)
                {
                    Assert(f.cardOpts.Bottom <= f.pnlRightCol.Height,
                        "Layout [860x680]: cardOpts fits inside right column",
                        string.Format("cardOpts.B={0}, pnlRightCol.H={1}", f.cardOpts.Bottom, f.pnlRightCol.Height));
                }

                if (f.swStartup != null && f.cardOpts != null)
                {
                    Assert(f.swStartup.Bottom <= f.cardOpts.Height,
                        "Layout [860x680]: swStartup fits inside cardOpts without clipping",
                        string.Format("swStartup.B={0}, cardOpts.H={1}", f.swStartup.Bottom, f.cardOpts.Height));
                }

                if (f.btnStartStop != null && f.pnlBottomBar != null)
                {
                    Assert(f.btnStartStop.Bottom <= f.pnlBottomBar.Top,
                        "Layout [860x680]: btnStartStop does not overlap bottom bar",
                        string.Format("btnStartStop.B={0}, pnlBottomBar.T={1}", f.btnStartStop.Bottom, f.pnlBottomBar.Top));
                    Assert(f.pnlBottomBar.Bottom <= f.ClientSize.Height,
                        "Layout [860x680]: pnlBottomBar fits inside client height",
                        string.Format("pnlBottomBar.B={0}, ClientH={1}", f.pnlBottomBar.Bottom, f.ClientSize.Height));
                }

                f.Close();
            }

            Assert(AppConfig.WindowWidth >= 860, "AppConfig: Minimum WindowWidth enforced >= 860",
                string.Format("WindowWidth={0}", AppConfig.WindowWidth));
            Assert(AppConfig.WindowHeight >= 680, "AppConfig: Minimum WindowHeight enforced >= 680",
                string.Format("WindowHeight={0}", AppConfig.WindowHeight));
        }

        private static void TestHeaderAntiCollision()
        {
            bool origLang = L10n.IsEnglish;
            try
            {
                // Test in English
                L10n.IsEnglish = true;
                using (MobiNotificationsPanel pnl = new MobiNotificationsPanel(0, 0, 836, 500, null))
                {
                    pnl.ApplyLanguage();
                    VerifyHeaderBounds(pnl, "English");
                }

                // Test in Turkish
                L10n.IsEnglish = false;
                using (MobiNotificationsPanel pnl = new MobiNotificationsPanel(0, 0, 836, 500, null))
                {
                    pnl.ApplyLanguage();
                    VerifyHeaderBounds(pnl, "Turkish");
                }
            }
            finally
            {
                L10n.IsEnglish = origLang;
            }
        }

        private static void VerifyHeaderBounds(MobiNotificationsPanel pnl, string langLabel)
        {
            Assert(pnl.pnlHeader != null, "Header [" + langLabel + "]: Header panel exists");
            if (pnl.pnlHeader == null) return;

            Assert(pnl.lblTitle.Right < pnl.lblCount.Left,
                "Header [" + langLabel + "]: Title does not collide with Count",
                string.Format("Title.R={0}, Count.L={1}", pnl.lblTitle.Right, pnl.lblCount.Left));

            Assert(pnl.lblCount.Right < pnl.lblStatusDot.Left,
                "Header [" + langLabel + "]: Count does not collide with StatusDot",
                string.Format("Count.R={0}, Dot.L={1}", pnl.lblCount.Right, pnl.lblStatusDot.Left));

            Assert(pnl.lblStatusDot.Right < pnl.lblStatusText.Left,
                "Header [" + langLabel + "]: StatusDot does not collide with StatusText",
                string.Format("Dot.R={0}, Status.L={1}", pnl.lblStatusDot.Right, pnl.lblStatusText.Left));

            if (pnl.cbDeviceFilter.Visible)
            {
                Assert(pnl.lblStatusText.Right < pnl.cbDeviceFilter.Left,
                    "Header [" + langLabel + "]: StatusText does not collide with Filter",
                    string.Format("Status.R={0}, Filter.L={1}", pnl.lblStatusText.Right, pnl.cbDeviceFilter.Left));

                Assert(pnl.cbDeviceFilter.Right < pnl.btnStartupToggle.Left,
                    "Header [" + langLabel + "]: Filter does not collide with Startup button",
                    string.Format("Filter.R={0}, Startup.L={1}", pnl.cbDeviceFilter.Right, pnl.btnStartupToggle.Left));
            }

            Assert(pnl.btnStartupToggle.Right < pnl.btnClearAll.Left,
                "Header [" + langLabel + "]: Startup button does not collide with Clear All button",
                string.Format("Startup.R={0}, Clear.L={1}", pnl.btnStartupToggle.Right, pnl.btnClearAll.Left));

            Assert(pnl.btnClearAll.Right <= pnl.pnlHeader.Width,
                "Header [" + langLabel + "]: Clear All button does not overflow header width",
                string.Format("Clear.R={0}, Header.W={1}", pnl.btnClearAll.Right, pnl.pnlHeader.Width));
        }

        private static void TestNotificationsResponsiveCards()
        {
            int[] testWidths = new int[] { 836, 1024, 1400 };
            foreach (int tw in testWidths)
            {
                using (MobiNotificationsPanel pnl = new MobiNotificationsPanel(0, 0, tw, 500, null))
                {
                    pnl.AddNotification(new NotificationCardItem
                    {
                        Id = 101,
                        Key = "resp_test_" + tw,
                        PackageName = "com.mobidesk.test",
                        AppName = "TestApp",
                        Title = "Notification with fairly long title text to check truncation and bounds",
                        Text = "Body line with sufficient content to verify responsive card layout",
                        DeviceSerial = "DEV_RESP",
                        Timestamp = DateTime.UtcNow.Ticks
                    });

                    Control card;
                    Assert(pnl.cardMap.TryGetValue("resp_test_" + tw, out card),
                        "Responsive [" + tw + "px]: Card added to map");

                    if (card != null)
                    {
                        int vpW = pnl.pnlNotifsViewport.ClientSize.Width;
                        int expectedCardW = Math.Max(200, vpW - 24);

                        Assert(card.Width == expectedCardW,
                            "Responsive [" + tw + "px]: Card width adapts dynamically",
                            string.Format("Actual={0}, Expected={1}", card.Width, expectedCardW));

                        Assert(card.Left == 12, "Responsive [" + tw + "px]: Card left margin is 12px");

                        ModernButton dismissBtn = null;
                        Label titleLbl = null;
                        foreach (Control c in card.Controls)
                        {
                            if (c is ModernButton && string.Equals(c.Tag as string, "BTN_DISMISS")) dismissBtn = (ModernButton)c;
                            if (c is Label && string.Equals(c.Tag as string, "LBL_TITLE")) titleLbl = (Label)c;
                        }

                        Assert(dismissBtn != null, "Responsive [" + tw + "px]: Dismiss button exists");
                        if (dismissBtn != null)
                        {
                            Assert(dismissBtn.Right <= card.Width,
                                "Responsive [" + tw + "px]: Dismiss button stays within card",
                                string.Format("Dismiss.R={0}, Card.W={1}", dismissBtn.Right, card.Width));

                            int expectedDismissX = card.Width - dismissBtn.Width - 12;
                            Assert(dismissBtn.Left == expectedDismissX,
                                "Responsive [" + tw + "px]: Dismiss button pinned to right edge with 12px padding",
                                string.Format("Dismiss.L={0}, Expected={1}", dismissBtn.Left, expectedDismissX));
                        }

                        if (titleLbl != null && dismissBtn != null)
                        {
                            Assert(titleLbl.Right < dismissBtn.Left,
                                "Responsive [" + tw + "px]: Title label does not collide with Dismiss button",
                                string.Format("Title.R={0}, Dismiss.L={1}", titleLbl.Right, dismissBtn.Left));
                        }
                    }

                    Assert(!pnl.pnlNotifsViewport.AutoScroll,
                        "Responsive [" + tw + "px]: Native AutoScroll is FALSE (no ugly horizontal scrollbar)");
                }
            }
        }

        private static void TestScrollBarVisibilityAndScrolling()
        {
            using (MobiNotificationsPanel pnl = new MobiNotificationsPanel(0, 0, 836, 400, null))
            {
                // 1. Zero notifications
                Assert(!pnl.vScrollBar.Visible, "ScrollTest: ScrollBar hidden when 0 notifications");
                Assert(pnl.lblEmptyHint.Visible, "ScrollTest: Empty hint visible when 0 notifications");

                // 2. Add 1 notification (height 72 + 8 = 80px <= viewport 354px)
                pnl.AddNotification(new NotificationCardItem { Key = "s1", Title = "Title 1", Text = "Msg 1" });
                Assert(!pnl.vScrollBar.Visible, "ScrollTest: ScrollBar hidden when notifications fit in viewport");
                Assert(!pnl.lblEmptyHint.Visible, "ScrollTest: Empty hint hidden when >= 1 notification exists");

                // 3. Add 9 more (total 10 cards -> 800px > 354px)
                for (int i = 2; i <= 10; i++)
                {
                    pnl.AddNotification(new NotificationCardItem { Key = "s" + i, Title = "Title " + i, Text = "Msg " + i });
                }

                Assert(pnl.vScrollBar.Visible, "ScrollTest: ScrollBar visible when cards exceed viewport");
                Assert(pnl.vScrollBar.Maximum > pnl.pnlNotifsViewport.Height,
                    "ScrollTest: ScrollBar Maximum exceeds viewport height",
                    string.Format("Max={0}, VpH={1}", pnl.vScrollBar.Maximum, pnl.pnlNotifsViewport.Height));

                // 4. Test scrolling
                pnl.vScrollBar.Value = 100;
                Assert(pnl.pnlNotifsContent.Top == -100,
                    "ScrollTest: Content panel scrolled up by scrollbar value",
                    string.Format("Content.Top={0}", pnl.pnlNotifsContent.Top));

                pnl.vScrollBar.Value = 0;
                Assert(pnl.pnlNotifsContent.Top == 0,
                    "ScrollTest: Content panel reset to top when scrollbar is 0");
            }
        }

        private static void TestNotificationDismissAndClearAll()
        {
            using (MobiNotificationsPanel pnl = new MobiNotificationsPanel(0, 0, 836, 400, null))
            {
                pnl.AddNotification(new NotificationCardItem { Key = "card_a", Title = "Card A", Text = "Msg A" });
                pnl.AddNotification(new NotificationCardItem { Key = "card_b", Title = "Card B", Text = "Msg B" });
                pnl.AddNotification(new NotificationCardItem { Key = "card_c", Title = "Card C", Text = "Msg C" });

                Assert(pnl.cardMap.Count == 3, "DismissTest: 3 cards added");
                Assert(pnl.lblCount.Text == "(3)", "DismissTest: Count shows (3)");

                // Dismiss 1 card
                pnl.RemoveNotification("card_b");
                Assert(pnl.cardMap.Count == 2, "DismissTest: 2 cards left after dismissing card_b");
                Assert(pnl.lblCount.Text == "(2)", "DismissTest: Count updated to (2)");
                Assert(!pnl.cardMap.ContainsKey("card_b"), "DismissTest: card_b removed from cardMap");

                // Clear All via btnClearAll.PerformClick()
                pnl.btnClearAll.PerformClick();
                Assert(pnl.cardMap.Count == 0, "DismissTest: 0 cards after Clear All");
                Assert(pnl.lblCount.Text == "(0)", "DismissTest: Count reset to (0)");
                Assert(pnl.lblEmptyHint.Visible, "DismissTest: Empty hint restored and visible after Clear All");
                Assert(!pnl.vScrollBar.Visible, "DismissTest: ScrollBar hidden after Clear All");
            }
        }

        private static void TestDeviceFiltering()
        {
            using (MobiNotificationsPanel pnl = new MobiNotificationsPanel(0, 0, 836, 400, null))
            {
                pnl.AddNotification(new NotificationCardItem { Key = "dev1_n1", DeviceSerial = "PHONE_1", Title = "P1-N1", Text = "M" });
                pnl.AddNotification(new NotificationCardItem { Key = "dev1_n2", DeviceSerial = "PHONE_1", Title = "P1-N2", Text = "M" });
                pnl.AddNotification(new NotificationCardItem { Key = "dev2_n1", DeviceSerial = "TABLET_2", Title = "T2-N1", Text = "M" });

                List<ConnectedDeviceItem> devs = new List<ConnectedDeviceItem>
                {
                    new ConnectedDeviceItem { Serial = "PHONE_1", Model = "Pixel 7" },
                    new ConnectedDeviceItem { Serial = "TABLET_2", Model = "Galaxy Tab" }
                };
                pnl.UpdateDeviceList(devs);

                Assert(pnl.cbDeviceFilter.Items.Count == 3, "FilterTest: 3 items in device filter (All + 2 devices)");

                // Filter to PHONE_1 (index 1)
                pnl.cbDeviceFilter.SelectedIndex = 1;
                Assert(pnl.cardMap["dev1_n1"].Visible, "FilterTest: dev1_n1 is visible");
                Assert(pnl.cardMap["dev1_n2"].Visible, "FilterTest: dev1_n2 is visible");
                Assert(!pnl.cardMap["dev2_n1"].Visible, "FilterTest: dev2_n1 is hidden");
                Assert(pnl.lblCount.Text == "(2)", "FilterTest: Count reflects filtered count (2)");

                // Filter to TABLET_2 (index 2)
                pnl.cbDeviceFilter.SelectedIndex = 2;
                Assert(!pnl.cardMap["dev1_n1"].Visible, "FilterTest: dev1_n1 is hidden");
                Assert(pnl.cardMap["dev2_n1"].Visible, "FilterTest: dev2_n1 is visible");
                Assert(pnl.lblCount.Text == "(1)", "FilterTest: Count reflects filtered count (1)");

                // Reset filter to All (index 0)
                pnl.cbDeviceFilter.SelectedIndex = 0;
                Assert(pnl.cardMap["dev1_n1"].Visible && pnl.cardMap["dev1_n2"].Visible && pnl.cardMap["dev2_n1"].Visible,
                    "FilterTest: All notifications visible when filter reset");
                Assert(pnl.lblCount.Text == "(3)", "FilterTest: Count reset to total (3)");
            }
        }

        private static void TestFlowPanelScrollBar()
        {
            using (MobiFlowPanel flow = new MobiFlowPanel(0, 0, 836, 500, null))
            {
                // 1. Viewport & Scrollbar structure
                Assert(flow.pnlFlowViewport != null && !flow.pnlFlowViewport.AutoScroll,
                    "FlowScrollTest: Viewport exists and native AutoScroll is FALSE");
                Assert(flow.vScrollBar != null && !flow.vScrollBar.Visible,
                    "FlowScrollTest: ModernDarkScrollBar initially hidden when 0 messages");
                Assert(flow.pnlMessages != null,
                    "FlowScrollTest: pnlMessages content panel exists inside viewport");

                // 2. Add 1 short message (fits in viewport ~400px)
                flow.AddMessage(new FlowMessageItem { Id = "m1", Text = "Merhaba", Sender = "mobile" });
                Assert(!flow.vScrollBar.Visible,
                    "FlowScrollTest: ScrollBar stays hidden when 1 message fits viewport");

                // 3. Add multiple messages until viewport is exceeded
                for (int i = 2; i <= 15; i++)
                {
                    flow.AddMessage(new FlowMessageItem
                    {
                        Id = "m" + i,
                        Text = "Test Flow Mesajı #" + i + " uzun metin içeriği ve detaylar",
                        Sender = (i % 2 == 0) ? "pc" : "mobile"
                    });
                }

                Assert(flow.vScrollBar.Visible,
                    "FlowScrollTest: ScrollBar visible when messages exceed viewport height");
                Assert(flow.vScrollBar.Maximum > flow.pnlFlowViewport.Height,
                    "FlowScrollTest: ScrollBar Maximum exceeds viewport height",
                    string.Format("Max={0}, VpH={1}", flow.vScrollBar.Maximum, flow.pnlFlowViewport.Height));

                // 4. Test scrolling behavior
                flow.vScrollBar.Value = 120;
                Assert(flow.pnlMessages.Top == -120,
                    "FlowScrollTest: pnlMessages scrolls up by scrollbar value (-120)",
                    string.Format("pnlMessages.Top={0}", flow.pnlMessages.Top));

                flow.vScrollBar.Value = 0;
                Assert(flow.pnlMessages.Top == 0,
                    "FlowScrollTest: pnlMessages resets to 0 when scrollbar is 0");

                // 5. Test auto-scroll to bottom on new message
                flow.AddMessage(new FlowMessageItem { Id = "m_last", Text = "Son mesaj", Sender = "pc" });
                int expectedMax = Math.Max(0, flow.vScrollBar.Maximum - flow.vScrollBar.LargeChange);
                Assert(flow.vScrollBar.Value == expectedMax,
                    "FlowScrollTest: Auto-scrolls to bottom on new message",
                    string.Format("Val={0}, Expected={1}", flow.vScrollBar.Value, expectedMax));
            }
        }
    }
}
