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
        # Keep existing version
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
Write-Host "  LD UNINSTALLER SURUM YAYINLAMA: v$newVersion" -ForegroundColor Yellow
Write-Host "==========================================" -ForegroundColor Cyan

# 1. Ana klasorde eski surumleri temizle
$oldFolders = Get-ChildItem -Path $root -Directory -Filter "LD Uninstaller v*"
foreach ($old in $oldFolders) {
    Write-Host "Eski surum klasoru temizleniyor: $($old.Name)..." -ForegroundColor Gray
    try {
        Remove-Item -Path $old.FullName -Recurse -Force -ErrorAction SilentlyContinue
    } catch { }
}

$targetFolderName = "LD Uninstaller v$newVersion"
$targetDir = Join-Path $root $targetFolderName

Write-Host "Yeni surum klasoru olusturuluyor: $targetFolderName..." -ForegroundColor Green
New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

# 2. Projeleri Release modunda derle ve yayinla
Write-Host "Projeler derleniyor ve yayinlaniyor..." -ForegroundColor Cyan

$projects = @(
    "src/LDUninstaller.UI/LDUninstaller.UI.csproj",
    "src/LDUninstaller.CLI/LDUninstaller.CLI.csproj",
    "src/LDUninstaller.Automatizer/LDUninstaller.Automatizer.csproj",
    "src/LDUninstaller.Launcher/LDUninstaller.Launcher.csproj"
)

foreach ($proj in $projects) {
    $projPath = Join-Path $root $proj
    Write-Host " -> Yayinlaniyor: $proj" -ForegroundColor Gray
    dotnet publish $projPath -c Release -o $targetDir --nologo -v q
}

# 3. Gereksiz pdb ve log dosyalarini temizle
Get-ChildItem -Path $targetDir -Filter "*.pdb" | Remove-Item -Force -ErrorAction SilentlyContinue

# 4. Eski publish ve dist klasorlerini de temizle
try { Remove-Item -Path (Join-Path $root "publish") -Recurse -Force -ErrorAction SilentlyContinue } catch { }
try { Remove-Item -Path (Join-Path $root "dist") -Recurse -Force -ErrorAction SilentlyContinue } catch { }

# 5. Surum ZIP Arsivini Olustur
$oldZips = Get-ChildItem -Path $root -File -Filter "LD-Uninstaller-v*.zip"
foreach ($oz in $oldZips) {
    try { Remove-Item -Path $oz.FullName -Force -ErrorAction SilentlyContinue } catch { }
}

$zipName = "LD-Uninstaller-v$newVersion.zip"
$zipPath = Join-Path $root $zipName
Write-Host "Surum arsivi olusturuluyor: $zipName..." -ForegroundColor Cyan
Compress-Archive -Path "$targetDir\*" -DestinationPath $zipPath -Force

# 6. Git Islemleri ve GitHub Push
if (Test-Path (Join-Path $root ".git")) {
    Write-Host "`nGit degisiklikleri isleniyor ve GitHub'a aktariliyor..." -ForegroundColor Cyan
    try {
        if ([string]::IsNullOrWhiteSpace($Message)) {
            $Message = "Release v${newVersion}: Yeni Logo ve Baslangic Yoneticisi Guncellemesi"
        }
        git add .
        git commit -m $Message
        git tag -a "v$newVersion" -m "LD Uninstaller v$newVersion - $Message" -f
        
        $remote = git remote get-url origin 2>$null
        if ($remote) {
            Write-Host "GitHub'a gonderiliyor (origin master --tags)..." -ForegroundColor Yellow
            git push origin master --tags -u -f
        }
    } catch {
        Write-Host "Git islemi uyarisi: $_" -ForegroundColor Yellow
    }
}

