using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Serialization;
using LDUninstaller.Core.Bulk;
using LDUninstaller.Core.Discovery;
using LDUninstaller.Core.InfoAdders;
using LDUninstaller.Core.Junk;
using LDUninstaller.Core.Models;
using LDUninstaller.Core.Tweaks;

namespace LDUninstaller.CLI
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            if (args.Length == 0 || args[0] is "-h" or "--help" or "/?")
            {
                PrintHelp();
                return 0;
            }

            string command = args[0].ToLowerInvariant();

            try
            {
                switch (command)
                {
                    case "list":
                        return await HandleListAsync(args.Skip(1).ToArray());

                    case "export":
                        return await HandleExportAsync(args.Skip(1).ToArray());

                    case "uninstall":
                        return await HandleUninstallAsync(args.Skip(1).ToArray());

                    case "debloat":
                        return HandleDebloat(args.Skip(1).ToArray());

                    default:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Bilinmeyen komut: {command}");
                        Console.ResetColor();
                        PrintHelp();
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Kritik Hata: {ex.Message}");
                Console.ResetColor();
                return 1;
            }
        }

        private static void PrintHelp()
        {
            Console.WriteLine(@"
LD Uninstaller CLI - Gelişmiş Komut Satırı Yönetim Aracı
Kullanım:
  LDUninstaller.CLI.exe list [/F=table|json|xml]
  LDUninstaller.CLI.exe export <dosya_yolu> [/F=json|xml|csv]
  LDUninstaller.CLI.exe uninstall <hedef|.bcul_dosyası> [/Q] [/U] [/J=VeryGood|Good] [/N]
  LDUninstaller.CLI.exe debloat [--list | --apply <tweak_id|all>]

Parametreler:
  /Q             Mümkünse sessiz (Quiet) kaldırıcıları tercih et
  /U             Unattended (Kullanıcıdan onay istemeden yürüt)
  /J=<Level>     Kaldırma sonrası otomatik artık temizleme düzeyi (VeryGood, Good, Questionable)
  /N, --dry-run  Simülasyon modu. Değişiklik yapmadan yapılacakları listeler
  /F=<Format>    Çıktı formatı: table, json, xml, csv
");
        }

        private static async Task<List<ApplicationUninstallerEntry>> ScanAndEnrichAsync()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine("[1/3] Sistem taranıyor (Kayıt Defteri, Store, Steam)...");
            var pipeline = new DiscoveryPipeline();
            pipeline.OnProgress += (msg, pct) => Console.WriteLine($"   -> {msg} (%{pct})");
            var list = await pipeline.DiscoverAllAsync();
            Console.WriteLine($"[1/3] Keşif tamamlandı ({list.Count} girdi, {sw.ElapsedMilliseconds} ms)");

            sw.Restart();
            Console.WriteLine("[2/3] Eksik bilgiler ve sertifikalar tamamlanıyor...");
            var adder = new InfoAdderManager();
            adder.ProcessAll(list);
            Console.WriteLine($"[2/3] Zenginleştirme tamamlandı ({sw.ElapsedMilliseconds} ms)");

            return list;
        }

        private static async Task<int> HandleListAsync(string[] args)
        {
            string format = GetArgValue(args, "/F", "table").ToLowerInvariant();
            var list = await ScanAndEnrichAsync();

            if (format == "json")
            {
                Console.WriteLine(JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
            }
            else if (format == "xml")
            {
                var serializer = new XmlSerializer(typeof(List<ApplicationUninstallerEntry>));
                using var sw = new StringWriter();
                serializer.Serialize(sw, list);
                Console.WriteLine(sw.ToString());
            }
            else
            {
                Console.WriteLine($"\nToplam Tespit Edilen Uygulama: {list.Count}\n");
                Console.WriteLine($"{"Uygulama Adı",-40} | {"Yayıncı",-25} | {"Boyut",-10} | {"Tür",-12}");
                Console.WriteLine(new string('-', 95));

                foreach (var app in list.Take(100))
                {
                    string name = Truncate(app.DisplayName, 38);
                    string pub = Truncate(app.Publisher, 23);
                    string sz = app.EstimatedSize.ToString();
                    string kind = app.UninstallerKind.ToString();
                    Console.WriteLine($"{name,-40} | {pub,-25} | {sz,-10} | {kind,-12}");
                }

                if (list.Count > 100)
                {
                    Console.WriteLine($"... ve {list.Count - 100} uygulama daha.");
                }
            }

            return 0;
        }

        private static async Task<int> HandleExportAsync(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Hata: Dışa aktarılacak dosya yolu belirtilmedi. Örnek: export \"C:\\apps.json\"");
                return 1;
            }

            string filePath = args[0];
            string format = GetArgValue(args, "/F", Path.GetExtension(filePath).TrimStart('.')).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(format)) format = "json";

            var list = await ScanAndEnrichAsync();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".");

            if (format == "json")
            {
                string json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
            }
            else if (format == "csv")
            {
                using var writer = new StreamWriter(filePath);
                writer.WriteLine("DisplayName,Publisher,DisplayVersion,EstimatedSizeBytes,UninstallerKind,InstallLocation");
                foreach (var a in list)
                {
                    writer.WriteLine($"\"{EscapeCsv(a.DisplayName)}\",\"{EscapeCsv(a.Publisher)}\",\"{EscapeCsv(a.DisplayVersion)}\",{a.EstimatedSize.Bytes},\"{a.UninstallerKind}\",\"{EscapeCsv(a.InstallLocation)}\"");
                }
            }
            else if (format == "xml")
            {
                var serializer = new XmlSerializer(typeof(List<ApplicationUninstallerEntry>));
                using var writer = new StreamWriter(filePath);
                serializer.Serialize(writer, list);
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Başarıyla aktarıldı: {filePath} ({list.Count} kayıt)");
            Console.ResetColor();
            return 0;
        }

        private static async Task<int> HandleUninstallAsync(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Hata: Kaldırılacak uygulama adı, GUID veya .bcul liste dosyası belirtilmedi.");
                return 1;
            }

            string target = args[0];
            bool quiet = args.Contains("/Q", StringComparer.OrdinalIgnoreCase);
            bool unattended = args.Contains("/U", StringComparer.OrdinalIgnoreCase);
            bool dryRun = args.Contains("/N", StringComparer.OrdinalIgnoreCase) || args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);
            string junkLevelStr = GetArgValue(args, "/J", "");

            var appsToUninstall = new List<ApplicationUninstallerEntry>();
            var allApps = await ScanAndEnrichAsync();

            if (File.Exists(target))
            {
                // File-based target (bcul, json, txt)
                string ext = Path.GetExtension(target).ToLowerInvariant();
                var lines = File.ReadAllLines(target);
                foreach (var line in lines)
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#")) continue;

                    var matched = allApps.FirstOrDefault(a =>
                        a.DisplayName.Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
                        a.RegistryKeyName.Equals(trimmed, StringComparison.OrdinalIgnoreCase));

                    if (matched != null) appsToUninstall.Add(matched);
                }
            }
            else
            {
                // Name or pattern match
                var matched = allApps.Where(a =>
                    a.DisplayName.Contains(target, StringComparison.OrdinalIgnoreCase) ||
                    a.RegistryKeyName.Equals(target, StringComparison.OrdinalIgnoreCase)).ToList();

                appsToUninstall.AddRange(matched);
            }

            if (appsToUninstall.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Kaldırılacak eşleşen uygulama bulunamadı: '{target}'");
                Console.ResetColor();
                return 1;
            }

            Console.WriteLine($"\nKaldırılacak Uygulamalar ({appsToUninstall.Count}):");
            foreach (var a in appsToUninstall)
            {
                Console.WriteLine($" - {a.DisplayName} [{a.UninstallerKind}]");
            }

            if (dryRun)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("\n[SIMULATION / DRY-RUN] Gerçek kaldırma yapılmadı.");
                Console.ResetColor();
                return 0;
            }

            if (!unattended)
            {
                Console.Write("\nDevam etmek istiyor musunuz? [E/H]: ");
                var key = Console.ReadKey();
                Console.WriteLine();
                if (key.Key != ConsoleKey.E && key.Key != ConsoleKey.Y)
                {
                    Console.WriteLine("İşlem iptal edildi.");
                    return 0;
                }
            }

            var bulkTask = new BulkUninstallTask(appsToUninstall, quiet);
            bulkTask.OnLog += msg => Console.WriteLine($"[Toplu Görev] {msg}");
            await bulkTask.ExecuteAsync();

            // Automatic Junk Clean
            if (!string.IsNullOrWhiteSpace(junkLevelStr) && Enum.TryParse<ConfidenceLevel>(junkLevelStr, true, out var minConfidence))
            {
                Console.WriteLine($"\nArtık temizleme motoru çalıştırılıyor (Asgari Güven: {minConfidence})...");
                var junkManager = new JunkManager();
                foreach (var a in appsToUninstall)
                {
                    var junks = junkManager.ScanJunk(a);
                    var cleanable = junks.Where(j => j.Confidence >= minConfidence).ToList();
                    int cleaned = JunkManager.CleanJunk(cleanable, JunkManager.BackupDirectory);
                    Console.WriteLine($" -> {a.DisplayName}: {cleaned} artık öğe temizlendi.");
                }
            }

            return 0;
        }

        private static int HandleDebloat(string[] args)
        {
            var tweaks = WindowsTweaks.GetDefaultTweaks();

            if (args.Contains("--list") || args.Length == 0)
            {
                Console.WriteLine("\nKullanılabilir Windows Debloat İnce Ayarları:\n");
                foreach (var t in tweaks)
                {
                    string status = t.IsApplied ? "[Uygulandı]" : "[Uygulanmadı]";
                    Console.WriteLine($"{status,-14} ID: {t.Id,-15} | {t.Title}");
                    Console.WriteLine($"               {t.Description}\n");
                }
                return 0;
            }

            int applyIdx = Array.IndexOf(args, "--apply");
            if (applyIdx >= 0 && applyIdx + 1 < args.Length)
            {
                string target = args[applyIdx + 1].ToLowerInvariant();
                foreach (var t in tweaks)
                {
                    if (target == "all" || t.Id.Equals(target, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.Write($"Uygulanıyor: {t.Title}... ");
                        bool ok = t.Apply();
                        Console.WriteLine(ok ? "BAŞARILI" : "HATA");
                    }
                }
                return 0;
            }

            return 0;
        }

        private static string GetArgValue(string[] args, string prefix, string fallback)
        {
            foreach (var a in args)
            {
                if (a.StartsWith(prefix + "=", StringComparison.OrdinalIgnoreCase))
                    return a[(prefix.Length + 1)..].Trim('\"');
                if (a.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase))
                    return a[(prefix.Length + 1)..].Trim('\"');
            }
            return fallback;
        }

        private static string Truncate(string val, int maxLen)
        {
            if (string.IsNullOrEmpty(val)) return "";
            return val.Length <= maxLen ? val : val[..(maxLen - 3)] + "...";
        }

        private static string EscapeCsv(string val)
        {
            if (string.IsNullOrEmpty(val)) return "";
            return val.Replace("\"", "\"\"");
        }
    }
}
