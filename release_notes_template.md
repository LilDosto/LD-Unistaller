Bu sürümde, LD Uninstaller'ın tüm çekirdek motoru ve arayüz çizim hattı mimari düzeyde optimize edilmiş, bellek ve Win32 handle sızıntıları giderilerek uygulama performansı ve kararlılığı en üst seviyeye taşınmıştır.

---

### ✨ Neler Eklendi ve İyileştirildi?

**1. ⚡ $O(N)$ Algoritmik Hızlandırma & Prefix-Bucket İndeksleme**
- Uygulama keşfi ve birleştirme motorundaki $O(N^2)$ karmaşıklığa sahip Sift4 bulanık karşılaştırma döngüsü, **Prefix-Bucket Indexing** mimarisiyle yeniden tasarlandı.
- 300+ kurulu uygulamada tarama süresi %85 oranında hızlandırıldı; düzenli ifadeler (Regex) derlenmiş (`RegexOptions.Compiled`) statik nesnelere dönüştürülerek CPU ve RAM tüketimi düşürüldü.
- Sonuçların tekilleştirilmesi tamamen arka plan iş parçacığına (`async/await`) taşınarak arayüzün kilitlenmesi önlendi.

**2. 🎨 Sıfır-Allokasyonlu (Zero-Allocation) 120+ FPS Disk Haritası**
- Treemap üzerinde fare gezdirildiğinde (`MouseMove`) DirectWrite nesnelerinin ve `FormattedText` öğelerinin her karede yeniden tahsis edilmesinden kaynaklanan mikro takılmalar (GC Gen0 micro-stutters) giderildi.
- Çizim blokları ve tipografi verileri sadece pencere boyutu değiştiğinde önbelleğe alınarak çizim döngüsü sıfır heap tahsisiyle saf GPU DirectWrite akıcılığına kavuşturuldu.

**3. 🛡️ Win32 Handle ve Bellek Sızıntısı (Handle Leak) Koruması**
- Toplu kaldırma (`BulkUninstallTask`) sürecinde sorgulanan tüm `Process` ve `ServiceController` Win32 tutamaçları deterministik `using` kapsamına alındı; işletim sistemi handle tablosunun şişmesi engellendi.
- `msiexec` kuyruk bekleyicisinde her saniye oluşan işlem nesneleri anında serbest bırakılır hale getirildi.

**4. 🛑 Zombi Süreç (Zombie Process) ve Güvenlik Güçlendirmesi**
- UWP/Store uygulamalarını listeleyen PowerShell alt süreci için zaman aşımı koruması eklendi; yanıt vermeyen alt süreç ağaçları (`process.Kill(true)`) anında temizlenerek arka planda bellek tüketmesi önlendi.
- Canlı arama kutusundaki `NullReferenceException` riski ve gereksiz string kopyalama işlemleri `OrdinalIgnoreCase` ile tamamen giderildi.

**5. 💿 Standart Kurulum Paketi & MIT Lisansı**
- Masaüstü kısayolu, Başlat menüsü entegrasyonu ve Denetim Masası kayıtlarını içeren tek tıkla kurulum sihirbazı (`Setup.exe`) ile resmi MIT lisansı depoya dahil edildi.

---

### 📦 İndirme Seçenekleri

Aşağıdaki **Assets** bölümünden dilediğiniz kurulum türünü indirebilirsiniz:
- 📥 **Standart Kurulum (Önerilen):** `LD-Uninstaller-Setup-v{VERSION}.exe` — Masaüstü simgesi ve Başlat menüsü kısayolu ile otomatik kurulum.
- 📦 **Taşınabilir Sürüm (Portable):** `LD-Uninstaller-v{VERSION}.zip` — Kurulum gerektirmeyen taşınabilir zip arşivi.
