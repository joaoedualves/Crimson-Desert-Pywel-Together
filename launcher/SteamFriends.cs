using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace CdmpLauncher
{
    public class FriendInfo
    {
        public ulong Id;
        public string Name = "";
        public bool InGame;   // playing Crimson Desert right now
        public int State;      // Steam persona state: 0 = offline (or invisible), 1 = online, 2+ = busy/away/...
        public override string ToString() { return InGame ? Name + "  (" + Lang.T("jogando Crimson Desert") + ")" : State <= 0 ? Name + "  (offline)" : Name; }
    }

    public class FriendScan
    {
        public List<FriendInfo> Friends = new List<FriendInfo>();
        public string Error;
    }

    // Lists the Steam friends that are online. Steam never tells whether ANOTHER person owns a game, so the list is
    // every online friend, with the ones playing Crimson Desert right now marked (and first).
    // The Steam API is read in a short child process (this exe with --friends) that starts as Spacewar (480):
    // nothing in the launcher process changes and the real game is not touched.
    public static class SteamFriends
    {
        const uint CrimsonDesertApp = 3321460;

        public static FriendScan Scan(string gameDir)
        {
            var r = new FriendScan();
            try
            {
                if (string.IsNullOrWhiteSpace(gameDir) || !File.Exists(Path.Combine(gameDir, "steam_api64.dll")))
                { r.Error = Lang.T("defina a pasta do jogo na Config"); return r; }
                var exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                var psi = new ProcessStartInfo(exe, "--friends \"" + gameDir.TrimEnd('\\') + "\"")
                {
                    UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                };
                psi.EnvironmentVariables["SteamAppId"] = "480";
                psi.EnvironmentVariables["SteamGameId"] = "480";
                using (var p = Process.Start(psi))
                {
                    var outText = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(20000)) { try { p.Kill(); } catch { } r.Error = Lang.T("a Steam n\u00e3o respondeu"); return r; }
                    foreach (var line in outText.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = line.TrimEnd('\r').Split('\t');
                        if (parts[0] == "ERR") { r.Error = parts.Length > 1 ? parts[1] : "?"; }
                        else if (parts[0] == "F" && parts.Length >= 5 && ulong.TryParse(parts[1], out var id))
                            r.Friends.Add(new FriendInfo { Id = id, State = int.TryParse(parts[2], out var stt) ? stt : 0, InGame = parts[3] == "1", Name = parts[4] });
                    }
                }
                r.Friends.Sort((a, b) => { int ga = a.InGame ? 0 : a.State > 0 ? 1 : 2, gb = b.InGame ? 0 : b.State > 0 ? 1 : 2; return ga != gb ? ga.CompareTo(gb) : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); });
                if (r.Error != null) r.Error = Lang.T("abra a Steam (") + r.Error + ")";
            }
            catch (Exception ex) { r.Error = ex.Message; }
            return r;
        }

        // ------------------------------------------------------------------ child process
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr LoadLibraryW(string path);
        [DllImport("kernel32", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr h, string name);
        [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool SetDllDirectoryW(string dir);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int InitFlatFn(IntPtr err);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr PtrFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void VoidFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int CountFn(IntPtr self, int flags);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate ulong ByIndexFn(IntPtr self, int i, int flags);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int StateFn(IntPtr self, ulong id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr NameFn(IntPtr self, ulong id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.U1)] delegate bool GameFn(IntPtr self, ulong id, IntPtr info);

        static string Utf8(IntPtr p)
        {
            if (p == IntPtr.Zero) return "";
            int n = 0; while (Marshal.ReadByte(p, n) != 0 && n < 512) n++;
            var b = new byte[n]; Marshal.Copy(p, b, 0, n);
            return Encoding.UTF8.GetString(b);
        }

        public static int RunChild(string gameDir)
        {
            var sb = new StringBuilder();
            try
            {
                Environment.SetEnvironmentVariable("SteamAppId", "480");
                Environment.SetEnvironmentVariable("SteamGameId", "480");
                var dll = Path.Combine(gameDir, "steam_api64.dll");
                if (!Environment.Is64BitProcess || !File.Exists(dll)) { sb.Append("ERR\tdll\n"); return Done(sb); }
                SetDllDirectoryW(gameDir);
                var h = LoadLibraryW(dll);
                if (h == IntPtr.Zero) { sb.Append("ERR\tload\n"); return Done(sb); }
                T Fn<T>(string name) where T : class
                {
                    var a = GetProcAddress(h, name);
                    if (a == IntPtr.Zero) throw new Exception("export " + name);
                    return (T)(object)Marshal.GetDelegateForFunctionPointer(a, typeof(T));
                }
                var err = Marshal.AllocHGlobal(1024);
                for (int i = 0; i < 1024; i++) Marshal.WriteByte(err, i, 0);
                int init = Fn<InitFlatFn>("SteamAPI_InitFlat")(err);
                if (init != 0) { sb.Append("ERR\t").Append(Utf8(err).Replace('\t', ' ').Replace('\n', ' ')).Append('\n'); return Done(sb); }
                var frn = Fn<PtrFn>("SteamAPI_SteamFriends_v017")();
                var run = Fn<VoidFn>("SteamAPI_RunCallbacks");
                for (int i = 0; i < 12; i++) { run(); System.Threading.Thread.Sleep(100); }   // lets Steam deliver names and states
                var count = Fn<CountFn>("SteamAPI_ISteamFriends_GetFriendCount");
                var byIndex = Fn<ByIndexFn>("SteamAPI_ISteamFriends_GetFriendByIndex");
                var state = Fn<StateFn>("SteamAPI_ISteamFriends_GetFriendPersonaState");
                var name = Fn<NameFn>("SteamAPI_ISteamFriends_GetFriendPersonaName");
                var game = Fn<GameFn>("SteamAPI_ISteamFriends_GetFriendGamePlayed");
                var info = Marshal.AllocHGlobal(24);
                int n = count(frn, 4);   // k_EFriendFlagImmediate
                for (int i = 0; i < n && i < 2000; i++)
                {
                    var id = byIndex(frn, i, 4);
                    int st = state(frn, id);
                    bool inGame = false;
                    for (int k = 0; k < 24; k++) Marshal.WriteByte(info, k, 0);
                    if (game(frn, id, info)) inGame = ((ulong)Marshal.ReadInt64(info) & 0xFFFFFFUL) == CrimsonDesertApp;
                    sb.Append("F\t").Append(id).Append('\t').Append(st).Append('\t').Append(inGame ? "1" : "0").Append('\t')
                      .Append(Utf8(name(frn, id)).Replace('\t', ' ').Replace('\n', ' ')).Append('\n');
                }
                Fn<VoidFn>("SteamAPI_Shutdown")();
            }
            catch (Exception ex) { sb.Append("ERR\t").Append(ex.Message.Replace('\t', ' ').Replace('\n', ' ')).Append('\n'); }
            return Done(sb);
        }

        static int Done(StringBuilder sb)
        {
            var bytes = new UTF8Encoding(false).GetBytes(sb.ToString());
            using (var o = Console.OpenStandardOutput()) { o.Write(bytes, 0, bytes.Length); o.Flush(); }
            return 0;
        }
    }
}