param([string]$ExecutablePath)
$ErrorActionPreference = 'Stop'
$cleanBoxRoot = Split-Path -Parent $PSScriptRoot
if (-not $ExecutablePath) { $ExecutablePath = Join-Path $cleanBoxRoot 'CleanBox.exe' }
$cleanBoxRelease = Join-Path $cleanBoxRoot 'release'
$cleanBoxStage = Join-Path $cleanBoxRelease ('stage-' + [Guid]::NewGuid().ToString('N'))
$cleanBoxPortable = Join-Path $cleanBoxStage 'CleanBox'
[void](New-Item -ItemType Directory -Path (Join-Path $cleanBoxPortable 'assets') -Force)

if (-not (Test-Path -LiteralPath $ExecutablePath -PathType Leaf)) { throw 'Build CleanBox.exe before packaging.' }
Copy-Item -LiteralPath $ExecutablePath -Destination (Join-Path $cleanBoxPortable 'CleanBox.exe')
foreach ($cleanBoxName in @('CleanBox.exe.config', 'README.md', 'LICENSE')) {
    Copy-Item -LiteralPath (Join-Path $cleanBoxRoot $cleanBoxName) -Destination $cleanBoxPortable
}
# Match the committed user guide without relying on a PowerShell 5.1 code page.
$cleanBoxGuides = @(Get-ChildItem -LiteralPath $cleanBoxRoot -File -Filter '*.txt')
if ($cleanBoxGuides.Count -ne 1) { throw 'Expected exactly one root-level user guide.' }
Copy-Item -LiteralPath $cleanBoxGuides[0].FullName -Destination $cleanBoxPortable
Copy-Item -LiteralPath (Join-Path $cleanBoxRoot 'assets\cleanbox.ico') -Destination (Join-Path $cleanBoxPortable 'assets')

$cleanBoxZip = Join-Path $cleanBoxRelease 'CleanBox-Windows-x64.zip'
Compress-Archive -LiteralPath $cleanBoxPortable -DestinationPath $cleanBoxZip -Force
$cleanBoxHash = (Get-FileHash -LiteralPath $cleanBoxZip -Algorithm SHA256).Hash.ToLowerInvariant()
"$cleanBoxHash  CleanBox-Windows-x64.zip" | Set-Content -LiteralPath ($cleanBoxZip + '.sha256') -Encoding ASCII
Write-Output $cleanBoxZip
