param([switch]$Test)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path (Join-Path $frameworkRoot 'csc.exe'))) { $frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$compilerPath = Join-Path $frameworkRoot 'csc.exe'
if (-not (Test-Path $compilerPath)) { throw 'The .NET Framework C# compiler is required.' }
$buildRoot = Join-Path $projectRoot 'build'
New-Item -ItemType Directory -Path $buildRoot -Force | Out-Null
$outputPath = Join-Path $buildRoot 'StepVela.exe'
$compilerArgs = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/warn:4',"/out:$outputPath")
foreach ($assembly in @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Runtime.Serialization.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll')) { $compilerArgs += "/reference:$(Join-Path $frameworkRoot $assembly)" }
foreach ($assembly in @('UIAutomationClient.dll','UIAutomationTypes.dll','WindowsBase.dll')) { $compilerArgs += "/reference:$(Join-Path $frameworkRoot ('WPF\' + $assembly))" }
if (Test-Path (Join-Path $projectRoot 'assets/stepvela.ico')) { $compilerArgs += "/win32icon:$(Join-Path $projectRoot 'assets/stepvela.ico')" }
if (Test-Path (Join-Path $projectRoot 'src/app.manifest')) { $compilerArgs += "/win32manifest:$(Join-Path $projectRoot 'src/app.manifest')" }
$compilerArgs += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
& $compilerPath @compilerArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
Write-Output "Built $outputPath"
if ($Test) {
    $testRoot = Join-Path $projectRoot 'test-output'
    $testRun = Start-Process -FilePath $outputPath -ArgumentList @('--self-test', ('"' + $testRoot + '"')) -WindowStyle Hidden -Wait -PassThru
    Get-Content -LiteralPath (Join-Path $testRoot 'self-test-results.txt')
    if ($testRun.ExitCode -ne 0) { throw 'Self-test failed.' }
}
