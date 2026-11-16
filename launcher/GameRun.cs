using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CdmpLauncher
{
    // One running copy of the game started by the launcher: start it with CDMP_ROLE, follow its mod log, press E.
    public class GameRun
    {
        public readonly Role Role;
        public Process Proc;
        public DateTime Started;
        public string State = "fechado";  // abrindo | titulo | entrando | mundo | fechado
        public string GuestState = "";     // host: "" | entrando | no mundo
        public string Ping = "";
        public string LogPath;
        public bool HostReadyRemote;       // guest: the mod says the host's server is reachable
        public bool ConnectionLocal;       // guest: the mod has not found the host ("connection local")
        public bool Tunnel;                // "Troc tunnel: connected"
        public bool PeerLost;              // "Session: peer timed out": the other side dropped (no automatic reconnect)
        readonly string envRole;           // CDMP_ROLE for the process; null = removed (guest on its own PC: cdmp.ini)
        public bool Alive => Proc != null && !Proc.HasExited;

        public event Action Changed;
        readonly string bin, alias;
        readonly CancellationTokenSource cts = new CancellationTokenSource();
        long pos;
        readonly StringBuilder partial = new StringBuilder();

        public GameRun(Role role, string bin, string logAlias, string envRole)
        {
            Role = role; this.bin = bin; alias = logAlias; this.envRole = envRole;
        }

        void Set(string s) { if (State != s) { State = s; AppLog.Write(Deployer.RoleName(Role) + ": " + Label(s)); } Changed?.Invoke(); }

        public static string Label(string s)
        {
            switch (s)
            {
                case "abrindo": return Lang.T("abrindo o jogo");
                case "titulo": return Lang.T("na tela de título");
                case "entrando": return Lang.T("carregando o mundo");
                case "mundo": return Lang.T("no mundo");
                default: return Lang.T("fechado");
            }
        }

        public void Start()
        {
            var psi = new ProcessStartInfo(Path.Combine(bin, "CrimsonDesert.exe"), "PlatformServiceType=Steam")
            {
                WorkingDirectory = bin,
                UseShellExecute = false,
            };
            psi.EnvironmentVariables["SteamAppId"] = "3321460";
            psi.EnvironmentVariables["SteamGameId"] = "3321460";
            // the variable may come from Windows (a user variable CDMP_ROLE=host): always set or removed here
            if (envRole == null) psi.EnvironmentVariables.Remove("CDMP_ROLE");
            else psi.EnvironmentVariables["CDMP_ROLE"] = envRole;
            Started = DateTime.Now;
            Proc = Process.Start(psi);
            Proc.EnableRaisingEvents = true;
            AppLog.Write(Deployer.RoleName(Role) + Lang.T(": jogo aberto (PID ") + Proc.Id + ")");
            Set("abrindo");
            Task.Run(Follow);
        }

        public void Stop()
        {
            cts.Cancel();
            try { if (Alive) Proc.Kill(); } catch { }
        }

        // ---------- log

        async Task Follow()
        {
            // the alias (cdmp.<role>.log) becomes this run's log once the mod starts; it names the dated file
            var t0 = DateTime.Now;
            while (!cts.IsCancellationRequested && Alive && LogPath == null && (DateTime.Now - t0).TotalSeconds < 240)
            {
                try
                {
                    if (File.Exists(alias))
                    {
                        var head = Util.ReadShared(alias, 16384);
                        var fresh = File.GetLastWriteTime(alias) >= Started && (DateTime.Now - t0).TotalSeconds > 60;
                        if (head.Contains("(PID " + Proc.Id + ")") || fresh)  // fresh: old mods that do not print the PID
                        {
                            var m = Regex.Match(head, @"\| log '([^']+)'");
                            LogPath = m.Success && File.Exists(m.Groups[1].Value) ? m.Groups[1].Value : alias;
                        }
                    }
                }
                catch { }
                if (LogPath == null) await Task.Delay(1000);
            }
            if (LogPath == null && Alive) AppLog.Write(Deployer.RoleName(Role) + Lang.T(": o mod não escreveu log (cdmp.asi/winmm.dll carregaram?)"));
            while (!cts.IsCancellationRequested)
            {
                if (LogPath != null) ReadNew();
                if (!Alive) break;
                await Task.Delay(700);
            }
            if (LogPath != null) ReadNew();
            Ping = "";
            Set("fechado");
            Exited?.Invoke(this);
        }

        public event Action<GameRun> Exited;

        void ReadNew()
        {
            try
            {
                using (var fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (fs.Length < pos) pos = 0;
                    if (fs.Length == pos) return;
                    fs.Seek(pos, SeekOrigin.Begin);
                    var buf = new byte[Math.Min(fs.Length - pos, 8 << 20)];
                    var n = fs.Read(buf, 0, buf.Length);
                    pos += n;
                    partial.Append(Encoding.UTF8.GetString(buf, 0, n));
                }
                var text = partial.ToString();
                var last = text.LastIndexOf('\n');
                if (last < 0) return;
                partial.Clear();
                partial.Append(text.Substring(last + 1));
                foreach (var line in text.Substring(0, last).Split('\n')) OnLine(line);
            }
            catch { }
        }

        static readonly Regex Rtt = new Regex(@"Net: connected \|.*?\| rtt ([\d.]+) ms");

        void OnLine(string l)
        {
            if (l.Contains("Late init: game is up")) { if (State == "abrindo") Set("titulo"); }
            else if (l.Contains("Local: pos (")) { if (State != "mundo") Set("mundo"); }
            else if (l.Contains("Net: connected"))
            {
                var m = Rtt.Match(l);
                if (m.Success) { Ping = m.Groups[1].Value + " ms"; Changed?.Invoke(); }
            }
            else if (l.Contains("Troc tunnel: connected")) { if (!Tunnel) { Tunnel = true; PeerLost = false; Changed?.Invoke(); } }
            else if (l.Contains("Troc tunnel: not connected")) { if (Tunnel) { Tunnel = false; Changed?.Invoke(); } }
            else if (l.Contains("Session: peer timed out"))
            {
                if (!PeerLost && State == "mundo")
                {
                    PeerLost = true; Tunnel = false; Ping = "";
                    AppLog.Write(Role == Role.Host ? Lang.T("host: a conexão com o guest caiu; o guest precisa entrar de novo")
                                                   : Lang.T("guest: a conexão com o host caiu; use Entrar de novo"));
                    if (Role == Role.Host) GuestState = "";
                    Changed?.Invoke();
                }
            }
            else if (l.Contains("Net: connecting"))
            {
                if (Ping != "") { Ping = ""; Changed?.Invoke(); }
                // host: the mod's link to the guest is gone (it went back to waiting for a player)
                if (Role == Role.Host && GuestState != "") { GuestState = ""; AppLog.Write(Lang.T("host: o guest desconectou")); Changed?.Invoke(); }
            }

            if (Role == Role.Host)
            {
                if (l.Contains("Session2 E3b H2: user 2 is in field")) { if (GuestState != "no mundo") { GuestState = "no mundo"; AppLog.Write(Lang.T("host: o guest entrou no mundo")); Changed?.Invoke(); } }
                else if (l.Contains("Session2 E3b H1") && GuestState == "") { GuestState = "entrando"; AppLog.Write(Lang.T("host: o guest está entrando")); Changed?.Invoke(); }
            }
            else if (l.Contains("Session2 guest:"))
            {
                var ready = l.Contains("connection REMOTE") && l.Contains("host ready yes");
                var local = l.Contains("connection local");
                if (ready != HostReadyRemote || local != ConnectionLocal) { HostReadyRemote = ready; ConnectionLocal = local; Changed?.Invoke(); }
            }
        }

        // ---------- auto E

        // Waits for the title, then presses E (only in this game's window) until `done` says it is in.
        public async Task AutoE(int firstDelaySec, int intervalSec, int maxPresses, Func<bool> canPress, Func<bool> done, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && Alive && State == "abrindo") await Task.Delay(1000, ct);
            if (!Alive || ct.IsCancellationRequested) return;
            var who = Deployer.RoleName(Role);
            // wait until pressing makes sense (guest: the host's server answered at the title)
            var tw = DateTime.Now;
            while (!ct.IsCancellationRequested && Alive && !done() && !canPress())
            {
                if ((DateTime.Now - tw).TotalSeconds > 300) { AppLog.Write(who + Lang.T(": o host não ficou pronto em 5 min; aperte E manualmente")); return; }
                await Task.Delay(1000, ct);
            }
            await Task.Delay(TimeSpan.FromSeconds(firstDelaySec), ct);
            for (int i = 1; i <= maxPresses && !ct.IsCancellationRequested && Alive && !done(); i++)
            {
                var err = Tools.Press(Proc.Id, Tools.ScanE);
                AppLog.Write(who + ": E (" + i + "/" + maxPresses + ")" + (err != null ? Lang.T(" falhou: ") + err : ""));
                for (int s = 0; s < intervalSec * 2 && !ct.IsCancellationRequested && Alive && !done(); s++) await Task.Delay(500, ct);
            }
            if (!done() && Alive) AppLog.Write(who + Lang.T(": não entrou com o carregamento automático; termine manualmente"));
        }

        public bool HasCrashDumpSinceStart() =>
            Directory.Exists(bin) && new DirectoryInfo(bin).GetFiles("cdmp_crash*.dmp").Any(f => f.LastWriteTime >= Started);
    }
}
