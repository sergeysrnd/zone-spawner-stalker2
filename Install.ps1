[CmdletBinding()]
param([string]$GameRoot,[string]$BaseArchive,[string]$FixArchive)
$ErrorActionPreference='Stop'
$installer=Join-Path $PSScriptRoot 'ZoneSpawner.Setup.exe'
if(-not(Test-Path -LiteralPath $installer)){throw 'Build the package with Build.ps1 first.'}
if(-not $GameRoot -and -not $BaseArchive -and -not $FixArchive){Start-Process -FilePath $installer;return}
function Quote-NativeArgument([string]$Value){
    $escaped=[regex]::Replace($Value,'(\\*)"','$1$1\"')
    $escaped=[regex]::Replace($escaped,'(\\+)$','$1$1')
    return '"'+$escaped+'"'
}
$arguments='--install'
foreach($pair in @(@('--game',$GameRoot),@('--base',$BaseArchive),@('--fix',$FixArchive))){
    if($pair[1]){$arguments+=' '+$pair[0]+' '+(Quote-NativeArgument ([IO.Path]::GetFullPath($pair[1])))}
}
$start=[Diagnostics.ProcessStartInfo]::new($installer,$arguments)
$start.UseShellExecute=$false;$start.CreateNoWindow=$true
$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
$start.StandardOutputEncoding=[Text.UTF8Encoding]::new($false)
$start.StandardErrorEncoding=[Text.UTF8Encoding]::new($false)
$process=[Diagnostics.Process]::new();$process.StartInfo=$start
try {
    [void]$process.Start();$output=$process.StandardOutput.ReadToEndAsync();$errors=$process.StandardError.ReadToEndAsync()
    $process.WaitForExit();$text=$output.GetAwaiter().GetResult();$errorText=$errors.GetAwaiter().GetResult()
    if($text){Write-Host $text}
    if($process.ExitCode-ne 0){throw $errorText}
} finally {$process.Dispose()}
