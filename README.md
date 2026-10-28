# LD Uninstaller (Modern v2.3.1 Pro Edition)

**LD Uninstaller**, Bulk Crap Uninstaller (BCU) endüstri standardı mimarisini temel alan; donanım hızlandırmalı modern **Windows 11 Fluent Dark** arayüzü, çoklu disk analizi ve akıllı otomasyon ajanıyla güçlendirilmiş açık kaynaklı, endüstri standardı sistem yönetimi ve yazılım kaldırma çözümüdür.

---

## ✨ Öne Çıkan Özellikler

### 1. ⚡ Ultra Akıcı Performans & Sıfır Takılma (Fluidity & Optimization)
- **Gecikmesiz Arama (Debounced Search):** Arama çubuğuna yazarken UI thread'i dondurmayan 160ms debounce filtreleme.
- **Piksel Düzeyinde Pürüzsüz Kaydırma:** DataGrid sanallaştırması `VirtualizingPanel.ScrollUnit="Pixel"` ve `Recycling` modlarıyla 120 FPS akıcılık sağlar.
- **Donanım Hızlandırmalı Squarified Treemap:** Vektörel çizim elemanları, dondurulmuş (frozen) Direct3D fırçaları ve statik tipografiler ile anında disk görselleştirmesi.
- **Asenkron Başlatma:** Başlangıç yöneticisi ve debloat ayarları arka plan görevlerinde yüklenerek uygulamanın anında açılmasını sağlar.

### 2. 💾 Çoklu Disk Analizi & Üst Sekmeler (Multi-Drive Tab System)
- Bilgisayara takılı tüm aktif diskleri (`C:`, `D:`, harici sürücüler vb.) otomatik tespit eder ve analiz eder.
- Birden fazla disk bağlıysa üst kısımda modern sekmeler belirir:
  - **`🌐 Tüm Diskler`**: Sistemdeki tüm sürücüleri ve tüm yazılımları bir arada sunar.
  - **`💾 C: (Yerel Disk)`**: C sürücüsündeki yazılımları, kullanılan alanı ve boş kapasiteyi filtreler.
  - **`💾 D: (Depolama / Oyunlar)`**: İkincil diskteki yazılımları ve Steam/Epic/Games kütüphanelerini listeler.
- Disk sekmeleri değiştikçe hem **Yazılım Tablosu** hem de **Disk Haritası (TreeMap)** sadece seçilen diski görselleştirecek şekilde dinamik olarak güncellenir.

### 3. 🧹 Otomatik Kalıntı ve Artık Temizleme Motoru (Leftover Cleaner)
- Kaldırma işlemi (tekli veya çoklu) tamamlandığı anda **Kalıntı Temizleme Ekranı** otomatik olarak açılır.
- **Derin Tarama Kapsamı:**
  - Ana kurulum klasörleri
  - `%AppData%`, `%LocalAppData%`, `%LocalAppData%\LocalLow`, `%ProgramData%`
  - Belgeler ve Kayıtlı Oyunlar (Saved Games)
  - 64-bit & 32-bit (`WOW6432Node`) Kayıt Defteri Software dalları
  - Uninstaller kayıt girdileri
  - Windows Prefetch, WER Crash Dumps ve Güvenlik Duvarı kuralları
- **Seçim Kolaylığı:** "Tümünü Seç / Seçimi Kaldır" özelliği ve her satır için bağımsız onay kutuları ile silme öncesinde otomatik `.reg` yedeği alınır.

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
# Ufak hata düzeltmesi / bugfix (0.0.1 artırır: v2.3.1 -> v2.3.2)
.\release.ps1 -Type patch

# Orta / yeni özellik güncellemesi (0.1 artırır: v2.3.1 -> v2.4.0)
.\release.ps1 -Type minor

# Büyük mimari güncelleme (1 artırır: v2.3.1 -> v3.0.0)
.\release.ps1 -Type major
```

Script, kök dizindeki eski sürümleri temizler ve yalnızca en güncel sürüm klasörünü (`LD Uninstaller vX.Y.Z`) oluşturur.

---

## 💻 Başlatma ve Kullanım

```powershell
# Yönetici yetkili başlatıcı:
.\LD Uninstaller v2.3.1\LDUninstaller.Launcher.exe

# veya doğrudan arayüz:
.\LD Uninstaller v2.3.1\LDUninstaller.UI.exe

# SysAdmin ve Otomasyon için CLI aracı:
.\LD Uninstaller v2.3.1\LDUninstaller.CLI.exe list /F=table
.\LD Uninstaller v2.3.1\LDUninstaller.CLI.exe export software_inventory.json /F=json
.\LD Uninstaller v2.3.1\LDUninstaller.CLI.exe debloat --list
```
