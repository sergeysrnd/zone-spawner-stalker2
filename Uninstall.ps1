param([string]$StartupDirectory = [Environment]::GetFolderPath('Startup'))
$ErrorActionPreference='Stop'
$manifest=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'installed-manifest.json') -Raw|ConvertFrom-Json
if(Get-Process -Name 'Stalker2-Win64-Shipping' -ErrorAction SilentlyContinue){throw 'Close the game first.'}
$binary=[IO.Path]::GetFullPath((Join-Path $manifest.GameRoot 'Stalker2\Binaries\Win64'))
$archive=Join-Path $PSScriptRoot ('removed-install-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
foreach($item in $manifest.Files){
    $target=[IO.Path]::GetFullPath((Join-Path $binary $item.Path))
    if(-not $target.StartsWith($binary+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid installation path.'}
    if((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target).Hash-ne $item.SHA256){throw "File changed since installation: $($item.Path). Keep it and review manually."}
}
# Move installed files into a recovery folder; keep logs and subsequently installed mods.
$autostartScript=Join-Path $PSScriptRoot 'Autostart.ps1'
if(Test-Path -LiteralPath $autostartScript){& $autostartScript -Mode Disable -StartupDirectory $StartupDirectory}
foreach($item in @($manifest.Files|Sort-Object @{Expression={if($_.Path-eq 'dwmapi.dll'){0}else{1}}})){
    $target=[IO.Path]::GetFullPath((Join-Path $binary $item.Path))
    if(Test-Path -LiteralPath $target){$destination=Join-Path $archive $item.Path;New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination)|Out-Null;Move-Item -LiteralPath $target -Destination $destination}
}
# If only our runtime log/empty directories remain, archive the entire leftover tree.
# Preserve an existing tree containing other mods or unrecognised files.
$loaderRoot=[IO.Path]::GetFullPath((Join-Path $binary 'ue4ss'))
if(-not $loaderRoot.StartsWith($binary+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid loader directory.'}
if(Test-Path -LiteralPath $loaderRoot){
    $remaining=@(Get-ChildItem -LiteralPath $loaderRoot -Recurse -File)
    $foreign=@($remaining | Where-Object {[IO.Path]::GetRelativePath($loaderRoot,$_.FullName) -ne 'UE4SS.log'})
    if($foreign.Count-eq 0){
        $runtimeArchive=Join-Path $archive 'ue4ss-runtime'
        New-Item -ItemType Directory -Force -Path $archive | Out-Null
        Move-Item -LiteralPath $loaderRoot -Destination $runtimeArchive
    }
}
Write-Host "Removed installed files into recovery folder: $archive"
