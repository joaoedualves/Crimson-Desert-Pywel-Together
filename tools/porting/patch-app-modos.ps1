$ErrorActionPreference = 'Stop'
$root = '<root>'
$app = "$root\Pywel-Together\Source-Code"
$lat = [Text.Encoding]::GetEncoding(28591)

# backup
$stamp = Get-Date -Format 'dd-MM_HH-mm'
$bk = "$root\Pywel-Together\Backup_Code\${stamp}_App-modos-codigo-amigos_backup"
New-Item -ItemType Directory -Force "$bk\01.Source_code\app" | Out-Null
robocopy $app "$bk\01.Source_code\app" /E /XD bin obj /NFL /NDL /NJH /NJS | Out-Null
"backup: $bk"

$texts = @{}
function Load($f) { if (-not $texts.ContainsKey($f)) { $raw = [IO.File]::ReadAllText("$app\$f", $lat); $texts[$f] = @{ crlf = $raw.Contains("`r`n"); t = $raw.Replace("`r`n", "`n") } }; $texts[$f] }
function Rep($f, $old, $new) {
  $o = Load $f
  $c = ([regex]::Matches($o.t, [regex]::Escape($old))).Count
  if ($c -ne 1) { throw "ancora achada $c vezes em ${f}: [$old]" }
  $o.t = $o.t.Replace($old, $new)
}
function RepRx($f, $pattern, $new, $opts) {
  $o = Load $f
  $m = [regex]::Matches($o.t, $pattern, $opts)
  if ($m.Count -ne 1) { throw "regex achou $($m.Count) vezes em ${f}: [$pattern]" }
  $o.t = [regex]::Replace($o.t, $pattern, { param($x) $new }, $opts)
}

# ---------------- Config.cs
Rep 'Config.cs' '[DataMember] public string FriendSteamId = "";' @'
[DataMember] public string FriendSteamId = "";
        [DataMember] public string FriendName = "";
        [DataMember] public string ConnMode = "code";   // "code" (session code, with anyone) or "friend" (a Steam friend, no code)
        public bool ByFriend => ConnMode == "friend";
'@

# ---------------- Theme.cs : Chevron public + new Seal
Rep 'Theme.cs' 'static void Chevron(Graphics g, Rectangle r, bool left, bool hot)' 'internal static void Chevron(Graphics g, Rectangle r, bool left, bool hot)'
$seal = @'
    // The session seal in the middle with two sections, switched by the arrows at the top:
    // the session code (anyone with the same code) or the Steam friends (pick a friend, no code).
    public class Seal : Glass
    {
        public readonly TextBox Code;
        public readonly OrnateButton Gen, Copy;
        public readonly ComboBox Friends;
        public string Hint = "";
        int mode, hoverArrow;
        public event Action<int> ModeChanged;
        public int Mode { get => mode; set { mode = value == 1 ? 1 : 0; ApplyMode(); } }
        const int W = 240;
        public Seal()
        {
            Height = 88;
            Code = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Ink, ForeColor = Theme.GoldLight, Font = Theme.Body(16f), TextAlign = HorizontalAlignment.Center, CharacterCasing = CharacterCasing.Upper };
            Friends = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Ink, ForeColor = Theme.GoldLight, Font = Theme.Body(11f), Visible = false, DropDownHeight = 220 };
            Gen = new OrnateButton { Text = Lang.T("Gerar") };
            Copy = new OrnateButton { Text = Lang.T("Copiar") };
            Controls.Add(Code); Controls.Add(Friends); Controls.Add(Gen); Controls.Add(Copy);
        }

        void ApplyMode()
        {
            Code.Visible = Gen.Visible = Copy.Visible = mode == 0;
            Friends.Visible = mode == 1;
            PerformLayout(); Invalidate();
        }

        Rectangle LeftBox => new Rectangle(Width / 2 - 150, 0, 34, 24);
        Rectangle RightBox => new Rectangle(Width / 2 + 116, 0, 34, 24);

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Code == null) return;
            int x = (Width - W) / 2;
            Code.SetBounds(x + 10, 31, W - 20, 28);
            Friends.SetBounds(x + 8, 31, W - 16, 28);
            Gen.SetBounds(x - 90, 27, 74, 34);
            Copy.SetBounds(x + W + 16, 27, 74, 34);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Smooth(g);
            int x = (Width - W) / 2;
            var title = mode == 0 ? Lang.T("C\u00d3DIGO DA SESS\u00c3O") : Lang.T("AMIGOS DA STEAM");
            using (var f = Theme.Body(7.5f)) Theme.DrawSpacedCentered(g, title, f, Theme.Gold, Width / 2f, 6, 2.6f);
            OptionRow.Chevron(g, LeftBox, true, hoverArrow == -1);
            OptionRow.Chevron(g, RightBox, false, hoverArrow == 1);
            using (var b = new SolidBrush(Theme.Ink)) g.FillRectangle(b, x, 24, W, 40);
            using (var p = new Pen(Theme.Gold)) g.DrawRectangle(p, x, 24, W, 40);
            Theme.Diamond(g, Theme.Gold, x, 44, 4f); Theme.Diamond(g, Theme.Gold, x + W, 44, 4f);
            using (var f = Theme.Body(9f))
                TextRenderer.DrawText(g, Hint, f, new Rectangle(0, 65, Width, 24), Theme.Ash, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var a = LeftBox.Contains(e.Location) ? -1 : RightBox.Contains(e.Location) ? 1 : 0;
            if (a != hoverArrow) { hoverArrow = a; Cursor = a != 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }
        protected override void OnMouseLeave(EventArgs e) { hoverArrow = 0; Cursor = Cursors.Default; Invalidate(); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (!LeftBox.Contains(e.Location) && !RightBox.Contains(e.Location)) return;
            Mode = 1 - mode;
            ModeChanged?.Invoke(mode);
        }
    }

    // Spaced capitals with diamonds between them; each opens something.
