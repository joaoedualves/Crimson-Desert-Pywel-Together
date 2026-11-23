$ErrorActionPreference = 'Stop'
$lat = [Text.Encoding]::GetEncoding(28591)
$src = '<root>\Pywel-Together\Source-Code'
function Rpl($file, $old, $new) {
  $p = "$src\$file"; $t = [IO.File]::ReadAllText($p, $lat)
  $n = ([regex]::Matches($t, [regex]::Escape($old))).Count
  if ($n -ne 1) { throw "$file : ancora achada $n vezes: $old" }
  [IO.File]::WriteAllText($p, $t.Replace($old, $new), $lat)
}
Rpl 'MainForm.cs' 'Items = new[] { "Mod", Lang.T("Saves"), Lang.T("Ferramentas"), Lang.T("Config") }' 'Items = new[] { "Mod", Lang.T("Ferramentas"), Lang.T("Config") }'
Rpl 'MainForm.cs' 'i == 0 ? (Lang.T("Mod e arquivos"), modPage) : i == 1 ? (Lang.T("Saves"), savePage) : i == 2 ? (Lang.T("Ferramentas"), toolsPage) :' 'i == 0 ? (Lang.T("Mod e arquivos"), modPage) : i == 1 ? (Lang.T("Ferramentas"), toolsPage) :'
Rpl 'MainForm.cs' '(Lang.T("Mod e arquivos"), f.modPage), (Lang.T("Saves"), f.savePage), (Lang.T("Ferramentas"), f.toolsPage)' '(Lang.T("Mod e arquivos"), f.modPage), (Lang.T("Ferramentas"), f.toolsPage)'
"ok"
