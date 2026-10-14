param([string]$PythonExe = 'python')
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'ZoneSpawner.Tests.exe') $PSScriptRoot
if($LASTEXITCODE -ne 0){throw 'WinForms checks failed'}
foreach($file in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -Filter 'test_*.py'){
    & $PythonExe $file.FullName
    if($LASTEXITCODE -ne 0){throw "Lua check failed: $($file.Name)"}
}
