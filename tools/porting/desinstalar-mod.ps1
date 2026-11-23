param([string]$g = 'F:\SteamLibrary\steamapps\common\Crimson Desert\bin64')
if (Get-Process CrimsonDesert*, CD-Pywel* -ErrorAction SilentlyContinue) { 'JOGO OU APP ABERTO: nada foi feito'; exit 1 }
if (-not (Test-Path "$g\CrimsonDesert.exe")) { 'pasta do jogo nao encontrada'; exit 1 }
# so arquivos do mod, direto em bin64: o carregador (winmm.dll) e tudo que comeca com cdmp
$itens = Get-ChildItem $g -Force | Where-Object { $_.Name -eq 'winmm.dll' -or $_.Name -like 'cdmp*' }
$bytes = ($itens | Where-Object { -not $_.PSIsContainer } | Measure-Object Length -Sum).Sum
"itens do mod: $($itens.Count) | $([math]::Round($bytes / 1GB, 1)) GB"
foreach ($i in $itens) { Remove-Item -LiteralPath $i.FullName -Recurse -Force -ErrorAction Continue }
$resto = Get-ChildItem $g -Force | Where-Object { $_.Name -eq 'winmm.dll' -or $_.Name -like 'cdmp*' }
"sobrou: $($resto.Count) " + (($resto | ForEach-Object Name) -join ', ')
"itens em bin64 agora: " + (Get-ChildItem $g -Force).Count
"FIM"
