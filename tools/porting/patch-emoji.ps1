$ErrorActionPreference = 'Stop'
$lat = [Text.Encoding]::GetEncoding(28591)
$root = '<root>\Pywel-Together'
$ts = Get-Date -Format 'dd-MM_HH-mm'
$bk = "$root\Backup_Code\${ts}_Nomes-emoji_backup"
New-Item -ItemType Directory -Force $bk | Out-Null
Copy-Item "$root\Source-Code\Theme.cs" $bk
$p = "$root\Source-Code\Theme.cs"
$t = [IO.File]::ReadAllText($p, $lat)
$anchor = '            Gen = new OrnateButton { Text = Lang.T("Gerar") };'
if (-not $t.Contains($anchor)) { throw 'ancora nao achada' }
$add = @'
            Friends.DrawMode = DrawMode.OwnerDrawFixed;
            Friends.ItemHeight = 22;
            Friends.DropDownWidth = 340;
            Friends.DrawItem += DrawFriend;

'@
$t = $t.Replace($anchor, $add + $anchor)
$anchor2 = '        void ApplyMode()'
if (-not $t.Contains($anchor2)) { throw 'ancora2 nao achada' }
$fn = @'
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

'@
$t = $t.Replace($anchor2, $fn + $anchor2)
[IO.File]::WriteAllText($p, $t, $lat)
"patch ok"
