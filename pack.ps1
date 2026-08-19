# Assemble the folder that gets uploaded to the Steam Workshop.
#
# Built fresh into dist/ rather than reusing the local test install. That install is a mod folder
# PLib has been writing config.json into all through testing, and PLib resolves the config next to
# the DLL, so uploading it would hand every subscriber this machine's test settings as their
# defaults on first launch.
$ErrorActionPreference = 'Stop'
$Repo = $PSScriptRoot
$Dist = Join-Path $Repo 'dist\CellsOfInterest'

dotnet build "$Repo\CellsOfInterest.csproj" -c Release -v minimal
if ($LASTEXITCODE -ne 0) { throw 'release build failed' }

if (Test-Path $Dist) { Remove-Item $Dist -Recurse -Force }
New-Item -ItemType Directory -Path $Dist -Force | Out-Null

Copy-Item "$Repo\bin\Release\CellsOfInterest.dll" $Dist
foreach ($f in 'mod.yaml', 'mod_info.yaml', 'preview.png') { Copy-Item (Join-Path $Repo $f) $Dist }

# Gates. Each one has already been wrong once.
$dll = Join-Path $Dist 'CellsOfInterest.dll'
$asmVer = [Reflection.AssemblyName]::GetAssemblyName($dll).Version
$yamlVer = ((Get-Content (Join-Path $Dist 'mod_info.yaml') | Where-Object { $_ -match '^version:' }) -split ':')[1].Trim()
$stray = @(Get-ChildItem $Dist -Recurse | Where-Object { $_.Name -notin @('CellsOfInterest.dll', 'mod.yaml', 'mod_info.yaml', 'preview.png') })

if ("$asmVer" -eq '0.0.0.0') { throw "assembly version is 0.0.0.0 - the mods list would read v.0.0.0.0" }
if ("$asmVer" -notlike "$yamlVer*") { throw "assembly $asmVer disagrees with mod_info.yaml $yamlVer" }
if ($stray.Count -gt 0) { throw "unexpected files in the package: $($stray.Name -join ', ')" }
if (Test-Path (Join-Path $Dist 'config.json')) { throw 'config.json must never ship - it becomes the subscriber default' }
if (Test-Path (Join-Path $Dist 'PLib.dll')) { throw 'PLib.dll should be merged into the DLL, not shipped beside it' }

"package: $Dist"
Get-ChildItem $Dist | Select-Object Name, Length
"assembly version: $asmVer   mod_info version: $yamlVer"
