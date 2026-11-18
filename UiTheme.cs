namespace WebsiteViewer;

internal enum ButtonTone
{
    Neutral,
    Primary,
    Blue,
    Danger
}

internal static class UiTheme
{
    // Align with the dark card-based visual language used by the user's OCR tools.
    public static readonly Color Bg = Color.FromArgb(9, 13, 21);          // #090d15
    public static readonly Color Card = Color.FromArgb(17, 24, 39);       // #111827
    public static readonly Color Card2 = Color.FromArgb(23, 32, 51);      // #172033
    public static readonly Color Input = Color.FromArgb(12, 19, 33);      // #0c1321
    public static readonly Color Surface = Color.FromArgb(5, 7, 11);      // #05070b
    public static readonly Color Border = Color.FromArgb(43, 55, 77);     // #2b374d
    public static readonly Color Text = Color.FromArgb(238, 244, 255);     // #eef4ff
    public static readonly Color Muted = Color.FromArgb(158, 171, 192);   // #9eabc0
    public static readonly Color Accent = Color.FromArgb(72, 215, 165);   // #48d7a5
    public static readonly Color Blue = Color.FromArgb(112, 167, 255);    // #70a7ff
    public static readonly Color Warn = Color.FromArgb(255, 204, 102);    // #ffcc66
    public static readonly Color Bad = Color.FromArgb(255, 125, 125);     // #ff7d7d
    public static readonly Color Good = Color.FromArgb(89, 210, 155);

    public static Font UiFont(float size = 9.5f, FontStyle style = FontStyle.Regular) =>
        new("Microsoft YaHei UI", size, style, GraphicsUnit.Point);

    public static Font MonoFont(float size = 9.2f, FontStyle style = FontStyle.Regular) =>
        new("Consolas", size, style, GraphicsUnit.Point);

    public static RoundedPanel CardPanel(int radius = 14)
    {
        return new RoundedPanel
        {
            Radius = radius,
            BackColor = Card,
            BorderColor = Color.Transparent,
            BorderWidth = 0f,
            Margin = new Padding(0)
        };
    }

    public static RoundedButton Button(string text, ButtonTone tone = ButtonTone.Neutral)
    {
        Color baseColor;
        Color hoverColor;
        Color pressedColor;
        Color foreColor;
        switch (tone)
        {
            case ButtonTone.Primary:
                baseColor = Accent;
                hoverColor = Color.FromArgb(88, 229, 179);
                pressedColor = Color.FromArgb(56, 193, 145);
                foreColor = Color.FromArgb(6, 38, 28);
                break;
            case ButtonTone.Blue:
                baseColor = Blue;
                hoverColor = Color.FromArgb(130, 179, 255);
                pressedColor = Color.FromArgb(90, 145, 236);
                foreColor = Color.FromArgb(7, 22, 43);
                break;
            case ButtonTone.Danger:
                baseColor = Color.FromArgb(74, 32, 38);
                hoverColor = Color.FromArgb(93, 39, 47);
                pressedColor = Color.FromArgb(62, 27, 32);
                foreColor = Color.FromArgb(255, 217, 222);
                break;
            default:
                baseColor = Card2;
                hoverColor = Color.FromArgb(35, 47, 70);
                pressedColor = Color.FromArgb(18, 27, 43);
                foreColor = Text;
                break;
        }

        return new RoundedButton
        {
            Text = text,
            Height = 38,
            Radius = 10,
            BaseColor = baseColor,
            HoverColor = hoverColor,
            PressedColor = pressedColor,
            ForeColor = foreColor,
            BorderColor = Color.Transparent,
            BorderWidth = 0f,
            Font = UiFont(9.3f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Margin = new Padding(4, 2, 4, 2),
            Padding = new Padding(10, 0, 10, 0)
        };
    }

    public static void StyleTextBox(TextBox box, bool readOnly = false)
    {
        box.BackColor = readOnly ? Color.FromArgb(10, 16, 28) : Input;
        box.ForeColor = readOnly ? Muted : Text;
        box.BorderStyle = BorderStyle.None;
        box.Font = UiFont(9.8f);
        box.AutoSize = false;
        box.Height = 36;
        box.ReadOnly = readOnly;
        box.ShortcutsEnabled = true;
    }

    // WinForms 的 FixedSingle 文本框边框来自系统主题，深色背景下会变成亮白色。
    // TextBox 本身禁用边框，置入同色容器并进行垂直居中，缩放时不会裁切文字。
    public static Panel InputHost(TextBox box, bool readOnly = false, Padding? margin = null)
    {
        var bg = readOnly ? Color.FromArgb(10, 16, 28) : Input;
        var host = new Panel
        {
            Dock = DockStyle.None,
            BackColor = bg,
            Margin = margin ?? Padding.Empty,
            Padding = Padding.Empty
        };
        box.BorderStyle = BorderStyle.None;
        box.BackColor = bg;
        box.AutoSize = true;
        box.Dock = DockStyle.None;
        box.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        box.Margin = Padding.Empty;
        void Reposition()
        {
            if (host.IsDisposed || box.IsDisposed) return;
            int h = box.PreferredHeight;
            int pad = Math.Max(1, (int)Math.Round(9 * host.DeviceDpi / 96.0));
            box.SetBounds(pad, Math.Max(0, (host.ClientSize.Height - h) / 2),
                Math.Max(1, host.ClientSize.Width - 2 * pad), h);
        }
        host.Controls.Add(box);
        host.SizeChanged += (_, _) => Reposition();
        box.FontChanged += (_, _) => Reposition();
        box.DpiChangedAfterParent += (_, _) => Reposition();
        Reposition();
        return host;
    }

    public static void StyleCheckBox(CheckBox box)
    {
        box.ForeColor = Muted;
        box.BackColor = Color.Transparent;
        box.Font = UiFont(9f);
        box.AutoSize = true;
        box.Cursor = Cursors.Hand;
    }

    public static void StyleGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.BackgroundColor = Card;
        grid.BorderStyle = BorderStyle.None;
        // 禁用原生 Raised 表头与网格的系统边线（部分 Windows 主题会绘制白线）。
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.None;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.ReadOnly = true;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.RowHeadersVisible = false;
        grid.EnableHeadersVisualStyles = false;
        grid.AutoGenerateColumns = false;
        grid.ColumnHeadersHeight = 38;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.RowTemplate.Height = 34;
        grid.ScrollBars = ScrollBars.Both;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Card2;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
        grid.ColumnHeadersDefaultCellStyle.Font = UiFont(9.1f, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Card2;
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(5, 0, 5, 0);
        grid.DefaultCellStyle.BackColor = Card;
        grid.DefaultCellStyle.ForeColor = Text;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(29, 73, 91);
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.DefaultCellStyle.Font = UiFont(9.1f);
        grid.DefaultCellStyle.Padding = new Padding(5, 0, 5, 0);
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(14, 21, 34);
        grid.GridColor = Color.FromArgb(34, 45, 63);
    }
}
