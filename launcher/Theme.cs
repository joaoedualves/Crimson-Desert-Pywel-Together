using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CdmpLauncher
{
    // Visual language taken from the game's own menus: warm ink panels over a blurred world, thin parchment lines,
    // the "Crimson Desert" title face for headings, Adelon for text, gold only for what is selected or reached.
    public static class Theme
    {
        public static readonly Color Ink = Color.FromArgb(16, 13, 11);
        public static readonly Color Ink2 = Color.FromArgb(26, 21, 17);
        public static readonly Color Ink3 = Color.FromArgb(38, 31, 25);
        public static readonly Color Row = Color.FromArgb(205, 30, 24, 19);       // translucent row, like the game's options
        public static readonly Color RowHi = Color.FromArgb(225, 74, 58, 40);     // selected row
        public static readonly Color Line = Color.FromArgb(46, 232, 214, 180);   // hairlines between rows
        public static readonly Color Parchment = Color.FromArgb(234, 225, 206);
        public static readonly Color Ash = Color.FromArgb(146, 134, 116);
        public static readonly Color Gold = Color.FromArgb(214, 178, 106);
        public static readonly Color GoldLight = Color.FromArgb(244, 222, 164);
        public static readonly Color GoldDark = Color.FromArgb(124, 98, 56);
        public static readonly Color Bronze = Color.FromArgb(96, 74, 48);
        public static readonly Color Ok = Color.FromArgb(170, 204, 120);
        public static readonly Color Warn = Color.FromArgb(230, 182, 92);
        public static readonly Color Bad = Color.FromArgb(222, 104, 84);

        // ---------- fonts: the official ones from launcher\fontes when present (never inside the exe)

        static readonly PrivateFontCollection pfc = new PrivateFontCollection();
        static FontFamily title, body;
        [DllImport("gdi32.dll")] static extern int AddFontResourceEx(string f, uint fl, IntPtr r);

        // config\fonts: GDI+ (drawing) and GDI (text boxes) both get a private copy; without the folder a similar
        // Windows serif is used.
        public static string FontDir
        {
            get
            {
                var own = Path.Combine(Config.DataDir, "assets", "fonts");
                var beside = Path.Combine(Config.ExeDir, "config", "assets", "fonts");
                return Directory.Exists(own) ? own : beside;
            }
        }

        // config\assets (fonts\*.ttf, icons\app.ico): put back from the exe's own copy when missing or emptied.
        public static List<string> RepairAssets()
        {
            var did = new List<string>();
            var root = Path.Combine(Config.DataDir, "assets");
            foreach (var (res, rel) in new[] { ("assets.fonts.crimson.ttf", @"fonts\crimson.ttf"), ("assets.fonts.adelon.ttf", @"fonts\adelon.ttf"), ("assets.icons.app.ico", @"icons\app.ico") })
            {
                var dst = Path.Combine(root, rel);
                try
                {
                    if (File.Exists(dst) && new FileInfo(dst).Length > 0) continue;
                    using (var s = typeof(Theme).Assembly.GetManifestResourceStream(res))
                    {
                        if (s == null) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(dst));
                        using (var o = File.Create(dst)) s.CopyTo(o);
                        did.Add(rel);
                    }
                }
                catch { }
            }
            return did;
        }

        public static readonly List<string> Repaired = new List<string>();

        static void LoadFonts()
        {
            if (title != null) return;
            Repaired.AddRange(RepairAssets());
            foreach (var name in new[] { "crimson.ttf", "adelon.ttf" })
            {
                var p = Path.Combine(FontDir, name);
                if (!File.Exists(p)) continue;
                try { pfc.AddFontFile(p); AddFontResourceEx(p, 0x10 /* FR_PRIVATE */, IntPtr.Zero); } catch { }
            }
            FontFamily Pick(string own, params string[] fallbacks)
            {
                var f = pfc.Families.FirstOrDefault(x => x.Name == own);
                if (f != null) return f;
                using (var inst = new InstalledFontCollection())
                    foreach (var n in fallbacks) { var g = inst.Families.FirstOrDefault(x => x.Name == n); if (g != null) return g; }
                return FontFamily.GenericSerif;
            }
            title = Pick("Crimson Desert", "Cinzel", "Trajan Pro", "Palatino Linotype", "Georgia");
            body = Pick("Adelon", "Palatino Linotype", "Book Antiqua", "Georgia");
        }

        public static bool OfficialFonts { get { LoadFonts(); return title.Name == "Crimson Desert"; } }
        public static Font Title(float s) { LoadFonts(); return new Font(title, s, FontStyle.Regular); }
        public static Font Body(float s = 10f, FontStyle st = FontStyle.Regular)
        {
            LoadFonts();
            if (!body.IsStyleAvailable(st)) st = FontStyle.Regular;
            return new Font(body, s, st);
        }
        public static Font Ui(float s = 9.5f, FontStyle st = FontStyle.Regular) => new Font("Segoe UI", s, st);
        public static Font Mono(float s = 9f) => new Font("Consolas", s);

        // The title face has no accents and little punctuation: those letters come from the body face, on the same
        // baseline, so "SESSÃO" still reads right.
        const string TitleGlyphs = " !\"$%',-.0123456789:;?ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

        static Font FontFor(char c, Font f, ref Font fb)
        {
            if (f.FontFamily.Name != "Crimson Desert" || TitleGlyphs.IndexOf(c) >= 0) return f;
            return fb ?? (fb = Body(f.Size * 0.92f));
        }

        static float Ascent(Font f, Graphics g) =>
            f.SizeInPoints * g.DpiY / 72f * f.FontFamily.GetCellAscent(f.Style) / f.FontFamily.GetEmHeight(f.Style);

        public static void DrawSpaced(Graphics g, string text, Font f, Color c, float x, float y, float tracking)
        {
            Font fb = null;
            var a0 = Ascent(f, g);
            using (var b = new SolidBrush(c))
                foreach (var ch in text)
                {
                    var ff = FontFor(ch, f, ref fb);
                    g.DrawString(ch.ToString(), ff, b, x, y + a0 - Ascent(ff, g), StringFormat.GenericTypographic);
                    x += Adv(g, ch, ff) + tracking;
                }
            fb?.Dispose();
        }

        static float Adv(Graphics g, char ch, Font f) =>
            ch == ' ' ? f.Size * 0.42f : g.MeasureString(ch.ToString(), f, PointF.Empty, StringFormat.GenericTypographic).Width;

        public static float SpacedWidth(Graphics g, string text, Font f, float tracking)
        {
            Font fb = null; float w = 0;
            foreach (var ch in text) w += Adv(g, ch, FontFor(ch, f, ref fb)) + tracking;
            fb?.Dispose();
            return Math.Max(0, w - tracking);
        }

        public static void DrawSpacedCentered(Graphics g, string text, Font f, Color c, float cx, float y, float tracking) =>
            DrawSpaced(g, text, f, c, cx - SpacedWidth(g, text, f, tracking) / 2f, y, tracking);

        // ---------- ornaments

        public static void Diamond(Graphics g, Color c, float cx, float cy, float r, bool fill = true)
        {
            var pts = new[] { new PointF(cx, cy - r), new PointF(cx + r, cy), new PointF(cx, cy + r), new PointF(cx - r, cy) };
            if (fill) using (var b = new SolidBrush(c)) g.FillPolygon(b, pts);
            else using (var p = new Pen(c, 1.2f)) g.DrawPolygon(p, pts);
        }

        // A hairline that fades at both ends; optional diamond on it.
        public static void Rule(Graphics g, float x1, float x2, float y, Color? col = null, float? diamondX = null, int alpha = 170)
        {
            if (x2 - x1 < 2) return;
            var c = col ?? Gold;
            using (var lg = new LinearGradientBrush(new PointF(x1 - 1, y), new PointF(x2 + 1, y), Color.Transparent, Color.Transparent))
            {
                lg.InterpolationColors = new ColorBlend
                {
                    Colors = new[] { Color.FromArgb(0, c), Color.FromArgb(alpha, c), Color.FromArgb(alpha, c), Color.FromArgb(0, c) },
                    Positions = new[] { 0f, 0.2f, 0.8f, 1f },
                };
                using (var p = new Pen(lg, 1f)) g.DrawLine(p, x1, y, x2, y);
            }
            if (diamondX.HasValue) Diamond(g, c, diamondX.Value, y, 3f);
        }

        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        public static Image SteamArt(string file)
        {
            try
            {
                var d = Path.Combine(Util.SteamPath(), "appcache", "librarycache", "3321460");
                var f = Directory.Exists(d) ? Directory.GetFiles(d, file, SearchOption.AllDirectories).FirstOrDefault() : null;
                if (f == null) return null;
                using (var fs = File.OpenRead(f)) return new Bitmap(Image.FromStream(fs));
            }
            catch { return null; }
        }

        // The window's backdrop: the characters of the key art, blurred and sunk into ink (like the in-game menus
        // over the world). Falls back to a warm ink gradient without Steam's art.
        public static Bitmap Backdrop(Size size)
        {
            var bmp = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height));
            using (var g = Graphics.FromImage(bmp))
            {
                Smooth(g);
                using (var b = new LinearGradientBrush(new Rectangle(Point.Empty, bmp.Size), Color.FromArgb(34, 26, 20), Ink, 90f)) g.FillRectangle(b, 0, 0, bmp.Width, bmp.Height);
                using (var hero = SteamArt("library_hero.jpg"))
                    if (hero != null)
                    {
                        // the right 55% holds the characters
                        var src = new RectangleF(hero.Width * 0.44f, 0, hero.Width * 0.54f, hero.Height);
                        float sc = Math.Max(bmp.Width / src.Width, bmp.Height / src.Height);
                        var small = new Bitmap(Math.Max(1, bmp.Width / 10), Math.Max(1, bmp.Height / 10));
                        using (var sg = Graphics.FromImage(small))
                        {
                            sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
                            float w = src.Width * sc / 10f, h = src.Height * sc / 10f;
                            sg.DrawImage(hero, new RectangleF((small.Width - w) / 2f, (small.Height - h) / 2f, w, h), src, GraphicsUnit.Pixel);
                        }
                        using (var ia = new ImageAttributes())
                        {
                            ia.SetColorMatrix(new ColorMatrix { Matrix00 = 0.55f, Matrix11 = 0.46f, Matrix22 = 0.40f, Matrix33 = 1f, Matrix44 = 1f });
                            ia.SetWrapMode(WrapMode.TileFlipXY);
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.DrawImage(small, new Rectangle(0, 0, bmp.Width, bmp.Height), 0, 0, small.Width, small.Height, GraphicsUnit.Pixel, ia);
                        }
                        small.Dispose();
                    }
                // vignette
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(-bmp.Width * 0.35f, -bmp.Height * 0.25f, bmp.Width * 1.7f, bmp.Height * 1.5f);
                    using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(0, Ink), SurroundColors = new[] { Color.FromArgb(200, Ink) } })
                        g.FillRectangle(pb, 0, 0, bmp.Width, bmp.Height);
                }
                using (var top = new LinearGradientBrush(new Rectangle(0, 0, bmp.Width, 140), Color.FromArgb(200, Ink), Color.FromArgb(0, Ink), 90f))
                    g.FillRectangle(top, 0, 0, bmp.Width, 140);
            }
            return bmp;
        }
    }

    // Base for the custom controls: double buffered, drawn over whatever is behind them.
    public class Glass : Control
    {
        public Glass()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent; ForeColor = Theme.Parchment;
        }
    }

    // Borderless window chrome: centered title block, drag anywhere on it, minimise and close.
    public class Chrome : Glass
    {
        public string Title = "CRIMSON DESERT", Sub = "PYWEL TOGETHER", Sub2 = "CO-OP ONLINE";
        public bool Small;
        int hover = -1;
        [DllImport("user32")] static extern bool ReleaseCapture();
        [DllImport("user32")] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);

        public Chrome() { Dock = DockStyle.Top; Height = 126; }

        Rectangle CloseBox => new Rectangle(Width - 46, 0, 46, 32);
        Rectangle MinBox => new Rectangle(Width - 92, 0, 46, 32);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Smooth(g);
            float cx = Width / 2f;
            using (var f = Theme.Title(Small ? 15f : 25f))
            {
                // gold "metal": a dark offset, then the face
                var tr = Small ? 5f : 9f;
                float y = Small ? 14 : 20;
                Theme.DrawSpacedCentered(g, Title, f, Color.FromArgb(150, 0, 0, 0), cx + 1, y + 2, tr);
                Theme.DrawSpacedCentered(g, Title, f, Theme.GoldLight, cx, y, tr);
                var yy = y + f.GetHeight(g) + (Small ? 4 : 6);
                bool twoSubs = !Small && !string.IsNullOrEmpty(Sub2);
                if (!string.IsNullOrEmpty(Sub))
                    // with a second subtitle the first one is the app's name: title face, gold
                    using (var sf = twoSubs ? Theme.Title(12.5f) : Theme.Body(Small ? 8.5f : 9.5f))
                    {
                        var trs = twoSubs ? 5.5f : 4.5f;
                        var w = Theme.SpacedWidth(g, Sub, sf, trs);
                        Theme.DrawSpacedCentered(g, Sub, sf, twoSubs ? Theme.Gold : Theme.Parchment, cx, yy, trs);
                        var my = yy + sf.GetHeight(g) / 2f;
                        Theme.Rule(g, cx - w / 2 - 70, cx - w / 2 - 14, my, Theme.Gold, cx - w / 2 - 14);
                        Theme.Rule(g, cx + w / 2 + 14, cx + w / 2 + 70, my, Theme.Gold, cx + w / 2 + 14);
                        yy += sf.GetHeight(g) + 3;
                    }
                if (twoSubs)
                    using (var sf2 = Theme.Body(8.5f))
                        Theme.DrawSpacedCentered(g, Sub2, sf2, Theme.Parchment, cx, yy, 4f);
            }
            Theme.Rule(g, 24, Width - 24, Height - 1, Theme.Gold, null, 90);
            using (var p = new Pen(hover == 1 ? Theme.GoldLight : Theme.Ash, 1.2f))
            {
                var r = CloseBox; if (hover == 1) using (var b = new SolidBrush(Color.FromArgb(60, Theme.Bad))) g.FillRectangle(b, r);
                g.DrawLine(p, r.X + 18, r.Y + 11, r.X + 28, r.Y + 21); g.DrawLine(p, r.X + 28, r.Y + 11, r.X + 18, r.Y + 21);
            }
            using (var p = new Pen(hover == 0 ? Theme.GoldLight : Theme.Ash, 1.2f))
            {
                var r = MinBox; if (hover == 0) using (var b = new SolidBrush(Color.FromArgb(40, Theme.Parchment))) g.FillRectangle(b, r);
                g.DrawLine(p, r.X + 18, r.Y + 17, r.X + 28, r.Y + 17);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var h = CloseBox.Contains(e.Location) ? 1 : MinBox.Contains(e.Location) ? 0 : -1;
            if (h != hover) { hover = h; Invalidate(); }
        }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (CloseBox.Contains(e.Location)) { FindForm()?.Close(); return; }
            if (MinBox.Contains(e.Location)) { var f = FindForm(); if (f != null) f.WindowState = FormWindowState.Minimized; return; }
            ReleaseCapture(); SendMessage(FindForm().Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
        }
    }

    public enum ButtonKind { Normal, Primary, Gold = Primary }

    // Plain: a dark plate with a hairline. Primary: the game's "selected row" (warm bronze, gold edge).
    public class OrnateButton : Button
    {
        public ButtonKind Kind;
        public string KeyHint;   // a shortcut letter shown in the corner
        bool hover, down;

        public OrnateButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat; Cursor = Cursors.Hand; ForeColor = Theme.Parchment; BackColor = Color.Transparent;
            FlatAppearance.BorderSize = 0;
            Font = Theme.Body(9.5f);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            InvokePaintBackground(this, e);  // transparent: the parent paints behind us
            Theme.Smooth(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            bool prim = Kind == ButtonKind.Primary;
            Color a, b, edge;
            if (!Enabled) { a = Color.FromArgb(150, 34, 30, 26); b = Color.FromArgb(150, 24, 21, 18); edge = Color.FromArgb(50, Theme.Parchment); }
            else if (prim)
            {
                a = hover ? Color.FromArgb(235, 122, 96, 62) : Color.FromArgb(230, 98, 77, 50);
                b = hover ? Color.FromArgb(235, 70, 54, 35) : Color.FromArgb(230, 54, 42, 28);
                edge = hover ? Theme.GoldLight : Theme.Gold;
            }
            else
            {
                a = hover ? Color.FromArgb(220, 60, 48, 36) : Color.FromArgb(200, 36, 29, 23);
                b = hover ? Color.FromArgb(220, 44, 35, 27) : Color.FromArgb(200, 26, 21, 17);
                edge = hover ? Theme.Gold : Color.FromArgb(70, Theme.Parchment);
            }
            if (down) { var t = a; a = b; b = t; }
            using (var br = new LinearGradientBrush(Rectangle.Round(r), a, b, 90f)) g.FillRectangle(br, r);
            using (var p = new Pen(edge, 1f)) g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
            var fg = !Enabled ? Theme.Ash : prim ? Theme.GoldLight : hover ? Theme.GoldLight : Theme.Parchment;
            if (prim)
                using (var f = Theme.Title(Math.Min(Font.Size + 1.5f, Height / 3.4f)))
                    Theme.DrawSpacedCentered(g, Text.ToUpperInvariant(), f, fg, Width / 2f, (Height - f.GetHeight(g)) / 2f + 1, 3f);
            else
                TextRenderer.DrawText(g, Text, Font, Rectangle.Round(r), fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            if (!string.IsNullOrEmpty(KeyHint) && Enabled)
            {
                var kr = new Rectangle(Width - 30, (Height - 20) / 2, 20, 20);
                using (var p = new Pen(Color.FromArgb(150, Theme.Gold))) g.DrawRectangle(p, kr);
                using (var f = Theme.Body(8.5f))
                    TextRenderer.DrawText(g, KeyHint, f, kr, Theme.GoldLight, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    // A role "card": its title, the player's name, a trail of milestones that light up as the session advances,
    // and the action button. HOST and GUEST sit side by side like two characters to choose from.
    public class RoleCard : Glass
    {
        public string Role = "", Tagline = "", Detail = "";
        public string[] Steps = new string[0];
        public string[] Waiting;     // text of a step while it is the one in progress (not reached yet)
        public int Reached;          // steps done (0..Steps.Length)
        public bool Live;            // something is happening: the next step pulses
        public bool Selected;        // this PC plays this role now
        public readonly TextBox Name;
        public readonly OrnateButton Action;
        readonly Timer pulse = new Timer { Interval = 70 };
        float phase;

        public RoleCard(string role, string tagline, string[] steps, string action)
        {
            Role = role; Tagline = tagline; Steps = steps;
            Name = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Ink, ForeColor = Theme.GoldLight, Font = Theme.Body(11f), TextAlign = HorizontalAlignment.Center };
            Action = new OrnateButton { Text = action, Kind = ButtonKind.Primary, Font = Theme.Body(10f) };
            Controls.Add(Name); Controls.Add(Action);
            Name.GotFocus += (s, e) => Invalidate(); Name.LostFocus += (s, e) => Invalidate();
            pulse.Tick += (s, e) => { phase += 0.12f; Invalidate(TrackRect()); };
        }

        public void Set(int reached, bool live, bool selected, string detail)
        {
            if (reached == Reached && live == Live && selected == Selected && detail == Detail) return;
            Reached = reached; Live = live; Selected = selected; Detail = detail;
            pulse.Enabled = live && reached < Steps.Length;
            Invalidate();
        }

        Rectangle TrackRect() => new Rectangle(24, 136, Width - 48, Steps.Length * 29 + 4);

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Name == null) return;
            Name.SetBounds(34, 103, Width - 68, 22);
            Action.SetBounds(22, Height - 62, Width - 44, 42);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Smooth(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var b = new LinearGradientBrush(Rectangle.Round(r), Color.FromArgb(212, 30, 24, 19), Color.FromArgb(226, 18, 15, 12), 90f)) g.FillRectangle(b, r);
            if (Selected) using (var glow = new LinearGradientBrush(new Rectangle(0, 0, Width, 92), Color.FromArgb(60, Theme.Gold), Color.FromArgb(0, Theme.Gold), 90f)) g.FillRectangle(glow, r.X, r.Y, r.Width, 90);
            using (var p = new Pen(Selected ? Theme.Gold : Color.FromArgb(70, Theme.Parchment))) g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
            foreach (var c in new[] { new PointF(r.Left, r.Top), new PointF(r.Right, r.Top), new PointF(r.Left, r.Bottom), new PointF(r.Right, r.Bottom) })
                Theme.Diamond(g, Selected ? Theme.Gold : Theme.GoldDark, c.X, c.Y, 3.5f);

            float cx = Width / 2f;
            using (var f = Theme.Title(21f)) Theme.DrawSpacedCentered(g, Role.ToUpperInvariant(), f, Selected ? Theme.GoldLight : Theme.Parchment, cx, 16, 8f);
            using (var f = Theme.Body(9.5f))
                TextRenderer.DrawText(g, Tagline, f, new Rectangle(0, 52, Width, 24), Theme.Ash, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            Theme.Rule(g, 30, Width - 30, 79, Theme.Gold, cx, 110);
            using (var f = Theme.Body(7.5f)) Theme.DrawSpaced(g, Lang.T("SEU NOME"), f, Theme.Gold, 28, 86, 2f);
            using (var b = new SolidBrush(Theme.Ink)) g.FillRectangle(b, 26, 99, Width - 52, 28);
            using (var p = new Pen(Name.Focused ? Theme.Gold : Color.FromArgb(70, Theme.Parchment))) g.DrawRectangle(p, 26, 99, Width - 53, 28);

            var tr = TrackRect();
            float x0 = tr.X + 12;
            for (int i = 0; i < Steps.Length; i++)
            {
                float y = tr.Y + 12 + i * 29;
                bool done = i < Reached, now = i == Reached && Live;
                if (i < Steps.Length - 1)
                    using (var p = new Pen(done ? Theme.Gold : Color.FromArgb(60, Theme.Parchment), 1.2f)) g.DrawLine(p, x0, y + 6, x0, y + 23);
                if (now)
                {
                    var a = (int)(70 + 70 * Math.Sin(phase));
                    using (var b = new SolidBrush(Color.FromArgb(Math.Max(0, a), Theme.Gold))) g.FillEllipse(b, x0 - 9, y - 9, 18, 18);
                }
                Theme.Diamond(g, done ? Theme.Gold : now ? Theme.GoldLight : Color.FromArgb(110, Theme.Parchment), x0, y, done ? 5f : 4.5f, done || now);
                using (var f = Theme.Body(10f))
                    TextRenderer.DrawText(g, now && Waiting != null && i < Waiting.Length ? Waiting[i] : Steps[i], f, new Rectangle((int)x0 + 16, (int)y - 12, Width - 80, 24), done ? Theme.Parchment : now ? Theme.GoldLight : Theme.Ash, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            if (!string.IsNullOrEmpty(Detail))
                using (var f = Theme.Body(9f))
                    TextRenderer.DrawText(g, Detail, f, new Rectangle(14, Action.Top - 30, Width - 28, 26), Theme.Ash, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        protected override void Dispose(bool d) { if (d) pulse.Dispose(); base.Dispose(d); }
    }

    // One line of the game's options list: label on the left, "<  value  >" on the right.
    public class OptionRow : Glass
    {
        public string Label = "";
        public List<string> Items = new List<string>();
        int index; bool hover; int hoverArrow;
        public event Action<int> Changed;
        public int Index { get => index; set { index = Items.Count == 0 ? 0 : Math.Max(0, Math.Min(Items.Count - 1, value)); Invalidate(); } }

        public OptionRow() { Height = 40; Cursor = Cursors.Hand; }

        Rectangle LeftBox => new Rectangle(Width / 2 + 10, 0, 34, Height);
        Rectangle RightBox => new Rectangle(Width - 44, 0, 34, Height);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Smooth(g);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var b = new SolidBrush(hover ? Theme.RowHi : Theme.Row)) g.FillRectangle(b, r);
            if (hover) using (var p = new Pen(Color.FromArgb(150, Theme.Gold))) g.DrawRectangle(p, r);
            else using (var p = new Pen(Theme.Line)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
            using (var f = Theme.Body(10.5f))
                TextRenderer.DrawText(g, Label, f, new Rectangle(16, 0, Width / 2, Height), Theme.Parchment, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            Chevron(g, LeftBox, true, hoverArrow == -1);
            Chevron(g, RightBox, false, hoverArrow == 1);
            var val = Items.Count > 0 ? Items[index] : "";
            var mid = new Rectangle(LeftBox.Right, 0, RightBox.Left - LeftBox.Right, Height);
            using (var f = Theme.Body(10f))
                TextRenderer.DrawText(g, val, f, new Rectangle(mid.X, -3, mid.Width, Height), Theme.Parchment, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Items.Count > 1 && Items.Count <= 12)
            {
                float w = (Items.Count - 1) * 7f, x = mid.X + mid.Width / 2f - w / 2f;
                for (int i = 0; i < Items.Count; i++)
                    using (var b = new SolidBrush(i == index ? Theme.Parchment : Color.FromArgb(70, Theme.Parchment))) g.FillEllipse(b, x + i * 7 - 1.5f, Height - 10, 3, 3);
            }
        }

        internal static void Chevron(Graphics g, Rectangle r, bool left, bool hot)
        {
            float cx = r.X + r.Width / 2f, cy = r.Height / 2f, s = 6f;
            using (var p = new Pen(hot ? Theme.GoldLight : Theme.Parchment, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(p, left
                    ? new[] { new PointF(cx + s / 2, cy - s), new PointF(cx - s / 2, cy), new PointF(cx + s / 2, cy + s) }
                    : new[] { new PointF(cx - s / 2, cy - s), new PointF(cx + s / 2, cy), new PointF(cx - s / 2, cy + s) });
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var a = LeftBox.Contains(e.Location) ? -1 : RightBox.Contains(e.Location) ? 1 : 0;
            if (!hover || a != hoverArrow) { hover = true; hoverArrow = a; Invalidate(); }
        }
        protected override void OnMouseLeave(EventArgs e) { hover = false; hoverArrow = 0; Invalidate(); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (Items.Count == 0) return;
            int d = LeftBox.Contains(e.Location) ? -1 : 1;
            index = (index + d + Items.Count) % Items.Count;
            Invalidate();
            Changed?.Invoke(index);
        }
    }

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
            Friends.DrawMode = DrawMode.OwnerDrawFixed;
            Friends.ItemHeight = 22;
            Friends.DropDownWidth = 340;
            Friends.DrawItem += DrawFriend;
            Gen = new OrnateButton { Text = Lang.T("Gerar") };
            Copy = new OrnateButton { Text = Lang.T("Copiar") };
            Controls.Add(Code); Controls.Add(Friends); Controls.Add(Gen); Controls.Add(Copy);
        }

        // Nomes da Steam podem ter emojis e simbolos: GDI+ DrawString faz troca de fonte automatica
        // (Segoe UI Symbol / Emoji), coisa que o desenho padrao do ComboBox nao faz.
        void DrawFriend(object s, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0) return;
            var sel = (e.State & DrawItemState.Selected) != 0;
            var text = Friends.Items[e.Index].ToString();
            var g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (var br = new SolidBrush(sel ? Theme.Ink : Theme.GoldLight))
            using (var sf = new StringFormat(StringFormat.GenericDefault))
            {
                sf.FormatFlags |= StringFormatFlags.NoWrap;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                sf.LineAlignment = StringAlignment.Center;
                if (sel) using (var bg = new SolidBrush(Theme.Gold)) g.FillRectangle(bg, e.Bounds);
                var r = new RectangleF(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height);
                g.DrawString(text, Friends.Font, br, r, sf);
            }
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
    public class MenuBar : Glass
    {
        public string[] Items = new string[0];
        public event Action<int> Clicked;
        int hover = -1; RectangleF[] boxes = new RectangleF[0];
        public MenuBar() { Height = 40; Cursor = Cursors.Hand; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Smooth(g);
            using (var f = Theme.Title(10.5f))
            {
                const float tr = 3f, gap = 46f;
                var ws = Items.Select(t => Theme.SpacedWidth(g, t.ToUpperInvariant(), f, tr)).ToArray();
                float total = ws.Sum() + gap * (Items.Length - 1), x = (Width - total) / 2f, y = (Height - f.GetHeight(g)) / 2f;
                boxes = new RectangleF[Items.Length];
                for (int i = 0; i < Items.Length; i++)
                {
                    boxes[i] = new RectangleF(x - 10, 0, ws[i] + 20, Height);
                    Theme.DrawSpaced(g, Items[i].ToUpperInvariant(), f, i == hover ? Theme.GoldLight : Theme.Parchment, x, y, tr);
                    if (i == hover) Theme.Rule(g, x - 6, x + ws[i] + 6, Height - 7, Theme.Gold, x + ws[i] / 2);
                    x += ws[i] + gap;
                    if (i < Items.Length - 1) Theme.Diamond(g, Theme.GoldDark, x - gap / 2, Height / 2f, 2.6f);
                }
            }
        }

        int Hit(Point p) { for (int i = 0; i < boxes.Length; i++) if (boxes[i].Contains(p)) return i; return -1; }
        protected override void OnMouseMove(MouseEventArgs e) { var h = Hit(e.Location); if (h != hover) { hover = h; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }
        protected override void OnMouseClick(MouseEventArgs e) { var h = Hit(e.Location); if (h >= 0) Clicked?.Invoke(h); }
    }

    // A titled block for the secondary windows: title with a diamond, content on a dark plate.
    public class Section : Panel
    {
        public readonly FlowLayoutPanel Stack;
        readonly string title;
        public Section(string title, bool fill = false)
        {
            this.title = title.ToUpperInvariant();
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Dock = fill ? DockStyle.Fill : DockStyle.Top; AutoSize = !fill; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(16, 38, 16, 22);
            BackColor = Theme.Ink2; ForeColor = Theme.Parchment;
            Stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill, BackColor = Theme.Ink2 };
            Controls.Add(Stack);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Smooth(g);
            g.Clear(Theme.Ink);
            var r = new RectangleF(0, 24, Width - 1, Height - 36);
            using (var b = new SolidBrush(Theme.Ink2)) g.FillRectangle(b, r);
            using (var p = new Pen(Theme.Line)) g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
            using (var f = Theme.Title(11f))
            {
                Theme.Diamond(g, Theme.Gold, 6, 11, 3.2f);
                Theme.DrawSpaced(g, title, f, Theme.GoldLight, 16, 2, 3f);
                var w = Theme.SpacedWidth(g, title, f, 3f);
                Theme.Rule(g, 26 + w, Math.Min(Width - 10, 26 + w + 300), 11, Theme.Gold, null, 120);
            }
        }
    }

    // Borderless window for the secondary pages, same chrome as the main one.
    public class ThemedDialog : Form
    {
        public ThemedDialog(string title, Control page, Size size)
        {
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Ink; ForeColor = Theme.Parchment; Font = Theme.Body(10f);
            Size = size; KeyPreview = true; ShowInTaskbar = false; Text = title;
            var chrome = new Chrome { Title = title.ToUpperInvariant(), Sub = null, Small = true, Height = 58 };
            page.Dock = DockStyle.Fill;
            Controls.Add(page);
            Controls.Add(chrome);
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            FormClosing += (s, e) => Controls.Remove(page);   // the page lives on in the main window
        }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ClassStyle |= 0x00020000; return cp; } }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Color.FromArgb(120, Theme.GoldDark))) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
    }
}
