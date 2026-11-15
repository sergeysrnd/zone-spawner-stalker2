param([string]$PythonExe = 'python', [string]$BinaryDirectory = $PSScriptRoot)
$ErrorActionPreference='Stop'
& (Join-Path $BinaryDirectory 'ZoneSpawner.Tests.exe') $PSScriptRoot
if($LASTEXITCODE -ne 0){throw 'WinForms checks failed'}
foreach($file in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -Filter 'test_*.py'){
    & $PythonExe $file.FullName
    if($LASTEXITCODE -ne 0){throw "Lua check failed: $($file.Name)"}
}
