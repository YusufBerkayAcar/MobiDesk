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

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

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

                        // 3. Win32 EnumWindows ile mevcut sürecin gerçek ana arayüz penceresini bulup geri yükle
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
                                            StringBuilder sbClass = new StringBuilder(256);
                                            GetClassName(hWnd, sbClass, 256);
                                            string className = sbClass.ToString();

                                            StringBuilder sb = new StringBuilder(256);
                                            GetWindowText(hWnd, sb, 256);
                                            string wTitle = sb.ToString();

                                            // STRICT FILTER: GDI+ ve sistem yardımcı pencerelerini KESİNLİKLE geri yükleme/gösterme!
                                            if (wTitle.IndexOf("GDI+", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                className.IndexOf("GdiPlus", StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                return true; // GDI+ penceresini atla!
                                            }

                                            // Yalnızca gerçek WindowsForms MobiDesk ana penceresini hedef al
                                            if (!string.IsNullOrEmpty(wTitle) &&
                                                wTitle.IndexOf("MobiDesk", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                                className.IndexOf("WindowsForms", StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                ShowWindow(hWnd, 9 /* SW_RESTORE */);
                                                ShowWindow(hWnd, 5 /* SW_SHOW */);
                                                SetForegroundWindow(hWnd);
                                                Log("Restored genuine MobiDesk window handle " + hWnd + " for PID " + p.Id);
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

                        // 4. Mevcut sürecin aktif olduğunu anında doğrula (asla p.Responding ile 5 saniye bloke olmadan!)
                        bool anyAlive = false;
                        foreach (Process p in othersList)
                        {
                            try
                            {
                                if (!p.HasExited)
                                {
                                    anyAlive = true;
                                    Log("Existing active MobiDesk instance (PID " + p.Id + ") is running.");
                                }
                            }
                            catch { }
                        }

                        if (anyAlive)
                        {
                            Log("Existing active MobiDesk instance signaled. Exiting launcher instance.");
                            return;
                        }

                        // Yalnızca süreçler gerçekten tamamen kapanmışsa temizlik yap
                        if (othersList.Count > 0)
                        {
                            Log("Existing processes were dead. Cleaning up before launching fresh instance...");
                            foreach (Process p in othersList)
                            {
                                try { if (!p.HasExited) { p.Kill(); p.WaitForExit(500); } } catch { }
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

}