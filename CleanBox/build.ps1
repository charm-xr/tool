param([string]$OutputName = 'CleanBox.exe')
$ErrorActionPreference = 'Stop'
if ([System.IO.Path]::GetFileName($OutputName) -ne $OutputName -or -not $OutputName.EndsWith('.exe')) { throw 'Output must be an executable filename in this folder' }
$cleanBoxRoot = $PSScriptRoot
$cleanBoxCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$cleanBoxSources = Get-ChildItem -LiteralPath (Join-Path $cleanBoxRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName }
$cleanBoxArgs = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/codepage:65001', "/out:$cleanBoxRoot\$OutputName", "/win32manifest:$cleanBoxRoot\src\app.manifest", '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll', '/r:Microsoft.CSharp.dll', "/r:$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\Microsoft.VisualBasic.dll")
if (Test-Path -LiteralPath "$cleanBoxRoot\assets\cleanbox.ico") { $cleanBoxArgs += "/win32icon:$cleanBoxRoot\assets\cleanbox.ico" }
& $cleanBoxCompiler @cleanBoxArgs @cleanBoxSources
if ($LASTEXITCODE -ne 0) { throw 'CleanBox build failed' }
Write-Output 'CleanBox.exe built successfully.'