'@
RepRx 'Theme.cs' '    // The session code as a seal in the middle.*?\n    // Spaced capitals with diamonds between them; each opens something\.\n' $seal ([Text.RegularExpressions.RegexOptions]::Singleline)

# ---------------- Program.cs
Rep 'Program.cs' 'if (args.Length >= 4 && args[0] == "--selftest")' @'
if (args.Length >= 2 && args[0] == "--friends") return SteamFriends.RunChild(args[1]);
            if (args.Length >= 4 && args[0] == "--selftest")
'@

# ---------------- MainForm.cs
Rep 'MainForm.cs' 'txtCode = seal.Code; txtCode.Text = cfg.SessionCode;' @'
txtCode = seal.Code; txtCode.Text = cfg.SessionCode;
            seal.Mode = cfg.ByFriend ? 1 : 0;
            seal.ModeChanged += OnConnModeChanged;
            seal.Friends.SelectedIndexChanged += OnFriendPicked;
            seal.Friends.DropDown += (s, e) => RefreshFriends();
            Shown += (s, e) => { if (cfg.ByFriend) RefreshFriends(); };
'@
Rep 'MainForm.cs' 'txtFriendId = T(cfg.FriendSteamId, 170);' ''
RepRx 'MainForm.cs' '[ \t]*Row\(L\(Lang\.T\("SteamID64 do amigo \(opcional\):"\)\), txtFriendId[^\n]*\n' '' ([Text.RegularExpressions.RegexOptions]::None)
Rep 'MainForm.cs' '                cfg.FriendSteamId = new string(txtFriendId.Text.Where(char.IsDigit).ToArray());
' ''
$o = Load 'MainForm.cs'
$n = ([regex]::Matches($o.t, [regex]::Escape('Steam = cfg.UseSteam, PeerSteamId = cfg.FriendSteamId, SessionCode = cfg.SessionCode'))).Count
if ($n -ne 2) { throw "Overrides: $n" }
$o.t = $o.t.Replace('Steam = cfg.UseSteam, PeerSteamId = cfg.FriendSteamId, SessionCode = cfg.SessionCode', 'Steam = cfg.UseSteam, PeerSteamId = cfg.ByFriend ? (cfg.FriendSteamId ?? "") : "", SessionCode = cfg.ByFriend ? "" : cfg.SessionCode')
Rep 'MainForm.cs' 'if (!PreLaunch(Role.Host)) return;' 'if (!CheckConn(true)) return;
            if (!PreLaunch(Role.Host)) return;'
