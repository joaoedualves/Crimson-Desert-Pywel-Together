$ErrorActionPreference = 'Stop'
$root = '<root>'
$repo = "$root\Pywel-Together\GitHub-Repo"
"tools do repo: " + ((Get-ChildItem "$repo\tools" | ForEach-Object Name) -join ', ')
# 1. ferramentas do porte de enderecos (scripts e mapas; sem zips nem binarios)
$pt = "$repo\tools\porting"
New-Item -ItemType Directory -Force $pt | Out-Null
Get-ChildItem "$root\port-ns" -File | Where-Object { $_.Extension -in '.py', '.json', '.sh', '.md', '.cpp' } | ForEach-Object { Copy-Item $_.FullName $pt -Force }
"porting: " + (Get-ChildItem $pt).Count + " arquivos"
# 2. fonte do mod antigo (modo copia, "v5", build anterior do jogo): so o codigo
$v5 = "$repo\legacy-copy-mode-v5"
if (Test-Path $v5) { Remove-Item $v5 -Recurse -Force }
New-Item -ItemType Directory -Force $v5 | Out-Null
$src = "$root\mod-atualizado"
"mod-atualizado: " + ((Get-ChildItem $src | ForEach-Object Name) -join ', ')
foreach ($d in 'src', 'cmake', 'tests', 'docs', 'third_party') { if (Test-Path "$src\$d") { Copy-Item "$src\$d" "$v5\$d" -Recurse } }
foreach ($f in 'CMakeLists.txt', 'LICENSE', 'README.md', 'CONTRIBUTING.md') { if (Test-Path "$src\$f") { Copy-Item "$src\$f" $v5 } }
"v5: {0:N1} MB em {1} arquivos" -f ((Get-ChildItem $v5 -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), (Get-ChildItem $v5 -Recurse -File).Count
Get-ChildItem $v5 -Recurse -File | Sort-Object Length -Descending | Select-Object -First 5 | ForEach-Object { $_.FullName.Replace("$v5\", '') + '  ' + $_.Length }
# 3. dados pessoais nos extras
$pat = '192\.168\.100|1164213154|1826351474|1714765639|386205289|7656119\d{10}|Users\\\\?Joao|Users\\\\?Felix|Documentos SSD|Felixs|F[eé]lix|Jo[aã]o|BucetinhaDoDeserto|SuggarDaddy|ATaverna|jbarbosarpc|joaomotion|Legion|SteamLibrary'
"--- achados nos extras"
Get-ChildItem $pt, $v5 -Recurse -File -Exclude *.png, *.ttf, *.ico, *.jpg | Select-String -Pattern $pat | Group-Object Path | ForEach-Object { $_.Name.Replace("$repo\", '') + '  x' + $_.Count + '  ' + (($_.Group | Select-Object -First 2 | ForEach-Object { $_.Line.Trim().Substring(0, [Math]::Min(110, $_.Line.Trim().Length)) }) -join ' || ') } | Select-Object -First 40
"FIM"
