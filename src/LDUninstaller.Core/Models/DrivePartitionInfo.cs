using System;
using System.IO;

namespace LDUninstaller.Core.Models
{
    public class DrivePartitionInfo
    {
        public string Name { get; set; } = string.Empty; // "C:\"
        public string DriveLetter { get; set; } = string.Empty; // "C:"
        public string VolumeLabel { get; set; } = string.Empty; // "Yerel Disk"
        public string DriveType { get; set; } = string.Empty; // "Sabit Disk"
        public long TotalBytes { get; set; }
        public long FreeBytes { get; set; }
        public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);
        public double PercentUsed => TotalBytes > 0 ? (double)UsedBytes / TotalBytes * 100.0 : 0;
        
        public int AppCount { get; set; }
        public long AppsTotalBytes { get; set; }

        public string DisplayTitle
        {
            get
            {
                if (string.IsNullOrWhiteSpace(VolumeLabel))
                    return $"💾 {DriveLetter}";
                return $"💾 {DriveLetter} ({VolumeLabel})";
            }
        }

        public string DisplayStats => $"{FileSize.FromBytes(UsedBytes)} / {FileSize.FromBytes(TotalBytes)} ({PercentUsed:F0}% Dolu)";
        public string DisplayBadge => $"{AppCount} Yazılım | {FileSize.FromBytes(AppsTotalBytes)}";
    }
}
