$ErrorActionPreference = 'Continue'
(Get-Process -Id $PID).PriorityClass = 'BelowNormal'
$root = '<root>'
$ns = "$root\nativeserver"
$log = "$root\porte-3732\build-r67.log"
"inicio $(Get-Date)" | Set-Content $log
$vs = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$cmake = Join-Path $vs 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
& $cmake --build "$ns\build" --config RelWithDebInfo --target cdmp_mod --parallel 4 -- /nodeReuse:false *>> $log
"EXIT $LASTEXITCODE" | Add-Content $log
Get-ChildItem "$ns\build" -Recurse -Filter cdmp.asi | Select-Object FullName,Length,LastWriteTime | Out-String | Add-Content $log
"fim $(Get-Date)" | Add-Content $log
