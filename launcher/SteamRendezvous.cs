using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace CdmpLauncher
{
    public class RvResult
    {
        public bool Found;
        public ulong Owner;
        public string State = "";
        public string Error;
    }

    // Meeting point on Steam BEFORE the game opens. The host app keeps a small Steam lobby (as Spacewar, app 480, so the
    // real game is never touched) that says "title screen" or "in the world". The guest app only looks for that lobby;
    // the guest's game is NOT opened until the lobby says the host is in the world.
    //   code mode:   key comes from the session code (anyone with the same code, friend or not)
    //   friend mode: key comes from the two SteamIDs (only those two friends) and has no password
    // The two modes use different keys, so one never sees the other.
    public sealed class RvHost : IDisposable
    {
        Process p;
        string last = "";

        public static RvHost Start(string gameDir, string kind, string arg)
        {
            var r = new RvHost();
            var exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var psi = SteamRv.Psi(exe, "--rv-host", gameDir, kind, arg);
            psi.RedirectStandardInput = true;
            r.p = Process.Start(psi);
            r.p.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) AppLog.Write("steam: " + e.Data); };
            r.p.BeginOutputReadLine();
            return r;
        }

        public bool Alive { get { try { return p != null && !p.HasExited; } catch { return false; } } }

        public void SetState(string state)
        {
            if (state == last || !Alive) return;
            try { p.StandardInput.WriteLine("STATE " + state); p.StandardInput.Flush(); last = state; } catch { }
        }

        public void Dispose()
        {
            try { if (p != null && !p.HasExited) { try { p.StandardInput.Close(); } catch { } if (!p.WaitForExit(3000)) p.Kill(); } } catch { }
            p = null;
        }
    }

    public static class SteamRv
    {
        internal static ProcessStartInfo Psi(string exe, string mode, string gameDir, string kind, string arg)
        {
            var psi = new ProcessStartInfo(exe, mode + " \"" + gameDir.TrimEnd('\\') + "\" " + kind + " \"" + (arg ?? "").Replace("\"", "") + "\"")
            {
                UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8,
            };
            psi.EnvironmentVariables["SteamAppId"] = "480";
            psi.EnvironmentVariables["SteamGameId"] = "480";
            return psi;
        }

        // ------------------------------------------------------------------ child processes
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr LoadLibraryW(string path);
        [DllImport("kernel32", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr h, string name);
        [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool SetDllDirectoryW(string dir);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int InitFlatFn(IntPtr err);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr PtrFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void VoidFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate ulong IdFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate ulong CreateFn(IntPtr self, int type, int max);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate ulong RequestFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.U1)] delegate bool SetDataFn(IntPtr self, ulong lobby, [MarshalAs(UnmanagedType.LPStr)] string key, [MarshalAs(UnmanagedType.LPStr)] string value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr GetDataFn(IntPtr self, ulong lobby, [MarshalAs(UnmanagedType.LPStr)] string key);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void FilterStrFn(IntPtr self, [MarshalAs(UnmanagedType.LPStr)] string key, [MarshalAs(UnmanagedType.LPStr)] string value, int cmp);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void IntArgFn(IntPtr self, int v);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate ulong ByIdxFn(IntPtr self, int i);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate ulong OwnerFn(IntPtr self, ulong lobby);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void LeaveFn(IntPtr self, ulong lobby);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.U1)] delegate bool DoneFn(IntPtr self, ulong call, ref byte failed);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.U1)] delegate bool ResultFn(IntPtr self, ulong call, IntPtr buf, int cb, int cbIdx, ref byte failed);

        const int LobbyMatchListCb = 510, LobbyCreatedCb = 513, LobbyTypePublic = 2;

        static string Utf8(IntPtr p)
        {
            if (p == IntPtr.Zero) return "";
            int n = 0; while (Marshal.ReadByte(p, n) != 0 && n < 512) n++;
            var b = new byte[n]; Marshal.Copy(p, b, 0, n);
            return Encoding.UTF8.GetString(b);
        }

        static string Fnv(string s)
        {
            uint h = 2166136261;
            foreach (var c in Encoding.UTF8.GetBytes(s)) { h ^= c; h *= 16777619; }
            return h.ToString("x8");
        }

        sealed class Api
        {
            public IntPtr H, Mm, Utils, User;
            public T Fn<T>(string name) where T : class
            {
                var a = GetProcAddress(H, name);
                if (a == IntPtr.Zero) throw new Exception("export " + name);
                return (T)(object)Marshal.GetDelegateForFunctionPointer(a, typeof(T));
            }
        }

        static Api Open(string gameDir)
        {
            Environment.SetEnvironmentVariable("SteamAppId", "480");
            Environment.SetEnvironmentVariable("SteamGameId", "480");
            var dll = Path.Combine(gameDir, "steam_api64.dll");
            if (!Environment.Is64BitProcess || !File.Exists(dll)) throw new Exception("dll");
            SetDllDirectoryW(gameDir);
            var a = new Api { H = LoadLibraryW(dll) };
            if (a.H == IntPtr.Zero) throw new Exception("load");
            var err = Marshal.AllocHGlobal(1024);
            for (int i = 0; i < 1024; i++) Marshal.WriteByte(err, i, 0);
            if (a.Fn<InitFlatFn>("SteamAPI_InitFlat")(err) != 0) throw new Exception("abra a Steam (" + Utf8(err).Replace('\t', ' ').Replace('\n', ' ') + ")");
            a.Mm = a.Fn<PtrFn>("SteamAPI_SteamMatchmaking_v009")();
            a.Utils = a.Fn<PtrFn>("SteamAPI_SteamUtils_v010")();
            a.User = a.Fn<PtrFn>("SteamAPI_SteamUser_v023")();
            return a;
        }

        // key shared by the two apps: session code, or the two SteamIDs of the friends
        static string KeyOf(Api a, string kind, string arg)
        {
            if (kind == "code")
            {
                var c = new StringBuilder();
                foreach (var ch in (arg ?? "").ToUpperInvariant()) if (!char.IsWhiteSpace(ch)) c.Append(ch);
                return "c" + Fnv(c.ToString());
            }
            ulong me = a.Fn<IdFn>("SteamAPI_ISteamUser_GetSteamID")(a.User), other;
            if (!ulong.TryParse(arg, out other) || other == 0) throw new Exception("amigo");
            ulong lo = Math.Min(me, other), hi = Math.Max(me, other);
            return "f" + Fnv(lo + ":" + hi);
        }

        // waits for a Steam call result of size cb; returns false on timeout/failure
        static bool Await(Api a, ulong call, int cbIdx, IntPtr buf, int cb, int ms)
        {
            var run = a.Fn<VoidFn>("SteamAPI_RunCallbacks");
            var done = a.Fn<DoneFn>("SteamAPI_ISteamUtils_IsAPICallCompleted");
            var get = a.Fn<ResultFn>("SteamAPI_ISteamUtils_GetAPICallResult");
            var t0 = Environment.TickCount;
            byte failed = 0;
            while (Environment.TickCount - t0 < ms)
            {
                run();
                failed = 0;
                if (call != 0 && done(a.Utils, call, ref failed))
                {
                    failed = 0;
                    return get(a.Utils, call, buf, cb, cbIdx, ref failed) && failed == 0;
                }
                Thread.Sleep(100);
            }
            return false;
        }

        public static int RunHost(string gameDir, string kind, string arg)
        {
            try
            {
                var a = Open(gameDir);
                var key = KeyOf(a, kind, arg);
                var buf = Marshal.AllocHGlobal(64);
                var call = a.Fn<CreateFn>("SteamAPI_ISteamMatchmaking_CreateLobby")(a.Mm, LobbyTypePublic, 4);
                if (!Await(a, call, LobbyCreatedCb, buf, 16, 15000) || Marshal.ReadInt32(buf) != 1)
                { Console.WriteLine("nao abriu o ponto de encontro"); return 0; }
                var lobby = (ulong)Marshal.ReadInt64(buf, 8);
                var set = a.Fn<SetDataFn>("SteamAPI_ISteamMatchmaking_SetLobbyData");
                set(a.Mm, lobby, "cdmp_rv", key);
                set(a.Mm, lobby, "cdmp_state", "titulo");
                Console.WriteLine("ponto de encontro aberto");
                Console.Out.Flush();
                var run = a.Fn<VoidFn>("SteamAPI_RunCallbacks");
                var q = new System.Collections.Concurrent.ConcurrentQueue<string>();
                bool eof = false;
                var th = new Thread(() => { try { string l; while ((l = Console.In.ReadLine()) != null) q.Enqueue(l); } catch { } eof = true; }) { IsBackground = true };
                th.Start();
                while (!eof)
                {
                    run();
                    string line;
                    while (q.TryDequeue(out line))
                        if (line.StartsWith("STATE ")) set(a.Mm, lobby, "cdmp_state", line.Substring(6).Trim());
                    Thread.Sleep(200);
                }
                a.Fn<LeaveFn>("SteamAPI_ISteamMatchmaking_LeaveLobby")(a.Mm, lobby);
                a.Fn<VoidFn>("SteamAPI_Shutdown")();
            }
            catch (Exception ex) { Console.WriteLine("ERR\t" + ex.Message.Replace('\t', ' ').Replace('\n', ' ')); }
            return 0;
        }

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