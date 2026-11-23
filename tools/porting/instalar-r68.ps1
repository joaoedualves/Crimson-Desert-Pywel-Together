$root = '<root>'
$asi = "$root\nativeserver\build\bin\RelWithDebInfo\cdmp.asi"
$srv = "$root\Pywel-Together\CD-Pywel-Together\config\server"
"novo asi: " + (Get-FileHash $asi).Hash.Substring(0,12) + " | antes: " + (Get-FileHash "$srv\cdmp.asi").Hash.Substring(0,12)
Copy-Item $asi "$srv\cdmp.asi" -Force
Set-Content "$srv\version.txt" 'r68' -NoNewline
$dst = "$root\Pywel-Together\CD-Pywel-Together"
$exe = "$dst\CD-Pywel-Together.exe"
$new = "$root\Pywel-Together\Source-Code\bin\app\CD-Pywel-Together.exe"
Get-ChildItem $dst -Filter '*.antigo' | ForEach-Object { Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue }
Copy-Item $new $exe -Force -ErrorAction SilentlyContinue
if ((Get-FileHash $exe).Hash -ne (Get-FileHash $new).Hash) { Rename-Item $exe ("CD-Pywel-Together.exe." + (Get-Date -Format HHmmss) + ".antigo"); Copy-Item $new $exe -Force; "exe em uso: renomeado e trocado" } else { "exe trocado" }
& 'bg.ps1' "powershell -NoProfile -ExecutionPolicy Bypass -File '$root\Pywel-Together\atualizar-release.ps1'"
