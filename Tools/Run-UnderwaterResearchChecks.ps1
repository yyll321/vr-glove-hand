param([ValidateSet('Refresh','Check')][string]$Action='Check')
$researchProject=Split-Path -Parent $PSScriptRoot
if($Action -eq 'Refresh') { & "$PSScriptRoot/Run-UnderwaterChecks.ps1" -Action Refresh; return }
$researchExe=Join-Path $researchProject 'Builds/UnderwaterResearch/UnderwaterResearch.exe'
Get-CimInstance Win32_Process -Filter "Name='UnderwaterResearch.exe'" | Where-Object { $_.ExecutablePath -eq $researchExe -and $_.CommandLine -like '*-underwater-research-check*' } | ForEach-Object { Stop-Process -Id $_.ProcessId }
$researchLog=Join-Path $researchProject 'Logs/UnderwaterResearch/player.log'
New-Item -ItemType Directory -Path (Split-Path -Parent $researchLog) -Force | Out-Null
Start-Process -FilePath $researchExe -ArgumentList '-underwater-research-check','-logFile',('"'+$researchLog+'"') -WorkingDirectory $researchProject -WindowStyle Hidden -PassThru | Select-Object Id
