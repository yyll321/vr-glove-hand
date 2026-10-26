param([switch]$VerifyPreview)
$ErrorActionPreference='Stop'
$underwaterProject=Split-Path -Parent $PSScriptRoot
$underwaterEditor='F:/UserData/softwaredownload/unity/2022.3.15f1/Editor/Unity.exe'
$underwaterRunning=Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object {
    $underwaterMatch=[regex]::Match($_.CommandLine,'-project[Pp]ath\s+("[^"]+"|[^ ]+)')
    $underwaterMatch.Success -and ($underwaterMatch.Groups[1].Value.Trim('"').Replace('/','\').TrimEnd('\') -eq $underwaterProject.TrimEnd('\')) -and
        ($null -ne (Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue))
}
if($underwaterRunning){throw 'This Unity project is already open. Close its editor normally before using the DX11 launcher; no process was terminated.'}
$underwaterLog=Join-Path $underwaterProject 'Logs/UnderwaterCrashFix'
New-Item -ItemType Directory -Path $underwaterLog -Force | Out-Null
$underwaterArguments=@('-projectPath',('"'+$underwaterProject+'"'),'-force-d3d11','-logFile',('"'+(Join-Path $underwaterLog 'dx11-editor.log')+'"'))
if($VerifyPreview){$underwaterArguments+=@('-executeMethod','UnderwaterBaseline.Editor.UnderwaterPreviewCompatibility.OpenAndVerify')}
$underwaterProcess=Start-Process -FilePath $underwaterEditor -ArgumentList $underwaterArguments -WindowStyle Hidden -PassThru
Write-Output ('Opened underwater project using DX11. Unity process: '+$underwaterProcess.Id)
