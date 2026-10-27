param(
    [string]$DocumentPath = 'Documentation/水下临场感系统与水动力扩展组会汇报.docx',
    [string]$OutputDirectory = 'Logs/PhysicsReview/ReportQA'
)
# Native fallback for Windows hosts without the skill renderer's LibreOffice.
# Opens only this report in its own hidden Word automation instance.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$inputPath = [IO.Path]::GetFullPath((Join-Path $projectRoot $DocumentPath))
$renderDirectory = [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
New-Item -ItemType Directory -Path $renderDirectory -Force | Out-Null
$pdfPath = Join-Path $renderDirectory 'underwater-report.pdf'
$wordApp = $null
$reportDoc = $null
try {
    $wordApp = New-Object -ComObject Word.Application
    $wordApp.Visible = $false
    $wordApp.DisplayAlerts = 0
    $reportDoc = $wordApp.Documents.Open($inputPath, $false, $false, $false)
    $reportDoc.Repaginate()
    $reportDoc.Fields.Update() | Out-Null
    $reportDoc.Save()
    $reportDoc.ExportAsFixedFormat($pdfPath, 17)
    Write-Output ('Pages: ' + $reportDoc.ComputeStatistics(2))
    Write-Output ('Preview: ' + $pdfPath)
}
finally {
    if ($null -ne $reportDoc) { $reportDoc.Close(0); [Runtime.InteropServices.Marshal]::FinalReleaseComObject($reportDoc) | Out-Null }
    if ($null -ne $wordApp) { $wordApp.Quit(); [Runtime.InteropServices.Marshal]::FinalReleaseComObject($wordApp) | Out-Null }
}
