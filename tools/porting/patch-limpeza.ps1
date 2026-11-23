$ErrorActionPreference = 'Stop'
$lat = [Text.Encoding]::GetEncoding(28591)
$root = '<root>\Pywel-Together'
$p = "$root\Source-Code\Program.cs"
Copy-Item $p "$root\Backup_Code\$((Get-ChildItem $root\Backup_Code | Sort-Object LastWriteTime | Select-Object -Last 1).Name)\Program.cs" -Force
$t = [IO.File]::ReadAllText($p, $lat)
$anchor = '            var cfg = Config.Load();'
if (-not $t.Contains($anchor)) { throw 'ancora nao achada' }
$add = @'
            // sobras de atualizacao (exe trocado enquanto estava aberto): apaga sempre, em silencio
            try
            {
                foreach (var f in System.IO.Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*.antigo"))
                    try { System.IO.File.Delete(f); } catch { }
            }
            catch { }

'@
$t = $t.Replace($anchor, $add + $anchor)
[IO.File]::WriteAllText($p, $t, $lat)
"limpeza ok"
