param([Parameter(Mandatory = $true)][string]$ScanPath)
$ErrorActionPreference = 'Stop'
$cleanBoxRoot = Split-Path -Parent $PSScriptRoot
[void][System.Reflection.Assembly]::LoadFrom((Join-Path $cleanBoxRoot 'CleanBox.exe'))
$cleanBoxFound = New-Object 'System.Collections.Generic.List[CleanBox.AppEntry]'
$cleanBoxCollect = [System.Action[CleanBox.AppEntry]] { param($entry) $cleanBoxFound.Add($entry) }
$cleanBoxStatus = [System.Action[string]] { param($message) }
[CleanBox.Scanner]::Disk((Resolve-Path -LiteralPath $ScanPath).Path, $cleanBoxCollect, $cleanBoxStatus, [System.Threading.CancellationToken]::None)
if ($cleanBoxFound.Count -lt 1) { throw 'No candidate found in the real portable-app test directory' }
[CleanBox.Store]::Export($cleanBoxFound, (Join-Path $cleanBoxRoot 'data\discovery-test.csv'))
"PASS: Real portable directory discovery found $($cleanBoxFound.Count) executable candidates. No candidates were executed or modified." | Set-Content -LiteralPath (Join-Path $cleanBoxRoot 'data\discovery-results.txt') -Encoding UTF8
