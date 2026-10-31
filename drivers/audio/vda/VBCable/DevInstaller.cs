using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MobiDesk
{
    public class Program
    {
        [DllImport("newdev.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool UpdateDriverForPlugAndPlayDevices(
            IntPtr hwndParent,
            string HardwareId,
            string FullInfPath,
            uint InstallFlags,
            out bool bRebootRequired);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr SetupDiCreateDeviceInfoList(ref Guid ClassGuid, IntPtr hwndParent);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetupDiCreateDeviceInfo(
            IntPtr DeviceInfoSet,
            string DeviceName,
            ref Guid ClassGuid,
            string DeviceDescription,
            IntPtr hwndParent,
            uint CreationFlags,
            ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetupDiSetDeviceRegistryProperty(
            IntPtr DeviceInfoSet,
            ref SP_DEVINFO_DATA DeviceInfoData,
            uint Property,
            byte[] PropertyBuffer,
            uint PropertyBufferSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiCallClassInstaller(
            uint InstallFunction,
            IntPtr DeviceInfoSet,
            ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr SetupDiGetClassDevs(
            ref Guid ClassGuid,
            string Enumerator,
            IntPtr hwndParent,
            uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInfo(
            IntPtr DeviceInfoSet,
            uint MemberIndex,
            ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetupDiGetDeviceRegistryProperty(
            IntPtr DeviceInfoSet,
            ref SP_DEVINFO_DATA DeviceInfoData,
            uint Property,
            out uint PropertyRegDataType,
            byte[] PropertyBuffer,
            uint PropertyBufferSize,
            out uint RequiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiRemoveDevice(
            IntPtr DeviceInfoSet,
            ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern bool EnumChildWindows(IntPtr hwndParent, EnumChildProc lpEnumFunc, IntPtr lParam);
        public delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        public const uint BM_CLICK = 0x00F5;
        public const uint WM_CLOSE = 0x0010;
        public const uint DIF_REGISTERDEVICE = 0x19;
        public const uint SPDRP_HARDWAREID = 0x1;
        public const uint INSTALLFLAG_FORCE = 0x1;
        public const uint DIGCF_ALLCLASSES = 0x4;
        public const uint DIGCF_PRESENT = 0x2;

        private static string logPath = @"C:\VirtualAudioDriver\devinstaller.log";

        public static void Log(string msg)
        {
            string line = string.Format("[{0:HH:mm:ss}] {1}", DateTime.Now, msg);
            Console.WriteLine(line);
            try
            {
                string dir = Path.GetDirectoryName(logPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(logPath, line + Environment.NewLine);
            }
            catch { }
        }

        public static int Main(string[] args)
        {
            try
            {
                if (args.Length < 1)
                {
                    Log("Usage: DevInstaller.exe install <inf> <hwid> | install-auto <inf> <setupExe> | auto <setupExe> | auto-remove <setupExe> | remove <hwid> | check <hwid>");
                    return 1;
                }

                string action = args[0].ToLowerInvariant();
                Log("Starting action: " + action + " (args=" + string.Join(" ", args) + ")");

                if (action == "check" && args.Length >= 2)
                {
                    bool present = IsDevicePresent(args[1]);
                    Log("Device present check (" + args[1] + "): " + present);
                    return present ? 0 : 1;
                }
                else if (action == "install" && args.Length >= 3)
                {
                    string infPath = Path.GetFullPath(args[1]);
                    string hardwareId = args[2];
                    return InstallDevice(infPath, hardwareId);
                }
                else if (action == "install-auto" && args.Length >= 3)
                {
                    string infPath = Path.GetFullPath(args[1]);
                    string setupExe = Path.GetFullPath(args[2]);
                    return InstallAuto(infPath, setupExe);
                }
                else if (action == "auto" && args.Length >= 2)
                {
                    string setupExe = Path.GetFullPath(args[1]);
                    return AutomateOfficialSetup(setupExe, true);
                }
                else if (action == "auto-remove" && args.Length >= 2)
                {
                    string setupExe = Path.GetFullPath(args[1]);
                    return AutomateOfficialSetup(setupExe, false);
                }
                else if (action == "remove" && args.Length >= 2)
                {
                    string hardwareId = args[1];
                    return RemoveDevice(hardwareId);
                }

                Log("Unknown command: " + action);
                return 1;
            }
            catch (Exception ex)
            {
                Log("Exception in Main: " + ex.ToString());
                return -1;
            }
        }

        public static bool IsDevicePresent(string hardwareId)
        {
            Guid mediaGuid = new Guid("4d36e96c-e325-11ce-bfc1-08002be10318");
            IntPtr devInfoSet = SetupDiGetClassDevs(ref mediaGuid, null, IntPtr.Zero, DIGCF_PRESENT);
            if (devInfoSet == (IntPtr)(-1)) return false;

            SP_DEVINFO_DATA devData = new SP_DEVINFO_DATA();
            devData.cbSize = (uint)Marshal.SizeOf(devData);
            uint idx = 0;
            bool found = false;

            while (SetupDiEnumDeviceInfo(devInfoSet, idx, ref devData))
            {
                byte[] buffer = new byte[1024];
                uint reqSize;
                uint regType;
                if (SetupDiGetDeviceRegistryProperty(devInfoSet, ref devData, SPDRP_HARDWAREID, out regType, buffer, (uint)buffer.Length, out reqSize))
                {
                    string hw = Encoding.Unicode.GetString(buffer, 0, (int)reqSize).TrimEnd('\0');
                    if (hw.IndexOf(hardwareId, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        found = true;
                        break;
                    }
                }
                idx++;
            }

            SetupDiDestroyDeviceInfoList(devInfoSet);
            return found;
        }

        public static int InstallAuto(string infPath, string setupExe)
        {
            Log("[InstallAuto] Checking if VBAudioVACWDM is already active...");
            if (IsDevicePresent("VBAudioVACWDM"))
            {
                Log("[InstallAuto] Device is already present and active!");
                return 0;
            }

            Log("[InstallAuto] Step A: Attempting SetupAPI direct device creation...");
            int directResult = InstallDevice(infPath, "VBAudioVACWDM");
            Thread.Sleep(1500);

            if (IsDevicePresent("VBAudioVACWDM"))
            {
                Log("[InstallAuto] SetupAPI direct installation succeeded! Device is present.");
                return 0;
            }

            Log("[InstallAuto] SetupAPI did not instantiate device (result=" + directResult + "). Step B: Launching official setup automation...");
            if (File.Exists(setupExe))
            {
                int autoResult = AutomateOfficialSetup(setupExe, true);
                Log("[InstallAuto] AutomateOfficialSetup returned: " + autoResult);
            }
            else
            {
                Log("[InstallAuto] Official setup not found at: " + setupExe);
            }

            for (int i = 0; i < 15; i++)
            {
                Thread.Sleep(1000);
                if (IsDevicePresent("VBAudioVACWDM"))
                {
                    Log("[InstallAuto] Device confirmed active after setup automation (wait " + i + "s)!");
                    return 0;
                }
            }

            Log("[InstallAuto] Warning: Device not detected after automated attempts.");
            return 1;
        }

        public static int InstallDevice(string infPath, string hardwareId)
        {
            Log("[InstallDevice] Installing device: " + hardwareId + " from: " + infPath);
            Guid mediaGuid = new Guid("4d36e96c-e325-11ce-bfc1-08002be10318");
            IntPtr devInfoSet = SetupDiCreateDeviceInfoList(ref mediaGuid, IntPtr.Zero);
            if (devInfoSet == (IntPtr)(-1))
            {
                int err = Marshal.GetLastWin32Error();
                Log("SetupDiCreateDeviceInfoList failed: " + err);
                return err;
            }

            SP_DEVINFO_DATA devData = new SP_DEVINFO_DATA();
            devData.cbSize = (uint)Marshal.SizeOf(devData);

            // DICD_GENERATE_ID = 0x1, DeviceName = ROOT\MEDIA\0000
            if (!SetupDiCreateDeviceInfo(devInfoSet, "ROOT\\MEDIA\\0000", ref mediaGuid, "VB-Audio Virtual Cable", IntPtr.Zero, 0x1, ref devData))
            {
                int err = Marshal.GetLastWin32Error();
                Log("SetupDiCreateDeviceInfo failed: " + err);
                SetupDiDestroyDeviceInfoList(devInfoSet);
                return err;
            }

            byte[] hwIdBytes = Encoding.Unicode.GetBytes(hardwareId + "\0\0");
            if (!SetupDiSetDeviceRegistryProperty(devInfoSet, ref devData, SPDRP_HARDWAREID, hwIdBytes, (uint)hwIdBytes.Length))
            {
                int err = Marshal.GetLastWin32Error();
                Log("SetupDiSetDeviceRegistryProperty failed: " + err);
                SetupDiDestroyDeviceInfoList(devInfoSet);
                return err;
            }

            if (!SetupDiCallClassInstaller(DIF_REGISTERDEVICE, devInfoSet, ref devData))
            {
                int err = Marshal.GetLastWin32Error();
                Log("SetupDiCallClassInstaller failed: " + err);
                SetupDiDestroyDeviceInfoList(devInfoSet);
                return err;
            }

            SetupDiDestroyDeviceInfoList(devInfoSet);

            bool reboot = false;
            bool updated = UpdateDriverForPlugAndPlayDevices(IntPtr.Zero, hardwareId, infPath, INSTALLFLAG_FORCE, out reboot);
            if (!updated)
            {
                int err = Marshal.GetLastWin32Error();
                Log("UpdateDriverForPlugAndPlayDevices failed: " + err);
                return err;
            }

            Log("[InstallDevice] Device installed successfully! RebootRequired=" + reboot);
            return 0;
        }

        public static int RemoveDevice(string hardwareId)
        {
            Log("[RemoveDevice] Removing device: " + hardwareId);
            Guid mediaGuid = new Guid("4d36e96c-e325-11ce-bfc1-08002be10318");
            IntPtr devInfoSet = SetupDiGetClassDevs(ref mediaGuid, null, IntPtr.Zero, DIGCF_PRESENT);
            if (devInfoSet == (IntPtr)(-1))
            {
                return Marshal.GetLastWin32Error();
            }

            SP_DEVINFO_DATA devData = new SP_DEVINFO_DATA();
            devData.cbSize = (uint)Marshal.SizeOf(devData);
            uint idx = 0;
            int removedCount = 0;

            while (SetupDiEnumDeviceInfo(devInfoSet, idx, ref devData))
            {
                byte[] buffer = new byte[1024];
                uint reqSize;
                uint regType;
                if (SetupDiGetDeviceRegistryProperty(devInfoSet, ref devData, SPDRP_HARDWAREID, out regType, buffer, (uint)buffer.Length, out reqSize))
                {
                    string hw = Encoding.Unicode.GetString(buffer, 0, (int)reqSize).TrimEnd('\0');
                    if (hw.IndexOf(hardwareId, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Log("Found matching device: " + hw + ". Removing...");
                        if (SetupDiRemoveDevice(devInfoSet, ref devData))
                        {
                            removedCount++;
                            continue;
                        }
                    }
                }
                idx++;
            }

            SetupDiDestroyDeviceInfoList(devInfoSet);
            Log("[RemoveDevice] Removed " + removedCount + " device(s).");
            return 0;
        }

        public static int AutomateOfficialSetup(string setupExe, bool isInstall)
        {
            Log("[AutomateOfficialSetup] Launching official setup: " + setupExe);
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = setupExe,
                WorkingDirectory = Path.GetDirectoryName(setupExe),
                UseShellExecute = true
            };
            Process p = Process.Start(psi);
            if (p == null)
            {
                Log("[AutomateOfficialSetup] Failed to start process.");
                return -1;
            }

            string targetBtnText = isInstall ? "Install Driver" : "Remove Driver";
            DateTime deadline = DateTime.Now.AddSeconds(20);
            bool clicked = false;
            IntPtr mainInstallerHwnd = IntPtr.Zero;

            while (DateTime.Now < deadline && !p.HasExited)
            {
                // Find window either by class name or text
                IntPtr hwnd = FindWindow("VBCABLE0Installer0MainWindow0", null);
                if (hwnd == IntPtr.Zero)
                {
                    // Scan all top level windows for this process
                    EnumWindows(delegate(IntPtr topHwnd, IntPtr l)
                    {
                        uint pid;
                        GetWindowThreadProcessId(topHwnd, out pid);
                        if (pid == (uint)p.Id)
                        {
                            StringBuilder sbTitle = new StringBuilder(256);
                            GetWindowText(topHwnd, sbTitle, sbTitle.Capacity);
                            string title = sbTitle.ToString();
                            if (title.IndexOf("VB-CABLE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                title.IndexOf("VB-Audio", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                title.IndexOf("Installer", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                hwnd = topHwnd;
                                return false;
                            }
                        }
                        return true;
                    }, IntPtr.Zero);
                }

                if (hwnd != IntPtr.Zero)
                {
                    mainInstallerHwnd = hwnd;
                    IntPtr btnFound = IntPtr.Zero;
                    EnumChildWindows(hwnd, delegate(IntPtr childHwnd, IntPtr l)
                    {
                        StringBuilder sb = new StringBuilder(256);
                        GetWindowText(childHwnd, sb, sb.Capacity);
                        string txt = sb.ToString();
                        if (txt.IndexOf(targetBtnText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            (isInstall && txt.IndexOf("Install", StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (!isInstall && txt.IndexOf("Remove", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            btnFound = childHwnd;
                            return false;
                        }
                        return true;
                    }, IntPtr.Zero);

                    if (btnFound != IntPtr.Zero)
                    {
                        Log("[AutomateOfficialSetup] Found target button: " + targetBtnText + ", sending click...");
                        SendMessage(btnFound, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                        clicked = true;
                        break;
                    }
                }
                Thread.Sleep(300);
            }

            // If button was clicked, wait for the result dialog (e.g. "Installation complete")
            if (clicked)
            {
                Log("[AutomateOfficialSetup] Button clicked, waiting for confirmation dialog...");
                DateTime waitDone = DateTime.Now.AddSeconds(15);
                while (DateTime.Now < waitDone && !p.HasExited)
                {
                    EnumWindows(delegate(IntPtr topHwnd, IntPtr l)
                    {
                        uint pid;
                        GetWindowThreadProcessId(topHwnd, out pid);
                        if (pid == (uint)p.Id)
                        {
                            EnumChildWindows(topHwnd, delegate(IntPtr childHwnd, IntPtr cl)
                            {
                                StringBuilder sb = new StringBuilder(256);
                                GetWindowText(childHwnd, sb, sb.Capacity);
                                string txt = sb.ToString();
                                if (txt.Equals("OK", StringComparison.OrdinalIgnoreCase) ||
                                    txt.Equals("Tamam", StringComparison.OrdinalIgnoreCase))
                                {
                                    Log("[AutomateOfficialSetup] Found dialog OK button, clicking...");
                                    SendMessage(childHwnd, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                                    return false;
                                }
                                return true;
                            }, IntPtr.Zero);
                        }
                        return true;
                    }, IntPtr.Zero);

                    Thread.Sleep(400);
                }

                // Close main window cleanly
                if (mainInstallerHwnd != IntPtr.Zero)
                {
                    Log("[AutomateOfficialSetup] Closing main installer window...");
                    SendMessage(mainInstallerHwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }
            }
            else
            {
                Log("[AutomateOfficialSetup] Button was not automatically clicked; window left open for manual user confirmation if visible.");
            }

            try { if (!p.HasExited) p.WaitForExit(3000); } catch { }
            return 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }
    }
}
