$dst = '<root>\Pywel-Together\CD-Pywel-Together'
$new = '<root>\Pywel-Together\Source-Code\bin\app\CD-Pywel-Together.exe'
$exe = "$dst\CD-Pywel-Together.exe"
# apaga sobras antigas
Get-ChildItem $dst -Filter '*.antigo' -ErrorAction SilentlyContinue | ForEach-Object {
  try { Remove-Item $_.FullName -Force -ErrorAction Stop; "apagado: $($_.Name)" } catch { "em uso, fica pro app apagar: $($_.Name)" }
}
$run = Get-Process -Name 'CD-Pywel-Together' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }
if ($run) { Rename-Item $exe 'CD-Pywel-Together.exe.antigo'; "exe em uso: renomeado" }
Copy-Item $new $exe -Force
"instalado: $((Get-Item $exe).Length) bytes"
