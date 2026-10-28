using System;
using System.Collections.Generic;
using LDUninstaller.Core.Models;
using LDUninstaller.Core.Tweaks;

namespace LDUninstaller.Core.Discovery
{
    public class ScriptFactory : IUninstallerFactory
    {
        public string SourceName => "Windows Debloat & Tweaks";

        public IEnumerable<ApplicationUninstallerEntry> GetUninstallerEntries()
        {
            var results = new List<ApplicationUninstallerEntry>();
            var tweaks = WindowsTweaks.GetDefaultTweaks();

            foreach (var tweak in tweaks)
            {
                results.Add(new ApplicationUninstallerEntry
                {
                    DisplayName = $"[Tweak] {tweak.Title}",
                    RawDisplayName = tweak.Title,
                    Publisher = "System Debloat",
                    DisplayVersion = tweak.Category,
                    AboutUrl = tweak.Description,
                    UninstallString = $"lduninstaller://tweak/apply/{tweak.Id}",
                    QuietUninstallString = $"lduninstaller://tweak/apply/{tweak.Id}",
                    UninstallerKind = UninstallerType.PowerShell,
                    SystemComponent = true,
                    IsRegistered = false
                });
            }

            return results;
        }
    }
}
