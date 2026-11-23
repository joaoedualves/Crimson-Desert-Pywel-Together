$ErrorActionPreference = 'Stop'
$lat = [Text.Encoding]::GetEncoding(28591)
$root = '<root>\Pywel-Together'
$src = "$root\Source-Code"
$bk = "$root\Backup_Code\$(Get-Date -Format 'dd-MM_HH-mm')_Guest-espera-host_Save-bakcoop_backup"
New-Item -ItemType Directory -Force $bk | Out-Null
Copy-Item "$src\*.cs" $bk
function Rpl($file, $old, $new) {
  $p = "$src\$file"; $t = [IO.File]::ReadAllText($p, $lat)
  $n = ([regex]::Matches($t, [regex]::Escape($old))).Count
  if ($n -ne 1) { throw "$file : ancora achada $n vezes: $old" }
  [IO.File]::WriteAllText($p, $t.Replace($old, $new), $lat)
}

# ---- Program.cs: processos filhos do ponto de encontro
Rpl 'Program.cs' '            if (args.Length >= 2 && args[0] == "--friends")' @'
            if (args.Length >= 4 && args[0] == "--rv-host") return SteamRv.RunHost(args[1], args[2], args[3]);
            if (args.Length >= 4 && args[0] == "--rv-find") return SteamRv.RunFind(args[1], args[2], args[3]);
            if (args.Length >= 2 && args[0] == "--friends")
'@.TrimEnd("`r","`n")

# ---- Deployer.cs: host joga no save ORIGINAL (sem sandbox)
Rpl 'Deployer.cs' '            if (role == Role.Host) ch |= Ini.Set(lines, "net", "host_loopback_only", "0");' @'
            if (role == Role.Host) ch |= Ini.Set(lines, "net", "host_loopback_only", "0");
            // the game always saves in its ORIGINAL save folder; the app only makes copies (SaveBackup)
            if (role == Role.Host) ch |= Ini.Set(lines, "session2", "save_guard", "0");
'@.TrimEnd("`r","`n")

# ---- SteamRendezvous.cs: procura continua (um processo so, sem piscar na Steam)
$p = "$src\SteamRendezvous.cs"; $t = [IO.File]::ReadAllText($p, [Text.Encoding]::UTF8)
$a = $t.IndexOf('        // one look for the host'); $b = $t.IndexOf('        // ------------------------------------------------------------------ child processes')
if ($a -lt 0 -or $b -lt 0) { throw 'rv ancora 1' }
$t = $t.Substring(0, $a) + $t.Substring($b)
$a = $t.IndexOf('        public static int RunFind'); if ($a -lt 0) { throw 'rv ancora 2' }
$t = $t.Substring(0, $a) + @'
        public static int RunFind(string gameDir, string kind, string arg)
        {
            try
            {
                var a = Open(gameDir);
                var key = KeyOf(a, kind, arg);
                var run = a.Fn<VoidFn>("SteamAPI_RunCallbacks");
                var fStr = a.Fn<FilterStrFn>("SteamAPI_ISteamMatchmaking_AddRequestLobbyListStringFilter");
                var fDist = a.Fn<IntArgFn>("SteamAPI_ISteamMatchmaking_AddRequestLobbyListDistanceFilter");
                var fCount = a.Fn<IntArgFn>("SteamAPI_ISteamMatchmaking_AddRequestLobbyListResultCountFilter");
                var request = a.Fn<RequestFn>("SteamAPI_ISteamMatchmaking_RequestLobbyList");
                var byIdx = a.Fn<ByIdxFn>("SteamAPI_ISteamMatchmaking_GetLobbyByIndex");
                var owner = a.Fn<OwnerFn>("SteamAPI_ISteamMatchmaking_GetLobbyOwner");
                var data = a.Fn<GetDataFn>("SteamAPI_ISteamMatchmaking_GetLobbyData");
                var buf = Marshal.AllocHGlobal(64);
                bool eof = false;
                var th = new Thread(() => { try { while (Console.In.ReadLine() != null) { } } catch { } eof = true; }) { IsBackground = true };
                th.Start();
                for (int i = 0; i < 5; i++) { run(); Thread.Sleep(100); }
                while (!eof)
                {
                    fStr(a.Mm, "cdmp_rv", key, 0);
                    fDist(a.Mm, 3);
                    fCount(a.Mm, 10);
                    var line = "NONE";
                    if (Await(a, request(a.Mm), LobbyMatchListCb, buf, 4, 10000))
                    {
                        uint n = (uint)Marshal.ReadInt32(buf);
                        for (int i = 0; i < n && i < 10; i++)
                        {
                            var l = byIdx(a.Mm, i);
                            if (Utf8(data(a.Mm, l, "cdmp_rv")) != key) continue;
                            line = "FOUND\t" + owner(a.Mm, l) + "\t" + Utf8(data(a.Mm, l, "cdmp_state"));
                            break;
                        }
                    }
                    Console.WriteLine(line); Console.Out.Flush();
                    for (int i = 0; i < 25 && !eof; i++) { run(); Thread.Sleep(100); }
                }
                a.Fn<VoidFn>("SteamAPI_Shutdown")();
            }
            catch (Exception ex) { Console.WriteLine("ERR\t" + ex.Message.Replace('\t', ' ').Replace('\n', ' ')); }
            return 0;
        }
    }

    // Guest side: keeps looking for the host's meeting point until disposed.
    public sealed class RvGuest : IDisposable
    {
        Process p;
        public volatile bool Found;
        public volatile string State = "";
        public volatile string Error;

        public static RvGuest Start(string gameDir, string kind, string arg)
        {
            var r = new RvGuest();
            var exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var psi = SteamRv.Psi(exe, "--rv-find", gameDir, kind, arg);
            psi.RedirectStandardInput = true;
            r.p = Process.Start(psi);
            r.p.OutputDataReceived += (s, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                var f = e.Data.Split('\t');
                if (f[0] == "FOUND" && f.Length >= 3) { r.State = f[2]; r.Found = true; }
                else if (f[0] == "NONE") { r.Found = false; r.State = ""; }
                else if (f[0] == "ERR") r.Error = f.Length > 1 ? f[1] : "?";
            };
            r.p.BeginOutputReadLine();
            return r;
        }

        public bool Alive { get { try { return p != null && !p.HasExited; } catch { return false; } } }

        public void Dispose()
        {
            try { if (p != null && !p.HasExited) { try { p.StandardInput.Close(); } catch { } if (!p.WaitForExit(3000)) p.Kill(); } } catch { }
            p = null;
        }
    }
}
'@
[IO.File]::WriteAllText($p, $t, (New-Object Text.UTF8Encoding($true)))

