$ErrorActionPreference = 'Stop'
$lat = [Text.Encoding]::GetEncoding(28591)
$root = '<root>\Pywel-Together'
$src = "$root\Source-Code"
$bk = "$root\Backup_Code\$(Get-Date -Format 'dd-MM_HH-mm')_Etapas-aguardando_backup"
New-Item -ItemType Directory -Force $bk | Out-Null
Copy-Item "$src\Theme.cs","$src\MainForm.cs" $bk
function Rpl($file, $old, $new) {
  $p = "$src\$file"; $t = [IO.File]::ReadAllText($p, $lat)
  $n = ([regex]::Matches($t, [regex]::Escape($old))).Count
  if ($n -ne 1) { throw "$file : ancora achada $n vezes: $old" }
  [IO.File]::WriteAllText($p, $t.Replace($old, $new), $lat)
}
# cartao: a etapa em andamento mostra o texto de "aguardando", nao o de "concluido"
Rpl 'Theme.cs' '        public string[] Steps = new string[0];' @'
        public string[] Steps = new string[0];
        public string[] Waiting;     // text of a step while it is the one in progress (not reached yet)
'@.TrimEnd("`r","`n")
Rpl 'Theme.cs' 'TextRenderer.DrawText(g, Steps[i], f, new Rectangle((int)x0 + 16' 'TextRenderer.DrawText(g, now && Waiting != null && i < Waiting.Length ? Waiting[i] : Steps[i], f, new Rectangle((int)x0 + 16'
Rpl 'MainForm.cs' 'Lang.T("Guest chegou") }, Lang.T("Abrir como host"));' @'
Lang.T("Guest chegou") }, Lang.T("Abrir como host"));
            hostCard.Waiting = new[] { Lang.T("Abrindo o jogo"), Lang.T("Carregando a tela de t\u00edtulo"), Lang.T("Entrando no mundo"), Lang.T("Aguardando o guest") };
'@.TrimEnd("`r","`n")
Rpl 'MainForm.cs' 'Lang.T("No mundo do host") }, Lang.T("Entrar no mundo"));' @'
Lang.T("No mundo do host") }, Lang.T("Entrar no mundo"));
            guestCard.Waiting = new[] { Lang.T("Procurando o host"), Lang.T("Aguardando o host entrar no mundo"), Lang.T("Abrindo o jogo"), Lang.T("Entrando no mundo do host") };
'@.TrimEnd("`r","`n")
Rpl 'MainForm.cs' 'if (rvFound && !saidFound) { saidFound = true;' 'btnGuest.Text = rvFound ? Lang.T("Aguardando o host") : Lang.T("Procurando host"); if (rvFound && !saidFound) { saidFound = true;'
"ok"
cd $src; dotnet build PywelTogether.csproj -c Release -o bin\app 2>&1 | Select-String 'error|Erro\(s\)' | Select-Object -First 8
