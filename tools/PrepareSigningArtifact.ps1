param([Parameter(Mandatory)][string]$BinaryDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'BuildVersion.ps1')
$buildVersion = Get-ZoneSpawnerBuildVersion
$binaryRoot = (Resolve-Path -LiteralPath $BinaryDirectory).Path
$artifactDirectory = Join-Path $binaryRoot 'unsigned-signing-input'
if (Test-Path -LiteralPath $artifactDirectory) { throw 'Signing artifact directory already exists; use a fresh build output.' }
$records = @()
$ownBinaries = @('ZoneSpawner.exe', 'ZoneSpawner.Setup.exe', 'ZoneSpawnerLauncher.dll')
foreach ($name in $ownBinaries) {
    $file = Get-Item -LiteralPath (Join-Path $binaryRoot $name)
    if ($file.VersionInfo.ProductName -ne 'Zone Spawner' -or $file.VersionInfo.FileVersion -ne $buildVersion -or $file.VersionInfo.ProductVersion -ne $buildVersion) {
        throw "Missing or inconsistent signing metadata: $name"
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
    if ($signature.Status -ne 'NotSigned') { throw "Expected a fresh unsigned build: $name" }
    # Read PE machine type without loading or executing the DLL.
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4d -or $bytes[1] -ne 0x5a) { throw "Invalid PE file: $name" }
    $peOffset = [BitConverter]::ToInt32($bytes, 60)
    if ($peOffset -lt 64 -or $peOffset -gt ($bytes.Length - 6) -or [BitConverter]::ToUInt32($bytes, $peOffset) -ne 0x00004550 -or [BitConverter]::ToUInt16($bytes, $peOffset + 4) -ne 0x8664) {
        throw "Expected an x64 PE file: $name"
    }
    $records += [ordered]@{File=$name;SHA256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant();Product='Zone Spawner';Version=$buildVersion;Signature='NotSigned'}
}
New-Item -ItemType Directory -Path $artifactDirectory | Out-Null
foreach ($name in $ownBinaries) {
    Copy-Item -LiteralPath (Join-Path $binaryRoot $name) -Destination (Join-Path $artifactDirectory $name)
}
$manifest = [ordered]@{
    State='UNSIGNED - not a player release'
    Version=$buildVersion
    SourceRepository='https://github.com/sergeysrnd/zone-spawner-stalker2'
    Commit=$env:GITHUB_SHA
    RunId=$env:GITHUB_RUN_ID
    RunnerEnvironment=$env:RUNNER_ENVIRONMENT
    Files=$records
}
[IO.File]::WriteAllText((Join-Path $artifactDirectory 'build-metadata.json'), ($manifest | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
$hashLines = @($records | ForEach-Object { $_.SHA256 + '  ' + $_.File })
[IO.File]::WriteAllText((Join-Path $artifactDirectory 'UNSIGNED-SHA256SUMS.txt'), (($hashLines -join [char]10) + [char]10), [Text.UTF8Encoding]::new($false))
Write-Output "Prepared unsigned signing input: $artifactDirectory"
