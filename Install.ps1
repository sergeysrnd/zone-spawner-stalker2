[CmdletBinding()]
param(
    [string]$GameRoot = 'C:\Program Files (x86)\Steam\steamapps\common\S.T.A.L.K.E.R. 2 Heart of Chornobyl',
    [Parameter(Mandatory)][string]$BaseArchive,
    [Parameter(Mandatory)][string]$FixArchive
)
$ErrorActionPreference = 'Stop'
$binaryDir = Join-Path $GameRoot 'Stalker2\Binaries\Win64'
$exe = Join-Path $binaryDir 'Stalker2-Win64-Shipping.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Steam game executable not found.' }
if (Get-Process -Name 'Stalker2-Win64-Shipping' -ErrorAction SilentlyContinue) { throw 'Close the game before installing.' }
$expected = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'catalog-info.json') -Raw | ConvertFrom-Json).gameExeSHA256
if ((Get-FileHash -LiteralPath $exe).Hash -ne $expected) { throw 'Game build changed. Update catalog and verify loader compatibility before installing.' }
if (Test-Path -LiteralPath (Join-Path $binaryDir 'dwmapi.dll')) { throw 'An existing dwmapi.dll was found. Do not replace an existing loader automatically.' }
if (Test-Path -LiteralPath (Join-Path $binaryDir 'ue4ss')) { throw 'An existing UE4SS installation was found. Review it before installing.' }
if ((Get-FileHash -LiteralPath $BaseArchive).Hash -ne '342D893C3F64CB36B88AC4D58CEFC1DD8571B9E37B03F86B793F63955CCC2C0B') { throw 'Unexpected UE4SS base archive; this installer requires build 1028.' }
if ((Get-FileHash -LiteralPath $FixArchive).Hash -ne 'DC46D795F1DD134FE3E760BAE196ECFEA495BBD7D53A657A2CA5BA0D5CBA405A') { throw 'Unexpected compatibility archive checksum.' }
$stage = Join-Path ([IO.Path]::GetTempPath()) ('ZoneSpawner-install-stage-' + [guid]::NewGuid().ToString('N'))
$base = Join-Path $stage 'base'; $fix = Join-Path $stage 'fix'; $payload = Join-Path $stage 'payload'
Expand-Archive -LiteralPath $BaseArchive -DestinationPath $base
Expand-Archive -LiteralPath $FixArchive -DestinationPath $fix
foreach ($line in Get-Content -LiteralPath (Join-Path $fix 'SHA256SUMS.txt')) {
    if ($line -notmatch '^([0-9a-fA-F]{64})\s+\*?(.+)$') { throw 'Invalid checksum manifest.' }
    $expectedFileHash=$Matches[1];$relative=$Matches[2]
    $target=[IO.Path]::GetFullPath((Join-Path $fix $relative))
    if (-not $target.StartsWith([IO.Path]::GetFullPath($fix)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid manifest path.' }
    if ((Get-FileHash -LiteralPath $target).Hash -ne $expectedFileHash) { throw "Fix checksum mismatch: $relative" }
}
New-Item -ItemType Directory -Path $payload | Out-Null
Copy-Item -LiteralPath (Join-Path $base 'dwmapi.dll') -Destination $payload
Copy-Item -LiteralPath (Join-Path $base 'ue4ss') -Destination $payload -Recurse
Copy-Item -LiteralPath (Join-Path $fix 'ue4ss\UE4SS.dll') -Destination (Join-Path $payload 'ue4ss\UE4SS.dll') -Force
Copy-Item -LiteralPath (Join-Path $fix 'ue4ss\UE4SS_Signatures') -Destination (Join-Path $payload 'ue4ss') -Recurse -Force
$notices = Join-Path $payload 'ue4ss\ZoneSpawnerNotices'
New-Item -ItemType Directory -Path $notices | Out-Null
foreach($name in @('licenses','SOURCE_AND_BUILD_INFO.txt','LICENSES_AND_ATTRIBUTION.txt','SETTINGS_REQUIRED.txt')) { Copy-Item -LiteralPath (Join-Path $fix $name) -Destination $notices -Recurse }
$settingsPath=Join-Path $payload 'ue4ss\UE4SS-settings.ini'
$settings=[IO.File]::ReadAllText($settingsPath)
$values=[ordered]@{
    bUseUObjectArrayCache='false';DefaultFNameToStringMethod='Scan';DefaultExecuteInGameThreadMethod='ProcessEvent'
    HookProcessLocalScriptFunction='0';HookInitGameState='0';HookLoadMap='0';HookCallFunctionByNameWithArguments='0'
    HookBeginPlay='0';HookLocalPlayerExec='0';HookAActorTick='0';HookEngineTick='0';HookGameViewportClientTick='0'
    HookProcessInternal='1';HookEndPlay='1';HookUObjectProcessEvent='1';HookProcessConsoleExec='1';HookUStructLink='1'
    ConsoleEnabled='0';GuiConsoleEnabled='0';GuiConsoleVisible='0';EnableHotReloadSystem='0';EnableAutoReloadingLuaMods='0'
}
foreach($key in $values.Keys) {
    $pattern='(?m)^'+[regex]::Escape($key)+'\s*=.*$'
    if ([regex]::Matches($settings,$pattern).Count-ne 1) { throw "Missing/duplicate setting: $key" }
    $settings=[regex]::Replace($settings,$pattern,"$key = $($values[$key])")
}
[IO.File]::WriteAllText($settingsPath,$settings,[Text.UTF8Encoding]::new($false))
$mods=Join-Path $payload 'ue4ss\Mods'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'mod') -Destination (Join-Path $mods 'ZoneSpawner') -Recurse
New-Item -ItemType File -Path (Join-Path $mods 'ZoneSpawner\enabled.txt') | Out-Null
[IO.File]::WriteAllText((Join-Path $mods 'mods.txt'),"ZoneSpawner : 1`n",[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $mods 'mods.json'),'[{"mod_name":"ZoneSpawner","mod_enabled":true}]',[Text.UTF8Encoding]::new($false))
$bridge=Join-Path $PSScriptRoot 'bridge';New-Item -ItemType Directory -Path $bridge -Force | Out-Null
$catalogPath=(Join-Path $PSScriptRoot 'catalog-bridge.tsv').Replace('\','/')
$bridgePath=$bridge.Replace('\','/')
$launcherPath=(Join-Path $PSScriptRoot 'ZoneSpawnerLauncher.dll').Replace('\','/')
if(-not(Test-Path -LiteralPath $launcherPath)){throw 'Build ZoneSpawnerLauncher.dll before installing.'}
$readerPath=(Join-Path $binaryDir 'ue4ss\Mods\ZoneSpawner\Scripts\inventory-reader.lua').Replace('\','/')
$worldPath=(Join-Path $binaryDir 'ue4ss\Mods\ZoneSpawner\Scripts\world.lua').Replace('\','/')
$actorsPath=(Join-Path $PSScriptRoot 'actors-bridge.tsv').Replace('\','/')
$config="return {bridge_dir = [=[$bridgePath]=], catalog_path = [=[$catalogPath]=], launcher_path = [=[$launcherPath]=], reader_path=[=[$readerPath]=],world_path=[=[$worldPath]=],actors_path=[=[$actorsPath]=]}"
[IO.File]::WriteAllText((Join-Path $mods 'ZoneSpawner\Scripts\config.lua'),$config,[Text.UTF8Encoding]::new($false))
$records=@(Get-ChildItem -LiteralPath $payload -Recurse -File | ForEach-Object {
    [pscustomobject]@{Path=[IO.Path]::GetRelativePath($payload,$_.FullName);SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash}
})
Copy-Item -LiteralPath (Join-Path $payload 'ue4ss') -Destination $binaryDir -Recurse
# The proxy is copied last so an interrupted installation cannot load a partial setup.
Copy-Item -LiteralPath (Join-Path $payload 'dwmapi.dll') -Destination $binaryDir
foreach($record in $records) {
    if((Get-FileHash -LiteralPath (Join-Path $binaryDir $record.Path)).Hash-ne $record.SHA256) { throw "Installed checksum mismatch: $($record.Path)" }
}
$manifest=[ordered]@{GameRoot=$GameRoot;GameExeSHA256=$expected;InstalledAt=(Get-Date).ToString('o');Files=$records;BaseSHA256=(Get-FileHash -LiteralPath $BaseArchive).Hash;FixSHA256=(Get-FileHash -LiteralPath $FixArchive).Hash}
$manifest|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $PSScriptRoot 'installed-manifest.json') -Encoding utf8
Write-Host 'Installed ZoneSpawner and UE4SS 1028 + compatibility fix 1.2; original game files preserved.'
