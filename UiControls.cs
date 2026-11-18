using System.Drawing.Drawing2D;
using System.Collections.ObjectModel;

namespace WebsiteViewer;

internal static class UiGeometry
{
    public static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        int diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(rect.Width - 1, rect.Height - 1)));
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

        using var path = UiGeometry.RoundedRect(rect, Math.Min((int)Math.Round(Radius * DeviceDpi / 96.0), Math.Min(rect.Width, rect.Height) / 2));
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
        using var path = UiGeometry.RoundedRect(rect, Math.Min((int)Math.Round(Radius * DeviceDpi / 96.0), Math.Min(rect.Width, rect.Height) / 2));
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

        using var path = UiGeometry.RoundedRect(rect, Math.Min((int)Math.Round(Radius * DeviceDpi / 96.0), Math.Min(Width, Height) / 2));
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
        if (Focused && ShowFocusCues && Enabled)
        {
            using var pen = new Pen(UiTheme.Blue, Math.Max(1, DeviceDpi / 96f));
            pevent.Graphics.DrawLine(pen, Width / 4, Height - 4, Width * 3 / 4, Height - 4);
        }
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
        using var path = UiGeometry.RoundedRect(rect, Math.Min((int)Math.Round(Radius * DeviceDpi / 96.0), Math.Min(rect.Width, rect.Height) / 2));
        using var brush = new SolidBrush(FillColor);
        e.Graphics.FillPath(brush, path);
        TextRenderer.DrawText(e.Graphics, Text, Font, rect, ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

// 不使用原生 TabControl：其未选中标签区域与边框由 Windows 主题绘制，
// 在深色窗口中会产生显眼的浅灰背景和白色外框，OwnerDrawFixed 无法覆盖这些区域。
// 改为纯自绘标签条 + 普通 Panel 页面：所有像素均由应用深色主题绘制。
internal sealed class FlatTabControl : UserControl
{
    private readonly TabStrip _tabStrip;
    private readonly Panel _pageHost;
    private Panel? _selectedTab;

    public TabCollection TabPages { get; }
    public event EventHandler? SelectedIndexChanged;

    public Panel? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (ReferenceEquals(_selectedTab, value)) return;
            if (value is not null && !TabPages.Contains(value))
                throw new ArgumentException("The selected page must be added to TabPages.", nameof(value));
            SuspendLayout();
            _pageHost.SuspendLayout();
            try
            {
                foreach (var page in TabPages)
                    page.Visible = ReferenceEquals(page, value);
                _selectedTab = value;
                value?.BringToFront();
            }
            finally
            {
                _pageHost.ResumeLayout(true);
                ResumeLayout(true);
            }
            _tabStrip.Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public int SelectedIndex
    {
        get => _selectedTab is null ? -1 : TabPages.IndexOf(_selectedTab);
        set => SelectedTab = value >= 0 && value < TabPages.Count ? TabPages[value] : null;
    }

    public FlatTabControl()
    {
        BackColor = UiTheme.Card;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        _pageHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Card,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        _tabStrip = new TabStrip(this)
        {
            Dock = DockStyle.Top,
            Height = 42,
            BackColor = UiTheme.Card,
            Margin = Padding.Empty
        };
        Controls.Add(_pageHost);
        Controls.Add(_tabStrip);
        TabPages = new TabCollection(this);
    }

    public int NavigationHeight => _tabStrip.Height;

    public void RefreshScale()
    {
        _tabStrip.Height = Math.Max((int)Math.Round(34 * DeviceDpi / 96.0), _tabStrip.Font.Height + (int)Math.Round(10 * DeviceDpi / 96.0));
        _tabStrip.Invalidate();
    }

    public sealed class TabCollection : Collection<Panel>
    {
        private readonly FlatTabControl _owner;
        internal TabCollection(FlatTabControl owner) => _owner = owner;

        protected override void InsertItem(int index, Panel item)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (Contains(item)) throw new ArgumentException("Duplicate tab page.", nameof(item));
            base.InsertItem(index, item);
            item.Dock = DockStyle.Fill;
            item.Margin = Padding.Empty;
            item.Padding = Padding.Empty;
            item.BackColor = UiTheme.Card;
            item.Visible = false;
            _owner._pageHost.Controls.Add(item);
            if (_owner._selectedTab is null) _owner.SelectedTab = item;
            else _owner._tabStrip.Invalidate();
        }

        protected override void RemoveItem(int index)
        {
            var removed = this[index];
            _owner._pageHost.Controls.Remove(removed);
            base.RemoveItem(index);
            if (ReferenceEquals(_owner.SelectedTab, removed))
                _owner.SelectedTab = Count == 0 ? null : this[Math.Min(index, Count - 1)];
            _owner._tabStrip.Invalidate();
        }

        protected override void ClearItems()
        {
            _owner.SelectedTab = null;
            _owner._pageHost.Controls.Clear();
            base.ClearItems();
            _owner._tabStrip.Invalidate();
        }
    }

    private sealed class TabStrip : Control
    {
        private readonly FlatTabControl _owner;
        private int _hoverIndex = -1;
        private int Px(int n) => Math.Max(1, (int)Math.Round(n * DeviceDpi / 96.0));

        public TabStrip(FlatTabControl owner)
        {
            _owner = owner;
            Font = UiTheme.UiFont(9.3f, FontStyle.Bold);
            TabStop = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
        }

        private Rectangle GetTabBounds(int index)
        {
            int gap = Px(3), start = Px(8), w = Math.Min(Px(126), Math.Max(1, (Width - 2*start - 3*gap) / Math.Max(1, _owner.TabPages.Count)));
            return new Rectangle(start+index*(w+gap), Px(3), w, Math.Max(1, Height-Px(5)));
        }

        private int HitTest(Point p)
        {
            for (int i = 0; i < _owner.TabPages.Count; i++)
                if (GetTabBounds(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = HitTest(e.Location);
            if (index == _hoverIndex) return;
            _hoverIndex = index;
            Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverIndex = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int index = HitTest(e.Location);
            if (index >= 0) _owner.SelectedIndex = index;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            if (key is Keys.Left or Keys.Right) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_owner.TabPages.Count == 0) return;
            if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Left)
            {
                int d = e.KeyCode == Keys.Right ? 1 : -1;
                _owner.SelectedIndex = Math.Clamp(_owner.SelectedIndex + d, 0, _owner.TabPages.Count - 1);
                e.Handled = true;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(UiTheme.Card);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < _owner.TabPages.Count; i++)
            {
                var tab = _owner.TabPages[i];
                var rect = GetTabBounds(i);
                bool selected = i == _owner.SelectedIndex;
                Color bg = selected ? UiTheme.Card2 : i == _hoverIndex
                    ? Color.FromArgb(28, 39, 58) : UiTheme.Card;
                using (var path = UiGeometry.RoundedRect(rect, 8))
                using (var brush = new SolidBrush(bg))
                    e.Graphics.FillPath(brush, path);

                TextRenderer.DrawText(e.Graphics, tab.Text, Font, rect,
                    selected ? UiTheme.Text : UiTheme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

                if (selected)
                {
                    using var accent = new SolidBrush(UiTheme.Accent);
                    e.Graphics.FillRectangle(accent, rect.Left + Px(10), rect.Bottom - Px(3),
                        Math.Max(1, rect.Width - Px(20)), Px(3));
                }
            }
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

internal sealed class BufferedPanel : Panel
{
    public BufferedPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
    }
}
