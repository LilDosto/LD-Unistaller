using System;

namespace LDUninstaller.Core.Models
{
    public enum StartupType
    {
        RegistryRun,
        RegistryRunOnce,
        StartupFolder,
        TaskScheduler,
        Service
    }

    public class StartupEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public string Publisher { get; set; } = string.Empty;
        public StartupType Type { get; set; }
        public string Location { get; set; } = string.Empty;
        public string ServiceName { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public byte[]? StartupApprovedBinary { get; set; }

        public override string ToString() => $"{Name} ({Type}) - {(IsEnabled ? "Enabled" : "Disabled")}";
    }

    public class DebloatTweak
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsApplied { get; set; }

        public Action? ApplyAction { get; set; }
        public Action? RevertAction { get; set; }

        public bool Apply()
        {
            try
            {
                ApplyAction?.Invoke();
                IsApplied = true;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool Revert()
        {
            try
            {
                RevertAction?.Invoke();
                IsApplied = false;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
