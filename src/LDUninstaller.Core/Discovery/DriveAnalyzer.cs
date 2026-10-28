using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LDUninstaller.Core.Models;

namespace LDUninstaller.Core.Discovery
{
    public static class DriveAnalyzer
    {
        public static List<DrivePartitionInfo> AnalyzeDrives(IEnumerable<ApplicationUninstallerEntry> apps)
        {
            var results = new List<DrivePartitionInfo>();
            var appList = apps?.ToList() ?? new List<ApplicationUninstallerEntry>();

            try
            {
                var drives = DriveInfo.GetDrives();
                foreach (var d in drives)
                {
                    if (!d.IsReady) continue;

                    string name = d.Name; // e.g. "C:\"
                    string letter = name.TrimEnd('\\');
                    string label = string.Empty;
                    try { label = d.VolumeLabel; } catch { }
                    if (string.IsNullOrWhiteSpace(label))
                    {
                        label = d.DriveType == DriveType.Fixed ? "Yerel Sürücü" : d.DriveType.ToString();
                    }

                    var part = new DrivePartitionInfo
                    {
                        Name = name,
                        DriveLetter = letter,
                        VolumeLabel = label,
                        TotalBytes = d.TotalSize,
                        FreeBytes = d.AvailableFreeSpace,
                        DriveType = d.DriveType == DriveType.Fixed ? "Sabit Disk" : "Çıkarılabilir"
                    };

                    // Correlate apps
                    foreach (var app in appList)
                    {
                        if (string.IsNullOrWhiteSpace(app.InstallLocation)) continue;
                        try
                        {
                            string? root = Path.GetPathRoot(app.InstallLocation);
                            if (!string.IsNullOrEmpty(root) && root.Equals(name, StringComparison.OrdinalIgnoreCase))
                            {
                                part.AppCount++;
                                part.AppsTotalBytes += app.EstimatedSize.Bytes;
                            }
                        }
                        catch { }
                    }

                    results.Add(part);
                }
            }
            catch { }

            return results;
        }
    }
}
