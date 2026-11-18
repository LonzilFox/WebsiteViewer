using System.Globalization;
using System.Drawing.Drawing2D;

namespace WebsiteViewer;

// Native edit control for IME, selection, copy/paste and keyboard navigation;
// custom dark increment/decrement buttons instead of the system spin control.
internal sealed class DarkNumber : UserControl
{
    private readonly TextBox _edit = new() { BorderStyle = BorderStyle.None, BackColor = UiTheme.Input, ForeColor = UiTheme.Text, TabStop = true };
    private readonly RoundedButton _minus = UiTheme.Button("−");
    private readonly RoundedButton _plus = UiTheme.Button("+");
    private decimal _value;
    public decimal Minimum { get; set; }
    public decimal Maximum { get; set; } = 100;
    public decimal Value
    {
        get => ReadValue();
        set { _value = Math.Clamp(value, Minimum, Maximum); FormatValue(); }
    }

    public DarkNumber()
    {
        BackColor = UiTheme.Input;
        ForeColor = UiTheme.Text;
        AutoScaleMode = AutoScaleMode.None;
        Margin = Padding.Empty;
        TabStop = false;
        _minus.TabStop = false;
        _plus.TabStop = false;
        _minus.AccessibleName = "减少";
        _plus.AccessibleName = "增加";
        _minus.Click += (_, _) => Value = ReadValue() - 1;
        _plus.Click += (_, _) => Value = ReadValue() + 1;
        _edit.KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Up or Keys.Down)
            {
                Value = ReadValue() + (e.KeyCode == Keys.Up ? 1 : -1);
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                Commit();
                e.SuppressKeyPress = true;
            }
        };
        _edit.Leave += (_, _) => Commit();
        DpiChangedAfterParent += (_, _) => PerformLayout();
        _edit.Text = "0";
        Controls.AddRange(new Control[] { _edit, _minus, _plus });
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    private decimal ReadValue() => decimal.TryParse(_edit.Text, NumberStyles.Integer | NumberStyles.AllowThousands,
        CultureInfo.CurrentCulture, out decimal n) ? Math.Clamp(n, Minimum, Maximum) : Math.Clamp(_value, Minimum, Maximum);
    private void Commit() { _value = ReadValue(); FormatValue(); }
    private void FormatValue() { _edit.Text = _value.ToString("N0", CultureInfo.CurrentCulture); }
    private int Px(int n) => Math.Max(1, (int)Math.Round(n * DeviceDpi / 96.0));

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        if (_edit is null) return;
        _edit.Font = Font;
        PerformLayout();
    }
    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        if (_edit is null) return;
        _edit.ReadOnly = !Enabled;
        _edit.BackColor = UiTheme.Input;
        _edit.ForeColor = Enabled ? UiTheme.Text : UiTheme.Muted;
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_edit is null || _minus is null || _plus is null) return;
        int p = Px(5), gap = Px(3), btn = Math.Min(Px(30), Math.Max(Px(18), Width / 5));
        int x = Width - p - 2 * btn - gap;
        _edit.SetBounds(Px(9), Math.Max(0, (Height - _edit.PreferredHeight) / 2),
            Math.Max(1, x - Px(13)), _edit.PreferredHeight);
        _minus.SetBounds(Math.Max(0, x), p, btn, Math.Max(1, Height - 2*p));
        _plus.SetBounds(Math.Max(0, x + btn + gap), p, btn, Math.Max(1, Height - 2*p));
    }
}

internal sealed class DarkCheckBox : CheckBox
{
    public DarkCheckBox()
    {
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
        AutoSize = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    private int Px(int n) => Math.Max(1, (int)Math.Round(n * DeviceDpi / 96.0));
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? UiTheme.Card);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int side = Px(17), x = Px(2), y = Math.Max(0, (Height-side)/2);
        var rect = new Rectangle(x, y, side, side);
        Color fill = Checked ? (Enabled ? UiTheme.Accent : UiTheme.Border) : UiTheme.Card2;
        using (var path = UiGeometry.RoundedRect(rect, Px(4)))
        using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
        if (Checked)
        {
            using var pen = new Pen(Enabled ? UiTheme.Bg : UiTheme.Muted, Px(2)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(pen, new[] { new Point(x+Px(4),y+Px(8)), new Point(x+Px(7),y+Px(11)), new Point(x+Px(13),y+Px(5)) });
        }
        int tx = x+side+Px(8);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(tx,0,Math.Max(1,Width-tx),Height),
            Enabled ? UiTheme.Text : UiTheme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        if (Focused && ShowFocusCues)
        {
            using var pen = new Pen(UiTheme.Blue);
            g.DrawLine(pen, tx, Height-2, Math.Max(tx,Width-Px(4)), Height-2);
        }
    }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
}
