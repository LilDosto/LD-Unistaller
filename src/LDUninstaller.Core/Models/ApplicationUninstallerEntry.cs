using System;
using System.Collections.Generic;
using System.Drawing;
using System.Security.Cryptography.X509Certificates;

namespace LDUninstaller.Core.Models
{
    public class ApplicationUninstallerEntry
    {
        // Tanımlayıcılar ve Temel Bilgiler
        public string DisplayName { get; set; } = string.Empty;
        public string RawDisplayName { get; set; } = string.Empty;
        public string DisplayVersion { get; set; } = string.Empty;
        public string Publisher { get; set; } = string.Empty;
        public string RatingId { get; set; } = string.Empty;
        public string RegistryKeyName { get; set; } = string.Empty;
        public string RegistryPath { get; set; } = string.Empty;
        public Guid BundleProviderKey { get; set; } = Guid.Empty;

        // Konumlar ve Yollar
        public string InstallLocation { get; set; } = string.Empty;
        public string UninstallerLocation { get; set; } = string.Empty;
        public string UninstallerFullFilename { get; set; } = string.Empty;
        public string DisplayIcon { get; set; } = string.Empty;
        public string AboutUrl { get; set; } = string.Empty;

        // Kaldırma Komutları
        public string UninstallString { get; set; } = string.Empty;
        public string QuietUninstallString { get; set; } = string.Empty;
        public string ModifyPath { get; set; } = string.Empty;
        public UninstallerType UninstallerKind { get; set; } = UninstallerType.Unknown;

        // Durum ve Nitelik Bayrakları
        public FileSize EstimatedSize { get; set; } = FileSize.Zero;
        public DateTime InstallDate { get; set; } = DateTime.MinValue;
        public MachineType Is64Bit { get; set; } = MachineType.Unknown;
        public bool IsProtected { get; set; }
        public bool IsRegistered { get; set; } = true;
        public bool IsOrphaned { get; set; }
        public bool IsUpdate { get; set; }
        public bool SystemComponent { get; set; }
        public bool IsValid { get; set; } = true;
        public bool IsSelected { get; set; }
        public bool IsCertificateValid { get; set; }

        // Görsel / Simge
        [System.Text.Json.Serialization.JsonIgnore]
        [System.Xml.Serialization.XmlIgnore]
        public Icon? Icon { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        [System.Xml.Serialization.XmlIgnore]
        public X509Certificate2? Certificate { get; set; }

        // İlişkili Varlıklar
        [System.Text.Json.Serialization.JsonIgnore]
        [System.Xml.Serialization.XmlIgnore]
        public List<StartupEntry> StartupEntries { get; set; } = new();

        [System.Text.Json.Serialization.JsonIgnore]
        [System.Xml.Serialization.XmlIgnore]
        public List<IJunkResult> AdditionalJunk { get; } = new();

        public override string ToString() =>
            $"{DisplayName} ({Publisher}) - {DisplayVersion} [{UninstallerKind}]";
    }
}