# 7. GitHub Releases API Uzerinde Surum ve Notlari Yayinla
try {
    Write-Host "`nGitHub Releases API uzerinden surum ve notlar yayinlaniyor..." -ForegroundColor Cyan

    $code = @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public class GitCredsEngine
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
    if (-not ([System.Management.Automation.PSTypeName]'GitCredsEngine').Type) {
        Add-Type -TypeDefinition $code -Language CSharp
    }
    
    $token = [GitCredsEngine]::GetToken()
    if ($env:GITHUB_TOKEN) { $token = $env:GITHUB_TOKEN }
    if ($env:GH_TOKEN) { $token = $env:GH_TOKEN }

    if (-not $token) {
        Write-Host "Uyari: GitHub kimlik belirteci bulunamadi. Release API atlaniyor." -ForegroundColor Yellow
    } else {
        $apiHeaders = @{
            "Authorization" = "Bearer $token"
            "User-Agent" = "LDUninstaller-ReleaseScript"
            "Accept" = "application/vnd.github+json"
        }

        $repoOwner = "LilDosto"
        $repoName = "LD-Unistaller"

        # Changelog / Surum Notlari (Sablon dosyasindan UTF-8 olarak okunur)
        $templatePath = Join-Path $root "release_notes_template.md"
        $releaseNotes = ""
        if (Test-Path $templatePath) {
            $releaseNotes = [System.IO.File]::ReadAllText($templatePath, [System.Text.Encoding]::UTF8)
            $releaseNotes = $releaseNotes.Replace("{VERSION}", $newVersion)
        } else {
            $releaseNotes = "LD Uninstaller v$newVersion guncellemesi."
        }

        $releaseTitle = if (![string]::IsNullOrWhiteSpace($Message)) { $Message } else { "LDUninstaller-v$newVersion - Guncelleme" }

        $releasePayload = @{
            tag_name = "v$newVersion"
            name = $releaseTitle
            body = $releaseNotes
            draft = $false
            prerelease = $false
        } | ConvertTo-Json -Depth 5

        $jsonBytes = [System.Text.Encoding]::UTF8.GetBytes($releasePayload)

        $releaseUrl = "https://api.github.com/repos/$repoOwner/$repoName/releases/tags/v$newVersion"
        $ghRelease = $null
        try {
            $ghRelease = Invoke-RestMethod -Uri $releaseUrl -Headers $apiHeaders -Method Get -ErrorAction Stop
        } catch { }

        if ($ghRelease) {
            Write-Host "Mevcut release guncelleniyor (ID: $($ghRelease.id))..." -ForegroundColor Yellow
            $patchUrl = "https://api.github.com/repos/$repoOwner/$repoName/releases/$($ghRelease.id)"
            $ghRelease = Invoke-RestMethod -Uri $patchUrl -Headers $apiHeaders -Method Patch -Body $jsonBytes -ContentType "application/json; charset=utf-8"
        } else {
            Write-Host "Yeni release olusturuluyor..." -ForegroundColor Green
            $postUrl = "https://api.github.com/repos/$repoOwner/$repoName/releases"
            $ghRelease = Invoke-RestMethod -Uri $postUrl -Headers $apiHeaders -Method Post -Body $jsonBytes -ContentType "application/json; charset=utf-8"
        }

        # ZIP dosyasini release asset olarak yukle
        if ($ghRelease -and (Test-Path $zipPath)) {
            Write-Host "Surum paketi ($zipName) release sayfasina yukleniyor..." -ForegroundColor Cyan

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
            Write-Host "Asset basariyla yuklendi: $($uploadResult.browser_download_url)" -ForegroundColor Green
        }

        Write-Host "`nGitHub Release yayinda:" -ForegroundColor Yellow
        Write-Host " -> https://github.com/$repoOwner/$repoName/releases/tag/v$newVersion" -ForegroundColor White
    }
} catch {
    Write-Host "GitHub Releases API islemi uyarisi: $_" -ForegroundColor Yellow
}

Write-Host "`n[+] Basariyla Tamamlandi!" -ForegroundColor Green
Write-Host "Ana dizinde olusturulan son guncel surum klasoru:" -ForegroundColor Yellow
Write-Host " -> $targetFolderName" -ForegroundColor White
Write-Host "GitHub Release hazir paketi:" -ForegroundColor White
Write-Host " -> $zipName" -ForegroundColor Cyan
Write-Host "Kullanima hazir yurutulebilir dosyalar:" -ForegroundColor Gray
Write-Host "    - LDUninstaller.Launcher.exe (Yonetici yetkili baslatici)"
Write-Host "    - LDUninstaller.UI.exe (Modern Fluent Arayuz)"
Write-Host "    - LDUninstaller.CLI.exe (Konsol araci)"
Write-Host "    - LDUninstaller.Automatizer.exe (Otomasyon ajani)`n"
