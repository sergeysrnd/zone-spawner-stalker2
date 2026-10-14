param(
    [ValidateSet('Enable','Disable')][string]$Mode = 'Disable',
    [string]$StartupDirectory = [Environment]::GetFolderPath('Startup')
)
$ErrorActionPreference = 'Stop'
$executable = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'ZoneSpawner.exe'))
$directory = [IO.Path]::GetFullPath($StartupDirectory)
$shortcutPath = Join-Path $directory 'ZoneSpawner.lnk'
$shell = New-Object -ComObject WScript.Shell
try {
    if (Test-Path -LiteralPath $shortcutPath) {
        $existing = $shell.CreateShortcut($shortcutPath)
        if ($existing.TargetPath -ne $executable -or $existing.Arguments -ne '--start-hidden') {
            throw 'A different shortcut uses this name; it was preserved.'
        }
    }
    if ($Mode -eq 'Disable') {
        if (Test-Path -LiteralPath $shortcutPath) { Remove-Item -LiteralPath $shortcutPath }
        Write-Output 'ZoneSpawner autostart disabled.'
        return
    }
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'ZoneSpawner.exe is missing.' }
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $executable
    $shortcut.Arguments = '--start-hidden'
    $shortcut.WorkingDirectory = $PSScriptRoot
    $shortcut.Description = 'Zone Spawner: start hidden; Ctrl + Alt + S opens the menu.'
    $shortcut.IconLocation = "$executable,0"
    $shortcut.WindowStyle = 7
    $shortcut.Save()
    Write-Output 'ZoneSpawner autostart enabled (hidden, current Windows user).'
} finally {
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
}
