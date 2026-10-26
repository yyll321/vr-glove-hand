param([Parameter(Mandatory=$true)][int]$UnityProcessId)
$ErrorActionPreference='Stop'
$reconstructionProject=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$reconstructionEditor='F:/UserData/softwaredownload/unity/2022.3.15f1/Editor/Unity.exe'
$reconstructionRunning=Get-Process -Id $UnityProcessId
if($reconstructionRunning.ProcessName -ne 'Unity') { throw 'The specified process is not Unity.' }
$reconstructionCommand=Join-Path $reconstructionProject 'Logs/Reconstruction/command.txt'
$reconstructionRequestedAt=[DateTime]::UtcNow
Set-Content -LiteralPath $reconstructionCommand -Value 'prepare-restart-dx12'
& (Join-Path (Split-Path -Parent $PSScriptRoot) 'Refresh-Unity.ps1') -UnityProcessId $UnityProcessId
$reconstructionResult=Join-Path $reconstructionProject 'Logs/Reconstruction/command-result.txt'
$reconstructionDeadline=[DateTime]::UtcNow.AddSeconds(25)
do {
    Start-Sleep -Milliseconds 500
    $reconstructionSaved=(Test-Path $reconstructionResult) -and ((Get-Item $reconstructionResult).LastWriteTimeUtc -ge $reconstructionRequestedAt) -and ((Get-Content $reconstructionResult -Raw) -match '^PASS prepare-restart-dx12') -and (-not (Test-Path $reconstructionCommand))
} while((-not $reconstructionSaved) -and [DateTime]::UtcNow -lt $reconstructionDeadline)
if(-not $reconstructionSaved) { throw 'The editor has not acknowledged saving the scene; no close was requested.' }
$null=$reconstructionRunning.CloseMainWindow()
$reconstructionExited=$reconstructionRunning.WaitForExit(25000)
$reconstructionLock=Join-Path $reconstructionProject 'Temp/UnityLockfile'
if((-not $reconstructionExited) -and (Test-Path $reconstructionLock)) { throw 'Unity still owns the project lock. Inspect the editor; it was not forcibly terminated.' }
$reconstructionArguments=@('-projectPath',('"'+$reconstructionProject+'"'),'-force-d3d12','-executeMethod','UnderwaterReconstruction.Editor.OfficialGaussianSetup.UpgradeAndCheck','-logFile',('"'+(Join-Path $reconstructionProject 'Logs/Reconstruction/dx12-editor.log')+'"'))
$reconstructionNew=Start-Process -FilePath $reconstructionEditor -ArgumentList $reconstructionArguments -PassThru
Write-Output ('Opened reconstruction project using DX12. Unity process: '+$reconstructionNew.Id)
