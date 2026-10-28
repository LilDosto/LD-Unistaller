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
Write-Host "  LD UNINSTALLER SÃœRÃœM YAYINLAMA: v$newVersion" -ForegroundColor Yellow
Write-Host "==========================================" -ForegroundColor Cyan

# 1. Ana klasÃ¶rde eski sÃ¼rÃ¼mleri temizle (Sadece tek ve son sÃ¼rÃ¼m kalacak ÅŸekilde)
$oldFolders = Get-ChildItem -Path $root -Directory -Filter "LD Uninstaller v*"
foreach ($old in $oldFolders) {
    Write-Host "Eski sÃ¼rÃ¼m klasÃ¶rÃ¼ temizleniyor: $($old.Name)..." -ForegroundColor Gray
    try {
        Remove-Item -Path $old.FullName -Recurse -Force -ErrorAction SilentlyContinue
    } catch { }
}

$targetFolderName = "LD Uninstaller v$newVersion"
$targetDir = Join-Path $root $targetFolderName

Write-Host "Yeni sÃ¼rÃ¼m klasÃ¶rÃ¼ oluÅŸturuluyor: $targetFolderName..." -ForegroundColor Green
New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

# 2. Projeleri Release modunda derle ve tek klasÃ¶re aktar
Write-Host "Projeler derleniyor ve yayÄ±nlanÄ±yor..." -ForegroundColor Cyan

$projects = @(
    "src/LDUninstaller.UI/LDUninstaller.UI.csproj",
    "src/LDUninstaller.CLI/LDUninstaller.CLI.csproj",
    "src/LDUninstaller.Automatizer/LDUninstaller.Automatizer.csproj",
    "src/LDUninstaller.Launcher/LDUninstaller.Launcher.csproj"
)

foreach ($proj in $projects) {
    $projPath = Join-Path $root $proj
    Write-Host " -> YayÄ±nlanÄ±yor: $proj" -ForegroundColor Gray
    dotnet publish $projPath -c Release -o $targetDir --nologo -v q
}

# 3. Gereksiz pdb ve log dosyalarÄ±nÄ± temizle
Get-ChildItem -Path $targetDir -Filter "*.pdb" | Remove-Item -Force -ErrorAction SilentlyContinue

# 4. Eski publish ve dist klasÃ¶rlerini de temizle
try { Remove-Item -Path (Join-Path $root "publish") -Recurse -Force -ErrorAction SilentlyContinue } catch { }
try { Remove-Item -Path (Join-Path $root "dist") -Recurse -Force -ErrorAction SilentlyContinue } catch { }

# 5. SÃ¼rÃ¼m ZIP ArÅŸivini OluÅŸtur (GitHub Releases iÃ§in)
$oldZips = Get-ChildItem -Path $root -File -Filter "LD-Uninstaller-v*.zip"
foreach ($oz in $oldZips) {
    try { Remove-Item -Path $oz.FullName -Force -ErrorAction SilentlyContinue } catch { }
}

$zipName = "LD-Uninstaller-v$newVersion.zip"
$zipPath = Join-Path $root $zipName
Write-Host "SÃ¼rÃ¼m arÅŸivi oluÅŸturuluyor: $zipName..." -ForegroundColor Cyan
Compress-Archive -Path "$targetDir\*" -DestinationPath $zipPath -Force

# 6. Git DeÄŸiÅŸiklikleri, Etiket (Tag) ve GitHub Senkronizasyonu
if (Test-Path (Join-Path $root ".git")) {
    Write-Host "`nGit deÄŸiÅŸiklikleri iÅŸleniyor ve GitHub'a aktarÄ±lÄ±yor..." -ForegroundColor Cyan
    try {
        if ([string]::IsNullOrWhiteSpace($Message)) {
            $Message = "Release v${newVersion}: 20 Katmanli Derin Kalinti Temizleme Motoru ve Arayuz SadeleÅŸtirmesi"
        }
        git add .
        git commit -m $Message
        git tag -a "v$newVersion" -m "LD Uninstaller v$newVersion - $Message" -f
        
        $remote = git remote get-url origin 2>$null
        if ($remote) {
            Write-Host "GitHub'a gÃ¶nderiliyor (origin master --tags)..." -ForegroundColor Yellow
            git push origin master --tags -u -f
        }
    } catch {
        Write-Host "Git iÅŸlemi uyarÄ±sÄ±: $_" -ForegroundColor Yellow
    }
}

