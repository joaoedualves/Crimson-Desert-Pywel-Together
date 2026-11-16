# Builds Pywel Together and lays out the app folder next to this one:
#   ..\CD-Pywel-Together\
#       CD-Pywel-Together.exe
#       config\tools\pywel-tools.exe
#       config\assets\fonts\*.ttf, config\assets\icons\app.ico
#       config\server\   (the co-op mod: cdmp.asi, winmm.dll, ini, version.txt; not touched here)
# Needs the .NET SDK (dotnet). Usage: powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$name = 'CD-Pywel-Together'
$exe = 'CD-Pywel-Together'
$app = Join-Path (Split-Path $here -Parent) $name
$out = Join-Path $here 'bin'

dotnet build (Join-Path $here 'PywelTogether.csproj') -c Release -o (Join-Path $out 'app')
if ($LASTEXITCODE -ne 0) { throw 'build failed (app)' }
dotnet build (Join-Path $here 'Tools\PywelTools.csproj') -c Release -o (Join-Path $out 'tools')
if ($LASTEXITCODE -ne 0) { throw 'build failed (tools)' }

$cfg = Join-Path $app 'config'
New-Item -ItemType Directory (Join-Path $cfg 'tools'), (Join-Path $cfg 'assets\fonts'), (Join-Path $cfg 'assets\icons'), (Join-Path $cfg 'server') -Force | Out-Null
Copy-Item (Join-Path $out "app\$exe.exe") $app -Force
Copy-Item (Join-Path $out 'tools\pywel-tools.exe') (Join-Path $cfg 'tools') -Force
Copy-Item (Join-Path $here 'fonts\*.ttf') (Join-Path $cfg 'assets\fonts') -Force
Copy-Item (Join-Path $here 'app.ico') (Join-Path $cfg 'assets\icons') -Force

Remove-Item $out, (Join-Path $here 'obj'), (Join-Path $here 'Tools\obj') -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "ok: $app"