Rep 'MainForm.cs' 'if (!PreLaunch(Role.Guest)) return;' 'if (!CheckConn(false)) return;
            if (!PreLaunch(Role.Guest)) return;'
Rep 'MainForm.cs' 'seal.Hint = beacon != null' 'if (seal.Mode == 0) seal.Hint = beacon != null'
Rep 'MainForm.cs' '        void SaveFields()' @'
        // ---- connection mode (arrows on the seal): session code, or a Steam friend picked from the list
        bool friendsBusy;

        void OnConnModeChanged(int m)
        {
            cfg.ConnMode = m == 1 ? "friend" : "code";
            SaveCfg();
            if (m == 1) RefreshFriends(); else { seal.Hint = ""; seal.Invalidate(); }
        }

        void OnFriendPicked(object s, EventArgs e)
        {
            var f = seal.Friends.SelectedItem as FriendInfo;
            if (f == null) return;
            cfg.FriendSteamId = f.Id.ToString(); cfg.FriendName = f.Name; SaveCfg();
        }

        async void RefreshFriends()
        {
            if (friendsBusy) return;
            friendsBusy = true;
            try
            {
                seal.Hint = Lang.T("procurando seus amigos online..."); seal.Invalidate();
                var gd = cfg.GameDir;
                var res = await Task.Run(() => SteamFriends.Scan(gd));
                seal.Friends.BeginUpdate();
                seal.Friends.Items.Clear();
                int sel = -1;
                foreach (var f in res.Friends) { int i = seal.Friends.Items.Add(f); if (f.Id.ToString() == cfg.FriendSteamId) sel = i; }
                if (sel >= 0) seal.Friends.SelectedIndex = sel;
                seal.Friends.EndUpdate();
                seal.Hint = res.Error ?? (res.Friends.Count == 0 ? Lang.T("nenhum amigo online agora") : res.Friends.Count + Lang.T(" amigos online"));
            }
            catch (Exception ex) { AppLog.Write(Lang.T("amigos: ") + ex.Message); seal.Hint = ""; }
            finally { friendsBusy = false; seal.Invalidate(); }
        }

        bool CheckConn(bool host)
        {
            if (!cfg.UseSteam) return true;
            if (cfg.ByFriend)
            {
                if ((cfg.FriendSteamId ?? "").Length < 15) { Msg(Lang.T("Escolha o amigo na lista (setas no topo do selo).")); return false; }
                return true;
            }
            if (!host && txtCode.Text.Trim().Length < 4) { Msg(Lang.T("Digite o c\u00f3digo da sess\u00e3o (o mesmo do host).")); return false; }
            return true;
        }

        void SaveFields()
'@

# ---------------- write everything (only reached if all anchors matched)
foreach ($f in $texts.Keys) {
  $t = $texts[$f].t
  if ($texts[$f].crlf) { $t = $t.Replace("`n", "`r`n") }
  [IO.File]::WriteAllText("$app\$f", $t, $lat)
  "ok: $f"
}

# ---------------- new file SteamFriends.cs
$sf = @'
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
        public override string ToString() { return InGame ? Name + "  (" + Lang.T("jogando Crimson Desert") + ")" : Name; }
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
                            r.Friends.Add(new FriendInfo { Id = id, InGame = parts[3] == "1", Name = parts[4] });
                    }
                }
                r.Friends.Sort((a, b) => a.InGame != b.InGame ? (a.InGame ? -1 : 1) : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
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
                    if (st <= 0 && !inGame) continue;   // offline
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
'@
[IO.File]::WriteAllText("$app\SteamFriends.cs", $sf, (New-Object Text.UTF8Encoding $false))
"ok: SteamFriends.cs"
