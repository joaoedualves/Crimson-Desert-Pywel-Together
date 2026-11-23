$root = '<root>'
$dst = "$root\Pywel-Together\CD-Pywel-Together"
$exe = "$dst\CD-Pywel-Together.exe"
$new = "$root\Pywel-Together\Source-Code\bin\app\CD-Pywel-Together.exe"
$uso = "$root\porte-3732\_em-uso"
New-Item -ItemType Directory -Force $uso | Out-Null
Get-ChildItem $uso -File | ForEach-Object { Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue }
Copy-Item $new $exe -Force -ErrorAction SilentlyContinue
if ((Get-FileHash $exe).Hash -ne (Get-FileHash $new).Hash) {
  # exe aberto: sai da pasta do app (nada de .antigo la) e entra o novo
  Move-Item $exe "$uso\em-uso-$(Get-Date -Format HHmmss).exe"
  Copy-Item $new $exe -Force
  "exe em uso: movido para fora da pasta do app"
} else { "exe trocado" }
"pasta do app: " + ((Get-ChildItem $dst | ForEach-Object Name) -join ', ')
& 'bg.ps1' "powershell -NoProfile -ExecutionPolicy Bypass -File '$root\Pywel-Together\atualizar-release.ps1'"
