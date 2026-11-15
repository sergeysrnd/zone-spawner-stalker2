param(
    [string]$TccDirectory,
    [string]$OutputDirectory = $PSScriptRoot,
    [ValidateSet('Tcc', 'Msvc')][string]$Compiler = 'Tcc'
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
$nativeLibrary = Join-Path $OutputDirectory 'ZoneSpawnerLauncher.dll'
$nativeSource = Join-Path $PSScriptRoot 'src\MenuLauncher.c'
if ($Compiler -eq 'Tcc') {
    if (-not $TccDirectory) { throw 'TccDirectory is required for a Tiny C Compiler development build.' }
    $nativeCompiler = Join-Path $TccDirectory 'tcc.exe'
    & $nativeCompiler -shared -Wall -o $nativeLibrary $nativeSource -lkernel32 -luser32
    if ($LASTEXITCODE -ne 0) { throw 'Native launcher build failed.' }
    Write-Warning 'Tiny C Compiler build has no version resource. Use -Compiler Msvc for signing candidates.'
} else {
    $vswhere = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio C++ Build Tools and Windows SDK are required.' }
    $vsInstallation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if ($LASTEXITCODE -ne 0 -or -not $vsInstallation) { throw 'Visual Studio x64 C++ tools were not found.' }
    $vsDevelopmentScript = Join-Path $vsInstallation 'Common7\Tools\VsDevCmd.bat'
    $msvcEnvironmentCommand = 'call "' + $vsDevelopmentScript + '" -arch=x64 -host_arch=x64 >nul && set'
    $msvcEnvironmentRows = & $env:ComSpec /d /c $msvcEnvironmentCommand
    if ($LASTEXITCODE -ne 0) { throw 'Cannot initialize the Visual Studio x64 build environment.' }
    foreach ($row in $msvcEnvironmentRows) {
        if ($row -match '^([^=]+)=(.*)$') { [Environment]::SetEnvironmentVariable($Matches[1], $Matches[2], 'Process') }
    }
    . (Join-Path $PSScriptRoot 'tools/BuildVersion.ps1')
    $buildVersion = Get-ZoneSpawnerBuildVersion
    $numericVersion = $buildVersion.Replace('.', ',')
    $nativeIntermediate = Join-Path $OutputDirectory 'native-build'
    New-Item -ItemType Directory -Path $nativeIntermediate -Force | Out-Null
    $resourceSource = Join-Path $nativeIntermediate 'launcher.rc'
    $resourceObject = Join-Path $nativeIntermediate 'launcher.res'
    $nativeObject = Join-Path $nativeIntermediate 'launcher.obj'
    $nativeImportLibrary = Join-Path $nativeIntermediate 'launcher.lib'
    $resourceText = @"
#include <winver.h>
1 VERSIONINFO
FILEVERSION $numericVersion
PRODUCTVERSION $numericVersion
FILEFLAGSMASK 0x3fL
FILEFLAGS 0x0L
FILEOS 0x40004L
FILETYPE 0x2L
FILESUBTYPE 0x0L
BEGIN
    BLOCK "StringFileInfo"
    BEGIN
        BLOCK "040904b0"
        BEGIN
            VALUE "CompanyName", "Zone Spawner contributors\0"
            VALUE "FileDescription", "Zone Spawner game-start launcher\0"
            VALUE "FileVersion", "$buildVersion\0"
            VALUE "InternalName", "ZoneSpawnerLauncher\0"
            VALUE "LegalCopyright", "Copyright (c) 2026 Zone Spawner contributors\0"
            VALUE "OriginalFilename", "ZoneSpawnerLauncher.dll\0"
            VALUE "ProductName", "Zone Spawner\0"
            VALUE "ProductVersion", "$buildVersion\0"
        END
    END
    BLOCK "VarFileInfo"
    BEGIN
        VALUE "Translation", 0x409, 1200
    END
END
"@
    [IO.File]::WriteAllText($resourceSource, $resourceText, [Text.UTF8Encoding]::new($false))
    & rc.exe /nologo /fo $resourceObject $resourceSource
    if ($LASTEXITCODE -ne 0) { throw 'Launcher version resource compilation failed.' }
    & cl.exe /nologo /LD /O2 /W4 /MT "/Fo$nativeObject" "/Fe$nativeLibrary" $nativeSource $resourceObject user32.lib kernel32.lib /link /INCREMENTAL:NO "/IMPLIB:$nativeImportLibrary"
    if ($LASTEXITCODE -ne 0) { throw 'MSVC launcher build failed.' }
    $exportTable = (& dumpbin.exe /nologo /exports $nativeLibrary) -join [Environment]::NewLine
    if ($LASTEXITCODE -ne 0 -or $exportTable -notmatch '\blaunch_for_pid\b' -or $exportTable -notmatch '\blaunch_menu\b') {
        throw 'Launcher exports are missing.'
    }
}
Write-Output 'Built ZoneSpawnerLauncher.dll (x64).'