# ---- MainForm.cs
Rpl 'MainForm.cs' '        HostBeacon beacon;' @'
        HostBeacon beacon;
        RvHost rvHost;                  // Steam meeting point: tells the guest's app when this host is in the world
        bool rvFound, rvWorld;          // guest: what the host's meeting point says
'@.TrimEnd("`r","`n")

# host: backup antes de abrir + ponto de encontro
Rpl 'MainForm.cs' '                hostRun = new GameRun(Role.Host,' @'
                await Task.Run(() => SaveBackup.Snapshot("antes de abrir o jogo"));
                if (cfg.UseSteam)
                {
                    rvHost?.Dispose();
                    try { rvHost = RvHost.Start(cfg.GameDir, cfg.ByFriend ? "friend" : "code", cfg.ByFriend ? (cfg.FriendSteamId ?? "") : cfg.SessionCode); rvHost.SetState("abrindo"); }
                    catch (Exception ex) { AppLog.Write("steam: " + ex.Message); rvHost = null; }
                }
                hostRun = new GameRun(Role.Host,
'@.TrimEnd("`r","`n")

# guest: NUNCA abre o jogo antes do host estar no mundo
Rpl 'MainForm.cs' @'
                    AppLog.Write(Lang.T("guest: conex
'@.TrimEnd("`r","`n") @'
                    if (!CheckConn(false)) return;
                    rvFound = rvWorld = false;
                    AppLog.Write("guest: procurando o host pela Steam. O jogo s\u00f3 abre quando o host estiver dentro do mundo");
                    var rvg = RvGuest.Start(cfg.GameDir, cfg.ByFriend ? "friend" : "code", cfg.ByFriend ? (cfg.FriendSteamId ?? "") : cfg.SessionCode);
                    try
                    {
                        bool saidFound = false; string lastErr = null;
                        while (true)
                        {
                            ct.ThrowIfCancellationRequested();
                            if (rvg.Error != null && rvg.Error != lastErr) { lastErr = rvg.Error; AppLog.Write("steam: " + lastErr); }
                            if (!rvg.Alive) { Msg("A Steam n\u00e3o respondeu" + (lastErr != null ? " (" + lastErr + ")" : "") + ". Abra a Steam e tente de novo."); return; }
                            rvFound = rvg.Found; rvWorld = rvFound && rvg.State == "mundo";
                            if (rvFound && !saidFound) { saidFound = true; AppLog.Write("guest: host encontrado na Steam; aguardando ele entrar no mundo"); }
                            if (!rvFound) saidFound = false;
                            if (rvWorld) break;
                            await Task.Delay(1000, ct);
                        }
                    }
                    finally { rvg.Dispose(); }
                    AppLog.Write("guest: host no mundo, abrindo o jogo");
                    if (false) AppLog.Write(Lang.T("guest: conex
'@.TrimEnd("`r","`n")

Rpl 'MainForm.cs' '                guestRun = new GameRun(Role.Guest,' @'
                await Task.Run(() => SaveBackup.Snapshot("antes de abrir o jogo"));
                guestRun = new GameRun(Role.Guest,
'@.TrimEnd("`r","`n")

# depois de fechar: backup; host fecha o ponto de encontro
Rpl 'MainForm.cs' '            var crashed = r.HasCrashDumpSinceStart();' @'
            if (r.Role == Role.Host) { rvHost?.Dispose(); rvHost = null; }
            else { rvFound = rvWorld = false; }
            Task.Run(() => SaveBackup.Snapshot("depois de fechar o jogo"));
            var crashed = r.HasCrashDumpSinceStart();
'@.TrimEnd("`r","`n")

# estado do host para o ponto de encontro + cartao do guest
Rpl 'MainForm.cs' '            // HOST card: this PC''s host, or the host a guest here found' @'
            if (rvHost != null && hostAlive) rvHost.SetState(hostRun.State == "mundo" ? "mundo" : "titulo");
            // HOST card: this PC's host, or the host a guest here found
'@.TrimEnd("`r","`n")
Rpl 'MainForm.cs' '                bool found = hostAlive || remoteHost != null;' '                bool found = hostAlive || remoteHost != null || rvFound;'
Rpl 'MainForm.cs' '(remoteHost != null && remoteHost.InWorld);' '(remoteHost != null && remoteHost.InWorld) || rvWorld;'
Rpl 'MainForm.cs' ': !found ? Lang.T(' ': !found ? cfg.UseSteam && cfg.ByFriend ? "procurando o amigo na Steam" : Lang.T('
"patch rv ok -> $bk"
