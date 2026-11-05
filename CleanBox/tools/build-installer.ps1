param(
    [string]$ArchivePath,
    [string]$CompilerPath
)

$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
if (-not $ArchivePath) {
    $ArchivePath = Join-Path $project 'downloads\CleanBox-Windows-x64.zip'
}
$archive = (Resolve-Path -LiteralPath $ArchivePath).Path
$expectedHash = ((Get-Content -LiteralPath "$archive.sha256" -Raw).Trim() -split '\s+')[0]
if ($expectedHash -notmatch '^[a-f0-9]{64}$' -or (Get-FileHash -LiteralPath $archive).Hash -ne $expectedHash) {
    throw 'The portable archive does not match its SHA-256 file.'
}

if (-not $CompilerPath) {
    $compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($compiler) {
        $CompilerPath = $compiler.Source
    } else {
        $CompilerPath = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
    }
}
if (-not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) {
    throw 'Install Inno Setup 6.7 or pass its ISCC.exe path with -CompilerPath.'
}

$outputDir = Join-Path $project 'release'
$stage = Join-Path $outputDir ('installer-stage-' + [Guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath $archive -DestinationPath $stage
$payload = Join-Path $stage 'CleanBox'
$chineseMessages = Join-Path (Split-Path -Parent $CompilerPath) 'Languages\ChineseSimplified.isl'
if (-not (Test-Path -LiteralPath $chineseMessages -PathType Leaf)) {
    $chineseMessages = Join-Path $stage 'ChineseSimplified.isl'
    $translationUrl = 'https://raw.githubusercontent.com/jrsoftware/issrc/6ef32198ef1f7b7b375cd4b6b90896c2a58eb4c2/Files/Languages/ChineseSimplified.isl'
    Invoke-WebRequest -UseBasicParsing -Uri $translationUrl -OutFile $chineseMessages -TimeoutSec 30
    if ((Get-FileHash -LiteralPath $chineseMessages).Hash -ne 'E0B0B350E2245F3C5E65586DFE43D574F6E7F06F2261149ABA284954B3FC9A8D') {
        throw 'The official Simplified Chinese messages file failed verification.'
    }
}
$script = Join-Path $PSScriptRoot 'CleanBox.iss'
& $CompilerPath "/DPayloadDir=$payload" "/DChineseMessages=$chineseMessages" "/O$outputDir" $script
if ($LASTEXITCODE -ne 0) {
    throw "Installer compilation failed with exit code $LASTEXITCODE."
}

$installerName = 'CleanBox-Setup-Windows-x64.exe'
$installer = Join-Path $outputDir $installerName
$installerHash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$installer.sha256", "$installerHash  $installerName" + [char]10, [Text.Encoding]::ASCII)
Write-Output $installer
