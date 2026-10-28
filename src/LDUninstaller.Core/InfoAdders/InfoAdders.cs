using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using LDUninstaller.Core.Models;
using LDUninstaller.Core.Native;

namespace LDUninstaller.Core.InfoAdders
{
    public interface IMissingInfoAdder
    {
        void AddInfo(ApplicationUninstallerEntry entry);
    }

    public class InstallLocationGenerator : IMissingInfoAdder
    {
        public void AddInfo(ApplicationUninstallerEntry entry)
        {
            if (!string.IsNullOrWhiteSpace(entry.InstallLocation) && Directory.Exists(entry.InstallLocation))
                return;

            if (!string.IsNullOrWhiteSpace(entry.UninstallString))
            {
                string path = ExtractExePath(entry.UninstallString);
                if (File.Exists(path))
                {
                    entry.InstallLocation = Path.GetDirectoryName(path) ?? string.Empty;
                    entry.UninstallerFullFilename = path;
                }
            }
        }

        private static string ExtractExePath(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            string trimmed = raw.Trim();
            if (trimmed.StartsWith("\""))
            {
                int end = trimmed.IndexOf('\"', 1);
                if (end > 0) return trimmed.Substring(1, end - 1);
            }
            int exeIdx = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIdx > 0) return trimmed.Substring(0, exeIdx + 4);
            return trimmed;
        }
    }

    public class UninstallerTypeRecognizer : IMissingInfoAdder
    {
        public void AddInfo(ApplicationUninstallerEntry entry)
        {
            if (entry.UninstallerKind != UninstallerType.Unknown)
                return;

            string unins = entry.UninstallString;
            string uninsLower = unins.ToLowerInvariant();

            if (uninsLower.Contains("msiexec.exe") || entry.BundleProviderKey != Guid.Empty)
            {
                entry.UninstallerKind = UninstallerType.Msiexec;
            }
            else if (uninsLower.Contains("unins000.exe") || uninsLower.Contains("inno setup"))
            {
                entry.UninstallerKind = UninstallerType.InnoSetup;
            }
            else if (uninsLower.Contains("uninstall.exe") || uninsLower.Contains("nsis"))
            {
                entry.UninstallerKind = UninstallerType.Nsis;
            }
            else if (uninsLower.Contains("installshield") || uninsLower.Contains("_isuninst"))
            {
                entry.UninstallerKind = UninstallerType.InstallShield;
            }
            else if (uninsLower.Contains("advanced installer") || uninsLower.Contains("ai_"))
            {
                entry.UninstallerKind = UninstallerType.AdvancedInstaller;
            }
            else if (uninsLower.Contains("update.exe --uninstall") || uninsLower.Contains("squirrel"))
            {
                entry.UninstallerKind = UninstallerType.Squirrel;
            }
            else if (uninsLower.Contains("dfshim.dll") || uninsLower.Contains("rundll32 dfshim"))
            {
                entry.UninstallerKind = UninstallerType.ClickOnce;
            }
            else if (uninsLower.StartsWith("steam://"))
            {
                entry.UninstallerKind = UninstallerType.Steam;
            }
            else if (uninsLower.Contains("remove-appxpackage"))
            {
                entry.UninstallerKind = UninstallerType.StoreApp;
            }
            else if (uninsLower.Contains("choco uninstall"))
            {
                entry.UninstallerKind = UninstallerType.Chocolatey;
            }
            else if (entry.IsOrphaned)
            {
                entry.UninstallerKind = UninstallerType.SimpleDelete;
            }
        }
    }

    public class QuietUninstallStringGenerator : IMissingInfoAdder
    {
        public void AddInfo(ApplicationUninstallerEntry entry)
        {
            if (!string.IsNullOrWhiteSpace(entry.QuietUninstallString))
                return;

            if (string.IsNullOrWhiteSpace(entry.UninstallString))
                return;

            string u = entry.UninstallString.Trim();

            switch (entry.UninstallerKind)
            {
                case UninstallerType.InnoSetup:
                    entry.QuietUninstallString = $"{u} /VERYSILENT /SUPPRESSMSGBOXES /NORESTART";
                    break;

                case UninstallerType.Nsis:
                    entry.QuietUninstallString = $"{u} /S";
                    break;

                case UninstallerType.Msiexec:
                    if (u.Contains("/I", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.QuietUninstallString = Regex.Replace(u, @"(?i)/i", "/X") + " /qn /norestart";
                    }
                    else if (!u.Contains("/qn", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.QuietUninstallString = $"{u} /qn /norestart";
                    }
                    else
                    {
                        entry.QuietUninstallString = u;
                    }
                    break;

                case UninstallerType.InstallShield:
                    entry.QuietUninstallString = $"{u} /s /v\"/qn /norestart\"";
                    break;

                case UninstallerType.AdvancedInstaller:
                    entry.QuietUninstallString = $"{u} /quiet /norestart";
                    break;

                case UninstallerType.Chocolatey:
                case UninstallerType.StoreApp:
                case UninstallerType.Steam:
                    entry.QuietUninstallString = u;
                    break;
            }
        }
    }

    public class CertificateGetter : IMissingInfoAdder
    {
        public void AddInfo(ApplicationUninstallerEntry entry)
        {
            string targetPath = entry.UninstallerFullFilename;
            if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath))
            {
                if (!string.IsNullOrWhiteSpace(entry.InstallLocation) && Directory.Exists(entry.InstallLocation))
                {
                    try
                    {
                        var files = Directory.GetFiles(entry.InstallLocation, "*.exe", SearchOption.TopDirectoryOnly);
                        if (files.Length > 0) targetPath = files[0];
                    }
                    catch { }
                }
            }

            if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath))
                return;

            try
            {
                bool isValid = Win32.VerifyEmbeddedSignature(targetPath);
                entry.IsCertificateValid = isValid;

                if (isValid)
                {
                    var cert = X509Certificate.CreateFromSignedFile(targetPath);
                    entry.Certificate = new X509Certificate2(cert);
                }
            }
            catch
            {
                entry.IsCertificateValid = false;
            }
        }
    }

    public class FastSizeGenerator : IMissingInfoAdder
    {
        public void AddInfo(ApplicationUninstallerEntry entry)
        {
            if (entry.EstimatedSize.Bytes > 0)
                return;

            if (string.IsNullOrWhiteSpace(entry.InstallLocation) || !Directory.Exists(entry.InstallLocation))
                return;

            // Never scan root or system directories
            if (Junk.SafetyBlacklist.IsDangerousDirectory(entry.InstallLocation))
                return;

            try
            {
                long totalBytes = CalculateDirSize(new DirectoryInfo(entry.InstallLocation), 0, 3);
                if (totalBytes > 0)
                {
                    entry.EstimatedSize = FileSize.FromBytes(totalBytes);
                }
            }
            catch { }
        }

        private static long CalculateDirSize(DirectoryInfo dir, int currentDepth, int maxDepth)
        {
            if (currentDepth > maxDepth) return 0;
            // Prevent junction / symlink loops
            if ((dir.Attributes & FileAttributes.ReparsePoint) != 0) return 0;

            long size = 0;
            try
            {
                foreach (var file in dir.EnumerateFiles())
                {
                    size += file.Length;
                }

                if (currentDepth < maxDepth)
                {
                    foreach (var sub in dir.EnumerateDirectories())
                    {
                        size += CalculateDirSize(sub, currentDepth + 1, maxDepth);
                    }
                }
            }
            catch { }

            return size;
        }
    }

    public class Is64BitGetter : IMissingInfoAdder
    {
        public void AddInfo(ApplicationUninstallerEntry entry)
        {
            if (entry.Is64Bit != MachineType.Unknown)
                return;

            string targetFile = entry.UninstallerFullFilename;
            if (string.IsNullOrWhiteSpace(targetFile) || !File.Exists(targetFile))
            {
                if (!string.IsNullOrWhiteSpace(entry.InstallLocation) && Directory.Exists(entry.InstallLocation))
                {
                    try
                    {
                        var files = Directory.GetFiles(entry.InstallLocation, "*.exe");
                        if (files.Length > 0) targetFile = files[0];
                    }
                    catch { }
                }
            }

            if (string.IsNullOrWhiteSpace(targetFile) || !File.Exists(targetFile))
                return;

            ushort arch = Win32.GetPeArchitecture(targetFile);
            entry.Is64Bit = arch switch
            {
                0x014c => MachineType.X86,
                0x8664 => MachineType.X64,
                0xAA64 => MachineType.Arm64,
                _ => MachineType.Unknown
            };
        }
    }

    public class InfoAdderManager
    {
        private readonly IMissingInfoAdder[] _adders = new IMissingInfoAdder[]
        {
            new InstallLocationGenerator(),
            new UninstallerTypeRecognizer(),
            new QuietUninstallStringGenerator(),
            new CertificateGetter(),
            new FastSizeGenerator(),
            new Is64BitGetter()
        };

        public void Process(ApplicationUninstallerEntry entry)
        {
            foreach (var adder in _adders)
            {
                try
                {
                    adder.AddInfo(entry);
                }
                catch { }
            }
        }

        public void ProcessAll(System.Collections.Generic.IEnumerable<ApplicationUninstallerEntry> entries)
        {
            System.Threading.Tasks.Parallel.ForEach(entries, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, Process);
        }
    }
}
