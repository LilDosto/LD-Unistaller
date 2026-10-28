param(
    [ValidateSet("major", "minor", "patch", "current")]
    [string]$Type = "current",
    [string]$Message = ""
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
if (-not $root) { $root = (Get-Location).Path }

$versionFile = Join-Path $root "version.json"
$propsFile = Join-Path $root "Directory.Build.props"

if (-not (Test-Path $versionFile)) {
    @{ major = 2; minor = 4; patch = 0; version = "2.4.0" } | ConvertTo-Json | Set-Content $versionFile
}

$vData = Get-Content $versionFile -Raw | ConvertFrom-Json
$major = [int]$vData.major
$minor = [int]$vData.minor
$patch = [int]$vData.patch

switch ($Type.ToLowerInvariant()) {
    "major" {
        $major += 1
        $minor = 0
        $patch = 0
    }
    "minor" {
        $minor += 1
        $patch = 0
    }
    "patch" {
        $patch += 1
    }
    "current" {
        # Keep existing version for initial packaging
    }
}

$newVersion = "$major.$minor.$patch"
$vData.major = $major
$vData.minor = $minor
$vData.patch = $patch
$vData.version = $newVersion

$vData | ConvertTo-Json | Set-Content $versionFile -Encoding UTF8

# Update Directory.Build.props
$propsContent = @"
<Project>
  <PropertyGroup>
    <Version>$newVersion</Version>
    <AssemblyVersion>$newVersion.0</AssemblyVersion>
    <FileVersion>$newVersion.0</FileVersion>
    <Product>LD Uninstaller</Product>
    <Company>LD Software</Company>
    <Authors>LD Software</Authors>
  </PropertyGroup>
</Project>
"@
Set-Content -Path $propsFile -Value $propsContent -Encoding UTF8

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "  LD UNINSTALLER SÜRÜM YAYINLAMA: v$newVersion" -ForegroundColor Yellow
Write-Host "==========================================" -ForegroundColor Cyan

# 1. Ana klasörde eski sürümleri temizle (Sadece tek ve son sürüm kalacak şekilde)
$oldFolders = Get-ChildItem -Path $root -Directory -Filter "LD Uninstaller v*"
foreach ($old in $oldFolders) {
    Write-Host "Eski sürüm klasörü temizleniyor: $($old.Name)..." -ForegroundColor Gray
    try {
        Remove-Item -Path $old.FullName -Recurse -Force -ErrorAction SilentlyContinue
    } catch { }
}

$targetFolderName = "LD Uninstaller v$newVersion"
$targetDir = Join-Path $root $targetFolderName

Write-Host "Yeni sürüm klasörü oluşturuluyor: $targetFolderName..." -ForegroundColor Green
New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

# 2. Projeleri Release modunda derle ve tek klasöre aktar
Write-Host "Projeler derleniyor ve yayınlanıyor..." -ForegroundColor Cyan

$projects = @(
    "src/LDUninstaller.UI/LDUninstaller.UI.csproj",
    "src/LDUninstaller.CLI/LDUninstaller.CLI.csproj",
    "src/LDUninstaller.Automatizer/LDUninstaller.Automatizer.csproj",
    "src/LDUninstaller.Launcher/LDUninstaller.Launcher.csproj"
)

foreach ($proj in $projects) {
    $projPath = Join-Path $root $proj
    Write-Host " -> Yayınlanıyor: $proj" -ForegroundColor Gray
    dotnet publish $projPath -c Release -o $targetDir --nologo -v q
}

# 3. Gereksiz pdb ve log dosyalarını temizle
Get-ChildItem -Path $targetDir -Filter "*.pdb" | Remove-Item -Force -ErrorAction SilentlyContinue

# 4. Eski publish ve dist klasörlerini de temizle
try { Remove-Item -Path (Join-Path $root "publish") -Recurse -Force -ErrorAction SilentlyContinue } catch { }
try { Remove-Item -Path (Join-Path $root "dist") -Recurse -Force -ErrorAction SilentlyContinue } catch { }

# 5. Sürüm ZIP Arşivini Oluştur (GitHub Releases için)
$oldZips = Get-ChildItem -Path $root -File -Filter "LD-Uninstaller-v*.zip"
foreach ($oz in $oldZips) {
    try { Remove-Item -Path $oz.FullName -Force -ErrorAction SilentlyContinue } catch { }
}

$zipName = "LD-Uninstaller-v$newVersion.zip"
$zipPath = Join-Path $root $zipName
Write-Host "Sürüm arşivi oluşturuluyor: $zipName..." -ForegroundColor Cyan
Compress-Archive -Path "$targetDir\*" -DestinationPath $zipPath -Force

# 6. Git Değişiklikleri, Etiket (Tag) ve GitHub Senkronizasyonu
if (Test-Path (Join-Path $root ".git")) {
    Write-Host "`nGit değişiklikleri işleniyor ve GitHub'a aktarılıyor..." -ForegroundColor Cyan
    try {
        if ([string]::IsNullOrWhiteSpace($Message)) {
            $Message = "Release v${newVersion}: 20 Katmanli Derin Kalinti Temizleme Motoru ve Arayuz Sadeleştirmesi"
        }
        git add .
        git commit -m $Message
        git tag -a "v$newVersion" -m "LD Uninstaller v$newVersion - $Message" -f
        
        $remote = git remote get-url origin 2>$null
        if ($remote) {
            Write-Host "GitHub'a gönderiliyor (origin master --tags)..." -ForegroundColor Yellow
            git push origin master --tags -u -f
        }
    } catch {
        Write-Host "Git işlemi uyarısı: $_" -ForegroundColor Yellow
    }
}

# 7. GitHub Releases API Üzerinde Sürüm ve Değişiklik Notlarını Yayınla
try {
    Write-Host "`nGitHub Releases API üzerinden sürüm ve notlar yayınlanıyor..." -ForegroundColor Cyan

    $code = @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public class GitCredsPublisher
{
    [DllImport("Advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    public static string GetToken()
    {
        IntPtr credPtr;
        if (CredRead("git:https://github.com", 1, 0, out credPtr))
        {
            var cred = (CREDENTIAL)Marshal.PtrToStructure(credPtr, typeof(CREDENTIAL));
            byte[] bytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, bytes, 0, cred.CredentialBlobSize);
            return Encoding.Unicode.GetString(bytes).Trim('\0', ' ', '\r', '\n');
        }
        return null;
    }
}
"@
    if (-not ([System.Management.Automation.PSTypeName]'GitCredsPublisher').Type) {
        Add-Type -TypeDefinition $code -Language CSharp
    }
    
    $token = [GitCredsPublisher]::GetToken()
    if ($env:GITHUB_TOKEN) { $token = $env:GITHUB_TOKEN }
    if ($env:GH_TOKEN) { $token = $env:GH_TOKEN }

    if (-not $token) {
        Write-Host "Uyarı: GitHub kimlik belirteci (token) bulunamadı. Release API atlanıyor." -ForegroundColor Yellow
    } else {
        $apiHeaders = @{
            "Authorization" = "Bearer $token"
            "User-Agent" = "LDUninstaller-ReleaseScript"
            "Accept" = "application/vnd.github+json"
        }

        $repoOwner = "LilDosto"
        $repoName = "LD-Unistaller"

        # Changelog / Sürüm Notları
        $releaseNotes = @"
## 🚀 LD Uninstaller v$newVersion

### 🧹 20 Katmanlı Derin Kalıntı ve Artık Temizleme Motoru
Kaldırma işlemi (tekli veya çoklu) tamamlandığında otomatik devreye giren gelişmiş kalıntı temizleme motoru:
1. **Ana Kurulum Klasörleri:** Kaldırıcı sonrası silinmemiş ana dizin kalıntıları.
2. **Kullanıcı & Sistem Dizinleri:** `%AppData%`, `%LocalAppData%`, `LocalLow`, `%ProgramData%`, `Documents`, `Saved Games`.
3. **Kullanıcı Profil Gizli Dizinleri (Dot-Folders):** Modern yazılımların `%USERPROFILE%` altındaki `.appname`, `.config`, `.cache`, `.local/share` klasörleri.
4. **Ortak Kütüphaneler (Common Files):** `Common Files` altında sahipsiz kalan DLL ve bileşenler.
5. **Geçici Dosyalar (Temp):** `%TEMP%` ve `%LOCALAPPDATA%\Temp` kurulum/kaldırma önbellekleri.
6. **Çoklu Sürücüler & Boş Kalan Klasörler:** İkincil disklerdeki (`D:\`, `E:\` vb.) oyun/yazılım artıkları ve boş kalan yayıncı klasörleri.
7. **Yazılım Kayıt Defteri:** `HKLM`, `HKCU` ve `WOW6432Node` Software dalları.
8. **Windows Hizmetleri:** `HKLM\SYSTEM\CurrentControlSet\Services` taranarak yetim servisler otomatik tespit edilir ve `sc stop` + `sc delete` ile kaldırılır.
9. **Görev Zamanlayıcı:** `C:\Windows\System32\Tasks` içerisindeki yetim görevler `schtasks /Delete` ile temizlenir.
10. **Uygulama Başlatma Yolları (App Paths):** Kayıt defteri `App Paths` kalıntıları.
11. **Başlangıç Kayıt Defteri Değerleri (Run & RunOnce):** Otomatik çalıştırma anahtarlarındaki ilgili değerler.
12. **Gezgin Sağ Tık & COM Bileşenleri:** ContextMenuHandlers, Approved kabuk uzantıları ve ilişkili CLSID anahtarları.
13. **Windows Shell MuiCache:** MuiCache önbellek ve simge kayıtları.
14. **Uyumluluk Katmanı (AppCompatFlags):** Compatibility Assistant ve Layers geçmişi.
15. **Windows Güvenlik Duvarı:** `FirewallRules` altındaki yetim kurallar.
16. **Windows Prefetch:** `C:\Windows\Prefetch` çalıştırma önbellek dosyaları.
17. **Hata Dökümleri:** `CrashDumps` ve WER arşiv raporları.
18. **UserAssist:** ROT13 ile şifrelenen çalıştırma geçmişi.
19. **Kısayollar & Son Belgeler:** Masaüstü, Başlat Menüsü, Startup ve Recent kısayolları.
20. **Windows Installer (MSI) & Registry Uninstall:** MSI Product/UserData ve Registry Uninstall girdileri.

### ⚡ Performans ve Tasarım
- **Ultra Akıcı Arayüz:** 160ms debounce filtreleme ve piksel kaydırmalı donanım hızlandırmalı modern koyu arayüz.
- **Çoklu Disk Sekmeleri:** `🌐 Tüm Diskler`, `💾 C:`, `💾 D:` dinamik sekmeleri ve disk haritası.
- **Tek Tıkla Kaldır:** Tekil veya çoklu kaldırma için tek ve net `🗑️ Kaldır` eylemi.
- **Sadeleştirilmiş Sürüm İsimlendirmesi:** Tüm gereksiz "Pro Edition" ifadeleri temizlendi, temiz sürüm standardı (`v$newVersion`) uygulandı.
"@

        $releasePayload = @{
            tag_name = "v$newVersion"
            name = "LD Uninstaller v$newVersion"
            body = $releaseNotes
            draft = $false
            prerelease = $false
        } | ConvertTo-Json -Depth 5

        $jsonBytes = [System.Text.Encoding]::UTF8.GetBytes($releasePayload)

        # Release var mı kontrol et
        $releaseUrl = "https://api.github.com/repos/$repoOwner/$repoName/releases/tags/v$newVersion"
        $ghRelease = $null
        try {
            $ghRelease = Invoke-RestMethod -Uri $releaseUrl -Headers $apiHeaders -Method Get -ErrorAction Stop
        } catch { }

        if ($ghRelease) {
            Write-Host "Mevcut release güncelleniyor (ID: $($ghRelease.id))..." -ForegroundColor Yellow
            $patchUrl = "https://api.github.com/repos/$repoOwner/$repoName/releases/$($ghRelease.id)"
            $ghRelease = Invoke-RestMethod -Uri $patchUrl -Headers $apiHeaders -Method Patch -Body $jsonBytes -ContentType "application/json; charset=utf-8"
        } else {
            Write-Host "Yeni release oluşturuluyor..." -ForegroundColor Green
            $postUrl = "https://api.github.com/repos/$repoOwner/$repoName/releases"
            $ghRelease = Invoke-RestMethod -Uri $postUrl -Headers $apiHeaders -Method Post -Body $jsonBytes -ContentType "application/json; charset=utf-8"
        }

        # ZIP dosyasını release asset olarak yükle
        if ($ghRelease -and (Test-Path $zipPath)) {
            Write-Host "Sürüm paketi ($zipName) release sayfasına yükleniyor..." -ForegroundColor Cyan

            # Eski asset varsa sil
            if ($ghRelease.assets) {
                foreach ($asset in $ghRelease.assets) {
                    if ($asset.name -eq $zipName) {
                        try {
                            Invoke-RestMethod -Uri "https://api.github.com/repos/$repoOwner/$repoName/releases/assets/$($asset.id)" -Headers $apiHeaders -Method Delete | Out-Null
                        } catch { }
                    }
                }
            }

            $uploadUri = $ghRelease.upload_url -replace '\{\?name,label\}', "?name=$zipName"
            $uploadHeaders = @{
                "Authorization" = "Bearer $token"
                "User-Agent" = "LDUninstaller-ReleaseScript"
                "Accept" = "application/vnd.github+json"
            }
            $zipBytes = [System.IO.File]::ReadAllBytes($zipPath)
            $uploadResult = Invoke-RestMethod -Uri $uploadUri -Headers $uploadHeaders -Method Post -Body $zipBytes -ContentType "application/zip"
            Write-Host "Asset başarıyla yüklendi: $($uploadResult.browser_download_url)" -ForegroundColor Green
        }

        Write-Host "`nGitHub Release yayında:" -ForegroundColor Yellow
        Write-Host " -> https://github.com/$repoOwner/$repoName/releases/tag/v$newVersion" -ForegroundColor White
    }
} catch {
    Write-Host "GitHub Releases API işlemi uyarısı: $_" -ForegroundColor Yellow
}

Write-Host "`n✅ Başarıyla Tamamlandı!" -ForegroundColor Green
Write-Host "Ana dizinde oluşturulan son güncel sürüm klasörü:" -ForegroundColor Yellow
Write-Host " -> $targetFolderName" -ForegroundColor White
Write-Host "GitHub Release için hazır paket:" -ForegroundColor White
Write-Host " -> $zipName" -ForegroundColor Cyan
Write-Host "Kullanıma hazır yürütülebilir dosyalar:" -ForegroundColor Gray
Write-Host "    - LDUninstaller.Launcher.exe (Yönetici yetkili başlatıcı)"
Write-Host "    - LDUninstaller.UI.exe (Modern Fluent Arayüz)"
Write-Host "    - LDUninstaller.CLI.exe (Konsol aracı)"
Write-Host "    - LDUninstaller.Automatizer.exe (Otomasyon ajanı)`n"
