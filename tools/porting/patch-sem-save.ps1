$ErrorActionPreference = 'Stop'
$lat = [Text.Encoding]::GetEncoding(28591)
$src = '<root>\Pywel-Together\Source-Code'
function Rpl($file, $old, $new) {
  $p = "$src\$file"; $t = [IO.File]::ReadAllText($p, $lat)
  $n = ([regex]::Matches($t, [regex]::Escape($old))).Count
  if ($n -lt 1) { throw "$file : ancora nao achada: $old" }
  [IO.File]::WriteAllText($p, $t.Replace($old, $new), $lat)
  "$n x $old"
}
# sem backup automatico de save
Rpl 'MainForm.cs' '                await Task.Run(() => SaveBackup.Snapshot("antes de abrir o jogo"));' ''
Rpl 'MainForm.cs' '            Task.Run(() => SaveBackup.Snapshot("depois de fechar o jogo"));' ''
Remove-Item "$src\SaveBackup.cs" -Force
# tira "HOST: save para o guest" e "Baixar save do host"
Rpl 'MainForm.cs' 'B(Lang.T("Baixar save do host"), async (s, e) => await DownloadSaveUi()), ' ''
Rpl 'MainForm.cs' 'Stack(p, guest, host, bk);' 'Stack(p, guest, bk);'
Rpl 'MainForm.cs' 'ShareSave = cfg.ShareSave,' 'ShareSave = false,'
Rpl 'MainForm.cs' 'if (!cfg.ShareSave) return null;' 'return null;   // the host''s save is never sent to anyone'
"ok"
