using System.Drawing.Drawing2D;

namespace WebsiteViewer;

internal static class UiGeometry
{
    public static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        int diameter = Math.Max(2, radius * 2);
        int right = rect.Right - 1;
        int bottom = rect.Bottom - 1;
        var path = new GraphicsPath();
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(right - diameter, bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class RoundedPanel : Panel
{
    public int Radius { get; set; } = 14;
    public Color BorderColor { get; set; } = Color.Transparent;
    public float BorderWidth { get; set; } = 0f;

    public RoundedPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var rect = ClientRectangle;
        if (rect.Width <= 1 || rect.Height <= 1) return;

        // 先把整个矩形区域绘制成父容器背景，再在其上绘制圆角实体。
        // 这样圆角四角和抗锯齿边缘不会露出 WinForms 默认背景形成浅色/白色光晕。
        e.Graphics.Clear(ResolveOpaqueParentBackColor());
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        using var path = UiGeometry.RoundedRect(rect, Math.Min(Radius, Math.Min(rect.Width, rect.Height) / 2));
        using var brush = new SolidBrush(BackColor);
        e.Graphics.FillPath(brush, path);
    }

    private Color ResolveOpaqueParentBackColor()
    {
        Control? current = Parent;
        while (current is not null)
        {
            Color c = current.BackColor;
            if (c.A == 255 && c != Color.Transparent) return c;
            current = current.Parent;
        }
        return UiTheme.Bg;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (BorderWidth <= 0 || BorderColor == Color.Transparent) return;
        var rect = ClientRectangle;
        if (rect.Width <= 1 || rect.Height <= 1) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = UiGeometry.RoundedRect(rect, Math.Min(Radius, Math.Min(rect.Width, rect.Height) / 2));
        using var pen = new Pen(BorderColor, BorderWidth) { Alignment = PenAlignment.Inset };
        e.Graphics.DrawPath(pen, path);
    }
}

internal sealed class RoundedButton : Button
{
    private bool _hovered;
    private bool _pressed;

    public int Radius { get; set; } = 10;
    public Color BaseColor { get; set; } = UiTheme.Card2;
    public Color HoverColor { get; set; } = Color.FromArgb(35, 47, 70);
    public Color PressedColor { get; set; } = Color.FromArgb(18, 27, 43);
    public Color BorderColor { get; set; } = Color.Transparent;
    public float BorderWidth { get; set; } = 0f;

    public RoundedButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        TabStop = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        if (mevent.Button == MouseButtons.Left) _pressed = true;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        pevent.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var rect = new Rectangle(0, 0, Width, Height);
        if (rect.Width <= 1 || rect.Height <= 1) return;

        // 先以实际父级背景清理矩形区域，避免禁用/重排时圆角外沿残留旧像素。
        pevent.Graphics.Clear(ResolveOpaqueParentBackColor());

        Color fill = _pressed ? PressedColor : _hovered ? HoverColor : BaseColor;
        if (!Enabled)
            fill = ControlPaint.Dark(fill, 0.22f);

        using var path = UiGeometry.RoundedRect(rect, Math.Min(Radius, Math.Min(Width, Height) / 2));
        using var brush = new SolidBrush(fill);
        pevent.Graphics.FillPath(brush, path);

        if (BorderWidth > 0)
        {
            using var pen = new Pen(Enabled ? BorderColor : Color.FromArgb(100, BorderColor), BorderWidth)
            {
                Alignment = PenAlignment.Inset
            };
            pevent.Graphics.DrawPath(pen, path);
        }

        Color textColor = Enabled ? ForeColor : UiTheme.Muted;
        TextRenderer.DrawText(
            pevent.Graphics,
            Text,
            Font,
            rect,
            textColor,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPadding);

    }

    private Color ResolveOpaqueParentBackColor()
    {
        Control? current = Parent;
        while (current is not null)
        {
            Color c = current.BackColor;
            if (c.A == 255 && c != Color.Transparent) return c;
            current = current.Parent;
        }
        return UiTheme.Card;
    }
}

internal sealed class PillLabel : Control
{
    public Color FillColor { get; set; } = UiTheme.Card2;
    public int Radius { get; set; } = 10;

    public PillLabel()
    {
        AutoSize = true;
        Padding = new Padding(8, 4, 8, 4);
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var textSize = TextRenderer.MeasureText(Text ?? string.Empty, Font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        return new Size(textSize.Width + Padding.Horizontal, textSize.Height + Padding.Vertical);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (AutoSize) Size = GetPreferredSize(Size.Empty);
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        if (AutoSize) Size = GetPreferredSize(Size.Empty);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var rect = ClientRectangle;
        if (rect.Width <= 1 || rect.Height <= 1) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = UiGeometry.RoundedRect(rect, Math.Min(Radius, Math.Min(rect.Width, rect.Height) / 2));
        using var brush = new SolidBrush(FillColor);
        e.Graphics.FillPath(brush, path);
        TextRenderer.DrawText(e.Graphics, Text, Font, rect, ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

internal sealed class FlatTabControl : TabControl
{
    public FlatTabControl()
    {
        DrawMode = TabDrawMode.OwnerDrawFixed;
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(112, 38);
        Font = UiTheme.UiFont(9.3f, FontStyle.Bold);
        Padding = new Point(12, 5);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= TabPages.Count) return;
        var page = TabPages[e.Index];
        var rect = GetTabRect(e.Index);
        bool selected = SelectedIndex == e.Index;

        using var bg = new SolidBrush(selected ? UiTheme.Card2 : UiTheme.Card);
        e.Graphics.FillRectangle(bg, rect);

        TextRenderer.DrawText(
            e.Graphics,
            page.Text,
            Font,
            rect,
            selected ? UiTheme.Text : UiTheme.Muted,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPadding);

        if (selected)
        {
            using var accent = new SolidBrush(UiTheme.Accent);
            e.Graphics.FillRectangle(accent, rect.Left + 10, rect.Bottom - 3, Math.Max(1, rect.Width - 20), 3);
        }
    }
}

internal sealed class SlimProgressBar : Control
{
    private int _value;
    private int _maximum = 1000;

    public int Maximum
    {
        get => _maximum;
        set
        {
            _maximum = Math.Max(1, value);
            Value = _value;
        }
    }

    public int Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0, Maximum);
            Invalidate();
        }
    }

    public SlimProgressBar()
    {
        Height = 8;
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = ClientRectangle;
        if (rect.Width <= 0 || rect.Height <= 0) return;

        int radius = Math.Max(2, rect.Height / 2);
        using var bgPath = UiGeometry.RoundedRect(rect, radius);
        using var bgBrush = new SolidBrush(Color.FromArgb(11, 17, 29));
        e.Graphics.FillPath(bgBrush, bgPath);

        double ratio = Maximum <= 0 ? 0 : (double)Value / Maximum;
        int fillWidth = (int)Math.Round(rect.Width * ratio);
        if (fillWidth > 0)
        {
            var fillRect = new Rectangle(0, 0, Math.Max(rect.Height, fillWidth), rect.Height);
            fillRect.Width = Math.Min(rect.Width, fillRect.Width);
            using var fillPath = UiGeometry.RoundedRect(fillRect, radius);
            using var fillBrush = new SolidBrush(UiTheme.Accent);
            e.Graphics.FillPath(fillBrush, fillPath);
        }
    }
}

internal sealed class BufferedDataGridView : DataGridView
{
    public BufferedDataGridView()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
    }
}

internal sealed class BufferedTreeView : TreeView
{
    public BufferedTreeView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
    }
}
