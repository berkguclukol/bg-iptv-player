$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $projectRoot 'native\BgIptvPlayer.Native\BgIptvPlayer.Native.csproj'

# Sürüm numarasının tek kaynağı csproj dosyasıdır; installer ve paket adları buradan türetilir.
$version = (Select-Xml -Path $csproj -XPath '/Project/PropertyGroup/Version').Node.InnerText
if (-not $version) { throw "Sürüm numarası okunamadı: $csproj" }
Write-Host "Sürüm: $version"

$compilerCandidates = @(
    'C:\Program Files\Inno Setup 7\ISCC.exe',
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
)
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compiler) {
    throw 'Inno Setup bulunamadı. https://jrsoftware.org/isdl.php adresinden Inno Setup 7 x64 kurun.'
}

$publishDir = Join-Path $projectRoot "artifacts\BG-IPTV-Player-v$version-publish"
if (Test-Path -LiteralPath $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }

Write-Host 'Uygulama yayınlanıyor (self-contained win-x64)...'
dotnet publish $csproj -c Release -r win-x64 --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish başarısız: $LASTEXITCODE" }

Write-Host 'Installer derleniyor...'
& $compiler "/DMyAppVersion=$version" (Join-Path $PSScriptRoot 'BG-IPTV-Player.iss')
if ($LASTEXITCODE -ne 0) { throw "Installer derlemesi başarısız: $LASTEXITCODE" }

Write-Host 'Taşınabilir paket hazırlanıyor...'
$zip = Join-Path $projectRoot "artifacts\BG-IPTV-Player-v$version-win-x64.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip }
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zip -CompressionLevel Optimal

$installer = Join-Path $projectRoot "artifacts\BG-IPTV-Player-v$version-Setup-x64.exe"
Get-Item -LiteralPath $installer, $zip
