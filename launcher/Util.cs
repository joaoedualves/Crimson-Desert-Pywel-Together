using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CdmpLauncher
{
    public static class Util
    {
        public static string Sha256(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var h = SHA256.Create())
                return Hex(h.ComputeHash(fs));
        }

        public static string Sha256Text(string s)
        {
            using (var h = SHA256.Create()) return Hex(h.ComputeHash(Encoding.UTF8.GetBytes(s)));
        }

        static string Hex(byte[] b) => BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();

        // Session code -> what goes on the wire (never the code itself).
        public static string CodeTag(string code) =>
            Sha256Text("cdmp-session:" + (code ?? "").Trim().ToUpperInvariant()).Substring(0, 16);

        public static string NewCode()
        {
            string[] words = { "LOBO", "CORVO", "URSO", "FALCAO", "TIGRE", "RAPOSA", "DRAGAO", "AGUIA", "PUMA", "TOURO", "COBRA", "LINCE" };
            var r = new Random();
            return words[r.Next(words.Length)] + "-" + r.Next(1000, 10000);
        }

        public static string Size(long b)
        {
            if (b >= 1L << 30) return (b / (double)(1L << 30)).ToString("0.0", Lang.English ? System.Globalization.CultureInfo.InvariantCulture : System.Globalization.CultureInfo.CurrentCulture) + " GB";
            if (b >= 1L << 20) return (b / (double)(1L << 20)).ToString("0.0", Lang.English ? System.Globalization.CultureInfo.InvariantCulture : System.Globalization.CultureInfo.CurrentCulture) + " MB";
            if (b >= 1L << 10) return (b / 1024.0).ToString("0") + " KB";
            return b + " B";
        }

        public static string ReadShared(string path, int maxBytes = int.MaxValue)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var n = (int)Math.Min(fs.Length, maxBytes);
                var buf = new byte[n];
                int got = 0;
                while (got < n) { var r = fs.Read(buf, got, n - got); if (r <= 0) break; got += r; }
                return Encoding.UTF8.GetString(buf, 0, got);
            }
        }

        // cdmp.asi / winmm.dll (and their copies): the helper does it (see Tools)
        public static bool IsModBinary(string path)
        {
            var n = Path.GetFileName(path).ToLowerInvariant();
            return n.EndsWith(".asi") || n.EndsWith(".dll") || n.Contains(".asi.") || n.Contains(".dll.");
        }

        public static void MoveFile(string src, string dst)
        {
            if (IsModBinary(src) || IsModBinary(dst))
            {
                var err = Tools.Run("move", src, dst);
                if (err != null) throw new IOException(err);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            if (File.Exists(dst)) File.Delete(dst);
            File.Move(src, dst);
        }

        public static void CopyFile(string src, string dst)
        {
            if (IsModBinary(src) || IsModBinary(dst))
            {
                var err = Tools.Run("copy", src, dst);
                if (err != null) throw new IOException(err);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            var tmp = dst + ".cdmp-tmp";
            using (var i = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var o = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                i.CopyTo(o);
            if (File.Exists(dst)) File.Delete(dst);
            File.Move(tmp, dst);
        }

        // ---------- Steam / game folder

        public static string SteamPath()
        {
            try
            {
                var p = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
                if (!string.IsNullOrEmpty(p)) return p.Replace('/', '\\');
            }
            catch { }
            return @"C:\Program Files (x86)\Steam";
        }

        public static uint ActiveSteamId32()
        {
            try
            {
                var v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam\ActiveProcess", "ActiveUser", 0);
                return v is int i ? unchecked((uint)i) : 0;
            }
            catch { return 0; }
        }

        // Last account that signed in on this PC (loginusers.vdf, MostRecent), when Steam is closed.
        public static uint RecentSteamId32()
        {
            try
            {
                var f = Path.Combine(SteamPath(), "config", "loginusers.vdf");
                var t = File.ReadAllText(f);
                uint best = 0;
                foreach (Match m in Regex.Matches(t, "\"(7656\\d{13})\"\\s*\\{([^}]*)\\}"))
                {
                    var id = ulong.Parse(m.Groups[1].Value) - 76561197960265728UL;
                    if (best == 0) best = (uint)id;
                    if (Regex.IsMatch(m.Groups[2].Value, "\"MostRecent\"\\s*\"1\"")) return (uint)id;
                }
                return best;
            }
            catch { return 0; }
        }

        public static uint SteamId32()
        {
            var a = ActiveSteamId32();
            return a != 0 ? a : RecentSteamId32();
        }

        public static string FindGameDir()
        {
            var libs = new List<string> { SteamPath() };
            try
            {
                var vdf = Path.Combine(SteamPath(), "steamapps", "libraryfolders.vdf");
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                    libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            }
            catch { }
            foreach (var d in DriveInfo.GetDrives())
            {
                try { if (d.IsReady) libs.Add(Path.Combine(d.RootDirectory.FullName, "SteamLibrary")); } catch { }
            }
            foreach (var l in libs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var b = Path.Combine(l, "steamapps", "common", "Crimson Desert", "bin64");
                if (File.Exists(Path.Combine(b, "CrimsonDesert.exe"))) return b;
            }
            return "";
        }

        static string verDir, verVal; static DateTime verAt;
        public static string GameVersion(string gameDir)
        {
            if (gameDir == verDir && (DateTime.Now - verAt).TotalSeconds < 15) return verVal;
            string v;
            try { v = FileVersionInfo.GetVersionInfo(Path.Combine(gameDir, "CrimsonDesert.exe")).FileVersion ?? ""; }
            catch { v = ""; }
            verDir = gameDir; verVal = v; verAt = DateTime.Now;
            return v;
        }

        public static string ExeStamp(string gameDir)
        {
            try
            {
                var f = new FileInfo(Path.Combine(gameDir, "CrimsonDesert.exe"));
                return f.Length + "|" + f.LastWriteTimeUtc.Ticks;
            }
            catch { return ""; }
        }

        public static string BuildOf(string version)
        {
            var parts = (version ?? "").Split('.');
            return parts.Length > 0 ? parts[parts.Length - 1] : "";
        }

        public static Process[] GameProcesses() => Process.GetProcessesByName("CrimsonDesert");

        public static bool SteamRunning() => Process.GetProcessesByName("steam").Length > 0;

        // ---------- network

        public static List<IPAddress> LocalIPv4()
        {
            var r = new List<IPAddress>();
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork) r.Add(ua.Address);
            }
            return r;
        }

        public static List<IPAddress> Broadcasts()
        {
            var r = new List<IPAddress> { IPAddress.Broadcast };
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;
                    var a = ua.Address.GetAddressBytes();
                    var m = ua.IPv4Mask.GetAddressBytes();
                    var b = new byte[4];
                    for (int i = 0; i < 4; i++) b[i] = (byte)(a[i] | ~m[i]);
                    r.Add(new IPAddress(b));
                }
            }
            return r.GroupBy(x => x.ToString()).Select(g => g.First()).ToList();
        }

        public static bool IsLocal(IPAddress a) =>
            IPAddress.IsLoopback(a) || LocalIPv4().Any(x => x.Equals(a));

        public static string MainLanIp()
        {
            try
            {
                using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    s.Connect("10.255.255.255", 1);
                    return ((IPEndPoint)s.LocalEndPoint).Address.ToString();
                }
            }
            catch { return LocalIPv4().FirstOrDefault(a => !IPAddress.IsLoopback(a))?.ToString() ?? "?"; }
        }

        public static void OpenFolder(string path)
        {
            try { Process.Start("explorer.exe", "\"" + path + "\""); } catch { }
        }
    }

    // Line based ini edit that keeps every comment and the order of the file.
    public static class Ini
    {
        public static bool Set(List<string> lines, string section, string key, string value)
        {
            int start = -1, end = lines.Count;
            for (int i = 0; i < lines.Count; i++)
            {
                var t = lines[i].Trim();
                if (t.StartsWith("[") && t.EndsWith("]"))
                {
                    if (start >= 0) { end = i; break; }
                    if (string.Equals(t.Substring(1, t.Length - 2).Trim(), section, StringComparison.OrdinalIgnoreCase)) start = i;
                }
            }
            if (start < 0)
            {
                lines.Add("");
                lines.Add("[" + section + "]");
                lines.Add(key + "=" + value);
                return true;
            }
            bool found = false, changed = false;
            var re = new Regex("^\\s*" + Regex.Escape(key) + "\\s*=", RegexOptions.IgnoreCase);
            for (int i = start + 1; i < end; i++)
            {
                if (!re.IsMatch(lines[i])) continue;
                found = true;
                var nl = key + "=" + value;
                if (lines[i].Trim() != nl) { lines[i] = nl; changed = true; }
            }
            if (!found)
            {
                int at = end;
                while (at - 1 > start && lines[at - 1].Trim() == "") at--;
                lines.Insert(at, key + "=" + value);
                changed = true;
            }
            return changed;
        }

        public static string Get(IEnumerable<string> lines, string section, string key)
        {
            bool inSec = false;
            string val = null;
            foreach (var raw in lines)
            {
                var t = raw.Trim();
                if (t.StartsWith("[") && t.EndsWith("]"))
                {
                    inSec = string.Equals(t.Substring(1, t.Length - 2).Trim(), section, StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inSec || t.StartsWith(";") || t.StartsWith("#")) continue;
                var eq = t.IndexOf('=');
                if (eq > 0 && string.Equals(t.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                    val = t.Substring(eq + 1).Trim();
            }
            return val;
        }

        public static List<string> Read(string path) => File.ReadAllLines(path, Encoding.UTF8).ToList();

        public static void Write(string path, List<string> lines) =>
            File.WriteAllText(path, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(false));
    }

    // The actions that touch other programs live in config\tools\pywel-tools.exe, not in this exe: key presses and
    // window placement in the game, and every copy/move of a mod binary (cdmp.asi, winmm.dll).
    public static class Tools
    {
        public static string ExePath
        {
            get
            {
                var own = Path.Combine(Config.DataDir, "tools", "pywel-tools.exe");
                if (File.Exists(own)) return own;
                var beside = Path.Combine(Config.ExeDir, "config", "tools", "pywel-tools.exe");
                return File.Exists(beside) ? beside : own;
            }
        }

        public static bool Available => File.Exists(ExePath);

        // null = ok, else the reason
        public static string Run(params string[] args)
        {
            if (!Available) return Lang.T("config\\tools\\pywel-tools.exe não encontrado");
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(ExePath, string.Join(" ", args.Select(Quote)))
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    var o = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(30000);
                    return o.StartsWith("ok") ? null : o.Replace("error: ", "");
                }
            }
            catch (Exception e) { return e.Message; }
        }

        static string Quote(string a) => a.Contains(" ") || a.Contains("\"") ? "\"" + a.Replace("\"", "\\\"") + "\"" : a;

        public const ushort ScanE = 0x12;
        public static string Press(int pid, ushort scan) => Run("press", pid.ToString(), scan.ToString());
    }
}
