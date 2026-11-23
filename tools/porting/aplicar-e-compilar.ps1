param([string]$Zip = 'src_3732_r1.zip', [string]$Tag = '3732-r1', [switch]$NoBackup)
$ErrorActionPreference = 'Stop'
$root = '<root>'
$ns = "$root\nativeserver"
$log = "$root\porte-3732\build-$Tag.log"
"inicio $(Get-Date)" | Set-Content $log
if (-not $NoBackup) {
  $stamp = Get-Date -Format 'dd-MM_HH-mm'
  $bk = "$root\Pywel-Together\Backup_Code\${stamp}_Porte-3732-antes_backup"
  New-Item -ItemType Directory -Force "$bk\01.Source_code","$bk\02.DLL","$bk\03.Previous_Deploy" | Out-Null
  robocopy "$ns\src" "$bk\01.Source_code\src" /E /NFL /NDL /NJH /NJS | Out-Null
  Copy-Item 'F:\SteamLibrary\steamapps\common\Crimson Desert\bin64\cdmp.asi' "$bk\02.DLL\" -EA SilentlyContinue
  Get-ChildItem 'F:\SteamLibrary\steamapps\common\Crimson Desert\bin64' -Include 'cdmp*.ini','cdmp.asi' -Recurse -Depth 0 | Copy-Item -Destination "$bk\03.Previous_Deploy\" -EA SilentlyContinue
  "backup $bk" | Add-Content $log
  if (-not (Test-Path "$ns\src-3663")) { Rename-Item "$ns\src" 'src-3663' } else { Rename-Item "$ns\src" ("src-old-" + $stamp) }
} else {
  if (-not (Test-Path "$ns\src-3663")) { Rename-Item "$ns\src" 'src-3663' } else { Rename-Item "$ns\src" ("src-prev-" + (Get-Date -Format 'HHmmss')) }
}
$tmp = "$root\porte-3732\x-$Tag"
if (Test-Path $tmp) { Rename-Item $tmp ("$tmp-" + (Get-Date -Format 'HHmmss')) }
Expand-Archive "$root\porte-3732\$Zip" $tmp
Move-Item "$tmp\src" "$ns\src"
"src aplicado" | Add-Content $log
Get-Process MSBuild -EA SilentlyContinue | Stop-Process -Force
$vs = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$cmake = Join-Path $vs 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
& $cmake --build "$ns\build" --config RelWithDebInfo --target cdmp_mod --parallel -- /nodeReuse:false *>> $log
"EXIT $LASTEXITCODE" | Add-Content $log
Get-ChildItem "$ns\build" -Recurse -Filter cdmp.asi | Select-Object FullName,Length,LastWriteTime | Out-String | Add-Content $log
"fim $(Get-Date)" | Add-Content $log

