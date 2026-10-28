using System;

namespace LDUninstaller.Core.Models
{
    public enum UninstallerType
    {
        Unknown = 0,
        Msiexec = 1,
        InnoSetup = 2,
        Steam = 3,
        Nsis = 4,
        InstallShield = 5,
        SdbInst = 6,
        WindowsFeature = 7,
        WindowsUpdate = 8,
        StoreApp = 9,
        SimpleDelete = 10,
        Chocolatey = 11,
        Oculus = 12,
        PowerShell = 13,
        AdvancedInstaller = 14,
        Wise = 15,
        Squirrel = 16,
        ClickOnce = 17,
        Scoop = 18
    }

    public enum ConfidenceLevel
    {
        Bad = 0,          // Dangerous / critical system location
        Questionable = 1, // Weak name match or shared temp
        Good = 2,         // Strong match in standard app folders
        VeryGood = 3      // Exact name/publisher match; safe to clean
    }

    public enum MachineType
    {
        Unknown = 0,
        X86 = 1,
        X64 = 2,
        Arm64 = 3
    }

    public readonly struct FileSize : IComparable<FileSize>, IEquatable<FileSize>
    {
        public long Bytes { get; }

        public FileSize(long bytes)
        {
            Bytes = bytes < 0 ? 0 : bytes;
        }

        public static FileSize FromBytes(long bytes) => new(bytes);
        public static FileSize FromKilobytes(double kb) => new((long)(kb * 1024));
        public static FileSize FromMegabytes(double mb) => new((long)(mb * 1024 * 1024));
        public static FileSize Zero => new(0);

        public override string ToString()
        {
            if (Bytes >= 1024L * 1024 * 1024)
                return $"{(double)Bytes / (1024 * 1024 * 1024):F2} GB";
            if (Bytes >= 1024L * 1024)
                return $"{(double)Bytes / (1024 * 1024):F1} MB";
            if (Bytes >= 1024L)
                return $"{(double)Bytes / 1024:F0} KB";
            if (Bytes > 0)
                return $"{Bytes} B";
            return "0 B";
        }

        public int CompareTo(FileSize other) => Bytes.CompareTo(other.Bytes);
        public bool Equals(FileSize other) => Bytes == other.Bytes;
        public override bool Equals(object? obj) => obj is FileSize other && Equals(other);
        public override int GetHashCode() => Bytes.GetHashCode();

        public static bool operator ==(FileSize left, FileSize right) => left.Equals(right);
        public static bool operator !=(FileSize left, FileSize right) => !left.Equals(right);
        public static bool operator <(FileSize left, FileSize right) => left.Bytes < right.Bytes;
        public static bool operator >(FileSize left, FileSize right) => left.Bytes > right.Bytes;
    }
}
