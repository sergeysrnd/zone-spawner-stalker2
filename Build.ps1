param([string]$OutputDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /utf8output /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Net.Http.dll /out:"$OutputDirectory\ZoneSpawner.exe" "$PSScriptRoot\src\ZoneSpawner.cs" "$PSScriptRoot\src\Features.cs" "$PSScriptRoot\src\SetupCore.cs" "$PSScriptRoot\src\SetupForm.cs" "$PSScriptRoot\src\SetupTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
& $compiler /nologo /target:exe /platform:x64 /utf8output /r:System.Windows.Forms.dll /r:"$OutputDirectory\ZoneSpawner.exe" /out:"$OutputDirectory\ZoneSpawner.Tests.exe" "$PSScriptRoot\src\SpawnerTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test runner build failed' }
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /utf8output /r:"$OutputDirectory\ZoneSpawner.exe" /out:"$OutputDirectory\ZoneSpawner.Setup.exe" "$PSScriptRoot\src\SetupEntry.cs"
if ($LASTEXITCODE -ne 0) { throw 'Setup launcher build failed' }
Write-Host 'Built ZoneSpawner.exe'
