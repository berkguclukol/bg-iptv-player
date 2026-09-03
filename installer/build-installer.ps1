$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$compilerCandidates = @(
    'C:\Program Files\Inno Setup 7\ISCC.exe',
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
)
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compiler) {
    throw 'Inno Setup bulunamadı. https://jrsoftware.org/isdl.php adresinden Inno Setup 7 x64 kurun.'
}

& $compiler (Join-Path $PSScriptRoot 'BG-IPTV-Player.iss')
if ($LASTEXITCODE -ne 0) { throw "Installer derlemesi başarısız: $LASTEXITCODE" }

$installer = Join-Path $projectRoot 'artifacts\BG-IPTV-Player-v1.3.0-Setup-x64.exe'
Get-Item -LiteralPath $installer
