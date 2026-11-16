using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CdmpLauncher
{
    // What a host launcher tells the guests that know its session code.
    public class HostInfo
    {
        public string Name = "", State = "fechado", GuestState = "", Game = "", AsiSha = "", AsiName = "", WinmmSha = "", Ping = "";
        public int Port = 37015;
        public bool ShareSave;
        public IPAddress Address;  // filled by the finder (where the answer came from)

        public bool InWorld => State == "mundo";

        public string Encode()
        {
            var d = new Dictionary<string, string>
            {
                ["name"] = Name, ["state"] = State, ["guest"] = GuestState, ["game"] = Game, ["asi"] = AsiSha,
                ["asiname"] = AsiName, ["winmm"] = WinmmSha, ["ping"] = Ping, ["port"] = Port.ToString(), ["save"] = ShareSave ? "1" : "0",
            };
            return string.Join("\n", d.Select(kv => kv.Key + "=" + (kv.Value ?? "").Replace("\n", " ")));
        }

        public static HostInfo Decode(string s)
        {
            var h = new HostInfo();
            foreach (var line in s.Split('\n'))
            {
                var i = line.IndexOf('=');
                if (i <= 0) continue;
                var k = line.Substring(0, i); var v = line.Substring(i + 1);
                switch (k)
                {
                    case "name": h.Name = v; break;
                    case "state": h.State = v; break;
                    case "guest": h.GuestState = v; break;
                    case "game": h.Game = v; break;
                    case "asi": h.AsiSha = v; break;
                    case "asiname": h.AsiName = v; break;
                    case "winmm": h.WinmmSha = v; break;
                    case "ping": h.Ping = v; break;
                    case "port": int.TryParse(v, out h.Port); break;
                    case "save": h.ShareSave = v == "1"; break;
                }
            }
            return h;
        }
    }

    // Host side: answers "who has session <tag>?" on UDP and serves the mod files to guests with the same code (TCP).
    public class HostBeacon : IDisposable
    {
        public const string Magic = "CDMP1";
        readonly string tag;
        readonly int udpPort, tcpPort;
        readonly Func<HostInfo> info;
        readonly Func<string, string> fileFor;  // "asi" | "winmm" | "guestini" | "save" -> path or null
        UdpClient udp;
        TcpListener tcp;
        CancellationTokenSource cts = new CancellationTokenSource();

        public HostBeacon(string code, int udpPort, int tcpPort, Func<HostInfo> info, Func<string, string> fileFor)
        {
            tag = Util.CodeTag(code);
            this.udpPort = udpPort; this.tcpPort = tcpPort; this.info = info; this.fileFor = fileFor;
        }

        public void Start()
        {
            udp = new UdpClient();
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, udpPort));
            Task.Run(UdpLoop);
            try
            {
                tcp = new TcpListener(IPAddress.Any, tcpPort);
                tcp.Start();
                Task.Run(TcpLoop);
            }
            catch (Exception e) { AppLog.Write(Lang.T("servidor de arquivos não abriu (porta ") + tcpPort + "): " + e.Message); }
            AppLog.Write(Lang.T("sessão aberta: guests com o mesmo código acham este PC (") + Util.MainLanIp() + ")");
        }

        async Task UdpLoop()
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    var r = await udp.ReceiveAsync();
                    var msg = Encoding.UTF8.GetString(r.Buffer);
                    if (msg != Magic + " FIND " + tag) continue;  // other sessions' codes: silence
                    var ans = Encoding.UTF8.GetBytes(Magic + " HERE " + tag + "\n" + info().Encode());
                    await udp.SendAsync(ans, ans.Length, r.RemoteEndPoint);
                }
                catch (ObjectDisposedException) { return; }
                catch { await Task.Delay(200); }
            }
        }

        async Task TcpLoop()
        {
            while (!cts.IsCancellationRequested)
            {
                TcpClient c;
                try { c = await tcp.AcceptTcpClientAsync(); }
                catch { return; }
                _ = Task.Run(() => Serve(c));
            }
        }

        void Serve(TcpClient c)
        {
            using (c)
            {
                try
                {
                    c.ReceiveTimeout = 10000;
                    var s = c.GetStream();
                    var line = ReadLine(s);
                    var parts = line.Split(' ');
                    if (parts.Length != 4 || parts[0] != Magic || parts[1] != "GET" || parts[2] != tag) { Send(s, "ERR código\n"); return; }
                    var path = fileFor(parts[3]);
                    if (path == null || !File.Exists(path)) { Send(s, "ERR sem arquivo\n"); return; }
                    using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        Send(s, "OK " + f.Length + "\n");
                        f.CopyTo(s);
                    }
                    AppLog.Write(parts[3] + Lang.T(" enviado para ") + ((IPEndPoint)c.Client.RemoteEndPoint).Address);
                }
                catch (Exception e) { AppLog.Write(Lang.T("envio falhou: ") + e.Message); }
            }
        }

        internal static string ReadLine(Stream s)
        {
            var sb = new StringBuilder();
            int b;
            while ((b = s.ReadByte()) >= 0 && b != '\n' && sb.Length < 512) sb.Append((char)b);
            return sb.ToString();
        }

        static void Send(Stream s, string t) { var b = Encoding.ASCII.GetBytes(t); s.Write(b, 0, b.Length); }

        public void Dispose()
        {
            cts.Cancel();
            try { udp?.Close(); } catch { }
            try { tcp?.Stop(); } catch { }
        }
    }

    // Guest side.
    public static class HostFinder
    {
        // Broadcasts on every LAN (and asks `manualIp` directly: VPNs like Radmin/ZeroTier/Tailscale don't broadcast).
        public static async Task<List<HostInfo>> Find(string code, int port, string manualIp, int waitMs = 1500)
        {
            var tag = Util.CodeTag(code);
            var found = new List<HostInfo>();
            using (var u = new UdpClient(0))
            {
                u.EnableBroadcast = true;
                var q = Encoding.UTF8.GetBytes(HostBeacon.Magic + " FIND " + tag);
                var targets = Util.Broadcasts();
                targets.Add(IPAddress.Loopback);
                if (!string.IsNullOrWhiteSpace(manualIp) && IPAddress.TryParse(manualIp.Trim(), out var mi)) targets.Add(mi);
                foreach (var t in targets)
                {
                    try { await u.SendAsync(q, q.Length, new IPEndPoint(t, port)); } catch { }
                }
                var end = DateTime.UtcNow.AddMilliseconds(waitMs);
                while (DateTime.UtcNow < end)
                {
                    var rt = u.ReceiveAsync();
                    var done = await Task.WhenAny(rt, Task.Delay(end - DateTime.UtcNow));
                    if (done != rt) break;
                    var r = rt.Result;
                    var msg = Encoding.UTF8.GetString(r.Buffer);
                    var head = HostBeacon.Magic + " HERE " + tag + "\n";
                    if (!msg.StartsWith(head)) continue;
                    var h = HostInfo.Decode(msg.Substring(head.Length));
                    h.Address = r.RemoteEndPoint.Address;
                    // the same host answers on several interfaces (and on loopback when it is this PC): keep one
                    if (found.Any(x => x.Name == h.Name && x.AsiSha == h.AsiSha && (Util.IsLocal(x.Address) == Util.IsLocal(h.Address)))) continue;
                    found.Add(h);
                }
            }
            return found;
        }

        public static async Task Download(IPAddress host, int tcpPort, string code, string what, string dst, Action<long, long> progress = null)
        {
            using (var c = new TcpClient())
            {
                var conn = c.ConnectAsync(host, tcpPort);
                if (await Task.WhenAny(conn, Task.Delay(5000)) != conn) throw new Exception(Lang.T("host não respondeu na porta ") + tcpPort);
                await conn;
                var s = c.GetStream();
                var req = Encoding.ASCII.GetBytes(HostBeacon.Magic + " GET " + Util.CodeTag(code) + " " + what + "\n");
                await s.WriteAsync(req, 0, req.Length);
                var head = HostBeacon.ReadLine(s);
                if (!head.StartsWith("OK ")) throw new Exception(Lang.T("host recusou ") + what + ": " + head);
                var len = long.Parse(head.Substring(3));
                var tmp = dst + ".baixando";
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                using (var o = File.Create(tmp))
                {
                    var buf = new byte[81920]; long got = 0;
                    while (got < len)
                    {
                        var n = await s.ReadAsync(buf, 0, (int)Math.Min(buf.Length, len - got));
                        if (n <= 0) throw new Exception(Lang.T("conexão caiu no meio do ") + what);
                        await o.WriteAsync(buf, 0, n);
                        got += n;
                        progress?.Invoke(got, len);
                    }
                }
                Util.MoveFile(tmp, dst);
            }
        }
    }
}
