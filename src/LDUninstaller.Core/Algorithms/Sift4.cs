using System;
using System.Text.RegularExpressions;

namespace LDUninstaller.Core.Algorithms
{
    public static class Sift4
    {
        // maxOffset: Arama penceresi boyutu (Genellikle 1-5 arası)
        public static int SimplestDistance(string? s1, string? s2, int maxOffset = 5)
        {
            if (string.IsNullOrEmpty(s1)) return string.IsNullOrEmpty(s2) ? 0 : s2.Length;
            if (string.IsNullOrEmpty(s2)) return s1.Length;

            int l1 = s1.Length;
            int l2 = s2.Length;
            int c1 = 0, c2 = 0;
            int local_cs = 0;
            int trans = 0; // yer değiştirme sayısı

            while ((c1 < l1) && (c2 < l2))
            {
                if (s1[c1] == s2[c2])
                {
                    local_cs++;
                    c1++;
                    c2++;
                }
                else
                {
                    trans += local_cs;
                    local_cs = 0;
                    bool matched = false;
                    for (int i = 0; i < maxOffset; i++)
                    {
                        if ((c1 + i < l1) && (s1[c1 + i] == s2[c2]))
                        {
                            c1 += i;
                            local_cs++;
                            c1++;
                            c2++;
                            matched = true;
                            break;
                        }
                        if ((c2 + i < l2) && (s1[c1] == s2[c2 + i]))
                        {
                            c2 += i;
                            local_cs++;
                            c1++;
                            c2++;
                            matched = true;
                            break;
                        }
                    }

                    if (!matched)
                    {
                        c1++;
                        c2++;
                    }
                }
            }
            trans += local_cs;
            return Math.Max(l1, l2) - trans;
        }

        public static double GetSimilarity(string? s1, string? s2, int maxOffset = 5)
        {
            if (string.IsNullOrEmpty(s1) && string.IsNullOrEmpty(s2)) return 1.0;
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0.0;

            string clean1 = NormalizeName(s1);
            string clean2 = NormalizeName(s2);

            if (clean1.Equals(clean2, StringComparison.OrdinalIgnoreCase))
                return 1.0;

            int maxLen = Math.Max(clean1.Length, clean2.Length);
            if (maxLen == 0) return 1.0;

            int dist = SimplestDistance(clean1.ToLowerInvariant(), clean2.ToLowerInvariant(), maxOffset);
            double similarity = 1.0 - ((double)dist / maxLen);
            return Math.Clamp(similarity, 0.0, 1.0);
        }

        private static readonly Regex CompanySuffixesRegex = new(
            @"\b(corp|corporation|inc|incorporated|ltd|limited|llc|gmbh|co|software|technologies|systems)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string NormalizeName(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            string s = CompanySuffixesRegex.Replace(input, "");
            s = Regex.Replace(s, @"[^\w\s]", " ");
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s;
        }
    }
}
