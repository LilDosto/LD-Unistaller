using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LDUninstaller.Core.Algorithms;
using LDUninstaller.Core.Models;

namespace LDUninstaller.Core.Discovery
{
    public class DiscoveryPipeline
    {
        public event Action<string, int>? OnProgress;

        public async Task<List<ApplicationUninstallerEntry>> DiscoverAllAsync(bool includeStoreApps = true, bool includeOrphaned = true, bool includeSteam = true)
        {
            var registryFactory = new RegistryFactory();
            OnProgress?.Invoke("Kayıt defteri taranıyor...", 10);
            var regEntries = await Task.Run(() => registryFactory.GetUninstallerEntries().ToList());

            var knownLocations = regEntries
                .Select(x => x.InstallLocation)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            var allEntries = new List<ApplicationUninstallerEntry>(regEntries);

            var tasks = new List<Task<IEnumerable<ApplicationUninstallerEntry>>>();

            if (includeStoreApps)
            {
                tasks.Add(Task.Run(() => new StoreAppFactory().GetUninstallerEntries()));
            }

            if (includeSteam)
            {
                tasks.Add(Task.Run(() => new SteamFactory().GetUninstallerEntries()));
            }

            tasks.Add(Task.Run(() => new ChocolateyFactory().GetUninstallerEntries()));
            tasks.Add(Task.Run(() => new ScoopFactory().GetUninstallerEntries()));

            if (includeOrphaned)
            {
                tasks.Add(Task.Run(() => new DirectoryFactory(knownLocations).GetUninstallerEntries()));
            }

            OnProgress?.Invoke("Diğer kaynaklar ve paketler taranıyor...", 40);

            var additionalResults = await Task.WhenAll(tasks);
            foreach (var set in additionalResults)
            {
                allEntries.AddRange(set);
            }

            OnProgress?.Invoke("Sonuçlar birleştiriliyor ve tekilleştiriliyor...", 80);
            var merged = await Task.Run(() => MergeResults(allEntries));

            OnProgress?.Invoke("Tarama tamamlandı.", 100);
            return merged;
        }

        public static List<ApplicationUninstallerEntry> MergeResults(IEnumerable<ApplicationUninstallerEntry> entries)
        {
            var merged = new List<ApplicationUninstallerEntry>();
            var guidMap = new Dictionary<Guid, ApplicationUninstallerEntry>();
            var pathMap = new Dictionary<string, ApplicationUninstallerEntry>(StringComparer.OrdinalIgnoreCase);
            var nameMap = new Dictionary<string, ApplicationUninstallerEntry>(StringComparer.OrdinalIgnoreCase);

            // OPTIMIZATION: Prefix bucket to replace O(N^2) inner scan with O(1) bucket lookup
            // Pre-caches CleanName and Publisher to eliminate redundant Regex.Replace / Sift4.NormalizeName calls.
            var prefixBuckets = new Dictionary<string, List<(ApplicationUninstallerEntry Entry, string CleanName, string Publisher)>>(StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in entries)
            {
                if (string.IsNullOrWhiteSpace(candidate.DisplayName))
                    continue;

                // 1. Fast GUID match O(1)
                if (candidate.BundleProviderKey != Guid.Empty && guidMap.TryGetValue(candidate.BundleProviderKey, out var matchGuid))
                {
                    Enrich(matchGuid, candidate);
                    continue;
                }

                // 2. Fast InstallLocation match O(1)
                if (!string.IsNullOrWhiteSpace(candidate.InstallLocation))
                {
                    try
                    {
                        string normPath = Path.GetFullPath(candidate.InstallLocation).TrimEnd('\\');
                        if (pathMap.TryGetValue(normPath, out var matchPath))
                        {
                            Enrich(matchPath, candidate);
                            continue;
                        }
                    }
                    catch { }
                }

                // 3. Fast Exact Normalized Name match O(1)
                string cleanName = Sift4.NormalizeName(candidate.DisplayName);
                if (nameMap.TryGetValue(cleanName, out var matchName))
                {
                    Enrich(matchName, candidate);
                    continue;
                }

                // 4. Sift4 Fuzzy match using Prefix Bucket Indexing (Reduces O(N^2) to O(k))
                string firstWord = cleanName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                ApplicationUninstallerEntry? existingFuzzy = null;

                if (firstWord.Length >= 3 && prefixBuckets.TryGetValue(firstWord, out var bucketList))
                {
                    for (int i = 0; i < bucketList.Count; i++)
                    {
                        var target = bucketList[i];
                        double simName = Sift4.GetSimilarity(target.CleanName, cleanName);
                        if (simName > 0.88)
                        {
                            if (string.IsNullOrWhiteSpace(target.Publisher) || string.IsNullOrWhiteSpace(candidate.Publisher))
                            {
                                existingFuzzy = target.Entry;
                                break;
                            }
                            double simPub = Sift4.GetSimilarity(target.Publisher, candidate.Publisher);
                            if (simPub > 0.70)
                            {
                                existingFuzzy = target.Entry;
                                break;
                            }
                        }
                    }
                }

                if (existingFuzzy != null)
                {
                    Enrich(existingFuzzy, candidate);
                    continue;
                }

                // New unique entry accepted
                merged.Add(candidate);

                if (candidate.BundleProviderKey != Guid.Empty)
                    guidMap[candidate.BundleProviderKey] = candidate;

                if (!string.IsNullOrWhiteSpace(candidate.InstallLocation))
                {
                    try { pathMap[Path.GetFullPath(candidate.InstallLocation).TrimEnd('\\')] = candidate; } catch { }
                }

                if (!string.IsNullOrWhiteSpace(cleanName))
                {
                    nameMap[cleanName] = candidate;

                    // Add to prefix bucket
                    if (firstWord.Length >= 3)
                    {
                        if (!prefixBuckets.TryGetValue(firstWord, out var bList))
                        {
                            bList = new List<(ApplicationUninstallerEntry, string, string)>(4);
                            prefixBuckets[firstWord] = bList;
                        }
                        bList.Add((candidate, cleanName, candidate.Publisher ?? ""));
                    }
                }
            }

            return merged.OrderBy(x => x.DisplayName).ToList();
        }

        private static void Enrich(ApplicationUninstallerEntry primary, ApplicationUninstallerEntry secondary)
        {
            if (string.IsNullOrWhiteSpace(primary.InstallLocation) && !string.IsNullOrWhiteSpace(secondary.InstallLocation))
                primary.InstallLocation = secondary.InstallLocation;

            if (string.IsNullOrWhiteSpace(primary.UninstallString) && !string.IsNullOrWhiteSpace(secondary.UninstallString))
                primary.UninstallString = secondary.UninstallString;

            if (string.IsNullOrWhiteSpace(primary.QuietUninstallString) && !string.IsNullOrWhiteSpace(secondary.QuietUninstallString))
                primary.QuietUninstallString = secondary.QuietUninstallString;

            if (primary.EstimatedSize.Bytes == 0 && secondary.EstimatedSize.Bytes > 0)
                primary.EstimatedSize = secondary.EstimatedSize;

            if (string.IsNullOrWhiteSpace(primary.DisplayIcon) && !string.IsNullOrWhiteSpace(secondary.DisplayIcon))
                primary.DisplayIcon = secondary.DisplayIcon;

            if (primary.InstallDate == DateTime.MinValue && secondary.InstallDate != DateTime.MinValue)
                primary.InstallDate = secondary.InstallDate;

            if (primary.UninstallerKind == UninstallerType.Unknown && secondary.UninstallerKind != UninstallerType.Unknown)
                primary.UninstallerKind = secondary.UninstallerKind;
        }
    }
}
