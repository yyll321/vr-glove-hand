$traceProject=Split-Path -Parent $PSScriptRoot
$traceAdb='F:/UserData/softwaredownload/unity/2022.3.15f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe'
$tracePython='F:/UserData/softwaredownload/conda/python.exe'
Set-Content (Join-Path $traceProject 'Logs/UnderwaterCommand.txt') 'previewcheck'
& (Join-Path $PSScriptRoot 'Run-UnderwaterResearchChecks.ps1') -Action Refresh
Start-Sleep -Seconds 6
& $tracePython (Join-Path $PSScriptRoot 'inspect_audio_sessions.py')
$traceHeadset=& $traceAdb -s PA9810MGL8240634G shell dumpsys audio
$traceHeadset | Set-Content (Join-Path $traceProject 'Logs/UnderwaterPresence/headset-audio-playing.txt')
$traceHeadset | Select-String -Pattern 'state:started|event:started|event:stopped' | Select-Object -Last 8
Get-Content 'C:/Program Files/Streaming Service/ps_server.log' -Tail 180 | Select-String -Pattern 'audio|Audio|sound|Sound' | Select-Object -Last 22
Get-CimInstance Win32_Process | Where-Object { $_.Name -match 'Unity.exe|PICO Streaming Service.exe|ps_server.exe' } | Select-Object Name,ProcessId
