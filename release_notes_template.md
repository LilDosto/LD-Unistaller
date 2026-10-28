Bu sürümde, kaldırılan uygulamaların arkasında bıraktığı tüm dosya, kayıt defteri, servis ve görev kalıntılarını eksiksiz temizleyen 20 katmanlı derin tarama motoru ve çoklu sürücü analiz sistemi arayüze entegre edilmiştir.

---

### ✨ Neler Eklendi ve İyileştirildi?

**1. 🧹 20 Katmanlı Derin Kalıntı ve Artık Temizleme Motoru**
- Kaldırma işlemi (tekli veya çoklu) tamamlandığı anda otomatik devreye giren kalıntı temizleme arayüzü eklendi:
  - **Servis & Görev Temizliği:** Arka planda unutulan Windows Servisleri (`sc delete`) ve Görev Zamanlayıcı (`Tasks`) updater görevleri otomatik taranıp temizlenir.
  - **Kayıt Defteri & COM:** Shell sağ tık menüleri (`ContextMenuHandlers`), `App Paths`, `Run/RunOnce` ve yetim `CLSID` nesneleri temizlenir.
  - **Kullanıcı Verileri:** `%AppData%`, `%LocalAppData%`, `%USERPROFILE%` altındaki gizli `.appname` / `.config` klasörleri ve boş kalan yayıncı dizinleri silinir.
  - **Güvenli Yedek:** Silme işleminden önce kayıt defteri anahtarları otomatik olarak `.reg` yedeğine aktarılır.

**2. 💾 Çoklu Disk Analizi ve Üst Sekmeler**
- Sistemdeki tüm takılı sürücüler (`C:`, `D:`, harici diskler vb.) otomatik analiz edilerek üst kısıma modern sekme butonları olarak eklendi:
  - **Dinamik Disk Haritası:** Sürücü sekmeleri arasında geçiş yapıldığında hem uygulama tablosu hem de görsel Treemap haritası anında seçilen diske göre filtrelenir.
  - **Oyun ve Kütüphane Tespiti:** İkincil disklerdeki `Games`, `Oyunlar` ve `SteamLibrary` dizinleri eksiksiz taranır.

**3. ⚡ Ultra Akıcı Arayüz ve Sıfır Takılma**
- Arama çubuğuna yazarken UI kilitlenmesini önleyen 160ms gecikmeli filtreleme (debounced search) getirildi.
- Donanım hızlandırmalı sanallaştırılmış liste kaydırması (`Pixel` scroll unit) ile yüksek kare hızında akıcılık sağlandı.

**4. 🗑️ Sadeleştirilmiş Tek Tuşla Kaldırma**
- Arayüzdeki kafa karıştırıcı çoklu kaldırma butonları kaldırılarak tek ve akıllı bir **`🗑️ Kaldır`** butonu eklendi.
- Tek uygulama seçildiğinde doğrudan kaldırma, birden fazla seçildiğinde çakışma önleyici akıllı kuyruk devreye girer.

---

### 📦 İndirme

Aşağıdaki **Assets** bölümünden `LD-Uninstaller-v{VERSION}.zip` dosyasını indirip arşivden çıkararak doğrudan `LDUninstaller.Launcher.exe` ile kullanabilirsiniz.
