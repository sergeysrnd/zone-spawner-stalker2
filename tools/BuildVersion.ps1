function Get-ZoneSpawnerBuildVersion {
    $versionFile = Join-Path (Split-Path -Parent $PSScriptRoot) 'version.txt'
    $value = (Get-Content -LiteralPath $versionFile -Raw).Trim()
    if ($value -notmatch '^\d{1,5}\.\d{1,5}\.\d{1,5}\.\d{1,5}$') {
        throw 'version.txt must contain four numeric version components.'
    }
    foreach ($part in $value.Split('.')) {
        if ([int]$part -gt 65534) { throw 'Version components must be between 0 and 65534.' }
    }
    return ([version]$value).ToString(4)
}
