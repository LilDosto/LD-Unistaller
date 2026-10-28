using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace LDUninstaller.Core.Native
{
    public static class Win32
    {
        // --- MSI API ---
        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        public static extern int MsiEnumProducts(int iProductIndex, StringBuilder lpProductBuf);

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        public static extern int MsiGetProductInfo(string szProduct, string szProperty, StringBuilder lpValueBuf, ref int pcchValueBuf);

        // --- Authenticode & WinTrust ---
        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
        public static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, WinTrustData pWVTData);

        private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        public static bool VerifyEmbeddedSignature(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return false;

            try
            {
                using var wtd = new WinTrustFileInfo(filePath);
                var data = new WinTrustData(wtd.Handle);
                uint result = WinVerifyTrust(new IntPtr(-1), WINTRUST_ACTION_GENERIC_VERIFY_V2, data);
                return result == 0; // 0 == ERROR_SUCCESS
            }
            catch
            {
                return false;
            }
        }

        // --- System Restore API (srclient.dll) ---
        [DllImport("srclient.dll", CharSet = CharSet.Unicode)]
        public static extern bool SRSetRestorePointW(ref RESTOREPOINTINFO pRestorePointInit, ref STATEMGRSTATUS pSMgrStatus);

        public static bool CreateRestorePoint(string description, out long sequenceNumber)
        {
            sequenceNumber = 0;
            try
            {
                var info = new RESTOREPOINTINFO
                {
                    dwEventType = 100, // BEGIN_SYSTEM_CHANGE
                    dwRestorePtType = 0, // APPLICATION_INSTALL / UNINSTALL
                    llSequenceNumber = 0,
                    szDescription = description
                };
                var status = new STATEMGRSTATUS();
                bool ok = SRSetRestorePointW(ref info, ref status);
                if (ok && status.nStatus == 0)
                {
                    sequenceNumber = status.llSequenceNumber;
                    return true;
                }
            }
            catch
            {
                // Fallback / SR disabled on machine
            }
            return false;
        }

        // --- MoveFileEx (Reboot cleanup) ---
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool MoveFileEx(string lpExistingFileName, string? lpNewFileName, int dwFlags);
        public const int MOVEFILE_DELAY_UNTIL_REBOOT = 0x00000004;

        public static bool ScheduleDeleteOnReboot(string path)
        {
            try
            {
                return MoveFileEx(path, null, MOVEFILE_DELAY_UNTIL_REBOOT);
            }
            catch
            {
                return false;
            }
        }

        // --- Shell Icons ---
        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        public static extern uint ExtractIconEx(string szFileName, int nIconIndex, IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, uint nIcons);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        // --- Window & Process Control ---
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public const uint WM_CLOSE = 0x0010;
        public const uint SMTO_ABORTIFHUNG = 0x0002;
        public const int SW_HIDE = 0;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOZORDER = 0x0004;

        public static void GentleKillProcess(Process process, int waitMs = 3000)
        {
            try
            {
                if (process.HasExited) return;

                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    SendMessageTimeout(process.MainWindowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, (uint)waitMs, out _);
                    if (process.WaitForExit(waitMs))
                        return;
                }

                process.CloseMainWindow();
                if (process.WaitForExit(waitMs))
                    return;

                process.Kill(true);
            }
            catch
            {
                // Process already terminated or access denied
            }
        }

        // --- PE COFF Architecture Reader ---
        public static ushort GetPeArchitecture(string filePath)
        {
            if (!File.Exists(filePath)) return 0;
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var br = new BinaryReader(fs);
                if (fs.Length < 64) return 0;
                if (br.ReadUInt16() != 0x5A4D) return 0; // MZ header
                fs.Seek(0x3C, SeekOrigin.Begin);
                int peOffset = br.ReadInt32();
                if (peOffset + 6 > fs.Length) return 0;
                fs.Seek(peOffset, SeekOrigin.Begin);
                if (br.ReadUInt32() != 0x00004550) return 0; // PE\0\0
                return br.ReadUInt16(); // Machine
            }
            catch
            {
                return 0;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct RESTOREPOINTINFO
    {
        public int dwEventType;
        public int dwRestorePtType;
        public long llSequenceNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szDescription;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct STATEMGRSTATUS
    {
        public int nStatus;
        public long llSequenceNumber;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public class WinTrustData : IDisposable
    {
        private uint cbStruct = (uint)Marshal.SizeOf(typeof(WinTrustData));
        private IntPtr pPolicyCallbackData = IntPtr.Zero;
        private IntPtr pSIPClientData = IntPtr.Zero;
        private uint dwUIChoice = 2; // WTD_UI_NONE
        private uint fdwRevocationChecks = 0; // WTD_REVOKE_NONE
        private uint dwUnionChoice = 1; // WTD_CHOICE_FILE
        private IntPtr pFile;
        private uint dwStateAction = 0;
        private IntPtr hWVTStateData = IntPtr.Zero;
        private IntPtr pwszURLReference = IntPtr.Zero;
        private uint dwProvFlags = 0x00000040 | 0x00000010 | 0x00001000; // WTD_SAFER_FLAG | WTD_REVOCATION_CHECK_NONE | WTD_CACHE_ONLY_URL_RETRIEVAL
        private uint dwUIContext = 0;

        public WinTrustData(IntPtr fileInfoPtr)
        {
            pFile = fileInfoPtr;
        }

        public void Dispose()
        {
            // No unmanaged allocations inside this class directly
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public class WinTrustFileInfo : IDisposable
    {
        private uint cbStruct = (uint)Marshal.SizeOf(typeof(WinTrustFileInfo));
        [MarshalAs(UnmanagedType.LPWStr)]
        private string pcwszFilePath;
        private IntPtr hFile = IntPtr.Zero;
        private IntPtr pgKnownSubject = IntPtr.Zero;
        private IntPtr _nativeMemory = IntPtr.Zero;

        public IntPtr Handle => _nativeMemory;

        public WinTrustFileInfo(string filePath)
        {
            pcwszFilePath = filePath;
            _nativeMemory = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustFileInfo)));
            Marshal.StructureToPtr(this, _nativeMemory, false);
        }

        public void Dispose()
        {
            if (_nativeMemory != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_nativeMemory);
                _nativeMemory = IntPtr.Zero;
            }
        }
    }
}
