using System.Drawing;
using System.Windows.Forms;

namespace CdmpLauncher
{
    // First window of the app (and the "Language / Idioma" link): English or Português (BR). Result: "en", "pt" or null.
    public class LanguagePicker : Form
    {
        public string Choice;
        Bitmap backdrop;

        public LanguagePicker()
        {
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.CenterScreen;
            Text = "Language / Idioma"; BackColor = Theme.Ink; ForeColor = Theme.Parchment; Font = Theme.Body(10f);
            ClientSize = new Size(520, 300); DoubleBuffered = true; KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            var chrome = new Chrome { Sub = "LANGUAGE  ·  IDIOMA", Sub2 = null, Height = 104 };
            var en = new OrnateButton { Text = "English", Kind = ButtonKind.Primary, Font = Theme.Body(11f) };
            var pt = new OrnateButton { Text = "Português (BR)", Kind = ButtonKind.Primary, Font = Theme.Body(11f) };
            en.Click += (s, e) => { Choice = "en"; DialogResult = DialogResult.OK; Close(); };
            pt.Click += (s, e) => { Choice = "pt"; DialogResult = DialogResult.OK; Close(); };
            Controls.Add(en); Controls.Add(pt); Controls.Add(chrome);
            Layout += (s, e) =>
            {
                int w = 210, gap = 24, x = (ClientSize.Width - 2 * w - gap) / 2, y = 150;
                en.SetBounds(x, y, w, 56);
                pt.SetBounds(x + w + gap, y, w, 56);
            };
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.Style |= 0x00020000 | 0x00080000; cp.ClassStyle |= 0x00020000; cp.ExStyle |= 0x02000000; return cp; }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (backdrop == null || backdrop.Size != ClientSize) { backdrop?.Dispose(); backdrop = Theme.Backdrop(ClientSize); }
            e.Graphics.DrawImageUnscaled(backdrop, 0, 0);
            Theme.Smooth(e.Graphics);
            Theme.Rule(e.Graphics, 60, ClientSize.Width - 60, 250, Theme.Gold, ClientSize.Width / 2f, 110);
            using (var p = new Pen(Color.FromArgb(110, Theme.GoldDark))) e.Graphics.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        protected override void Dispose(bool d) { if (d) backdrop?.Dispose(); base.Dispose(d); }

        public static void RenderTo(string dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            var f = new LanguagePicker { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000), ShowInTaskbar = false };
            f.Shown += async (s, e) =>
            {
                await System.Threading.Tasks.Task.Delay(800);
                using (var b = new Bitmap(f.Width, f.Height)) { f.DrawToBitmap(b, new Rectangle(0, 0, f.Width, f.Height)); b.Save(System.IO.Path.Combine(dir, "idioma.png")); }
                f.Close();
            };
            Application.Run(f);
        }

        // Returns "en" / "pt", or null when the window was closed without a choice.
        public static string Ask(IWin32Window owner = null)
        {
            using (var f = new LanguagePicker())
            {
                if (owner != null) { f.StartPosition = FormStartPosition.CenterParent; f.ShowInTaskbar = false; return f.ShowDialog(owner) == DialogResult.OK ? f.Choice : null; }
                return f.ShowDialog() == DialogResult.OK ? f.Choice : null;
            }
        }
    }
}
