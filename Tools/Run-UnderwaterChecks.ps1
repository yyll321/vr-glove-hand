param([ValidateSet('Refresh','Smoke')][string]$Action='Smoke')
$underwaterProject = Split-Path -Parent $PSScriptRoot
if ($Action -eq 'Refresh') {
    $underwaterEditor = Get-Process Unity | Where-Object { $_.MainWindowTitle -like 'My project -*' } | Select-Object -First 1
    if (!$underwaterEditor) { throw 'Target Unity editor window not found.' }
    & "$PSScriptRoot/Refresh-Unity.ps1" -UnityProcessId $underwaterEditor.Id
    return
}
$underwaterExecutable = Join-Path $underwaterProject 'Builds/UnderwaterBaseline/UnderwaterBaseline.exe'
# Only stop previous opt-in smoke players belonging to this exact project executable.
Get-CimInstance Win32_Process -Filter "Name='UnderwaterBaseline.exe'" | Where-Object {
    $_.ExecutablePath -eq $underwaterExecutable -and $_.CommandLine -like '*-underwater-smoke*'
} | ForEach-Object { Stop-Process -Id $_.ProcessId }
$underwaterLog = Join-Path $underwaterProject 'Logs/UnderwaterSmoke/player.log'
Start-Process -FilePath $underwaterExecutable -ArgumentList '-underwater-smoke','-logFile',('"'+$underwaterLog+'"') -WorkingDirectory $underwaterProject -WindowStyle Hidden -PassThru | Select-Object Id
