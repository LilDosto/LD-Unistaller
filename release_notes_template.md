Bu sürümde, LD Uninstaller için özel tasarlanan modern logo/ikon paketi entegre edilmiş ve Başlangıç Yöneticisi'ndeki yetki ve açılış engelleme sorunları tamamen giderilmiştir.

---

### ✨ Neler Eklendi ve İyileştirildi?

**1. 🎨 Yepyeni Modern Logo ve Uygulama Simgesi (App Icon)**
- Uygulama için modern Windows 11 Fluent tasarımına uyumlu fütüristik 'LD' amblemi ve temizleme girdabı motifli yeni logo tasarlandı.
- **Tüm Alanlara Entegre Edildi:**
  - Arayüzün sol üst köşesindeki geçici mavi kutu kaldırılarak yüksek çözünürlüklü yeni logo yerleştirildi.
  - Görev Yöneticisi (Task Manager), pencere başlıkları ve Windows Gezgini (klasör) üzerinde görünmesi için çoklu çözünürlüklü (`16x16`'dan `256x256`'ya) `.ico` dosyası yürütülebilir dosyalara gömüldü.

**2. ⚡ Başlangıç Yöneticisi ve Yetki Hatası Düzeltmesi**
- Uygulama zaten Yönetici olarak açılmasına rağmen bazı uygulamaları kapatırken ortaya çıkan *"Yönetici yetkisi gerekebilir"* uyarısı ve kapatamama sorunu kökten çözüldü:
  - **Başlangıç Klasörü Desteği:** `StartupFolder` altındaki kısayollar için hem `StartupApproved` ikili anahtarı hem de dosya devre dışı bırakma desteği eklendi.
  - **HKLM & 32-bit (Run32) Uyumu:** 64-bit Windows sistemlerinde `WOW6432Node` ve `HKLM` anahtarlarına ait programların açılış durumları doğru kayıt defteri dallarına yazılarak sorunsuz hale getirildi.
  - **Servis Yönetimi:** Windows Servisleri'nin başlangıç durumları (`Automatic`/`Disabled`) doğrudan `sc.exe config` ve sistem kayıt defteri üzerinden anında değiştirilebilir kılındı.

**3. 🧹 20 Katmanlı Derin Kalıntı Temizleme Motoru**
- Kaldırma işlemi bittiğinde arkada kalan tüm öksüz servisler, zamanlanmış görevler, kayıt defteri dalları ve gizli kullanıcı yapılandırma dosyaları eksiksiz taranır ve silinir.

**4. 💾 Çoklu Sürücü Yönetimi ve Disk Haritası**
- Bilgisayara takılı birden fazla disk (`C:`, `D:` vb.) varsa üst sekme çubuğu üzerinden kolayca geçiş yapılır ve görsel Treemap haritası seçilen diske göre dinamik güncellenir.

---

### 📦 İndirme

Aşağıdaki **Assets** bölümünden `LD-Uninstaller-v{VERSION}.zip` dosyasını indirip arşivden çıkararak doğrudan `LDUninstaller.Launcher.exe` ile kullanabilirsiniz.
