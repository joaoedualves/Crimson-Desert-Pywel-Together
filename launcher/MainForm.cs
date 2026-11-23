using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CdmpLauncher
{
    public partial class MainForm : Form
    {
        readonly Config cfg;
        readonly Deployer dep;
        GameRun hostRun, guestRun;
        HostBeacon beacon;
        string beaconCode;
        bool hostLostForGuest;          // the host the guest is in closed or restarted: the guest must join again
        int remoteMisses;
        HostInfo remoteHost;            // the host the guest found (null when the host is this PC)
        CancellationTokenSource guestCts, hostCts;
        FileSystemWatcher watcher;
        readonly List<DateTime> restores = new List<DateTime>();
        System.Windows.Forms.Timer tick;

        // theme
        static readonly Color Bg = Theme.Ink, Panel2 = Theme.Ink3, Fg = Theme.Parchment, Dim = Theme.Ash,
            Crimson = Color.FromArgb(1, 2, 3), Ok = Theme.Ok, Warn = Theme.Warn, Bad = Theme.Bad;

        // controls
        RoleCard hostCard, guestCard;
        Seal seal;
        OptionRow optAutoE;
        MenuBar menu;
        Chrome chrome;
        Glass logFrame;
        Bitmap backdrop;
        Panel modPage, savePage, toolsPage, cfgPage;
        Label banner, lblLang, lblIp, lblSteam, lblGuestDir, lblRec, lblLogs, lblDumps, lblProfInfo;
        TextBox txtHostName, txtGuestName, txtCode, txtLog, txtGameDir, txtManualIp, txtShareDir;
        CheckBox chkUdp; TextBox txtFriendId;
        CheckBox chkRecord, chkSync, chkWatch, chkShare;
        Button btnHost, btnGuest, btnStop;
        ListView lvFiles;
        NumericUpDown numPeer, numDisc, numFile, numFirst, numInterval, numMax, numKeep;

        public MainForm()
        {
            cfg = Config.Load();
            dep = new Deployer(cfg);
            Text = "Crimson Desert: Pywel Together";
            FormBorderStyle = FormBorderStyle.None;
            Font = Theme.Body(10f);
            BackColor = Bg; ForeColor = Fg;
            ClientSize = new Size(800, 880);
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            BuildUi();
            AppLog.Line += l => { if (IsHandleCreated) BeginInvoke((Action)(() => AppendLog(l))); };
            KeyPreview = true;
            KeyDown += OnShortcut;
            Load += (s, e) => FirstRun();
            FormClosing += OnClosingForm;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style |= 0x00020000 | 0x00080000;   // minimise from the taskbar, system menu
                cp.ClassStyle |= 0x00020000;            // drop shadow
                cp.ExStyle |= 0x02000000;               // WS_EX_COMPOSITED: no flicker under the translucent parts
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (backdrop == null || backdrop.Size != ClientSize) { backdrop?.Dispose(); backdrop = Theme.Backdrop(ClientSize); }
            e.Graphics.DrawImageUnscaled(backdrop, 0, 0);
            using (var p = new Pen(Color.FromArgb(110, Theme.GoldDark))) e.Graphics.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        // =====================================================================================  UI

        void BuildUi()
        {
            chrome = new Chrome();
            banner = new Label { AutoSize = false, Visible = false, BackColor = Color.FromArgb(215, 58, 36, 22), ForeColor = Theme.GoldLight, Font = Theme.Body(9.5f), TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand };
            banner.Click += (s, e) => ShowBanner(null);

            seal = new Seal();
            txtCode = seal.Code; txtCode.Text = cfg.SessionCode;
            seal.Gen.Click += (s, e) => { txtCode.Text = Util.NewCode(); SaveFields(); };
            seal.Copy.Click += (s, e) => { if (txtCode.Text != "") { Clipboard.SetText(txtCode.Text); AppLog.Write(Lang.T("código copiado: ") + txtCode.Text); } };

            hostCard = new RoleCard("Host", Lang.T("Abre o seu mundo e recebe os amigos"),
                new[] { Lang.T("Jogo aberto"), Lang.T("Tela de título"), Lang.T("No mundo"), Lang.T("Guest chegou") }, Lang.T("Abrir como host"));
            guestCard = new RoleCard("Guest", Lang.T("Entra no mundo de quem é o host"),
                new[] { Lang.T("Host encontrado"), Lang.T("Host no mundo"), Lang.T("Jogo aberto"), Lang.T("No mundo do host") }, Lang.T("Entrar no mundo"));
            txtHostName = hostCard.Name; txtHostName.Text = cfg.HostName;
            txtGuestName = guestCard.Name; txtGuestName.Text = cfg.GuestName;
            txtHostName.Leave += (s, e) => SaveFields(); txtGuestName.Leave += (s, e) => SaveFields();
            btnHost = hostCard.Action; btnHost.Click += OnHost; hostCard.Action.KeyHint = "H";
            btnGuest = guestCard.Action; btnGuest.Click += OnGuest; guestCard.Action.KeyHint = "G";

            optAutoE = new OptionRow { Label = Lang.T("Carregar o jogo automaticamente") };
            optAutoE.Items.AddRange(new[] { Lang.T("Ligado"), Lang.T("Desligado") });
            optAutoE.Index = cfg.AutoE ? 0 : 1;
            optAutoE.Changed += i => { cfg.AutoE = i == 0; SaveCfg(); };

            txtLog = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.None, BackColor = Theme.Ink,
                ForeColor = Color.FromArgb(206, 194, 172), BorderStyle = BorderStyle.None, Font = Theme.Mono(8.5f),
            };
            logFrame = new Glass();
            logFrame.Controls.Add(txtLog);
            logFrame.Layout += (s, e) => txtLog.SetBounds(10, 24, logFrame.Width - 20, logFrame.Height - 32);
            logFrame.Paint += (s, e) =>
            {
                var g = e.Graphics; Theme.Smooth(g);
                using (var b = new SolidBrush(Color.FromArgb(225, Theme.Ink))) g.FillRectangle(b, 0, 18, logFrame.Width - 1, logFrame.Height - 19);
                using (var p = new Pen(Theme.Line)) g.DrawRectangle(p, 0, 18, logFrame.Width - 1, logFrame.Height - 19);
                using (var f = Theme.Body(7.5f)) Theme.DrawSpaced(g, Lang.T("CRÔNICA DA SESSÃO"), f, Theme.Gold, 2, 2, 2.4f);
            };

            menu = new MenuBar { Items = new[] { "Mod", Lang.T("Saves"), Lang.T("Ferramentas"), Lang.T("Config") } };
            modPage = ModTab(); savePage = SaveTab(); toolsPage = ToolsTab(); cfgPage = ConfigTab();
            menu.Clicked += OpenPage;

            lblIp = new Label { AutoSize = false, BackColor = Color.Transparent, ForeColor = Dim, Font = Theme.Body(9f), TextAlign = ContentAlignment.MiddleLeft };
            lblLang = new Label { AutoSize = false, BackColor = Color.Transparent, ForeColor = Theme.Gold, Font = Theme.Body(9f, FontStyle.Underline), TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand, Text = "Language / Idioma" };
            lblLang.Click += (s, e) => ChangeLanguage();
            btnStop = B(Lang.T("Fechar o jogo"), OnStop);
            btnStop.Height = 28;

            Controls.AddRange(new Control[] { chrome, banner, seal, hostCard, guestCard, optAutoE, logFrame, menu, lblIp, lblLang, btnStop });
            Layout += (s, e) => LayoutMain();
            tick = new System.Windows.Forms.Timer { Interval = 1000 };
            tick.Tick += (s, e) => RefreshStatus();
            tick.Start();
        }

        void LayoutMain()
        {
            int w = ClientSize.Width, m = 24, y = chrome.Height;
            chrome.SetBounds(0, 0, w, chrome.Height);
            if (banner.Visible) { banner.SetBounds(m, y + 6, w - 2 * m, 30); y += 40; }
            seal.SetBounds(m, y + 8, w - 2 * m, 88); y = seal.Bottom + 10;
            int cw = (w - 2 * m - 20) / 2;
            hostCard.SetBounds(m, y, cw, 350);
            guestCard.SetBounds(m + cw + 20, y, cw, 350); y = hostCard.Bottom + 16;
            optAutoE.SetBounds(m, y, w - 2 * m, 40); y += 52;
            int bottom = ClientSize.Height;
            int footerY = bottom - 40, menuY = footerY - 44;
            logFrame.SetBounds(m, y, w - 2 * m, Math.Max(60, menuY - y - 6));
            menu.SetBounds(m, menuY, w - 2 * m, 40);
            btnStop.SetBounds(w - m - 150, footerY + 4, 150, 28);
            lblLang.SetBounds(w - m - 150 - 150, footerY + 4, 140, 28);
            lblIp.SetBounds(m, footerY + 4, w - 2 * m - 310, 28);
        }

        // H = open as host, G = join the world; never while typing (a focused text field gets the letter).
        void OnShortcut(object s, KeyEventArgs e)
        {
            if (e.Control || e.Alt || TypingSomewhere(this)) return;
            if (e.KeyCode == Keys.H) { e.Handled = e.SuppressKeyPress = true; Trigger("host", Lang.T("tecla H")); }
            else if (e.KeyCode == Keys.G) { e.Handled = e.SuppressKeyPress = true; Trigger("guest", Lang.T("tecla G")); }
        }

        static bool TypingSomewhere(Control c)
        {
            foreach (Control k in c.Controls)
            {
                if (k.Focused && (k is TextBoxBase || k is NumericUpDown || k is ComboBox || k is UpDownBase)) return true;
                if (k.ContainsFocus && TypingSomewhere(k)) return true;
            }
            return false;
        }

        // The HOST / GUEST buttons from a key or from a command (--host / --guest sent to this window).
        public void Trigger(string what, string from)
        {
            var b = what == "host" ? btnHost : btnGuest;
            if (!b.Enabled) { AppLog.Write(from + ": " + what + Lang.T(" ignorado (já está aberto)")); return; }
            AppLog.Write(from + ": " + (what == "host" ? Lang.T("Abrir como host") : Lang.T("Entrar no mundo")));
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            b.PerformClick();
        }

        public string StartCommand;   // --host / --guest given when the app was started

        public void OnCommand(string cmd)
        {
            AppLog.Write(Lang.T("comando recebido: ") + cmd);
            if (cmd == "--host") Trigger("host", Lang.T("comando"));
            else if (cmd == "--guest") Trigger("guest", Lang.T("comando"));
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        void ChangeLanguage()
        {
            var pick = LanguagePicker.Ask(this);
            if (pick == null || pick == cfg.Language) return;
            SaveFields();
            cfg.Language = pick;
            SaveCfg();
            if (new[] { hostRun, guestRun }.Any(r => r != null && r.Alive) || guestCts != null)
            {
                Msg(pick == "en" ? "The language changes the next time the launcher opens." : "O idioma muda na próxima vez que o launcher abrir.");
                return;
            }
            Application.Restart();
        }

        void OpenPage(int i)
        {
            var (title, page) = i == 0 ? (Lang.T("Mod e arquivos"), modPage) : i == 1 ? (Lang.T("Saves"), savePage) : i == 2 ? (Lang.T("Ferramentas"), toolsPage) : (Lang.T("Config"), cfgPage);
            if (i == 0) RefreshFiles(); if (i == 1) RefreshSaves(); if (i == 2) RefreshCleanup();
            using (var d = new ThemedDialog(title, page, new Size(920, Math.Min(820, Screen.FromControl(this).WorkingArea.Height - 40))))
                d.ShowDialog(this);
            SaveFields();
        }

        Panel Page(string title) => new Panel { Tag = title.Trim(), Dock = DockStyle.Fill, BackColor = Bg, ForeColor = Fg, Padding = new Padding(22, 14, 22, 14) };

        static Label L(string t, Color? c = null, float size = 10f, bool bold = false) =>
            new Label { Text = t, AutoSize = true, ForeColor = c ?? Fg, Font = Theme.Body(size, bold ? FontStyle.Bold : FontStyle.Regular), Margin = new Padding(3, 8, 6, 3) };

        static Button B(string t, EventHandler h, Color? bg = null, int w = 0)
        {
            var b = new OrnateButton { Text = t, Kind = bg == Crimson ? ButtonKind.Primary : ButtonKind.Normal, Margin = new Padding(3, 4, 8, 4), Height = 34 };
            b.Width = w > 0 ? w : TextRenderer.MeasureText(t, b.Font).Width + 36;
            b.Click += h;
            return b;
        }

        static TextBox T(string v, int w = 220) => new TextBox { Text = v ?? "", Width = w, BackColor = Theme.Ink, ForeColor = Theme.GoldLight, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(3, 7, 8, 3), Font = Theme.Body(10f) };

        static CheckBox C(string t, bool v) => new CheckBox { Text = t, Checked = v, AutoSize = true, ForeColor = Fg, Margin = new Padding(3, 6, 12, 3), FlatStyle = FlatStyle.Flat, Font = Theme.Body(10f) };

        static FlowLayoutPanel Row(params Control[] cs)
        {
            var f = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Top, Margin = new Padding(0, 2, 0, 2) };
            f.Controls.AddRange(cs);
            return f;
        }

        static Section Group(string t, params Control[] rows)
        {
            var g = new Section(t);
            foreach (var r in rows) g.Stack.Controls.Add(r);
            return g;
        }

        // A page whose groups stack from the top (Dock=Top adds in reverse, so they are added last-first).
        static void Stack(Control page, params Control[] items)
        {
            var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            for (int i = items.Length - 1; i >= 0; i--) host.Controls.Add(items[i]);
            page.Controls.Add(host);
        }

        Panel ModTab()
        {
            var p = Page("  Mod e arquivos  ");
            lblProfInfo = L("", Dim, 9.5f);
            var prof = Group(Lang.T("Mod instalado"),
                lblProfInfo,
                Row(B(Lang.T("Verificar atualização"), async (s, e) => await CheckUpdateUi()),
                    B(Lang.T("Desligar mod (jogar sem co-op)"), (s, e) =>
                    {
                        if (new[] { hostRun, guestRun }.Any(r => r != null && r.Alive)) { Msg(Lang.T("Feche o jogo antes.")); return; }
                        Busy(() => { dep.DisableMod(); SaveCfg(); AppLog.Write(Lang.T("mod desligado: abra o jogo pelo Steam normal. HOST/GUEST religa.")); });
                        RefreshFiles();
                    })));

            lvFiles = new ListView { View = View.Details, FullRowSelect = true, Width = 860, Height = 140, BackColor = Theme.Ink, ForeColor = Fg, BorderStyle = BorderStyle.None, HeaderStyle = ColumnHeaderStyle.None, OwnerDraw = true };
            lvFiles.DrawColumnHeader += (s, e) =>
            {
                using (var b = new SolidBrush(Theme.Ink3)) e.Graphics.FillRectangle(b, e.Bounds);
                using (var f = Theme.Title(8.5f)) TextRenderer.DrawText(e.Graphics, e.Header.Text.ToUpperInvariant(), f, Rectangle.Inflate(e.Bounds, -6, 0), Theme.Gold, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                using (var p = new Pen(Color.FromArgb(90, Theme.Gold))) e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            };
            lvFiles.DrawSubItem += (s, e) =>
            {
                using (var b = new SolidBrush(e.ItemIndex % 2 == 0 ? Theme.Ink : Color.FromArgb(22, 18, 15))) e.Graphics.FillRectangle(b, e.Bounds);
                if (e.ColumnIndex == 0) Theme.Diamond(e.Graphics, e.Item.ForeColor, e.Bounds.Left + 9, e.Bounds.Top + e.Bounds.Height / 2f, 3.5f);
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, lvFiles.Font, new Rectangle(e.Bounds.Left + (e.ColumnIndex == 0 ? 20 : 6), e.Bounds.Top, e.Bounds.Width - 24, e.Bounds.Height), e.ColumnIndex == 0 ? Theme.Parchment : e.Item.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            lvFiles.Columns.Add("Arquivo no jogo", 240);
            lvFiles.Columns.Add("Situação", 860 - 240 - 4);
            var files = Group(Lang.T("Restaurador"),
                lvFiles,
                Row(B(Lang.T("Verificar"), (s, e) => RefreshFiles()), B(Lang.T("Restaurar"), (s, e) => { var d = dep.RestoreMissing(); SaveCfg(); RefreshFiles(); AppLog.Write(d.Count == 0 ? Lang.T("nada faltando") : Lang.T("restaurado: ") + string.Join(", ", d)); })),
                Row(B(Lang.T("Salvar ini do HOST do jogo como modelo"), (s, e) => SaveTplUi(Role.Host)), B(Lang.T("Salvar ini do GUEST do jogo como modelo"), (s, e) => SaveTplUi(Role.Guest))),
                L(Lang.T("O ini do jogo é editado no lugar (só nome, IP, porta e gravação); suas outras mudanças ficam. Backup em config\\backups."), Dim, 9f));

            var sync = Group(Lang.T("Implantador do guest"),
                L(Lang.T("Ao conectar como guest, o mod é comparado com o do host; cdmp.asi, winmm.dll e o ini de guest são baixados dele quando forem diferentes."), Dim, 9f),
                Row(B(Lang.T("Baixar arquivos do host agora"), async (s, e) => await SyncFromHostUi())));
            Stack(p, prof, files, sync);
            return p;
        }

        Panel SaveTab()
        {
            var p = Page("  Saves  ");
            lblSteam = L("");
            lblGuestDir = L("", Dim, 9f);
            var guest = Group(Lang.T("Save do GUEST (o personagem do convidado)"),
                lblSteam, lblGuestDir,
                Row(B(Lang.T("Importar save (.zip)..."), (s, e) => ImportSaveUi(true)), B(Lang.T("Importar pasta..."), (s, e) => ImportSaveUi(false)),
                    B(Lang.T("Baixar save do host"), async (s, e) => await DownloadSaveUi()), B(Lang.T("Abrir pasta"), (s, e) => { var d = Deployer.GuestSaveDir(Util.SteamId32()); Directory.CreateDirectory(d); Util.OpenFolder(d); })),
                L(Lang.T("A pasta slotN é localizada dentro do zip ou da pasta e copiada para save_cdmp_guest\\<steamid>\\, sem aninhar a pasta do id. Backup automático antes."), Dim, 9f));
            chkShare = C(Lang.T("Permitir que o guest baixe este save (no papel de host)"), cfg.ShareSave);
            chkShare.CheckedChanged += (s, e) => { cfg.ShareSave = chkShare.Checked; SaveCfg(); };
            txtShareDir = T(cfg.ShareSaveDir, 520);
            txtShareDir.Leave += (s, e) => { cfg.ShareSaveDir = txtShareDir.Text.Trim(); SaveCfg(); };
            var host = Group(Lang.T("HOST: save para o guest"),
                chkShare,
                Row(L(Lang.T("Pasta:")), txtShareDir, B("...", (s, e) => { var d = PickFolder(txtShareDir.Text); if (d != null) { txtShareDir.Text = d; cfg.ShareSaveDir = d; SaveCfg(); } })));
            var bk = Group(Lang.T("Backup"),
                Row(B(Lang.T("Backup de todos os saves agora"), (s, e) => Busy(() => { var z = Deployer.BackupAllSaves(); AppLog.Write(Lang.T("backup: ") + z); })),
                    B(Lang.T("Abrir pasta de backups"), (s, e) => { Directory.CreateDirectory(Deployer.BackupDir); Util.OpenFolder(Deployer.BackupDir); }),
                    B(Lang.T("Abrir pasta CD"), (s, e) => Util.OpenFolder(Deployer.CdDir))));
            Stack(p, guest, host, bk);
            return p;
        }

        Panel ToolsTab()
        {
            var p = Page("  Ferramentas  ");
            lblRec = L(""); lblLogs = L(""); lblDumps = L("");
            var logs = Group(Lang.T("Deu ruim?"),
                Row(B(Lang.T("Coletar logs e dumps de crash (zip na Área de Trabalho)"), (s, e) => Busy(() => { var z = dep.CollectLogs(); AppLog.Write(Lang.T("logs coletados: ") + z); Util.OpenFolder(Path.GetDirectoryName(z)); }))),
                Row(B(Lang.T("Abrir log do host"), (s, e) => OpenLog(Role.Host)), B(Lang.T("Abrir log do guest"), (s, e) => OpenLog(Role.Guest)), B(Lang.T("Abrir pasta do jogo"), (s, e) => Util.OpenFolder(cfg.GameDir))));
            var clean = Group(Lang.T("Limpeza (o jogo precisa estar fechado para apagar o que ele usa)"),
                Row(lblRec, B(Lang.T("Apagar gravações de pacotes"), (s, e) => CleanUi(Lang.T("gravações"), dep.Recordings()))),
                Row(lblLogs, B(Lang.T("Apagar logs antigos"), (s, e) => CleanUi(Lang.T("logs antigos"), dep.OldLogs(cfg.KeepLogs)))),
                Row(lblDumps, B(Lang.T("Apagar dumps com mais de 3 dias"), (s, e) => CleanUi(Lang.T("dumps antigos"), dep.OldDumps(3)))),
                Row(B(Lang.T("Recalcular"), (s, e) => RefreshCleanup())),
                L(Lang.T("As gravações (cdmp_packets) só existem com 'Gravar pacotes' ligado na Config; com record=1 elas passam de 100 GB."), Dim, 9f));
            var win = Group(Lang.T("Windows"),
                Row(B(Lang.T("Abrir o Firewall do Windows"), (s, e) => FirewallUi()), B(Lang.T("Abrir as exclusões do Defender"), (s, e) => DefenderUi())),
                L(Lang.T("Firewall: o host precisa receber UDP ") + cfg.PeerPort + Lang.T(" (jogo) e ") + cfg.DiscoveryPort + "/" + cfg.FilePort + Lang.T(" (launcher). Defender: evita que ele apague o winmm.dll/cdmp.asi."), Dim, 9f));
            Stack(p, logs, clean, win);
            return p;
        }

        Panel ConfigTab()
        {
            var p = Page("  Config  ");
            txtGameDir = T(cfg.GameDir, 520);
            txtManualIp = T(cfg.ManualHostIp, 170);
            txtFriendId = T(cfg.FriendSteamId, 170);
            chkUdp = C(Lang.T("Usar rede local/VPN (Radmin, ZeroTier, Tailscale) em vez da Steam (opcional)"), !cfg.UseSteam);
            NumericUpDown N(int v, int min, int max) => new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, v)), Width = 80, BackColor = Theme.Ink, ForeColor = Theme.GoldLight, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(3, 7, 12, 3) };
            numPeer = N(cfg.PeerPort, 1024, 65535); numDisc = N(cfg.DiscoveryPort, 1024, 65535); numFile = N(cfg.FilePort, 1024, 65535);
            numFirst = N(cfg.FirstEDelaySec, 0, 300); numInterval = N(cfg.EIntervalSec, 3, 300); numMax = N(cfg.MaxEPresses, 1, 50);
            numKeep = N(cfg.KeepLogs, 1, 100);
            chkRecord = C(Lang.T("Gravar pacotes (record=1, só para depurar: arquivos ENORMES)"), cfg.RecordPackets);
            chkSync = C(Lang.T("Guest: baixar o mod do host automaticamente quando for diferente"), cfg.AutoSyncFromHost);
            chkWatch = C(Lang.T("Vigiar a pasta do jogo e recolocar arquivos apagados"), cfg.AutoRestoreWatch);

            var paths = Group(Lang.T("Pastas"),
                Row(L(Lang.T("Jogo (bin64):")), txtGameDir, B("...", (s, e) => { var d = PickFolder(txtGameDir.Text); if (d != null) txtGameDir.Text = d; }), B(Lang.T("Detectar"), (s, e) => { var d = Util.FindGameDir(); if (d != "") txtGameDir.Text = d; else Msg(Lang.T("Jogo não encontrado nas bibliotecas do Steam.")); })));
            var net = Group(Lang.T("Rede"),
                L(Lang.T("Padrão: conexão pela Steam (como o mod original). Os dois precisam ser amigos na Steam."), Dim, 9f),
                Row(L(Lang.T("SteamID64 do amigo (opcional):")), txtFriendId, L(Lang.T("vazio = acha sozinho o amigo da Steam que está jogando"), Dim, 9f)),
                Row(chkUdp),
                Row(L(Lang.T("IP do host (só no modo rede local/VPN):")), txtManualIp, L(Lang.T("use para VPN (Radmin/ZeroTier/Tailscale) ou se a busca na rede não achar"), Dim, 9f)),
                Row(L(Lang.T("Porta do jogo:")), numPeer, L(Lang.T("busca:")), numDisc, L(Lang.T("arquivos:")), numFile));
            var auto = Group(Lang.T("Carregamento automático e outros"),
                Row(L(Lang.T("Espera antes do 1º E (s):")), numFirst, L(Lang.T("entre apertos (s):")), numInterval, L(Lang.T("máximo:")), numMax),
                Row(L(Lang.T("Logs guardados por papel:")), numKeep),
                chkRecord, chkSync, chkWatch);
            Stack(p, paths, net, auto, Row(B(Lang.T("Salvar config"), (s, e) => { SaveFields(); SetupWatcher(); AppLog.Write(Lang.T("config salva")); }, Crimson, 220)));
            return p;
        }

        // =====================================================================================  startup

        void FirstRun()
        {
            AppLog.Write(Lang.T("Pywel Together iniciado (") + Config.ExeDir + ")");
            if (string.IsNullOrEmpty(cfg.GameDir) || !File.Exists(Path.Combine(cfg.GameDir, "CrimsonDesert.exe")))
            {
                var d = Util.FindGameDir();
                if (d != "") { cfg.GameDir = d; AppLog.Write(Lang.T("jogo encontrado: ") + d); }
                else ShowBanner(Lang.T("Crimson Desert não encontrado. Informe a pasta bin64 em Config."));
            }
            txtGameDir.Text = cfg.GameDir;
            foreach (var r in Theme.Repaired) AppLog.Write(Lang.T("restaurado: ") + @"config\assets\" + r);
            if (!Tools.Available) ShowBanner(Lang.T("config\\tools\\pywel-tools.exe não encontrado") + Lang.T(": o carregamento automático e a cópia do mod ficam desligados. Baixe o app de novo."));
            try { dep.SyncPackage(); } catch (Exception e) { AppLog.Write(Lang.T("pacote do mod: ") + e.Message); }
            try { dep.CaptureFromGame(); } catch (Exception e) { AppLog.Write(Lang.T("captura inicial: ") + e.Message); }
            // only the mod in the game counts: leftovers of older launcher versions go away
            cfg.Profiles.RemoveAll(x => x.AsiSha != cfg.DeployedAsiSha && !File.Exists(Deployer.CachePath(x.AsiSha, ".asi")));
            // Steam update check
            var stamp = Util.ExeStamp(cfg.GameDir);
            if (!string.IsNullOrEmpty(cfg.LastExeStamp) && stamp != "" && stamp != cfg.LastExeStamp)
                ShowBanner(Lang.T("O CrimsonDesert.exe mudou desde a última vez (o Steam atualizou?). Versão agora: ") + Util.GameVersion(cfg.GameDir) + Lang.T(". O mod depende do build certo."));
            if (stamp != "") cfg.LastExeStamp = stamp;
            if (cfg.AutoRestoreWatch)
            {
                var d = dep.RestoreMissing();
                if (d.Count > 0) ShowBanner(Lang.T("Arquivos que faltavam no jogo foram restaurados: ") + string.Join(", ", d) + Lang.T(". Se isso repetir, use 'Abrir as exclusões do Defender' em Ferramentas."));
            }
            SaveCfg();
            FillProfiles();
            SetupWatcher();
            if (StartCommand != null) BeginInvoke((Action)(() => OnCommand(StartCommand)));
            RefreshStatus();
            CheckVersionBanner();
        }

        void CheckVersionBanner()
        {
            var p = cfg.Current;
            var b = Util.BuildOf(Util.GameVersion(cfg.GameDir));
            if (p != null && !string.IsNullOrEmpty(p.ExpectedBuild) && b != "" && p.ExpectedBuild != b)
                ShowBanner(Lang.T("O mod '") + p.Name + Lang.T("' é do build ") + p.ExpectedBuild + Lang.T(" e o jogo está no ") + b + Lang.T(". Troque a versão do mod."));
        }

        // =====================================================================================  HOST

        async void OnHost(object s, EventArgs e)
        {
            if (hostRun != null && hostRun.Alive) { Msg(Lang.T("O host já está aberto.")); return; }
            SaveFields();
            if (!PreLaunch(Role.Host)) return;
            try
            {
                if (string.IsNullOrWhiteSpace(cfg.SessionCode)) { txtCode.Text = Util.NewCode(); SaveFields(); AppLog.Write(Lang.T("código da sessão gerado: ") + cfg.SessionCode); }
                await Task.Run(() => { dep.SyncPackage(); dep.AdoptGameAsi(); }); await Task.Run(() => dep.Deploy(Role.Host, cfg.Current, new Deployer.Overrides { PlayerName = cfg.HostName, PeerPort = cfg.PeerPort, Record = cfg.RecordPackets, Steam = cfg.UseSteam, PeerSteamId = cfg.FriendSteamId }, d => false));
                SaveCfg();
                if (!cfg.UseSteam) StartBeacon();
                hostCts?.Cancel();
                hostCts = new CancellationTokenSource();
                hostRun = new GameRun(Role.Host, cfg.GameDir, dep.LogAlias(cfg.Current, Role.Host), dep.EnvRole(Role.Host));
                hostRun.Exited += r => BeginInvoke((Action)(() => OnExited(r)));
                hostRun.Start();
                if (cfg.UseSteam) AppLog.Write(Lang.T("host: conexão pela Steam. O amigo entra pelo app dele (vocês precisam ser amigos na Steam)")); else AppLog.Write(Lang.T("host: passe o código ") + cfg.SessionCode + Lang.T(" para o guest"));
                if (cfg.AutoE)
                {
                    var run = hostRun;
                    await run.AutoE(cfg.FirstEDelaySec, cfg.EIntervalSec, cfg.MaxEPresses, () => true, () => run.State == "mundo", hostCts.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Fail("host", ex); }
        }

        void StartBeacon()
        {
            if (beacon != null && beaconCode == cfg.SessionCode) return;
            beacon?.Dispose();
            beaconCode = cfg.SessionCode;
            beacon = new HostBeacon(cfg.SessionCode, cfg.DiscoveryPort, cfg.FilePort, LocalHostInfo, FileForGuest);
            try { beacon.Start(); }
            catch (Exception ex) { AppLog.Write(Lang.T("busca na rede não abriu (porta ") + cfg.DiscoveryPort + Lang.T(" ocupada?): ") + ex.Message); beacon = null; }
        }

        HostInfo LocalHostInfo()
        {
            var p = dep.ProfileBySha(cfg.DeployedAsiSha);
            return new HostInfo
            {
                Name = cfg.HostName,
                State = hostRun != null && hostRun.Alive ? hostRun.State : "fechado",
                GuestState = hostRun?.GuestState ?? "",
                Game = Util.GameVersion(cfg.GameDir),
                AsiSha = cfg.DeployedAsiSha,
                AsiName = p?.Name ?? "",
                WinmmSha = cfg.WinmmSha,
                Ping = hostRun?.Ping ?? "",
                Port = cfg.PeerPort,
                ShareSave = cfg.ShareSave,
            };
        }

        string zippedSave;
        string FileForGuest(string what)
        {
            switch (what)
            {
                case "asi": return File.Exists(Deployer.CachePath(cfg.DeployedAsiSha, ".asi")) ? Deployer.CachePath(cfg.DeployedAsiSha, ".asi") : null;
                case "winmm": return File.Exists(Deployer.CachePath(cfg.WinmmSha, ".dll")) ? Deployer.CachePath(cfg.WinmmSha, ".dll") : null;
                case "guestini":
                    var t = dep.TemplatePath(dep.ProfileBySha(cfg.DeployedAsiSha) ?? cfg.Current, Role.Guest);
                    return File.Exists(t) ? t : null;
                case "save":
                    if (!cfg.ShareSave) return null;
                    var dir = string.IsNullOrEmpty(cfg.ShareSaveDir) ? Deployer.HostSaveDir(Util.SteamId32()) : cfg.ShareSaveDir;
                    try { zippedSave = Deployer.ZipSaveForShare(dir); return zippedSave; } catch (Exception e) { AppLog.Write(Lang.T("save para o guest: ") + e.Message); return null; }
            }
            return null;
        }

        // =====================================================================================  GUEST

        async void OnGuest(object s, EventArgs e)
        {
            if (guestRun != null && guestRun.Alive)
            {
                if (!GuestMustRejoin()) { Msg(Lang.T("O guest já está aberto.")); return; }
                // no automatic reconnect in the mod: the guest's game closes and joins again from the start
                AppLog.Write(Lang.T("guest: fechando o jogo para entrar de novo"));
                var old = guestRun;
                old.Stop();
                for (int i = 0; i < 30 && old.Alive; i++) await Task.Delay(500);
            }
            if (guestCts != null) { AppLog.Write(Lang.T("o guest já está esperando o host (use Fechar/cancelar para parar)")); return; }
            if (hostRun != null && hostRun.Alive) { Msg(Lang.T("Este PC já é o host. Host e guest precisam estar em PCs diferentes.")); return; }
            SaveFields();
            guestCts = new CancellationTokenSource();
            var ct = guestCts.Token;
            btnGuest.Text = Lang.T("Procurando host");
            try
            {
                string peer;
                remoteHost = null; hostLostForGuest = false; remoteMisses = 0;
                if (cfg.UseSteam)
                {
                    peer = "";
                    AppLog.Write(Lang.T("guest: conexão pela Steam; o mod acha o amigo que está jogando (vocês precisam ser amigos na Steam)"));
                }
                else if (string.IsNullOrWhiteSpace(cfg.SessionCode))
                {
                    if (string.IsNullOrWhiteSpace(cfg.ManualHostIp)) { Msg(Lang.T("Digite o código da sessão (o mesmo do host) ou um IP do host na Config.")); return; }
                    peer = cfg.ManualHostIp.Trim();
                    if (MessageBox.Show(this, Lang.T("Sem código não há como saber se o host já está no mundo.\nO host ") + peer + Lang.T(" já entrou no mundo?"), "Guest", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                }
                else
                {
                    remoteHost = await FindHost(ct);
                    if (remoteHost == null) return;
                    if (Util.IsLocal(remoteHost.Address)) { Msg(Lang.T("O host encontrado é este PC. Host e guest precisam estar em PCs diferentes.")); return; }
                    peer = remoteHost.Address.ToString();
                    var mine = Util.GameVersion(cfg.GameDir);
                    if (remoteHost.Game != "" && mine != "" && remoteHost.Game != mine)
                        AppLog.Write(Lang.T("ATENCAO: host com o jogo ") + remoteHost.Game + Lang.T(" e este PC com ") + mine + Lang.T(": versões diferentes não funcionam juntas"));
                    if (cfg.AutoSyncFromHost) await SyncFromHost(remoteHost, ct, false);
                    await WaitHostWorld(ct);
                }
                if (!PreLaunch(Role.Guest)) return;
                await Task.Run(() => dep.SyncPackage()); await Task.Run(() => dep.Deploy(Role.Guest, cfg.Current, new Deployer.Overrides { PlayerName = cfg.GuestName, PeerHost = peer, PeerPort = remoteHost?.Port ?? cfg.PeerPort, Record = cfg.RecordPackets, Steam = cfg.UseSteam, PeerSteamId = cfg.FriendSteamId }, cfg.AutoSyncFromHost && remoteHost != null ? (Func<string, bool>)(d => true) : d => false), ct);
                SaveCfg();
                guestRun = new GameRun(Role.Guest, cfg.GameDir, dep.LogAlias(cfg.Current, Role.Guest), dep.EnvRole(Role.Guest));
                guestRun.Exited += r => BeginInvoke((Action)(() => OnExited(r)));
                guestRun.Start();
                var run = guestRun;
                _ = PollRemoteHost(run, ct);
                if (cfg.AutoE)
                {
                    await run.AutoE(Math.Min(cfg.FirstEDelaySec, 10), cfg.EIntervalSec, cfg.MaxEPresses,
                        () => run.HostReadyRemote,
                        () => run.State == "mundo" || GuestStateAtHost() != "", ct);
                }
            }
            catch (OperationCanceledException) { AppLog.Write(Lang.T("guest: cancelado")); }
            catch (Exception ex) { Fail("guest", ex); }
            finally { guestCts = null; btnGuest.Text = Lang.T("Entrar no mundo"); }
        }

        bool GuestMustRejoin() =>
            guestRun != null && guestRun.Alive && guestCts == null &&
            (guestRun.PeerLost || hostLostForGuest);

        string GuestStateAtHost() => hostRun != null && hostRun.Alive ? hostRun.GuestState : remoteHost?.GuestState ?? "";

        async Task WaitUntil(Func<bool> cond, string msg, CancellationToken ct)
        {
            if (cond()) return;
            AppLog.Write(msg);
            while (!cond()) await Task.Delay(1000, ct);
        }

        async Task<HostInfo> FindHost(CancellationToken ct)
        {
            AppLog.Write(Lang.T("guest: procurando o host com o código ") + cfg.SessionCode + "...");
            var t0 = DateTime.Now;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var list = await HostFinder.Find(cfg.SessionCode, cfg.DiscoveryPort, cfg.ManualHostIp);
                if (list.Count == 1) { AppLog.Write(Lang.T("guest: host encontrado: ") + list[0].Name + " (" + list[0].Address + "), " + GameRun.Label(list[0].State)); return list[0]; }
                if (list.Count > 1)
                {
                    var pick = PickHost(list);
                    if (pick == null) return null;
                    AppLog.Write(Lang.T("guest: host escolhido: ") + pick.Name + " (" + pick.Address + ")");
                    return pick;
                }
                if ((DateTime.Now - t0).TotalSeconds > 20 && (DateTime.Now - t0).TotalSeconds < 23)
                    AppLog.Write(Lang.T("guest: ainda nada. O host apertou HOST com o mesmo código? Firewall liberado? (Ferramentas)"));
                await Task.Delay(2000, ct);
            }
        }

        HostInfo PickHost(List<HostInfo> list)
        {
            using (var f = new Form { Text = Lang.T("Mais de um host com esse código"), Size = new Size(460, 300), StartPosition = FormStartPosition.CenterParent, BackColor = Bg, ForeColor = Fg, Font = Font })
            {
                var lb = new ListBox { Dock = DockStyle.Fill, BackColor = Panel2, ForeColor = Fg };
                foreach (var h in list) lb.Items.Add(h.Name + "  (" + h.Address + ")  " + GameRun.Label(h.State));
                lb.SelectedIndex = 0;
                var ok = new Button { Text = Lang.T("Conectar"), Dock = DockStyle.Bottom, Height = 40, DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, BackColor = Theme.Bronze };
                f.Controls.Add(lb); f.Controls.Add(ok); f.AcceptButton = ok;
                return f.ShowDialog(this) == DialogResult.OK ? list[lb.SelectedIndex] : null;
            }
        }

        async Task WaitHostWorld(CancellationToken ct)
        {
            bool said = false;
            while (!remoteHost.InWorld)
            {
                if (!said) { AppLog.Write(Lang.T("guest: aguardando o host entrar no mundo (") + GameRun.Label(remoteHost.State) + ")..."); said = true; }
                await Task.Delay(3000, ct);
                var again = await HostFinder.Find(cfg.SessionCode, cfg.DiscoveryPort, remoteHost.Address.ToString(), 1200);
                var same = again.FirstOrDefault(h => h.Address.Equals(remoteHost.Address)) ?? again.FirstOrDefault(h => h.Name == remoteHost.Name);
                if (same != null) remoteHost = same;
            }
            AppLog.Write(Lang.T("guest: host no mundo, abrindo o jogo"));
        }

        async Task PollRemoteHost(GameRun run, CancellationToken ct)
        {
            if (remoteHost == null) return;
            var addr = remoteHost.Address;
            while (run.Alive && !ct.IsCancellationRequested)
            {
                try
                {
                    var l = await HostFinder.Find(cfg.SessionCode, cfg.DiscoveryPort, addr.ToString(), 1200);
                    var h = l.FirstOrDefault(x => x.Address.Equals(addr)) ?? l.FirstOrDefault();
                    if (h != null)
                    {
                        remoteMisses = 0;
                        if (remoteHost != null && remoteHost.GuestState != h.GuestState && h.GuestState != "") AppLog.Write(Lang.T("host diz: guest ") + Lang.T(h.GuestState));
                        if (remoteHost != null && remoteHost.InWorld && !h.InWorld)
                        {
                            AppLog.Write(Lang.T("host diz: ") + GameRun.Label(h.State));
                            if (run.State == "mundo") { hostLostForGuest = true; AppLog.Write(Lang.T("guest: o host fechou ou reiniciou; use Entrar de novo")); }
                        }
                        remoteHost = h;
                    }
                    else if (++remoteMisses == 3 && run.State == "mundo")
                    {
                        hostLostForGuest = true;
                        AppLog.Write(Lang.T("guest: o launcher do host não responde; se o host caiu, use Entrar de novo"));
                    }
                }
                catch { }
                try { await Task.Delay(5000, ct); } catch { return; }
            }
        }

        // Guest: the host's mod files when they differ from mine (or when I have no guest template).
        async Task SyncFromHost(HostInfo h, CancellationToken ct, bool force)
        {
            var needAsi = !string.IsNullOrEmpty(h.AsiSha) && (force || cfg.Current == null || cfg.Current.AsiSha != h.AsiSha);
            var needWinmm = !string.IsNullOrEmpty(h.WinmmSha) && (force || h.WinmmSha != cfg.WinmmSha || !File.Exists(Path.Combine(cfg.GameDir, "winmm.dll")));
            var tplPath = Path.Combine(Deployer.TemplateDir, "guest-do-host-" + (h.AsiSha.Length >= 8 ? h.AsiSha.Substring(0, 8) : "x") + ".ini");
            var needTpl = force || needAsi || !File.Exists(dep.TemplatePath(cfg.Current, Role.Guest));
            if (!needAsi && !needWinmm && !needTpl) { AppLog.Write(Lang.T("guest: mod igual ao do host")); return; }
            Profile p = dep.ProfileBySha(h.AsiSha);
            if (needAsi && p == null)
            {
                AppLog.Write(Lang.T("guest: baixando o mod do host (") + h.AsiName + ")...");
                var tmp = Path.Combine(Deployer.CacheDir, "baixado.asi");
                await HostFinder.Download(h.Address, cfg.FilePort, cfg.SessionCode, "asi", tmp);
                if (Util.Sha256(tmp) != h.AsiSha) throw new Exception(Lang.T("o asi baixado veio corrompido (hash diferente)"));
                p = dep.AddProfile(tmp, Lang.T("Do host: ") + (h.AsiName == "" ? h.AsiSha.Substring(0, 8) : h.AsiName), true);
                p.AsiPath = Deployer.CachePath(p.AsiSha, ".asi");
                p.ExpectedBuild = Util.BuildOf(h.Game);
                File.Delete(tmp);
            }
            if (needWinmm)
            {
                var tmp = Path.Combine(Deployer.CacheDir, "baixado.dll");
                try
                {
                    await HostFinder.Download(h.Address, cfg.FilePort, cfg.SessionCode, "winmm", tmp);
                    cfg.WinmmSha = Deployer.CacheStore(tmp, ".dll");
                    File.Delete(tmp);
                    var w = Path.Combine(cfg.GameDir, "winmm.dll");
                    if (!File.Exists(w) || Util.Sha256(w) != cfg.WinmmSha) { if (File.Exists(w)) Deployer.Backup(w, "dll"); Util.CopyFile(Deployer.CachePath(cfg.WinmmSha, ".dll"), w); }
                    AppLog.Write(Lang.T("guest: winmm.dll igual ao do host"));
                }
                catch (Exception e) { AppLog.Write(Lang.T("guest: winmm do host: ") + e.Message); }
            }
            if (needTpl)
            {
                try
                {
                    await HostFinder.Download(h.Address, cfg.FilePort, cfg.SessionCode, "guestini", tplPath);
                    if (p == null) p = cfg.Current;
                    if (p != null) p.GuestIni = tplPath;
                    // the game's guest ini follows the new template (backup first)
                    var ini = Path.Combine(cfg.GameDir, dep.IniName(p, Role.Guest));
                    if (File.Exists(ini) && needAsi) { Deployer.Backup(ini, "ini"); File.Copy(tplPath, ini, true); }
                    AppLog.Write(Lang.T("guest: ini de guest do host recebido"));
                }
                catch (Exception e) { AppLog.Write(Lang.T("guest: ini do host: ") + e.Message); }
            }
            if (p != null) cfg.SelectedProfile = p.Name;
            SaveCfg();
            FillProfiles();
        }

        // =====================================================================================  common launch steps

        bool PreLaunch(Role role)
        {
            if (!File.Exists(Path.Combine(cfg.GameDir, "CrimsonDesert.exe"))) { Msg(Lang.T("CrimsonDesert.exe não encontrado. Ajuste a pasta em Config.")); return false; }
            if (cfg.Current == null) { Msg(Lang.T("cdmp.asi não encontrado no jogo e sem cópia guardada. Coloque o mod na pasta bin64 do jogo.")); return false; }
            var p = cfg.Current;
            var b = Util.BuildOf(Util.GameVersion(cfg.GameDir));
            if (!string.IsNullOrEmpty(p.ExpectedBuild) && b != "" && b != p.ExpectedBuild)
            {
                if (MessageBox.Show(this, Lang.T("O mod '") + p.Name + Lang.T("' foi feito para o build ") + p.ExpectedBuild + Lang.T(", mas o jogo está no ") + b + Lang.T(" (o Steam atualizou?).\n\nAbrir mesmo assim?"), Lang.T("Versão do jogo"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return false;
            }
            // other copies of the game that are not ours
            var ours = new[] { hostRun, guestRun }.Where(r => r != null && r.Alive).Select(r => r.Proc.Id).ToHashSet();
            var strays = Util.GameProcesses().Where(x => !ours.Contains(x.Id)).ToList();
            if (strays.Count > 0)
            {
                if (MessageBox.Show(this, Lang.T("Já tem ") + strays.Count + Lang.T(" Crimson Desert aberto que não foi este launcher que abriu.\nFechar para abrir o ") + Deployer.RoleName(role) + "?", Lang.T("Jogo aberto"), MessageBoxButtons.YesNo) != DialogResult.Yes)
                    return false;
                foreach (var x in strays) try { x.Kill(); x.WaitForExit(10000); } catch { }
                Thread.Sleep(2000);
            }
            if (!Util.SteamRunning())
            {
                var steam = Path.Combine(Util.SteamPath(), "steam.exe");
                if (File.Exists(steam))
                {
                    AppLog.Write(Lang.T("Steam fechado: abrindo o Steam"));
                    try { System.Diagnostics.Process.Start(steam, "-silent"); } catch { }
                    var t0 = DateTime.Now;
                    while ((DateTime.Now - t0).TotalSeconds < 60 && Util.ActiveSteamId32() == 0) { Application.DoEvents(); Thread.Sleep(500); }
                    if (Util.ActiveSteamId32() == 0) { Msg(Lang.T("O Steam não logou. Entre na sua conta no Steam e tente de novo.")); return false; }
                }
                else { Msg(Lang.T("Abra o Steam e entre na sua conta antes.")); return false; }
            }
            return true;
        }

        void OnExited(GameRun r)
        {
            var who = Deployer.RoleName(r.Role);
            var crashed = r.HasCrashDumpSinceStart();
            AppLog.Write(who + Lang.T(": o jogo fechou") + (crashed ? Lang.T(" e deixou um dump de crash") : ""));
            if (crashed && MessageBox.Show(this, "O " + who + Lang.T(" fechou com crash. Coletar logs e dumps num zip na Área de Trabalho?"), Lang.T("Crash"), MessageBoxButtons.YesNo) == DialogResult.Yes)
                Busy(() => { var z = dep.CollectLogs(); AppLog.Write(Lang.T("logs coletados: ") + z); Util.OpenFolder(Path.GetDirectoryName(z)); });
        }

        void OnStop(object s, EventArgs e)
        {
            if (guestCts != null) { guestCts.Cancel(); }
            var alive = new[] { hostRun, guestRun }.Where(r => r != null && r.Alive).ToList();
            if (alive.Count == 0) return;
            var what = string.Join(" e ", alive.Select(r => Deployer.RoleName(r.Role)));
            if (MessageBox.Show(this, Lang.T("Fechar o jogo (") + what + Lang.T(")? O que não foi salvo no jogo se perde."), Lang.T("Fechar"), MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            hostCts?.Cancel();
            foreach (var r in alive) r.Stop();
        }

        void OnClosingForm(object s, FormClosingEventArgs e)
        {
            if (hostRun != null && hostRun.Alive)
            {
                if (MessageBox.Show(this, Lang.T("O host está aberto. Se fechar o launcher, guests novos não acham mais a sessão (o jogo continua). Fechar?"), Lang.T("Sair"), MessageBoxButtons.YesNo) != DialogResult.Yes) { e.Cancel = true; return; }
            }
            SaveFields();
            beacon?.Dispose();
            watcher?.Dispose();
        }

        // =====================================================================================  watcher (restorer)

        void SetupWatcher()
        {
            watcher?.Dispose(); watcher = null;
            if (!cfg.AutoRestoreWatch || !Directory.Exists(cfg.GameDir)) return;
            watcher = new FileSystemWatcher(cfg.GameDir) { NotifyFilter = NotifyFilters.FileName, IncludeSubdirectories = false };
            FileSystemEventHandler h = (s, e) =>
            {
                var n = e.Name.ToLowerInvariant();
                if (n == "cdmp.asi" || n == "winmm.dll" || (n.StartsWith("cdmp") && n.EndsWith(".ini")))
                    BeginInvoke((Action)(async () => { await Task.Delay(3000); AutoRestore(); }));
            };
            watcher.Deleted += h;
            watcher.Renamed += (s, e) => h(s, new FileSystemEventArgs(WatcherChangeTypes.Deleted, cfg.GameDir, e.OldName));
            watcher.EnableRaisingEvents = true;
        }

        void AutoRestore()
        {
            restores.RemoveAll(t => (DateTime.Now - t).TotalMinutes > 10);
            if (restores.Count >= 4)
            {
                ShowBanner(Lang.T("Algo continua apagando os arquivos do mod (antivírus?). Restauração automática pausada. Use 'Abrir as exclusões do Defender' em Ferramentas e depois 'Restaurar' em Mod."));
                return;
            }
            var d = dep.RestoreMissing();
            if (d.Count == 0) return;
            restores.Add(DateTime.Now);
            ShowBanner(string.Join(", ", d) + Lang.T(" foi apagado da pasta do jogo e já foi restaurado."));
            RefreshFiles();
        }

        // =====================================================================================  mod tab

        void FillProfiles() => ShowProfile();

        void ShowProfile()
        {
            var p = cfg.Current;
            if (p == null) { lblProfInfo.Text = Lang.T("Nenhum cdmp.asi no jogo."); return; }
            var a = Path.Combine(cfg.GameDir, "cdmp.asi");
            var when = File.Exists(a) ? File.GetLastWriteTime(a).ToString("dd/MM/yyyy HH:mm") : "?";
            lblProfInfo.Text = Lang.T("Versão: ") + p.Name + Lang.T("   ·   arquivo de ") + when + Lang.T("   ·   feito para o build ") + (p.ExpectedBuild == "" ? "?" : p.ExpectedBuild)
                + Lang.T("   ·   jogo ") + Util.GameVersion(cfg.GameDir) + "\nsha " + (p.AsiSha.Length > 12 ? p.AsiSha.Substring(0, 12) : p.AsiSha);
            CheckVersionBanner();
        }

        async Task CheckUpdateUi()
        {
            if (!Updater.Configured) { Msg(Lang.T("Verificação de atualização ainda não disponível.")); return; }
            if (new[] { hostRun, guestRun }.Any(r => r != null && r.Alive)) { Msg(Lang.T("Feche o jogo antes de atualizar o mod.")); return; }
            try
            {
                AppLog.Write(Lang.T("verificando atualização..."));
                var f = await Updater.FetchLatest(Updater.Repo);
                if (f == null) { Msg(Lang.T("A versão mais recente não tem cdmp.asi para baixar.")); return; }
                if (Util.Sha256(f.AsiPath) == cfg.DeployedAsiSha) { Msg(Lang.T("O mod já está na versão mais recente (") + f.Tag + ")."); return; }
                if (MessageBox.Show(this, Lang.T("Nova versão do mod disponível: ") + f.Tag + Lang.T(".\nAtualizar agora? (a versão atual fica guardada)"), Lang.T("Atualização"), MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                Busy(() =>
                {
                    dep.InstallAsi(f.AsiPath, f.Tag, Util.BuildOf(Util.GameVersion(cfg.GameDir)));
                    var w = Path.Combine(f.Dir, "winmm.dll");
                    if (File.Exists(w)) { cfg.WinmmSha = Deployer.CacheStore(w, ".dll"); Util.CopyFile(w, Path.Combine(cfg.GameDir, "winmm.dll")); }
                    foreach (Role r in new[] { Role.Host, Role.Guest })
                    {
                        var ini = Path.Combine(f.Dir, "cdmp." + Deployer.RoleName(r) + ".ini");
                        if (!File.Exists(ini)) continue;
                        var tpl = Path.Combine(Deployer.TemplateDir, "cdmp." + Deployer.RoleName(r) + ".ini");
                        if (File.Exists(tpl)) Deployer.Backup(tpl, "templates");
                        Directory.CreateDirectory(Deployer.TemplateDir);
                        File.Copy(ini, tpl, true);
                        var game = Path.Combine(cfg.GameDir, dep.IniName(cfg.Current, r));
                        if (File.Exists(game)) Deployer.Backup(game, "ini");
                        File.Copy(ini, game, true);
                    }
                    SaveCfg();
                    AppLog.Write(Lang.T("mod atualizado para ") + f.Tag);
                });
                ShowProfile(); RefreshFiles();
            }
            catch (Exception e) { Fail(Lang.T("atualização"), e); }
        }

        void RefreshFiles()
        {
            if (lvFiles == null) return;
            lvFiles.Items.Clear();
            if (!Directory.Exists(cfg.GameDir)) return;
            foreach (var f in dep.Check())
            {
                var it = new ListViewItem(new[] { f.Name, f.State }) { ForeColor = f.Ok ? (f.State == "ok" || !(f.State.Contains("faltando") || f.State.Contains("missing") || f.State.Contains("desatualizada") || f.State.Contains("outdated")) ? Ok : Warn) : Bad };
                lvFiles.Items.Add(it);
            }
        }

        void SaveTplUi(Role r)
        {
            Busy(() => { dep.SaveTemplateFromGame(r); AppLog.Write(Lang.T("modelo de ") + Deployer.RoleName(r) + Lang.T(" atualizado a partir do ini do jogo")); });
            RefreshFiles();
        }

        async Task SyncFromHostUi()
        {
            if (string.IsNullOrWhiteSpace(txtCode.Text)) { Msg(Lang.T("Digite o código da sessão na tela principal.")); return; }
            SaveFields();
            try
            {
                var l = await HostFinder.Find(cfg.SessionCode, cfg.DiscoveryPort, cfg.ManualHostIp, 2000);
                if (l.Count == 0) { Msg(Lang.T("Nenhum host com esse código na rede. O host precisa ter apertado HOST.")); return; }
                await SyncFromHost(l.Count == 1 ? l[0] : PickHost(l), CancellationToken.None, true);
                RefreshFiles();
            }
            catch (Exception e) { Fail("sincronizar", e); }
        }

        // =====================================================================================  saves tab

        void RefreshSaves()
        {
            var id = Util.SteamId32();
            lblSteam.Text = Lang.T("Conta Steam deste PC: ") + (id == 0 ? Lang.T("não encontrada (abra o Steam)") : id.ToString()) + (Util.ActiveSteamId32() == 0 && id != 0 ? Lang.T(" (última que logou)") : "");
            var d = Deployer.GuestSaveDir(id);
            var slots = Directory.Exists(d) ? Directory.GetDirectories(d, "slot*").Length : 0;
            lblGuestDir.Text = Lang.T("Pasta: ") + d + "   (" + slots + Lang.T(" slot(s))");
            if (string.IsNullOrEmpty(txtShareDir.Text) && id != 0) txtShareDir.Text = Deployer.HostSaveDir(id);
        }

        void ImportSaveUi(bool zip)
        {
            string src = null;
            if (zip)
            {
                using (var d = new OpenFileDialog { Filter = Lang.T("Save (*.zip)|*.zip") })
                    if (d.ShowDialog(this) == DialogResult.OK) src = d.FileName;
            }
            else src = PickFolder("");
            if (src == null) return;
            if (guestRun != null && guestRun.Alive) { Msg(Lang.T("Feche o guest antes de trocar o save.")); return; }
            Busy(() => { var dst = Deployer.ImportGuestSave(src, Util.SteamId32()); AppLog.Write(Lang.T("save do guest implantado em ") + dst); });
            RefreshSaves();
        }

        async Task DownloadSaveUi()
        {
            if (string.IsNullOrWhiteSpace(txtCode.Text)) { Msg(Lang.T("Digite o código da sessão na tela principal.")); return; }
            if (guestRun != null && guestRun.Alive) { Msg(Lang.T("Feche o guest antes de trocar o save.")); return; }
            SaveFields();
            try
            {
                var l = await HostFinder.Find(cfg.SessionCode, cfg.DiscoveryPort, cfg.ManualHostIp, 2000);
                if (l.Count == 0) { Msg(Lang.T("Nenhum host com esse código na rede.")); return; }
                var h = l.Count == 1 ? l[0] : PickHost(l);
                if (h == null) return;
                if (!h.ShareSave) { Msg(Lang.T("O host não liberou o save dele (em Saves, no launcher do host).")); return; }
                var tmp = Path.Combine(Path.GetTempPath(), "cdmp_save_do_host.zip");
                AppLog.Write(Lang.T("baixando save do host..."));
                await HostFinder.Download(h.Address, cfg.FilePort, cfg.SessionCode, "save", tmp);
                var dst = await Task.Run(() => Deployer.ImportGuestSave(tmp, Util.SteamId32()));
                File.Delete(tmp);
                AppLog.Write(Lang.T("save do host implantado em ") + dst);
                RefreshSaves();
            }
            catch (Exception e) { Fail(Lang.T("baixar save"), e); }
        }

        // =====================================================================================  tools tab

        void RefreshCleanup()
        {
            if (lblRec == null) return;
            var r = dep.Recordings(); var l = dep.OldLogs(cfg.KeepLogs); var d = dep.OldDumps(3);
            lblRec.Text = Lang.T("Gravações de pacotes: ") + r.Count + Lang.T(" arquivo(s), ") + Util.Size(Deployer.Total(r));
            lblLogs.Text = Lang.T("Logs antigos (ficam ") + cfg.KeepLogs + Lang.T(" por papel): ") + l.Count + " (" + Util.Size(Deployer.Total(l)) + ")";
            lblDumps.Text = Lang.T("Dumps antigos: ") + d.Count + " (" + Util.Size(Deployer.Total(d)) + ")";
            lblRec.ForeColor = Deployer.Total(r) > (20L << 30) ? Bad : Fg;
        }

        void CleanUi(string what, List<FileInfo> files)
        {
            if (files.Count == 0) { Msg(Lang.T("Nada para apagar.")); return; }
            if (MessageBox.Show(this, Lang.T("Apagar de vez ") + files.Count + " " + what + " (" + Util.Size(Deployer.Total(files)) + Lang.T(")?\nNao vai para a lixeira."), Lang.T("Limpeza"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            Busy(() => { var n = Deployer.DeleteAll(files, out var freed); AppLog.Write(n + " " + what + Lang.T(" apagados, ") + Util.Size(freed) + Lang.T(" liberados") + (n < files.Count ? " (" + (files.Count - n) + Lang.T(" em uso ficaram)") : "")); });
            RefreshCleanup();
        }

        // Opens "Allow an app through Windows Firewall"; the player adds the two programs there.
        void FirewallUi()
        {
            var game = Path.Combine(cfg.GameDir, "CrimsonDesert.exe");
            try { System.Diagnostics.Process.Start("control.exe", "/name Microsoft.WindowsFirewall /page pageConfigureApps"); } catch { }
            try { Clipboard.SetText(game); } catch { }
            Msg(Lang.T("Na janela do Firewall: Alterar configurações > Permitir outro aplicativo > Procurar, e adicione os dois, marcando Privada:") + "\n\n" + game + "\n" + Application.ExecutablePath
                + "\n\n" + Lang.T("(o caminho do jogo já foi copiado; cole com Ctrl+V)"));
        }

        // Opens Windows Security's threat settings; the player adds the two folders as exclusions there.
        void DefenderUi()
        {
            try { System.Diagnostics.Process.Start("windowsdefender://threatsettings"); } catch { }
            try { Clipboard.SetText(cfg.GameDir); } catch { }
            Msg(Lang.T("Em Segurança do Windows: Gerenciar configurações > Exclusões > Adicionar ou remover exclusões > Adicionar > Pasta, e adicione as duas:") + "\n\n" + cfg.GameDir + "\n" + Config.ExeDir
                + "\n\n" + Lang.T("(a pasta do jogo já foi copiada; cole com Ctrl+V)"));
        }

        void OpenLog(Role r)
        {
            var run = r == Role.Host ? hostRun : guestRun;
            var path = run?.LogPath;
            if (path == null && r == Role.Guest)
                path = File.Exists(Path.Combine(cfg.GameDir, "cdmp.log")) ? Path.Combine(cfg.GameDir, "cdmp.log") : null;
            path = path ?? dep.LogAlias(cfg.Current, r);
            if (File.Exists(path)) try { System.Diagnostics.Process.Start("notepad.exe", "\"" + path + "\""); } catch { }
            else Msg(Lang.T("Ainda não tem log de ") + Deployer.RoleName(r) + ".");
        }

        // =====================================================================================  status

        void RefreshStatus()
        {
            bool hostAlive = hostRun != null && hostRun.Alive, guestAlive = guestRun != null && guestRun.Alive, searching = guestCts != null;
            int Lvl(string st) => st == "abrindo" ? 1 : st == "titulo" ? 2 : st == "mundo" ? 3 : 0;

            // HOST card: this PC's host, or the host a guest here found
            if (hostAlive)
            {
                var r = Lvl(hostRun.State) + (hostRun.GuestState == "no mundo" ? 1 : 0);
                var d = hostRun.PeerLost ? Lang.T("o guest caiu; ele precisa entrar de novo")
                    : hostRun.GuestState == "entrando" ? Lang.T("o guest está entrando...") : hostRun.Ping != "" ? Lang.T("guest conectado, ping ") + hostRun.Ping : Lang.T("sessão ") + beaconCode + Lang.T(" aberta neste PC");
                hostCard.Set(r, true, true, d);
            }
            else if (remoteHost != null)
                hostCard.Set(Lvl(remoteHost.State) + (remoteHost.GuestState == "no mundo" ? 1 : 0), true, false, remoteHost.Name + Lang.T(" em ") + remoteHost.Address);
            else hostCard.Set(0, false, false, "");

            // GUEST card
            bool otherPcGuest = hostAlive && !guestAlive && !searching && hostRun.GuestState != "";
            if (searching || guestAlive)
            {
                bool found = hostAlive || remoteHost != null;
                bool world = (hostAlive && hostRun.State == "mundo") || (remoteHost != null && remoteHost.InWorld);
                int r = !found ? 0 : !world ? 1 : !guestAlive ? 2 : guestRun.State == "mundo" ? 4 : 3;
                bool rejoin = GuestMustRejoin();
                if (rejoin) r = Math.Min(r, 3);
                string d = rejoin ? Lang.T("o host caiu ou reiniciou: entre de novo")
                    : !found ? Lang.T("procurando o host com o código ") + cfg.SessionCode
                    : !world ? Lang.T("esperando o host entrar no mundo")
                    : !guestAlive ? Lang.T("abrindo o jogo")
                    : guestRun.State == "titulo" ? (guestRun.HostReadyRemote ? Lang.T("servidor do host pronto")
                        : guestRun.ConnectionLocal ? Lang.T("o mod ainda não achou o host (firewall?)") : Lang.T("esperando o servidor do host"))
                    : guestRun.Ping != "" ? "ping " + guestRun.Ping : "";
                guestCard.Set(r, true, true, d);
            }
            else if (otherPcGuest) guestCard.Set(hostRun.GuestState == "no mundo" ? 4 : 3, true, false, Lang.T("guest em outro PC"));
            else guestCard.Set(0, false, false, "");

            btnHost.Enabled = !hostAlive;
            btnHost.Text = hostAlive ? Lang.T("Host aberto") : Lang.T("Abrir como host");
            bool rejoinNow = GuestMustRejoin();
            btnGuest.Enabled = !guestAlive || rejoinNow;
            if (!searching) btnGuest.Text = rejoinNow ? Lang.T("Entrar de novo") : guestAlive ? Lang.T("Guest aberto") : Lang.T("Entrar no mundo");
            btnStop.Enabled = hostAlive || guestAlive || searching;

            seal.Hint = beacon != null ? Lang.T("sessão aberta  ·  seu IP ") + Util.MainLanIp() : Lang.T("o guest digita o mesmo código");
            seal.Invalidate();
            var v = Util.GameVersion(cfg.GameDir);
            var exp = cfg.Current?.ExpectedBuild ?? "";
            var vOk = exp == "" || Util.BuildOf(v) == exp;
            lblIp.Text = Lang.T("Jogo ") + (v == "" ? Lang.T("não encontrado") : v) + (vOk ? "" : Lang.T("  (o mod é do build ") + exp + ")") + Lang.T("   ·   Mod ") + (cfg.Current?.Name ?? Lang.T("não encontrado")) + Lang.T("   ·   IP ") + Util.MainLanIp();
            lblIp.ForeColor = v == "" || !vOk ? Bad : Dim;
        }

        // Dev aid: draws the main window and each secondary window into PNGs (no screen capture), then exits.
        public static void RenderTabs(string dir)
        {
            Directory.CreateDirectory(dir);
            var f = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000), ShowInTaskbar = false };
            f.cfg.Language = Lang.English ? "en" : "pt";
            void Shot(Control c, string name)
            {
                using (var bmp = new Bitmap(c.Width, c.Height))
                {
                    c.DrawToBitmap(bmp, new Rectangle(0, 0, c.Width, c.Height));
                    bmp.Save(Path.Combine(dir, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            f.Shown += async (s, e) =>
            {
                await Task.Delay(1500);
                f.RefreshStatus();
                Shot(f, "aba0");
                f.tick.Stop();
                f.txtCode.Text = "LOBO-4821";
                f.hostCard.Set(3, true, true, Lang.T("sessão LOBO-4821 aberta neste PC"));
                f.guestCard.Set(1, true, true, Lang.T("esperando o host entrar no mundo"));
                f.btnHost.Enabled = false; f.btnHost.Text = Lang.T("Host aberto");
                await Task.Delay(300);
                Shot(f, "aba0-sessao");
                f.tick.Start();
                var pages = new[] { (Lang.T("Mod e arquivos"), f.modPage), (Lang.T("Saves"), f.savePage), (Lang.T("Ferramentas"), f.toolsPage), (Lang.T("Config"), f.cfgPage) };
                for (int i = 0; i < pages.Length; i++)
                {
                    if (i == 0) f.RefreshFiles(); if (i == 1) f.RefreshSaves(); if (i == 2) f.RefreshCleanup();
                    var d = new ThemedDialog(pages[i].Item1, pages[i].Item2, new Size(920, 820)) { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000) };
                    d.Show(f);
                    await Task.Delay(500);
                    Shot(d, "aba" + (i + 1));
                    d.Close(); d.Dispose();
                }
                f.Close();
            };
            Application.Run(f);
        }

        // =====================================================================================  helpers

        void SaveFields()
        {
            cfg.HostName = txtHostName.Text.Trim();
            cfg.GuestName = txtGuestName.Text.Trim();
            cfg.SessionCode = txtCode.Text.Trim().ToUpperInvariant();
            if (txtGameDir != null)
            {
                cfg.GameDir = txtGameDir.Text.Trim();
                cfg.ManualHostIp = txtManualIp.Text.Trim();
                cfg.Transport = chkUdp.Checked ? "udp" : "steam";
                cfg.FriendSteamId = new string(txtFriendId.Text.Where(char.IsDigit).ToArray());
                cfg.PeerPort = (int)numPeer.Value; cfg.DiscoveryPort = (int)numDisc.Value; cfg.FilePort = (int)numFile.Value;
                cfg.FirstEDelaySec = (int)numFirst.Value; cfg.EIntervalSec = (int)numInterval.Value; cfg.MaxEPresses = (int)numMax.Value;
                cfg.KeepLogs = (int)numKeep.Value;
                cfg.RecordPackets = chkRecord.Checked; cfg.AutoSyncFromHost = chkSync.Checked; cfg.AutoRestoreWatch = chkWatch.Checked;
                cfg.ShareSaveDir = txtShareDir.Text.Trim();
            }
            if (beacon != null && beaconCode != cfg.SessionCode && hostRun != null && hostRun.Alive) StartBeacon();
            SaveCfg();
        }

        void SaveCfg()
        {
            try { cfg.Save(); } catch (Exception e) { AppLog.Write(Lang.T("não salvei a config: ") + e.Message); }
        }

        void AppendLog(string l)
        {
            if (txtLog.TextLength > 200000) txtLog.Text = txtLog.Text.Substring(100000);
            txtLog.AppendText(l + "\r\n");
        }

        void ShowBanner(string t)
        {
            banner.Text = string.IsNullOrEmpty(t) ? "" : t + Lang.T("   (clique para fechar)");
            banner.Visible = !string.IsNullOrEmpty(t);
            LayoutMain();
            if (!string.IsNullOrEmpty(t)) AppLog.Write(Lang.T("AVISO: ") + t);
        }

        void Msg(string t) => MessageBox.Show(this, t, "Crimson Desert: Pywel Together");

        void Fail(string what, Exception e)
        {
            AppLog.Write(what + Lang.T(": ERRO: ") + e.Message);
            MessageBox.Show(this, e.Message, Lang.T("Erro (") + what + ")", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        void Busy(Action a)
        {
            Cursor = Cursors.WaitCursor;
            try { a(); }
            catch (Exception e) { Fail(Lang.T("ação"), e); }
            finally { Cursor = Cursors.Default; }
        }

        string PickFolder(string start)
        {
            using (var d = new FolderBrowserDialog { SelectedPath = Directory.Exists(start) ? start : "" })
                return d.ShowDialog(this) == DialogResult.OK ? d.SelectedPath : null;
        }
    }
}
