# LD Uninstaller

**LD Uninstaller**, Bulk Crap Uninstaller (BCU) mimarisini temel alan; donanım hızlandırmalı modern **Windows 11 Fluent Dark** arayüzü, 20 katmanlı derin kalıntı temizleme motoru, çoklu disk analizi ve akıllı otomasyon ajanıyla güçlendirilmiş açık kaynaklı, gelişmiş sistem yönetimi ve yazılım kaldırma çözümüdür.

---

## ✨ Öne Çıkan Özellikler

### 1. 🧹 20 Katmanlı Derin Kalıntı ve Artık Temizleme Motoru (Leftover Cleaner)
Kaldırma işlemi (tekli veya çoklu) bittiği anda otomatik olarak açılan kalıntı temizleme ekranı, sistemde hiçbir yazılımın tek bir izini dahi bırakmaz:
1. **Ana Kurulum Dizinleri:** Kaldırıcı sonrası silinmeyen klasörler.
2. **Kullanıcı Verileri:** `%AppData%`, `%LocalAppData%`, `LocalLow`, `ProgramData`, `Documents`, `Saved Games`.
3. **Kullanıcı Gizli Dizinleri (Dot-Folders):** Modern yazılımların `%USERPROFILE%` altındaki `.appname`, `.config`, `.cache` klasörleri.
4. **Ortak Kütüphaneler (Common Files):** `Common Files` altında terk edilen DLL ve bileşenler.
5. **Geçici Dosyalar (Temp):** `%TEMP%` ve `%LOCALAPPDATA%\Temp` kurulum/kaldırma artıkları.
6. **Çoklu Sürücüler & Boş Dizinler:** İkincil disklerdeki (`D:\`, `E:\`) oyun/yazılım artıkları ve içi boş kalan yayıncı klasörleri.
7. **Yazılım Kayıt Defteri Anahtarları:** `HKLM`, `HKCU` ve `WOW6432Node` Software dalları.
8. **Windows Hizmetleri & Sürücüler:** `HKLM\SYSTEM\CurrentControlSet\Services` taranarak uygulamaya ait arka plan servisleri tespit edilir ve `sc stop` + `sc delete` ile kaldırılır.
9. **Görev Zamanlayıcı (Scheduled Tasks):** `C:\Windows\System32\Tasks` içindeki güncelleyici ve otomatik görevler taranarak temizlenir.
10. **Uygulama Başlatma Yolları (App Paths):** `App Paths` altındaki yetim kayıtlar.
11. **Başlangıç Kayıt Defteri Değerleri (Run & RunOnce):** Otomatik çalıştırma anahtarlarındaki ilgili değerler.
12. **Gezgin Sağ Tık & COM Bileşenleri:** `ContextMenuHandlers`, `Approved` shell uzantıları ve ilişkili `CLSID` anahtarları.
13. **Windows Shell MuiCache:** MuiCache önbellek ve simge kayıtları.
14. **Uyumluluk Katmanı (AppCompatFlags):** Compatibility Assistant ve Layers geçmişi.
15. **Windows Güvenlik Duvarı:** `FirewallRules` altındaki ölü kurallar.
16. **Windows Prefetch:** `C:\Windows\Prefetch` yürütme önbellek dosyaları.
17. **Hata Dökümleri:** `CrashDumps` ve WER arşiv raporları.
18. **UserAssist:** ROT13 ile şifrelenen çalıştırma geçmişi.
19. **Kısayollar & Son Belgeler:** Masaüstü, Başlat Menüsü, Startup ve Recent kısayolları.
20. **Windows Installer (MSI) & Uninstall Kayıtları:** MSI Product/UserData ve Registry Uninstall girdileri.

### 2. ⚡ Ultra Akıcı Performans & Sıfır Takılma (Fluidity & Optimization)
- **Gecikmesiz Arama (Debounced Search):** Arama çubuğuna yazarken UI thread'i dondurmayan 160ms debounce filtreleme.
- **Piksel Düzeyinde Pürüzsüz Kaydırma:** DataGrid sanallaştırması `VirtualizingPanel.ScrollUnit="Pixel"` ve `Recycling` modlarıyla 120 FPS akıcılık sağlar.
- **Donanım Hızlandırmalı Squarified Treemap:** Vektörel çizim elemanları, dondurulmuş (frozen) Direct3D fırçaları ve statik tipografiler ile anında disk görselleştirmesi.
- **Asenkron Başlatma:** Başlangıç yöneticisi ve debloat ayarları arka plan görevlerinde yüklenerek uygulamanın anında açılmasını sağlar.

### 3. 💾 Çoklu Disk Analizi & Üst Sekmeler (Multi-Drive Tab System)
- Bilgisayara takılı tüm aktif diskleri (`C:`, `D:`, harici sürücüler vb.) otomatik tespit eder ve analiz eder.
- Birden fazla disk bağlıysa üst kısımda modern sekmeler belirir:
  - **`🌐 Tüm Diskler`**: Sistemdeki tüm sürücüleri ve tüm yazılımları bir arada sunar.
  - **`💾 C: (Yerel Disk)`**: C sürücüsündeki yazılımları, kullanılan alanı ve boş kapasiteyi filtreler.
  - **`💾 D: (Depolama / Oyunlar)`**: İkincil diskteki yazılımları ve Steam/Epic/Games kütüphanelerini listeler.
- Disk sekmeleri değiştikçe hem **Yazılım Tablosu** hem de **Disk Haritası (TreeMap)** dinamik olarak güncellenir.

### 4. 🚀 Tek Tıkla Kaldırma & Akıllı Kuyruk Matrisi
- Karmaşık butonlar yerine sadece **`🗑️ Kaldır`** butonu. 1 yazılım seçildiğinde tekli, birden fazla seçildiğinde toplu kuyruk yönetimini otomatik üstlenir.
- **Çakışma Önleyici Kuyruk (Collision Matrix):** Windows Installer (`_MSIExecute` mutex) kilitlerini, aynı yayıncı ve aynı dizin bağımlılıklarını otomatik olarak sıraya dizer.
- **Uninstaller Automatizer Daemon:** UI Automation (FlaUI) arka plan servisi ile kaldırıcı pencerelerindeki "İleri", "Kaldır", "Bitir" adımlarını sessizce otomatik tıklar.

### 5. ⚡ Başlangıç Yöneticisi & Windows Debloat
- **StartupApproved Standardı:** Kayıt defteri ve başlangıç klasörlerindeki programları silmeden güvenle açıp kapatma.
- **Debloat Optimizasyonları:** Windows telemetri servislerini (DiagTrack), Cortana/Bing web aramalarını ve sponsorlu arka plan mağaza uygulamalarını tek tıkla kapatma.

---

## 📦 Dağıtım ve Yayınlama (Release Management)

Projeyi derlemek ve belirlenen kurallara göre tek klasörde paketlemek için PowerShell otomasyonu:

```powershell
# Ufak hata düzeltmesi / bugfix (0.0.1 artırır: v2.4.0 -> v2.4.1)
.\release.ps1 -Type patch

# Orta / yeni özellik güncellemesi (0.1 artırır: v2.4.0 -> v2.5.0)
.\release.ps1 -Type minor

# Büyük mimari güncelleme (1 artırır: v2.4.0 -> v3.0.0)
.\release.ps1 -Type major
```

Script, kök dizindeki eski sürümleri temizler, yalnızca en güncel sürüm klasörünü (`LD Uninstaller vX.Y.Z`) oluşturur ve GitHub Releases üzerinde yayınlar.

---

## 💻 Başlatma ve Kullanım

```powershell
# Yönetici yetkili başlatıcı:
.\LD Uninstaller v2.4.0\LDUninstaller.Launcher.exe

# veya doğrudan arayüz:
.\LD Uninstaller v2.4.0\LDUninstaller.UI.exe

# SysAdmin ve Otomasyon için CLI aracı:
.\LD Uninstaller v2.4.0\LDUninstaller.CLI.exe list /F=table
.\LD Uninstaller v2.4.0\LDUninstaller.CLI.exe export software_inventory.json /F=json
.\LD Uninstaller v2.4.0\LDUninstaller.CLI.exe debloat --list
```