# 7. GitHub Releases API Ãœzerinde SÃ¼rÃ¼m ve DeÄŸiÅŸiklik NotlarÄ±nÄ± YayÄ±nla
try {
    Write-Host "`nGitHub Releases API Ã¼zerinden sÃ¼rÃ¼m ve notlar yayÄ±nlanÄ±yor..." -ForegroundColor Cyan

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
        Write-Host "UyarÄ±: GitHub kimlik belirteci (token) bulunamadÄ±. Release API atlanÄ±yor." -ForegroundColor Yellow
    } else {
        $apiHeaders = @{
            "Authorization" = "Bearer $token"
            "User-Agent" = "LDUninstaller-ReleaseScript"
            "Accept" = "application/vnd.github+json"
        }

        $repoOwner = "LilDosto"
        $repoName = "LD-Unistaller"

        # Changelog / Sürüm Notları (Şablon dosyasından temiz UTF-8 olarak yüklenir)
        $templatePath = Join-Path $root "release_notes_template.md"
        $releaseNotes = ""
        if (Test-Path $templatePath) {
            $releaseNotes = [System.IO.File]::ReadAllText($templatePath, [System.Text.Encoding]::UTF8)
            $releaseNotes = $releaseNotes.Replace("{VERSION}", $newVersion)
        } else {
            $releaseNotes = "LD Uninstaller v$newVersion güncellemesi."
        }

        $releaseTitle = "🧹 LDUninstaller-v$newVersion — Derin Kalıntı Temizleme ve Çoklu Disk Güncellemesi"

        $releasePayload = @{
            tag_name = "v$newVersion"
            name = $releaseTitle
            body = $releaseNotes
            draft = $false
            prerelease = $false
        } | ConvertTo-Json -Depth 5

        $jsonBytes = [System.Text.Encoding]::UTF8.GetBytes($releasePayload)

        # Release var mÄ± kontrol et
        $releaseUrl = "https://api.github.com/repos/$repoOwner/$repoName/releases/tags/v$newVersion"
        $ghRelease = $null
        try {
            $ghRelease = Invoke-RestMethod -Uri $releaseUrl -Headers $apiHeaders -Method Get -ErrorAction Stop
        } catch { }

        if ($ghRelease) {
            Write-Host "Mevcut release gÃ¼ncelleniyor (ID: $($ghRelease.id))..." -ForegroundColor Yellow
            $patchUrl = "https://api.github.com/repos/$repoOwner/$repoName/releases/$($ghRelease.id)"
            $ghRelease = Invoke-RestMethod -Uri $patchUrl -Headers $apiHeaders -Method Patch -Body $jsonBytes -ContentType "application/json; charset=utf-8"
        } else {
            Write-Host "Yeni release oluÅŸturuluyor..." -ForegroundColor Green
            $postUrl = "https://api.github.com/repos/$repoOwner/$repoName/releases"
            $ghRelease = Invoke-RestMethod -Uri $postUrl -Headers $apiHeaders -Method Post -Body $jsonBytes -ContentType "application/json; charset=utf-8"
        }

        # ZIP dosyasÄ±nÄ± release asset olarak yÃ¼kle
        if ($ghRelease -and (Test-Path $zipPath)) {
            Write-Host "SÃ¼rÃ¼m paketi ($zipName) release sayfasÄ±na yÃ¼kleniyor..." -ForegroundColor Cyan

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
            Write-Host "Asset baÅŸarÄ±yla yÃ¼klendi: $($uploadResult.browser_download_url)" -ForegroundColor Green
        }

        Write-Host "`nGitHub Release yayÄ±nda:" -ForegroundColor Yellow
        Write-Host " -> https://github.com/$repoOwner/$repoName/releases/tag/v$newVersion" -ForegroundColor White
    }
} catch {
    Write-Host "GitHub Releases API iÅŸlemi uyarÄ±sÄ±: $_" -ForegroundColor Yellow
}

Write-Host "`nâœ… BaÅŸarÄ±yla TamamlandÄ±!" -ForegroundColor Green
Write-Host "Ana dizinde oluÅŸturulan son gÃ¼ncel sÃ¼rÃ¼m klasÃ¶rÃ¼:" -ForegroundColor Yellow
Write-Host " -> $targetFolderName" -ForegroundColor White
Write-Host "GitHub Release iÃ§in hazÄ±r paket:" -ForegroundColor White
Write-Host " -> $zipName" -ForegroundColor Cyan
Write-Host "KullanÄ±ma hazÄ±r yÃ¼rÃ¼tÃ¼lebilir dosyalar:" -ForegroundColor Gray
Write-Host "    - LDUninstaller.Launcher.exe (YÃ¶netici yetkili baÅŸlatÄ±cÄ±)"
Write-Host "    - LDUninstaller.UI.exe (Modern Fluent ArayÃ¼z)"
Write-Host "    - LDUninstaller.CLI.exe (Konsol aracÄ±)"
Write-Host "    - LDUninstaller.Automatizer.exe (Otomasyon ajanÄ±)`n"
