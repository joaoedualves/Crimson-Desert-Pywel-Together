$ErrorActionPreference = 'Stop'
$lat = [Text.Encoding]::GetEncoding(28591)
$root = '<root>\Pywel-Together'
$src = "$root\Source-Code"
$bk = "$root\Backup_Code\$(Get-Date -Format 'dd-MM_HH-mm')_Cada-um-no-seu-mundo_backup"
New-Item -ItemType Directory -Force $bk | Out-Null
Copy-Item "$src\Deployer.cs","$src\MainForm.cs" $bk
function Rpl($file, $old, $new) {
  $p = "$src\$file"; $t = [IO.File]::ReadAllText($p, $lat)
  $n = ([regex]::Matches($t, [regex]::Escape($old))).Count
  if ($n -ne 1) { throw "$file : ancora achada $n vezes: $old" }
  [IO.File]::WriteAllText($p, $t.Replace($old, $new), $lat)
}
Rpl 'Deployer.cs' '            if (role == Role.Host) ch |= Ini.Set(lines, "net", "host_loopback_only", "0");' @'
            if (role == Role.Host) ch |= Ini.Set(lines, "net", "host_loopback_only", "0");
            // Each player in their OWN world, with their own character and their own save (nothing about saves is
            // hooked): the other player is shown as a copy. The "enter the host's world" mode (session2) is off.
            ch |= Ini.Set(lines, "session2", "enabled", "0");
            ch |= Ini.Set(lines, "session2", "guest", "0");
            foreach (var kv in new[] { "enabled=1", "delay_ms=200", "offset_m=0", "equip=1", "horse=1", "damage=0", "minimap_icon=0x124", "map_pin=1",
                "enemies_target_copy=1", "guest_hits=1", "guest_hit_stagger=1", "player_hit_react=1", "player_hit_lethal=1", "copy_attack_slots=2" })
            { var kvp = kv.Split('='); ch |= Ini.Set(lines, "coop", kvp[0], kvp[1]); }
            ch |= Ini.Set(lines, "dev", "enemy_relay", "1");
'@.TrimEnd("`r","`n")
Rpl 'MainForm.cs' '                        () => run.HostReadyRemote,' '                        () => true,'
Rpl 'MainForm.cs' 'Lang.T("No mundo do host") }' 'Lang.T("No mundo") }'
Rpl 'MainForm.cs' 'Lang.T("Entrando no mundo do host")' 'Lang.T("Entrando no mundo")'
$p = "$src\MainForm.cs"; $t = [IO.File]::ReadAllText($p, $lat)
$rx = [regex]'(?s)guestRun\.State == "titulo" \? \(guestRun\.HostReadyRemote.*?(\r?\n\s*): guestRun\.Ping'
if ($rx.Matches($t).Count -ne 1) { throw 'ancora titulo' }
$t = $rx.Replace($t, 'guestRun.State == "titulo" ? "carregando o seu mundo"$1: guestRun.Ping')
[IO.File]::WriteAllText($p, $t, $lat)
"ok"
cd $src; dotnet build PywelTogether.csproj -c Release -o bin\app 2>&1 | Select-String 'error|Erro\(s\)' | Select-Object -First 8
