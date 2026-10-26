param([ValidateSet('Pico','Restore')][string]$Action='Pico')
$audioPython='F:/UserData/softwaredownload/conda/python.exe'
& $audioPython (Join-Path $PSScriptRoot 'select_preview_audio_route.py') $Action
if ($LASTEXITCODE -ne 0) { throw 'Audio route switch failed.' }
& $audioPython (Join-Path $PSScriptRoot 'inspect_audio_route.py')
