$repo = '<root>\Pywel-Together\GitHub-Repo'
$pat = '192\.168\.100|1164213154|1826351474|1714765639|386205289|7656119\d{10}|Users\\\\?Joao|Users\\\\?Felix|Documentos SSD|Felixs|F[eé]lix|Jo[aã]o|BucetinhaDoDeserto|SuggarDaddy|ATaverna|jbarbosarpc|joaomotion|Legion'
Set-Location $repo
"--- arvore atual"
Get-ChildItem $repo -Recurse -File -Exclude *.png,*.ttf,*.ico,*.jpg | Where-Object { $_.FullName -notmatch '\\\.git\\' } | Select-String -Pattern $pat | Group-Object Path | ForEach-Object { $_.Name.Replace("$repo\", '') + '  x' + $_.Count + '  ' + (($_.Group | Select-Object -First 2 | ForEach-Object { $_.Line.Trim().Substring(0, [Math]::Min(90, $_.Line.Trim().Length)) }) -join ' || ') } | Select-Object -First 40
"--- historico (todos os commits)"
$revs = git rev-list --all
$hits = git grep -I -l -E '192\.168\.100|1164213154|1826351474|1714765639|386205289|7656119[0-9]{10}|Documentos SSD|Felixs|BucetinhaDoDeserto|SuggarDaddy|ATaverna|jbarbosarpc|joaomotion' $revs 2>$null
($hits | ForEach-Object { ($_ -split ':', 2)[1] } | Sort-Object -Unique | Select-Object -First 30)
"commits com achados: " + (($hits | ForEach-Object { ($_ -split ':', 2)[0] } | Sort-Object -Unique).Count)
"--- tamanho"
"{0:N1} MB em {1} arquivos" -f ((Get-ChildItem $repo -Recurse -File | Where-Object { $_.FullName -notmatch '\\\.git\\' } | Measure-Object Length -Sum).Sum / 1MB), (Get-ChildItem $repo -Recurse -File | Where-Object { $_.FullName -notmatch '\\\.git\\' }).Count
"FIM"
