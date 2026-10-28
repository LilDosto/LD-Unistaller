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
    @{ major = 2; minor = 1; patch = 1; version = "2.1.1" } | ConvertTo-Json | Set-Content $versionFile
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
# Eski ZIP arşivlerini temizle
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
            $Message = "Release v${newVersion}: Gelismis 20 Katmanli Derin Kalinti Temizleme Motoru"
        }
        git add .
        git commit -m $Message
        git tag -a "v$newVersion" -m "LD Uninstaller v$newVersion - $Message" -f
        
        $remote = git remote get-url origin 2>$null
        if ($remote) {
            Write-Host "GitHub'a gönderiliyor (origin master --tags)..." -ForegroundColor Yellow
            git push origin master --tags -u
        }
    } catch {
        Write-Host "Git işlemi uyarısı: $_" -ForegroundColor Yellow
    }
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
