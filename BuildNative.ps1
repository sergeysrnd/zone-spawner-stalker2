param([Parameter(Mandatory)][string]$TccDirectory, [string]$OutputDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$compiler = Join-Path $TccDirectory 'tcc.exe'
& $compiler -shared -Wall -o (Join-Path $OutputDirectory 'ZoneSpawnerLauncher.dll') (Join-Path $PSScriptRoot 'src\MenuLauncher.c') -lkernel32 -luser32
if ($LASTEXITCODE -ne 0) { throw 'Native launcher build failed.' }
Write-Output 'Built ZoneSpawnerLauncher.dll (x64).'
