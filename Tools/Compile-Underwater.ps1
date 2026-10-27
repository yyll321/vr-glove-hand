param([string]$UnityData = 'F:/UserData/softwaredownload/Unity/2022.3.15f1/Editor/Data')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compileRoot = Join-Path $projectRoot 'Logs/PhysicsReview/Compile'
New-Item -ItemType Directory -Path $compileRoot -Force | Out-Null
function Compile-UnityAssembly([string]$assemblyName, [string[]]$extraSources) {
    [xml]$projectXml = Get-Content -LiteralPath (Join-Path $projectRoot ($assemblyName + '.csproj')) -Raw
    $namespace = New-Object System.Xml.XmlNamespaceManager($projectXml.NameTable)
    $namespace.AddNamespace('p', 'http://schemas.microsoft.com/developer/msbuild/2003')
    $options = New-Object 'System.Collections.Generic.List[string]'
    $options.Add('/nologo'); $options.Add('/target:library'); $options.Add('/langversion:9.0'); $options.Add('/nostdlib+')
    $options.Add('/out:"' + (Join-Path $compileRoot ($assemblyName + '.dll')) + '"')
    $defines = $projectXml.SelectSingleNode('//p:DefineConstants', $namespace).InnerText
    $options.Add('/define:' + $defines)
    foreach ($reference in $projectXml.SelectNodes('//p:Reference/p:HintPath', $namespace)) {
        $refPath = $reference.InnerText
        if (-not [IO.Path]::IsPathRooted($refPath)) { $refPath = Join-Path $projectRoot $refPath }
        if (Test-Path -LiteralPath $refPath) { $options.Add('/reference:"' + $refPath + '"') }
        else { throw ('Missing compiler reference ' + $refPath) }
    }
    foreach ($reference in $projectXml.SelectNodes('//p:ProjectReference/p:Name', $namespace)) {
        $refPath = Join-Path $compileRoot ($reference.InnerText + '.dll')
        if (-not (Test-Path -LiteralPath $refPath)) { $refPath = Join-Path $projectRoot ('Library/ScriptAssemblies/' + $reference.InnerText + '.dll') }
        if (-not (Test-Path -LiteralPath $refPath)) { throw ('Missing project reference ' + $refPath) }
        $options.Add('/reference:"' + $refPath + '"')
    }
    $sources = @($projectXml.SelectNodes('//p:Compile', $namespace) | ForEach-Object { $_.Include }) + $extraSources
    foreach ($source in ($sources | Sort-Object -Unique)) { $options.Add('"' + (Join-Path $projectRoot $source) + '"') }
    $responseFile = Join-Path $compileRoot ($assemblyName + '.rsp')
    [IO.File]::WriteAllLines($responseFile, $options, (New-Object Text.UTF8Encoding($false)))
    & (Join-Path $UnityData 'NetCoreRuntime/dotnet.exe') (Join-Path $UnityData 'DotNetSdkRoslyn/csc.dll') ('@' + $responseFile) 2>&1 |
        Tee-Object -FilePath (Join-Path $compileRoot ($assemblyName + '.txt'))
    if ($LASTEXITCODE -ne 0) { throw ($assemblyName + ' compile failed with exit code ' + $LASTEXITCODE) }
}
Compile-UnityAssembly 'Assembly-CSharp' @('Assets/Underwater/Runtime/UnderwaterFluidVolume.cs', 'Assets/Underwater/Runtime/UnderwaterHydrodynamics.cs')
Compile-UnityAssembly 'Assembly-CSharp-Editor' @('Assets/Underwater/Editor/UnderwaterPhysicsUpgrade.cs')
Write-Output 'PASS runtime and editor assemblies compiled using Unity Roslyn. This does not run Unity or verify HMD rendering.'
