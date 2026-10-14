param([Parameter(Mandatory=$true)][string]$WixDirectory,[switch]$SkipBuild)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
if(-not $SkipBuild){& (Join-Path $PSScriptRoot 'build.ps1') -Test}
$distRoot=Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Path $distRoot -Force | Out-Null
$license=Get-Content -Raw -LiteralPath (Join-Path $projectRoot 'LICENSE')
$escaped=$license.Replace('\','\\').Replace('{','\{').Replace('}','\}').Replace("`r",'').Replace("`n",'\par ')
[IO.File]::WriteAllText((Join-Path $projectRoot 'installer/license.rtf'),'{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\f0\fs20 '+$escaped+'}',[Text.Encoding]::ASCII)
$candle=Join-Path $WixDirectory 'candle.exe'
$light=Join-Path $WixDirectory 'light.exe'
if(-not(Test-Path -LiteralPath $candle)-or-not(Test-Path -LiteralPath $light)){throw 'WiX 3.14 portable binaries are required.'}
& $candle '-nologo' "-dProjectRoot=$projectRoot" '-out' (Join-Path $projectRoot 'build/StepVela.wixobj') (Join-Path $projectRoot 'installer/StepVela.wxs')
if($LASTEXITCODE-ne 0){throw 'Installer source compilation failed.'}
& $light '-nologo' '-ext' (Join-Path $WixDirectory 'WixUIExtension.dll') '-ext' (Join-Path $WixDirectory 'WixUtilExtension.dll') '-out' (Join-Path $distRoot 'StepVela-Setup.msi') (Join-Path $projectRoot 'build/StepVela.wixobj')
if($LASTEXITCODE-ne 0){throw 'Installer linking/validation failed.'}
$portable=Join-Path $distRoot 'StepVela-Portable.zip'
if(Test-Path -LiteralPath $portable){Remove-Item -LiteralPath $portable}
Compress-Archive -LiteralPath (Join-Path $projectRoot 'build/StepVela.exe'),(Join-Path $projectRoot 'LICENSE'),(Join-Path $projectRoot 'docs/QUICKSTART.txt') -DestinationPath $portable
$checksums=@('StepVela-Setup.msi','StepVela-Portable.zip')|ForEach-Object{$asset=Join-Path $distRoot $_;((Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLower()+'  '+$_)}
[IO.File]::WriteAllLines((Join-Path $distRoot 'SHA256SUMS.txt'),$checksums,(New-Object Text.UTF8Encoding $false))
Get-ChildItem -LiteralPath $distRoot | Select-Object Name,Length
