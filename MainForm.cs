using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace WebsiteViewer;

internal sealed class MainForm : Form
{
    private readonly TextBox _urlBox = new();
    private readonly DarkNumber _maxPages = new();
    private readonly DarkNumber _maxDepth = new();
    private readonly DarkNumber _delayMs = new();
    private readonly DarkNumber _concurrency = new();
    private readonly DarkCheckBox _stripQuery = new();
    private readonly DarkCheckBox _respectRobots = new();
    private readonly DarkCheckBox _includeSubdomains = new();
    private readonly TextBox _outputBox = new();
    private readonly TextBox _searchBox = new();

    private readonly RoundedButton _startButton;
    private readonly RoundedButton _stopButton;
    private readonly RoundedButton _exportButton;
    private readonly RoundedButton _openOutputButton;
    private readonly RoundedButton _clearButton;
    private readonly RoundedButton _browseButton;
    private readonly RoundedButton _clearSearchButton;
    private readonly RoundedButton _clearTreeFilterButton;
    private readonly RoundedButton _showBranchPagesButton;

    private readonly FlatTabControl _tabs = new();
    private readonly Panel _pagesTab = new() { Text = "页面列表" };
    private readonly Panel _sectionsTab = new() { Text = "栏目统计" };
    private readonly Panel _treeTab = new() { Text = "结构树" };
    private readonly Panel _logTab = new() { Text = "运行日志" };

    private readonly BufferedTreeView _tree = new();
    private readonly BufferedDataGridView _pagesGrid = new();
    private readonly BufferedDataGridView _sectionsGrid = new();
    private readonly RichTextBox _logBox = new();
    private readonly Label _statusLabel = new();
    private readonly Label _filterLabel = new();
    private readonly Label _selectedStatusValue = new();
    private readonly TextBox _selectedParentBox = new();
    private readonly TextBox _selectedSourceBox = new();
    private readonly TextBox _selectedCanonicalBox = new();
    private readonly SlimProgressBar _progress = new();
    private readonly System.Windows.Forms.Timer _uiTimer = new();
    private readonly BufferedPanel _scrollHost = new() { AutoScroll = true, BackColor = UiTheme.Bg };
    private readonly BufferedPanel _surface = new() { BackColor = UiTheme.Bg };
    private readonly BufferedPanel _header = new() { BackColor = UiTheme.Bg };
    private readonly RoundedPanel _settings = UiTheme.CardPanel();
    private readonly BufferedPanel _statsLayout = new() { BackColor = UiTheme.Bg };
    private readonly RoundedPanel _workspace = UiTheme.CardPanel();
    private readonly BufferedPanel _statusBar = new() { BackColor = UiTheme.Bg };
    private readonly Label _title = new();
    private readonly Label _subtitle = new();
    private readonly Label _headerHint = new();
    private readonly Label _urlLabel = FieldLabel("目标网站");
    private readonly Label _outputLabel = FieldLabel("输出目录");
    private readonly Label[] _numberLabels = { FieldLabel("最多页面"), FieldLabel("最大深度"), FieldLabel("请求间隔 ms"), FieldLabel("并发") };
    private readonly Label[] _detailLabels = { FieldLabel("父级 URL"), FieldLabel("发现来源"), FieldLabel("Canonical / 错误") };
    private readonly List<(RoundedPanel Card, Label Name, Label Value)> _statCards = new();
    private readonly RoundedPanel _pageDetailCard = UiTheme.CardPanel(10);
    private Panel _urlHost = null!, _outputHost = null!, _searchHost = null!;
    private Panel[] _detailHosts = null!;
    private readonly ToolTip _toolTip = new() { ShowAlways = true, OwnerDraw = true, BackColor = UiTheme.Card2, ForeColor = UiTheme.Text };
    private Control _versionPill = null!;
    private bool _uiBuilt;
    private bool _layingOut;
    private bool _compactLayout;
    private int _gridDpi;
    private int _measureDpi;
    private readonly Dictionary<(string, Font), Size> _textMeasurements = new();
    private readonly RoundedButton _settingsToggle = UiTheme.Button("收起设置");
    private readonly RoundedButton _detailToggle = UiTheme.Button("收起详情");
    private bool _settingsExpanded = true;
    private bool _detailsExpanded = true;

    private readonly Dictionary<string, Label> _statValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TreeNode> _treeNodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SectionCounter> _sectionCounters = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CrawlPageResult> _pages = new();
    private readonly List<CrawlPageResult> _visiblePages = new();
    private readonly List<(string Source, string Target)> _edges = new();

    private SiteCrawler? _crawler;
    private CancellationTokenSource? _cts;
    private Stopwatch _runWatch = new();
    private CrawlProgressSnapshot _lastProgress = new();
    private volatile CrawlProgressSnapshot? _pendingProgress;
    private readonly ConcurrentQueue<CrawlPageResult> _pendingPages = new();
    private readonly ConcurrentQueue<string> _pendingLogs = new();
    private string _treePrefixFilter = "";
    private int _treeSyncedPageCount;
    private bool _sectionsDirty;
    private bool _filterDirty;
    private DateTime _filterDueAtUtc;
    private bool _layoutDirty;
    private bool _processingUiBatch;
    private long _lastSectionsRefreshTick;
    private long _lastElapsedRefreshTick;
    private bool _initialWindowSized;
    private bool _running;

    public MainForm()
    {
        Text = AppInfo.DisplayName;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(640, 480);
        Size = new Size(1480, 920);
        BackColor = UiTheme.Bg;
        ForeColor = UiTheme.Text;
        Font = UiTheme.UiFont();
        // Layout is measured in device pixels; do not apply WinForms autoscaling a second time.
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;

        _startButton = UiTheme.Button("开始扫描", ButtonTone.Primary);
        _stopButton = UiTheme.Button("停止扫描", ButtonTone.Danger);
        _exportButton = UiTheme.Button("导出结果", ButtonTone.Blue);
        _openOutputButton = UiTheme.Button("打开输出目录");
        _clearButton = UiTheme.Button("清空结果");
        _browseButton = UiTheme.Button("选择目录");
        _clearSearchButton = UiTheme.Button("清除筛选");
        _clearTreeFilterButton = UiTheme.Button("取消结构筛选");
        _showBranchPagesButton = UiTheme.Button("查看此分支页面", ButtonTone.Blue);

        _toolTip.Draw += (_, e) => { e.DrawBackground(); e.DrawText(TextFormatFlags.Left | TextFormatFlags.VerticalCenter); };
        BuildUi();
        WireEvents();
        SetRunning(false);
        ClearSelectedPageDetail();

        _uiTimer.Interval = 100;
        _uiTimer.Tick += (_, _) => ProcessUiBatch();
        _uiTimer.Start();
        ClientSizeChanged += (_, _) => { _layoutDirty = true; ApplyResponsiveLayout(); };
        _scrollHost.ClientSizeChanged += (_, _) => { if (!_layingOut) _layoutDirty = true; };
        DpiChanged += (_, _) =>
        {
            _gridDpi = 0;
            _layoutDirty = true;
            if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)(() => { FitMinimumSize(); ApplyResponsiveLayout(); }));
        };
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        FitMinimumSize();
        FitWindowToWorkingAreaOnce();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        NativeDarkTheme.ApplyFrame(Handle);
        ApplyResponsiveLayout();
        _scrollHost.AutoScrollPosition = Point.Empty;
    }

    private void FitWindowToWorkingAreaOnce()
    {
        if (_initialWindowSized) return;
        _initialWindowSized = true;
        Rectangle work = Screen.FromControl(this).WorkingArea;
        int frameW = Math.Max(0, Width - ClientSize.Width);
        int frameH = Math.Max(0, Height - ClientSize.Height);
        int maxW = Math.Max(1, work.Width - frameW);
        int maxH = Math.Max(1, work.Height - frameH);
        // All settings and details start expanded. Prefer fitting the complete view
        // over retaining an arbitrary initial window size or hiding controls.
        int w = Math.Min(Px(1480), maxW);
        int h = Math.Min(Px(820), maxH);
        _compactLayout = maxH < Px(880);
        if (_gridDpi != DeviceDpi) ApplyGridMetrics();
        int required = ArrangeSurface(w, 0);
        if (required > h && w < maxW)
        {
            w = maxW;
            required = ArrangeSurface(w, 0);
        }
        h = Math.Min(maxH, Math.Max(h, required));
        // A compact layout may also fit when the normal one cannot.
        if (required > maxH && !_compactLayout)
        {
            _compactLayout = true;
            required = ArrangeSurface(w, 0);
            h = Math.Min(maxH, Math.Max(h, required));
        }
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(w, h);
        Location = new Point(work.Left + Math.Max(0, (work.Width - Width) / 2),
            work.Top + Math.Max(0, (work.Height - Height) / 2));
        ApplyResponsiveLayout();
        if (_surface.Height > _scrollHost.ClientSize.Height && WindowState == FormWindowState.Normal)
        {
            // Use the remaining monitor work area before falling back to scrolling.
            WindowState = FormWindowState.Maximized;
            ApplyResponsiveLayout();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_running) _cts?.Cancel();
        _crawler?.Dispose();
        _cts?.Dispose();
        _uiTimer.Stop();
        _uiTimer.Dispose();
        _toolTip.Dispose();
        base.OnFormClosing(e);
    }

    // Keep the native Windows frame (Snap, resize, maximize and system menu).
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeDarkTheme.ApplyFrame(Handle);
    }

    private int Px(int logical) => Math.Max(1, (int)Math.Round(logical * DeviceDpi / 96.0));

    private void FitMinimumSize()
    {
        Rectangle work = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min(Px(760), work.Width), Math.Min(Px(480), work.Height));
    }

    private void BuildUi()
    {
        SuspendLayout();
        _scrollHost.Dock = DockStyle.Fill;
        Controls.Add(_scrollHost);
        _scrollHost.Controls.Add(_surface);
        _surface.Controls.AddRange(new Control[] { _header, _settings, _statsLayout, _workspace, _statusBar });

        SetLabel(_title, "WebsiteViewer", 15.5f, true, UiTheme.Text);
        _title.AutoEllipsis = false;
        _title.AutoSize = true;
        SetLabel(_subtitle, "网站结构扫描 · 页面数量、栏目、父子关系与请求 / 解析耗时", 9f, false, UiTheme.Muted);
        SetLabel(_headerHint, "双击页面可在浏览器打开", 8.5f, false, UiTheme.Muted);
        _versionPill = CreatePill("v" + AppInfo.Version, UiTheme.Card2, UiTheme.Blue);
        _header.Controls.AddRange(new Control[] { _title, _versionPill, _subtitle, _settingsToggle });
        _settingsToggle.Click += (_, _) =>
        {
            _settingsExpanded = !_settingsExpanded;
            _settingsToggle.Text = _settingsExpanded ? "收起设置" : "展开设置";
            ApplyResponsiveLayout();
        };

        _urlBox.Text = "https://www.huaweicloud.com";
        _outputBox.Text = Path.Combine(AppContext.BaseDirectory, "out");
        UiTheme.StyleTextBox(_urlBox);
        UiTheme.StyleTextBox(_outputBox);
        _urlHost = UiTheme.InputHost(_urlBox);
        _outputHost = UiTheme.InputHost(_outputBox);
        _settings.Controls.AddRange(new Control[] { _urlLabel, _urlHost, _startButton, _stopButton,
            _outputLabel, _outputHost, _browseButton, _exportButton, _openOutputButton, _clearButton });
        ConfigureNumeric(_maxPages, 10, 100000, 3000);
        ConfigureNumeric(_maxDepth, 0, 100, 10);
        ConfigureNumeric(_delayMs, 0, 10000, 400);
        ConfigureNumeric(_concurrency, 1, 16, 4);
        _maxPages.AccessibleName = "最多页面"; _maxDepth.AccessibleName = "最大深度";
        _delayMs.AccessibleName = "请求间隔 ms"; _concurrency.AccessibleName = "并发";
        _settings.Controls.AddRange(_numberLabels);
        _settings.Controls.AddRange(new Control[] { _maxPages, _maxDepth, _delayMs, _concurrency });
        ConfigureCheck(_stripQuery, "忽略 URL 参数", true);
        ConfigureCheck(_respectRobots, "遵守 robots.txt", true);
        ConfigureCheck(_includeSubdomains, "包含子域名", false);
        _settings.Controls.AddRange(new Control[] { _stripQuery, _respectRobots, _includeSubdomains });
        _urlBox.AccessibleName = "目标网站";
        _outputBox.AccessibleName = "输出目录";
        _toolTip.SetToolTip(_urlBox, _urlBox.Text);
        _toolTip.SetToolTip(_outputBox, _outputBox.Text);
        _urlBox.TextChanged += (_, _) => _toolTip.SetToolTip(_urlBox, _urlBox.Text);
        _outputBox.TextChanged += (_, _) => _toolTip.SetToolTip(_outputBox, _outputBox.Text);
        _toolTip.SetToolTip(_urlHost, "输入要扫描的网站首页 URL");
        _toolTip.SetToolTip(_outputHost, "导出结果保存目录，可编辑或选择目录");

        AddStat("已扫描", "0"); AddStat("成功 / 失败", "0 / 0");
        AddStat("已发现 / 队列", "0 / 0"); AddStat("扫描速度", "0.00 页/秒");
        AddStat("运行时间", "00:00:00"); AddStat("预计剩余", "--");
        AddStat("平均请求", "0 ms"); AddStat("平均解析", "0 ms");

        _workspace.Controls.Add(_tabs);
        _tabs.Dock = DockStyle.Fill;
        ConfigurePagesGrid();
        _pagesGrid.Dock = DockStyle.None;
        _searchBox.PlaceholderText = "筛选 URL / 标题 / 栏目 / 状态";
        UiTheme.StyleTextBox(_searchBox);
        _searchHost = UiTheme.InputHost(_searchBox);
        _searchBox.AccessibleName = "筛选页面";
        _pageDetailCard.BackColor = UiTheme.Input;
        SetLabel(_selectedStatusValue, "尚未选择页面", 9f, true, UiTheme.Muted);
        _pageDetailCard.Controls.Add(_selectedStatusValue);
        var detailBoxes = new[] { _selectedParentBox, _selectedSourceBox, _selectedCanonicalBox };
        _detailHosts = new Panel[3];
        for (int i = 0; i < 3; i++)
        {
            UiTheme.StyleTextBox(detailBoxes[i], readOnly: true);
            detailBoxes[i].AccessibleName = _detailLabels[i].Text;
            TextBox detailBox = detailBoxes[i];
            detailBox.TextChanged += (_, _) => _toolTip.SetToolTip(detailBox, detailBox.Text);
            _detailHosts[i] = UiTheme.InputHost(detailBoxes[i], readOnly: true);
            _pageDetailCard.Controls.Add(_detailLabels[i]);
            _pageDetailCard.Controls.Add(_detailHosts[i]);
        }
        _pagesTab.Controls.AddRange(new Control[] { _searchHost, _clearSearchButton, _detailToggle, _headerHint, _pagesGrid, _pageDetailCard });
        _detailToggle.Click += (_, _) =>
        {
            _detailsExpanded = !_detailsExpanded;
            _detailToggle.Text = _detailsExpanded ? "收起详情" : "展开详情";
            ApplyResponsiveLayout();
        };
        _pagesTab.ClientSizeChanged += (_, _) => LayoutPages();
        ConfigureSectionsGrid();
        _sectionsGrid.Dock = DockStyle.None;
        _sectionsTab.Controls.Add(_sectionsGrid);
        _sectionsTab.ClientSizeChanged += (_, _) => LayoutSections();
        SetLabel(_filterLabel, "当前：显示全部页面", 9.1f, false, UiTheme.Muted);
        _tree.Dock = DockStyle.None;
        _tree.BackColor = UiTheme.Input;
        _tree.ForeColor = UiTheme.Text;
        _tree.BorderStyle = BorderStyle.None;
        _tree.Font = UiTheme.UiFont(9.4f);
        _tree.HideSelection = false;
        _tree.ShowNodeToolTips = true;
        _tree.FullRowSelect = true;
        _treeTab.Controls.AddRange(new Control[] { _filterLabel, _clearTreeFilterButton, _showBranchPagesButton, _tree });
        _treeTab.ClientSizeChanged += (_, _) => LayoutTree();
        ConfigureLogBox();
        _logBox.Dock = DockStyle.None;
        _logTab.Controls.Add(_logBox);
        _logTab.ClientSizeChanged += (_, _) => LayoutLogs();
        _tabs.TabPages.Add(_pagesTab);
        _tabs.TabPages.Add(_sectionsTab);
        _tabs.TabPages.Add(_treeTab);
        _tabs.TabPages.Add(_logTab);
        _tabs.SelectedIndexChanged += (_, _) => { LayoutPages(); LayoutSections(); LayoutTree(); LayoutLogs(); };
        SetLabel(_statusLabel, "就绪 · 输入网址后点击“开始扫描”", 8.9f, false, UiTheme.Muted);
        _statusBar.Controls.AddRange(new Control[] { _statusLabel, _progress });
        _progress.Maximum = 1000;

        foreach (var control in new Control[] { _scrollHost, _tree, _pagesGrid, _sectionsGrid, _logBox })
            NativeDarkTheme.Attach(control);
        _uiBuilt = true;
        ResumeLayout(true);
    }

    private static void SetLabel(Label label, string text, float size, bool bold, Color color)
    {
        label.Text = text;
        label.Font = UiTheme.UiFont(size, bold ? FontStyle.Bold : FontStyle.Regular);
        label.ForeColor = color;
        label.BackColor = Color.Transparent;
        label.AutoSize = false;
        label.AutoEllipsis = true;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.UseMnemonic = false;
        label.Margin = Padding.Empty;
    }

    private Size Measure(string text, Font font)
    {
        if (_measureDpi != DeviceDpi) { _textMeasurements.Clear(); _measureDpi = DeviceDpi; }
        var key = (text, font);
        if (_textMeasurements.TryGetValue(key, out var size)) return size;
        using var graphics = CreateGraphics();
        size = TextRenderer.MeasureText(graphics, text, font, Size.Empty,
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        if (_textMeasurements.Count < 256) _textMeasurements[key] = size;
        return size;
    }
    private int Line(Control c)
    {
        int preferred = c is Label label ? label.PreferredHeight : 0;
        return Math.Max(Px(16), Math.Max(preferred, Math.Max(c.Font.Height, Measure("国Ag09", c.Font).Height)) + Px(_compactLayout ? 2 : 4));
    }
    private int ButtonWidth(RoundedButton b) => Measure(b.Text, b.Font).Width + Px(24);
    private int InputHeight => Math.Max(Px(_compactLayout ? 30 : 36), Math.Max(_urlBox.PreferredHeight, Line(_startButton)) + Px(_compactLayout ? 6 : 10));
    private static void Place(Control c, int x, int y, int w, int h)
    {
        var bounds = new Rectangle(x, y, Math.Max(1, w), Math.Max(1, h));
        if (c.Bounds != bounds) c.Bounds = bounds;
    }

    private void ApplyResponsiveLayout()
    {
        if (!_uiBuilt || _layingOut || IsDisposed || !IsHandleCreated || WindowState == FormWindowState.Minimized) return;
        _layingOut = true;
        _layoutDirty = false;
        _surface.SuspendLayout();
        _scrollHost.SuspendLayout();
        try
        {
            if (_gridDpi != DeviceDpi) ApplyGridMetrics();
            int barW = Math.Max(Px(18), SystemInformation.VerticalScrollBarWidth);
            int availableW = Math.Max(1, _scrollHost.ClientSize.Width + (_scrollHost.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0));
            int availableH = Math.Max(1, _scrollHost.ClientSize.Height + (_scrollHost.HorizontalScroll.Visible ? SystemInformation.HorizontalScrollBarHeight : 0));
            _compactLayout = availableH < Px(880);
            int width = availableW;
            int totalH = ArrangeSurface(width, availableH);
            if (totalH > availableH && !_compactLayout)
            {
                _compactLayout = true;
                totalH = ArrangeSurface(width, availableH);
            }
            if (totalH > availableH)
            {
                width = Math.Max(1, availableW - barW);
                totalH = ArrangeSurface(width, availableH);
            }
            // Never force the surface wider than the actual viewport. Only use
            // vertical scrolling when even the full compact layout cannot fit.
            _surface.Size = new Size(width, totalH);
            _scrollHost.AutoScrollMinSize = new Size(0, totalH);
            LayoutPages(); LayoutSections(); LayoutTree(); LayoutLogs();
        }
        finally
        {
            _surface.ResumeLayout(false);
            _scrollHost.ResumeLayout(true);
            _layingOut = false;
        }
    }

    private int ArrangeSurface(int width, int availableH)
    {
        int pad = Px(_compactLayout ? 8 : 12), gap = Px(_compactLayout ? 6 : 8), w = Math.Max(1, width - 2 * pad), y = pad;
        int titleH = Math.Max(Math.Max(Line(_title), _title.PreferredHeight), Line(_settingsToggle)+Px(6)), subtitleH = Line(_subtitle);
        int titleW = Math.Max(_title.PreferredWidth, Measure(_title.Text, _title.Font).Width) + Px(8);
        int pillW = Math.Max(_versionPill.GetPreferredSize(Size.Empty).Width, Measure(_versionPill.Text, _versionPill.Font).Width) + Px(14);
        int toggleW = ButtonWidth(_settingsToggle);
        _versionPill.AutoSize = false;
        Place(_title, 0, Math.Max(0,(titleH-_title.PreferredHeight)/2), titleW, titleH);
        Place(_versionPill, Math.Max(titleW, _title.Right)+gap, 0, pillW, titleH);
        bool wrapHeader = _versionPill.Right+toggleW+gap > w;
        int subtitleY = titleH;
        if (wrapHeader)
        {
            Place(_settingsToggle, Math.Max(0,w-toggleW), titleH+gap, toggleW, titleH);
            subtitleY += titleH+gap;
        }
        else Place(_settingsToggle, w-toggleW, 0, toggleW, titleH);
        int subtitleX = _versionPill.Right + gap;
        int subtitleW = w - toggleW - gap - subtitleX;
        bool inlineSubtitle = _compactLayout && !wrapHeader && subtitleW >= Measure(_subtitle.Text, _subtitle.Font).Width + Px(8);
        if (inlineSubtitle) Place(_subtitle, subtitleX, 0, subtitleW, titleH);
        else Place(_subtitle, 0, subtitleY, w, subtitleH);
        Place(_header, pad, y, w, inlineSubtitle ? titleH : subtitleY + subtitleH);
        y += _header.Height + gap;
        int settingsH = LayoutSettings(w);
        Place(_settings, pad, y, w, settingsH);
        y += settingsH + gap;
        int statsH = LayoutStats(w);
        Place(_statsLayout, pad, y, w, statsH);
        y += statsH + gap;
        int statusH = Line(_statusLabel) + Px(4);
        int pagePad = Px(_compactLayout ? 6 : 10);
        int pageGap = Px(_compactLayout ? 4 : 7);
        int minGrid = _pagesGrid.ColumnHeadersHeight + (_compactLayout ? 2 : 4) * _pagesGrid.RowTemplate.Height + Px(18);
        int minWorkspace = _tabs.NavigationHeight + 2 * pagePad + InputHeight + Line(_headerHint) + pageGap + minGrid
            + (_detailsExpanded ? PageDetailHeight(w - 2*pagePad) + pageGap : 0);
        int workH = Math.Max(minWorkspace, availableH - y - gap - statusH - pad);
        Place(_workspace, pad, y, w, workH);
        y += workH + gap;
        Place(_statusBar, pad, y, w, statusH);
        int progressWidth = Math.Min(Px(220), w / 4);
        Place(_statusLabel, 0, 0, w - progressWidth - gap, statusH);
        Place(_progress, w - progressWidth, (statusH - Px(7)) / 2, progressWidth, Px(7));
        return y + statusH + pad;
    }

    private int LayoutSettings(int width)
    {
        int p = Px(_compactLayout ? 8 : 12), g = Px(_compactLayout ? 5 : 8), w = Math.Max(1, width - 2*p), y = p;
        int labelH = Line(_urlLabel), inputH = InputHeight;
        int startW = ButtonWidth(_startButton), stopW = ButtonWidth(_stopButton);
        int urlLabelW = Measure(_urlLabel.Text, _urlLabel.Font).Width + Px(6);
        bool inlineUrl = _compactLayout && w >= urlLabelW+startW+stopW+3*g+Px(180);
        int urlX = inlineUrl ? p+urlLabelW+g : p;
        if (inlineUrl) Place(_urlLabel, p, y, urlLabelW, inputH);
        else { Place(_urlLabel, p, y, w, labelH); y += labelH; }
        int urlW = p+w-urlX;
        bool urlWrap = urlW < startW + stopW + 2*g + Px(160);
        Place(_urlHost, urlX, y, urlWrap ? urlW : urlW-startW-stopW-2*g, inputH);
        if (urlWrap) y += inputH + g;
        Place(_startButton, p+w-startW-stopW-g, y, startW, inputH);
        Place(_stopButton, p+w-stopW, y, stopW, inputH);
        y += inputH + g;
        var numbers = new[] { _maxPages, _maxDepth, _delayMs, _concurrency };
        // The target and start/stop remain visible even when advanced settings are collapsed.
        foreach (Control c in _numberLabels) c.Visible = _settingsExpanded;
        foreach (Control c in numbers) c.Visible = _settingsExpanded;
        foreach (Control c in new Control[] { _stripQuery, _respectRobots, _includeSubdomains, _outputLabel, _outputHost,
                _browseButton, _exportButton, _openOutputButton, _clearButton }) c.Visible = _settingsExpanded;
        if (!_settingsExpanded) return y-g+p;
        int numMin = Math.Max(_numberLabels.Max(l => Measure(l.Text, l.Font).Width + Px(15)),
            Measure("100,000",_maxPages.Font).Width+Px(78));
        int nc = w >= 4*numMin+3*g ? 4 : w >= 2*numMin+g ? 2 : 1;
        int nw = (w-(nc-1)*g)/nc;
        for (int i = 0; i < 4; i++)
        {
            int x = p + (i%nc)*(nw+g), yy = y+(i/nc)*(labelH+inputH+g);
            Place(_numberLabels[i], x, yy, nw, labelH);
            Place(numbers[i], x, yy+labelH, nw, inputH);
        }
        y += (4/nc)*(labelH+inputH+g);
        int xCheck = p, checkH = Math.Max(Px(_compactLayout ? 22 : 27), Line(_stripQuery));
        foreach (var check in new[] { _stripQuery, _respectRobots, _includeSubdomains })
        {
            int cw = Measure(check.Text, check.Font).Width + Px(30);
            if (xCheck > p && xCheck + cw > p+w) { xCheck = p; y += checkH+Px(3); }
            Place(check, xCheck, y, Math.Min(w,cw), checkH); xCheck += cw+Px(18);
        }
        y += checkH+g;
        var actions = new[] { _browseButton, _exportButton, _openOutputButton, _clearButton };
        int actionW = actions.Sum(ButtonWidth) + (actions.Length-1)*g;
        int outLabelW = Measure(_outputLabel.Text,_outputLabel.Font).Width+Px(6);
        bool inlineOutput = _compactLayout && w >= actionW+outLabelW+2*g+Px(140);
        int outX = inlineOutput ? p+outLabelW+g : p;
        if (inlineOutput) Place(_outputLabel,p,y,outLabelW,inputH);
        else { Place(_outputLabel, p, y, w, labelH); y += labelH; }
        int outW = p+w-outX;
        bool outputWrap = outW < actionW+g+Px(140);
        Place(_outputHost, outX, y, outputWrap ? outW : outW-actionW-g, inputH);
        if (outputWrap) y += inputH+g;
        int xAction = outputWrap ? p : p+w-actionW;
        foreach (var button in actions)
        {
            int bw = Math.Min(w, ButtonWidth(button));
            if (xAction > p && xAction+bw > p+w) { xAction=p; y+=inputH+g; }
            Place(button, xAction, y, bw, inputH); xAction += bw+g;
        }
        return y+inputH+p;
    }

    private int LayoutStats(int w)
    {
        int g=Px(_compactLayout ? 5 : 8), p=Px(_compactLayout ? 5 : 9);
        int labelH = Line(_statCards[0].Name), valueH = Line(_statCards[0].Value);
        // Compute the actual per-card widths rather than reserving the largest count
        // width for every card. On short screens use a one-line label/value pair
        // when all cards can accommodate their full contents.
        string[] worstValues = { "100,000", "100,000 / 100,000", "100,000 / 100,000", "999.99 页/秒", "99d 00:00:00", "99d 00:00:00", "99999 ms", "99999 ms" };
        int minW = 0, inlineMinW = 0;
        for (int i=0;i<_statCards.Count;i++)
        {
            var card = _statCards[i];
            int nameW=Measure(card.Name.Text,card.Name.Font).Width, valueW=Measure(worstValues[i],card.Value.Font).Width;
            minW = Math.Max(minW, Math.Max(nameW,valueW)+2*p+Px(6));
            inlineMinW = Math.Max(inlineMinW, nameW+valueW+2*p+Px(10));
        }
        int cols = w >= 8*minW+7*g ? 8 : w >= 4*minW+3*g ? 4 : w >= 2*minW+g ? 2 : 1;
        int cw = (w-(cols-1)*g)/cols;
        bool inline = _compactLayout && cw >= inlineMinW;
        int h = 2*p+(inline ? Math.Max(labelH,valueH) : labelH+valueH);
        for (int i=0;i<8;i++)
        {
            var item=_statCards[i];
            Place(item.Card,(i%cols)*(cw+g),(i/cols)*(h+g),cw,h);
            if (inline)
            {
                int nameW = Measure(item.Name.Text,item.Name.Font).Width+Px(5);
                Place(item.Name,p,p,nameW,h-2*p);
                item.Value.TextAlign = ContentAlignment.MiddleRight;
                Place(item.Value,p+nameW+Px(5),p,cw-2*p-nameW-Px(5),h-2*p);
            }
            else
            {
                item.Value.TextAlign = ContentAlignment.MiddleLeft;
                Place(item.Name,p,p,cw-2*p,labelH);
                Place(item.Value,p,p+labelH,cw-2*p,valueH);
            }
        }
        return (8/cols)*(h+g)-g;
    }

    private int PageDetailHeight(int width)
    {
        int p=Px(_compactLayout ? 6 : 10), g=Px(_compactLayout ? 3 : 6);
        int rows = width >= Px(520) ? 1 : 3;
        return 2*p+Line(_selectedStatusValue)+g+rows*(Line(_detailLabels[0])+InputHeight)+(rows-1)*g;
    }

    private void LayoutPages()
    {
        if (!_uiBuilt) return;
        int p=Px(_compactLayout ? 6 : 10), g=Px(_compactLayout ? 4 : 7), w=_pagesTab.ClientSize.Width-2*p, h=_pagesTab.ClientSize.Height-2*p;
        if (w<=0 || h<=0) return;
        int buttonW = ButtonWidth(_clearSearchButton), toggleW=ButtonWidth(_detailToggle), inputH=InputHeight, hintH=Line(_headerHint);
        Place(_searchHost,p,p,w-buttonW-toggleW-2*g,inputH);
        Place(_clearSearchButton,p+w-buttonW-toggleW-g,p,buttonW,inputH);
        Place(_detailToggle,p+w-toggleW,p,toggleW,inputH);
        Place(_headerHint,p,p+inputH,w,hintH);
        int gridY=p+inputH+hintH+g;
        _pageDetailCard.Visible = _detailsExpanded;
        if (!_detailsExpanded)
        {
            Place(_pagesGrid,p,gridY,w,p+h-gridY);
            return;
        }
        int detailH=PageDetailHeight(w), detailY=Math.Max(gridY+_pagesGrid.ColumnHeadersHeight+g,p+h-detailH);
        Place(_pagesGrid,p,gridY,w,detailY-gridY-g);
        Place(_pageDetailCard,p,detailY,w,detailH);
        int dp=Px(_compactLayout ? 6 : 10), dg=Px(_compactLayout ? 3 : 6), dw=w-2*dp, dy=dp+Line(_selectedStatusValue)+dg;
        Place(_selectedStatusValue,dp,dp,dw,Line(_selectedStatusValue));
        int cols=w>=Px(520)?3:1, fieldW=(dw-(cols-1)*dg)/cols, lh=Line(_detailLabels[0]);
        for (int i=0;i<3;i++)
        {
            int xx=dp+(i%cols)*(fieldW+dg), yy=dy+(i/cols)*(lh+inputH+dg);
            Place(_detailLabels[i],xx,yy,fieldW,lh);
            Place(_detailHosts[i],xx,yy+lh,fieldW,inputH);
        }
    }

    private void LayoutSections()
    {
        if (!_uiBuilt) return;
        int p=Px(10);
        Place(_sectionsGrid,p,p,_sectionsTab.ClientSize.Width-2*p,_sectionsTab.ClientSize.Height-2*p);
    }
    private void LayoutTree()
    {
        if (!_uiBuilt) return;
        int p=Px(10), g=Px(6), w=_treeTab.ClientSize.Width-2*p, h=_treeTab.ClientSize.Height-2*p;
        int y=p, line=Line(_filterLabel), bh=InputHeight;
        Place(_filterLabel,p,y,w,line); y+=line+g;
        int b1=ButtonWidth(_clearTreeFilterButton), b2=ButtonWidth(_showBranchPagesButton);
        Place(_clearTreeFilterButton,p,y,b1,bh);
        Place(_showBranchPagesButton,p+b1+g,y,b2,bh); y+=bh+g;
        Place(_tree,p,y,w,p+h-y);
    }
    private void LayoutLogs()
    {
        if (!_uiBuilt) return;
        int p=Px(10);
        Place(_logBox,p,p,_logTab.ClientSize.Width-2*p,_logTab.ClientSize.Height-2*p);
    }

    private void ApplyGridMetrics()
    {
        _gridDpi=DeviceDpi;
        foreach (var grid in new[] { _pagesGrid, _sectionsGrid })
        {
            var headerFont=grid.ColumnHeadersDefaultCellStyle.Font ?? Font;
            var cellFont=grid.DefaultCellStyle.Font ?? Font;
            grid.ColumnHeadersHeight=Math.Max(Px(30),Measure("国Ag",headerFont).Height+Px(10));
            grid.RowTemplate.Height=Math.Max(Px(27),Measure("国Ag",cellFont).Height+Px(8));
            // Shared virtual rows must not be unshared just to change their height.
            grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.None;
            if (grid.VirtualMode && grid.RowCount > 0) grid.UpdateRowHeightInfo(0, true);
            grid.ColumnHeadersDefaultCellStyle.Padding=new Padding(Px(4),0,Px(4),0);
            grid.DefaultCellStyle.Padding=new Padding(Px(4),0,Px(4),0);
            foreach (DataGridViewColumn c in grid.Columns)
            {
                int min=Measure(c.HeaderText,headerFont).Width+Px(14);
                c.MinimumWidth=min;
                if (c.AutoSizeMode==DataGridViewAutoSizeColumnMode.Fill)
                    c.MinimumWidth=Math.Max(min,Px(c.Name=="Url"?135:86));
                else
                    c.Width=Math.Max(min,Px(c.Name switch { "Index"=>35,"Status"=>54,"Depth"=>48,"Section"=>76,"Fetch" or "Parse"=>80,_=>82 }));
            }
        }
        _tree.ItemHeight=Math.Max(Px(26),Line(_tree)+Px(5));
        _tree.Indent=Px(22);
        _tabs.RefreshScale();
    }

    private void ConfigurePagesGrid()
    {
        UiTheme.StyleGrid(_pagesGrid);
        _pagesGrid.VirtualMode = true;
        _pagesGrid.RowHeightInfoNeeded += (_, e) => { e.MinimumHeight = 2; e.Height = _pagesGrid.RowTemplate.Height; };
        _pagesGrid.RowCount = 0;
        _pagesGrid.Columns.Add(Column("Index", "#", 50));
        var statusColumn = Column("Status", "状态", 60);
        statusColumn.DefaultCellStyle.Font = UiTheme.UiFont(9f, FontStyle.Bold);
        _pagesGrid.Columns.Add(statusColumn);
        _pagesGrid.Columns.Add(Column("Depth", "深度", 54));
        _pagesGrid.Columns.Add(Column("Section", "栏目", 112));
        _pagesGrid.Columns.Add(Column("Fetch", "请求耗时", 84));
        _pagesGrid.Columns.Add(Column("Parse", "解析耗时", 84));

        var title = Column("Title", "标题", 300);
        title.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        title.MinimumWidth = 160;
        title.FillWeight = 42;
        _pagesGrid.Columns.Add(title);

        var url = Column("Url", "URL", 470);
        url.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        url.MinimumWidth = 260;
        url.FillWeight = 58;
        _pagesGrid.Columns.Add(url);

        _pagesGrid.CellValueNeeded += (_, e) => SupplyPageGridValue(e);
        _pagesGrid.CellDoubleClick += (_, e) => OpenSelectedPage(e.RowIndex);
        _pagesGrid.SelectionChanged += (_, _) => UpdateSelectedPageDetail();
        _pagesGrid.CellFormatting += (_, e) => FormatPageGridCell(e);
    }

    private void ConfigureSectionsGrid()
    {
        UiTheme.StyleGrid(_sectionsGrid);
        var section = Column("Section", "栏目", 320);
        section.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        section.MinimumWidth = 220;
        _sectionsGrid.Columns.Add(section);
        _sectionsGrid.Columns.Add(Column("Pages", "页面数", 120));
        _sectionsGrid.Columns.Add(Column("Success", "成功", 110));
        _sectionsGrid.Columns.Add(Column("Failed", "失败", 110));
        _sectionsGrid.Columns.Add(Column("Percent", "占比", 110));
    }

    private void ConfigureLogBox()
    {
        _logBox.Dock = DockStyle.Fill;
        _logBox.ReadOnly = true;
        _logBox.BorderStyle = BorderStyle.None;
        _logBox.BackColor = UiTheme.Surface;
        _logBox.ForeColor = Color.FromArgb(200, 211, 229);
        _logBox.Font = UiTheme.MonoFont(9.2f);
        _logBox.DetectUrls = false;
        _logBox.WordWrap = false;
    }

    private void WireEvents()
    {
        _startButton.Click += async (_, _) => await StartCrawlAsync();
        _stopButton.Click += (_, _) => StopCrawl();
        _exportButton.Click += (_, _) => ExportCurrent();
        _openOutputButton.Click += (_, _) => OpenOutputDirectory();
        _clearButton.Click += (_, _) => ClearResults();
        _browseButton.Click += (_, _) => ChooseOutputDirectory();
        _clearSearchButton.Click += (_, _) => _searchBox.Clear();
        _clearTreeFilterButton.Click += (_, _) => ClearTreeFilter();
        _showBranchPagesButton.Click += (_, _) => _tabs.SelectedTab = _pagesTab;
        _searchBox.TextChanged += (_, _) =>
        {
            _filterDirty = true;
            _filterDueAtUtc = DateTime.UtcNow.AddMilliseconds(250);
        };
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            if (_tabs.SelectedTab == _sectionsTab) _sectionsDirty = true;
        };
        _tree.AfterSelect += (_, e) =>
        {
            if (e.Node is not null) ApplyTreeFilter(e.Node);
        };
        _tree.NodeMouseDoubleClick += (_, e) => OpenTreeNode(e.Node);
        KeyDown += MainForm_KeyDown;
    }

    private void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F5 && !_running)
        {
            e.Handled = true;
            _ = StartCrawlAsync();
        }
        else if (e.KeyCode == Keys.Escape && _running)
        {
            e.Handled = true;
            StopCrawl();
        }
    }

    private async Task StartCrawlAsync()
    {
        if (_running) return;
        if (!TryGetStartUri(out var uri)) return;

        ClearResults();
        string outDir = string.IsNullOrWhiteSpace(_outputBox.Text)
            ? Path.Combine(AppContext.BaseDirectory, "out")
            : _outputBox.Text.Trim();

        var options = new CrawlOptions
        {
            StartUri = uri,
            MaxPages = (int)_maxPages.Value,
            MaxDepth = (int)_maxDepth.Value,
            DelayMs = (int)_delayMs.Value,
            Concurrency = (int)_concurrency.Value,
            StripQuery = _stripQuery.Checked,
            RespectRobots = _respectRobots.Checked,
            IncludeSubdomains = _includeSubdomains.Checked,
            OutputDirectory = outDir
        };

        _cts = new CancellationTokenSource();
        _crawler = new SiteCrawler();
        _crawler.PageCompleted += CrawlerOnPageCompleted;
        _crawler.ProgressChanged += CrawlerOnProgressChanged;
        _crawler.Log += CrawlerOnLog;
        _runWatch = Stopwatch.StartNew();
        SetRunning(true);
        AddLog($"[{DateTime.Now:HH:mm:ss}] START {uri.AbsoluteUri}");
        _tabs.SelectedTab = _pagesTab;

        try
        {
            var summary = await _crawler.CrawlAsync(options, _cts.Token);
            FlushPendingUi(forceAll: true);
            _edges.Clear();
            _edges.AddRange(summary.Edges);
            ExportService.ExportAll(new CrawlSummary
            {
                Pages = _pages.ToList(),
                Edges = _edges.ToList(),
                Elapsed = summary.Elapsed,
                Cancelled = summary.Cancelled
            }, outDir);

            _statusLabel.Text = summary.Cancelled
                ? $"已停止 · 保留 {_pages.Count:N0} 页结果 · 已导出"
                : $"扫描完成 · 共 {_pages.Count:N0} 页 · 已导出到 {outDir}";
            AddLog($"[{DateTime.Now:HH:mm:ss}] {(summary.Cancelled ? "STOP" : "DONE")} pages={_pages.Count:N0} elapsed={FormatDuration(summary.Elapsed.TotalSeconds)}");
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = $"已停止 · 保留 {_pages.Count:N0} 页结果";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "运行失败 · " + ex.Message;
            AddLog($"[{DateTime.Now:HH:mm:ss}] ERROR {ex}");
            MessageBox.Show(this, ex.Message, "扫描失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            FlushPendingUi(forceAll: true);
            _runWatch.Stop();
            SetRunning(false);
            if (_crawler is not null)
            {
                _crawler.PageCompleted -= CrawlerOnPageCompleted;
                _crawler.ProgressChanged -= CrawlerOnProgressChanged;
                _crawler.Log -= CrawlerOnLog;
                _crawler.Dispose();
                _crawler = null;
            }
            _cts?.Dispose();
            _cts = null;
            UpdateSectionsGrid();
            RebuildPagesGrid();
        }
    }

    private void StopCrawl()
    {
        if (!_running) return;
        _statusLabel.Text = "正在停止 · 已扫描结果会保留";
        _stopButton.Enabled = false;
        _cts?.Cancel();
    }

    private void CrawlerOnPageCompleted(CrawlPageResult page)
    {
        // Worker 线程只入队，不再为每一页 BeginInvoke 一次。
        // 以前数千个 UI 消息会挤爆 WinForms 消息泵，是扫描时卡顿的主要来源。
        _pendingPages.Enqueue(page);
        string status = page.StatusCode == 0 ? "ERR" : page.StatusCode.ToString();
        string marker = page.StatusCode is >= 200 and < 400 && string.IsNullOrEmpty(page.Error) ? "OK" : "!!";
        _pendingLogs.Enqueue($"[{DateTime.Now:HH:mm:ss}] {marker} #{page.Index,5} {status,3} fetch={page.FetchMs,7:F0}ms parse={page.ParseMs,6:F0}ms  {page.Url}");
    }

    private void CrawlerOnProgressChanged(CrawlProgressSnapshot progress)
    {
        // 只保留最新快照。UI 线程每 100ms 读取一次，避免每页一次 BeginInvoke。
        _pendingProgress = progress;
    }

    private void CrawlerOnLog(string message)
    {
        _pendingLogs.Enqueue($"[{DateTime.Now:HH:mm:ss}] {message}");
    }

    private void ProcessUiBatch()
    {
        if (_processingUiBatch || IsDisposed) return;
        _processingUiBatch = true;
        try
        {
            FlushPendingUi(forceAll: false);

            if (_layoutDirty) ApplyResponsiveLayout();

            if (_filterDirty && DateTime.UtcNow >= _filterDueAtUtc)
            {
                _filterDirty = false;
                RebuildPagesGrid();
            }

            long now = Environment.TickCount64;
            if (_running && now - _lastElapsedRefreshTick >= 900)
            {
                _lastElapsedRefreshTick = now;
                UpdateElapsedDisplay();
            }

            if (_sectionsDirty && _tabs.SelectedTab == _sectionsTab && now - _lastSectionsRefreshTick >= 450)
            {
                _lastSectionsRefreshTick = now;
                _sectionsDirty = false;
                UpdateSectionsGrid();
            }

            if (_tabs.SelectedTab == _treeTab)
                SyncTreeIncremental(maxPages: 80);
        }
        finally
        {
            _processingUiBatch = false;
        }
    }

    private void FlushPendingUi(bool forceAll)
    {
        int maxPages = forceAll ? int.MaxValue : 48;
        long budgetMs = forceAll ? long.MaxValue : 14;
        var watch = Stopwatch.StartNew();
        int processed = 0;

        int visibleAdded = 0;
        _pagesGrid.SuspendLayout();
        try
        {
            while (processed < maxPages && watch.ElapsedMilliseconds <= budgetMs && _pendingPages.TryDequeue(out var page))
            {
                _pages.Add(page);
                if (!string.IsNullOrWhiteSpace(page.DiscoveredFrom))
                    _edges.Add((page.DiscoveredFrom, page.Url));
                UpdateSectionCounter(page);
                _sectionsDirty = true;

                if (MatchesCurrentFilter(page))
                {
                    _visiblePages.Add(page);
                    visibleAdded++;
                }
                processed++;
            }
            if (visibleAdded > 0)
                _pagesGrid.RowCount = _visiblePages.Count;
        }
        finally
        {
            _pagesGrid.ResumeLayout(false);
        }

        var progress = _pendingProgress;
        if (progress is not null)
        {
            _pendingProgress = null;
            _lastProgress = progress;
            UpdateStats(progress);
        }

        FlushLogQueue(forceAll ? int.MaxValue : 160);

        if (forceAll)
        {
            _sectionsDirty = false;
            UpdateSectionsGrid();
            if (_tabs.SelectedTab == _treeTab)
                SyncTreeIncremental(maxPages: 160);
        }
    }

    private void FlushLogQueue(int maxLines)
    {
        if (_pendingLogs.IsEmpty) return;
        var sb = new StringBuilder(4096);
        int count = 0;
        while (count < maxLines && _pendingLogs.TryDequeue(out var line))
        {
            sb.AppendLine(line);
            count++;
        }
        if (sb.Length == 0) return;

        _logBox.AppendText(sb.ToString());
        if (_logBox.TextLength > 1_000_000)
        {
            _logBox.Select(0, Math.Min(200_000, _logBox.TextLength));
            if (_logBox.SelectionLength > 0) _logBox.SelectedText = "";
        }
        if (_tabs.SelectedTab == _logTab)
        {
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();
        }
    }

    private void SyncTreeIncremental(int maxPages)
    {
        if (_treeSyncedPageCount >= _pages.Count) return;
        int end = Math.Min(_pages.Count, _treeSyncedPageCount + Math.Max(1, maxPages));
        _tree.BeginUpdate();
        try
        {
            for (int i = _treeSyncedPageCount; i < end; i++)
                AddPageToTree(_pages[i]);
            _treeSyncedPageCount = end;
        }
        finally
        {
            _tree.EndUpdate();
        }
    }

    private void UpdateStats(CrawlProgressSnapshot p)
    {
        SetStat("已扫描", p.Crawled.ToString("N0"));
        SetStat("成功 / 失败", $"{p.Success:N0} / {p.Failed:N0}");
        SetStat("已发现 / 队列", $"{p.Discovered:N0} / {p.Queued:N0}");
        SetStat("扫描速度", $"{p.PagesPerSecond:F2} 页/秒");
        SetStat("运行时间", FormatDuration(p.ElapsedSeconds));
        SetStat("预计剩余", p.EtaSeconds <= 0 ? "--" : FormatDuration(p.EtaSeconds));
        SetStat("平均请求", $"{p.AverageFetchMs:F0} ms");
        SetStat("平均解析", $"{p.AverageParseMs:F0} ms");
        _statusLabel.Text = $"扫描中 · 已完成 {p.Crawled:N0} · 已发现 {p.Discovered:N0} · {p.PagesPerSecond:F2} 页/秒";
        int max = Math.Max(1, (int)_maxPages.Value);
        _progress.Value = Math.Clamp((int)(p.Crawled * 1000.0 / max), 0, 1000);
    }

    private void UpdateElapsedDisplay()
    {
        if (!_running) return;
        SetStat("运行时间", FormatDuration(_runWatch.Elapsed.TotalSeconds));
    }

    private void UpdateSectionCounter(CrawlPageResult page)
    {
        if (!_sectionCounters.TryGetValue(page.Section, out var counter))
        {
            counter = new SectionCounter { Section = page.Section };
            _sectionCounters[page.Section] = counter;
        }
        counter.Total++;
        if (page.StatusCode is >= 200 and < 400 && string.IsNullOrEmpty(page.Error)) counter.Success++;
        else counter.Failed++;
    }

    private void UpdateSectionsGrid()
    {
        int total = Math.Max(1, _pages.Count);
        _sectionsGrid.SuspendLayout();
        _sectionsGrid.Rows.Clear();
        foreach (var c in _sectionCounters.Values.OrderByDescending(x => x.Total).ThenBy(x => x.Section))
        {
            int rowIndex = _sectionsGrid.Rows.Add(
                c.Section,
                c.Total.ToString("N0"),
                c.Success.ToString("N0"),
                c.Failed.ToString("N0"),
                $"{c.Total * 100.0 / total:F1}%");
            if (c.Failed > 0)
                _sectionsGrid.Rows[rowIndex].Cells["Failed"].Style.ForeColor = UiTheme.Bad;
        }
        _sectionsGrid.ResumeLayout();
    }

    private void AddPageToTree(CrawlPageResult page)
    {
        if (!Uri.TryCreate(page.Url, UriKind.Absolute, out var uri)) return;
        string hostKey = uri.GetLeftPart(UriPartial.Authority);
        TreeNode current;
        if (!_treeNodes.TryGetValue(hostKey, out var rootNode))
        {
            current = new TreeNode(uri.Host)
            {
                Name = hostKey,
                Tag = hostKey,
                ToolTipText = hostKey
            };
            _tree.Nodes.Add(current);
            _treeNodes[hostKey] = current;
            current.Expand();
        }
        else
        {
            current = rootNode;
        }

        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string currentPath = "";
        foreach (string encodedPart in parts)
        {
            currentPath += "/" + encodedPart;
            string key = hostKey + currentPath;
            if (!_treeNodes.TryGetValue(key, out var next))
            {
                string display = Uri.UnescapeDataString(encodedPart);
                if (display.Length > 64) display = display[..61] + "…";
                next = new TreeNode(display)
                {
                    Name = key,
                    Tag = key,
                    ToolTipText = key
                };
                current.Nodes.Add(next);
                _treeNodes[key] = next;
            }
            current = next;
        }

        current.Tag = page.Url;
        current.ToolTipText = string.IsNullOrWhiteSpace(page.Title)
            ? page.Url
            : page.Title + Environment.NewLine + page.Url;
        if (parts.Length == 0) current.Tag = hostKey;
    }

    private void ApplyTreeFilter(TreeNode node)
    {
        _treePrefixFilter = node.Tag as string ?? "";
        _filterLabel.Text = string.IsNullOrWhiteSpace(_treePrefixFilter)
            ? "当前：显示全部页面"
            : "当前分支：" + _treePrefixFilter;
        RebuildPagesGrid();
    }

    private void ClearTreeFilter()
    {
        _treePrefixFilter = "";
        _filterLabel.Text = "当前：显示全部页面";
        _tree.SelectedNode = null;
        RebuildPagesGrid();
    }

    private bool MatchesCurrentFilter(CrawlPageResult page)
    {
        if (!string.IsNullOrWhiteSpace(_treePrefixFilter) &&
            !page.Url.StartsWith(_treePrefixFilter, StringComparison.OrdinalIgnoreCase))
            return false;

        string query = _searchBox.Text.Trim();
        if (query.Length == 0) return true;
        return page.Url.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               page.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               page.Section.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void RebuildPagesGrid()
    {
        int selectedIndex = _pagesGrid.SelectedRows.Count > 0 ? _pagesGrid.SelectedRows[0].Index : -1;
        _visiblePages.Clear();
        _visiblePages.AddRange(_pages.Where(MatchesCurrentFilter));

        _pagesGrid.SuspendLayout();
        try
        {
            _pagesGrid.RowCount = _visiblePages.Count;
            if (selectedIndex >= 0 && selectedIndex < _visiblePages.Count)
                _pagesGrid.CurrentCell = _pagesGrid.Rows[selectedIndex].Cells[0];
            else if (_visiblePages.Count == 0)
                _pagesGrid.ClearSelection();
            _pagesGrid.Invalidate();
        }
        finally
        {
            _pagesGrid.ResumeLayout(false);
        }
        UpdateSelectedPageDetail();
    }

    private void SupplyPageGridValue(DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _visiblePages.Count || e.ColumnIndex < 0 || e.ColumnIndex >= _pagesGrid.Columns.Count)
            return;

        var p = _visiblePages[e.RowIndex];
        e.Value = _pagesGrid.Columns[e.ColumnIndex].Name switch
        {
            "Index" => p.Index,
            "Status" => p.StatusCode == 0 ? "ERR" : p.StatusCode,
            "Depth" => p.Depth,
            "Section" => p.Section,
            "Fetch" => $"{p.FetchMs:F0} ms",
            "Parse" => $"{p.ParseMs:F0} ms",
            "Title" => p.Title,
            "Url" => p.Url,
            _ => null
        };
    }

    private void FormatPageGridCell(DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _visiblePages.Count) return;
        var page = _visiblePages[e.RowIndex];

        string column = _pagesGrid.Columns[e.ColumnIndex].Name;
        var cellStyle = e.CellStyle;
        if (cellStyle is null) return;

        if (column == "Status")
        {
            bool ok = page.StatusCode is >= 200 and < 400 && string.IsNullOrEmpty(page.Error);
            cellStyle.ForeColor = ok ? UiTheme.Good : UiTheme.Bad;
        }
        else if (column == "Fetch" && page.FetchMs >= 2000)
        {
            cellStyle.ForeColor = UiTheme.Warn;
        }
        else if (column == "Parse" && page.ParseMs >= 500)
        {
            cellStyle.ForeColor = UiTheme.Warn;
        }
    }

    private void UpdateSelectedPageDetail()
    {
        if (_pagesGrid.SelectedRows.Count == 0)
        {
            ClearSelectedPageDetail();
            return;
        }
        int rowIndex = _pagesGrid.SelectedRows[0].Index;
        if (rowIndex < 0 || rowIndex >= _visiblePages.Count)
        {
            ClearSelectedPageDetail();
            return;
        }
        var page = _visiblePages[rowIndex];

        string status = page.StatusCode == 0 ? "ERR" : page.StatusCode.ToString();
        _selectedStatusValue.Text = $"#{page.Index} · {status} · 深度 {page.Depth}";
        _selectedStatusValue.ForeColor = page.StatusCode is >= 200 and < 400 && string.IsNullOrEmpty(page.Error)
            ? UiTheme.Good
            : UiTheme.Bad;
        _selectedParentBox.Text = page.ParentUrl;
        _selectedSourceBox.Text = page.DiscoveredFrom;
        _selectedCanonicalBox.Text = string.IsNullOrWhiteSpace(page.Error) ? page.Canonical : "ERROR: " + page.Error;
    }

    private void ClearSelectedPageDetail()
    {
        _selectedStatusValue.Text = "尚未选择页面";
        _selectedStatusValue.ForeColor = UiTheme.Muted;
        _selectedParentBox.Text = "";
        _selectedSourceBox.Text = "";
        _selectedCanonicalBox.Text = "";
    }

    private void OpenSelectedPage(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _visiblePages.Count) return;
        OpenUrl(_visiblePages[rowIndex].Url);
    }

    private void OpenTreeNode(TreeNode? node)
    {
        string? target = node?.Tag as string;
        if (string.IsNullOrWhiteSpace(target)) return;
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            OpenUrl(target);
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }

    private void ExportCurrent()
    {
        if (_pages.Count == 0)
        {
            MessageBox.Show(this, "当前还没有可导出的页面。", "WebsiteViewer", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            string outDir = EnsureOutputDirectory();
            var summary = new CrawlSummary
            {
                Pages = _pages.ToList(),
                Edges = _edges.ToList(),
                Elapsed = _runWatch.Elapsed,
                Cancelled = _running
            };
            ExportService.ExportAll(summary, outDir);
            _statusLabel.Text = $"已导出 {_pages.Count:N0} 页结果 · {outDir}";
            AddLog($"[{DateTime.Now:HH:mm:ss}] EXPORT pages={_pages.Count:N0} -> {outDir}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenOutputDirectory()
    {
        try
        {
            string dir = EnsureOutputDirectory();
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "无法打开目录", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private string EnsureOutputDirectory()
    {
        string dir = string.IsNullOrWhiteSpace(_outputBox.Text)
            ? Path.Combine(AppContext.BaseDirectory, "out")
            : _outputBox.Text.Trim();
        Directory.CreateDirectory(dir);
        _outputBox.Text = dir;
        return dir;
    }

    private void ChooseOutputDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择扫描结果输出目录",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_outputBox.Text) ? _outputBox.Text : AppContext.BaseDirectory
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) _outputBox.Text = dialog.SelectedPath;
    }

    private bool TryGetStartUri(out Uri uri)
    {
        string input = _urlBox.Text.Trim();
        if (!input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            input = "https://" + input;

        if (Uri.TryCreate(input, UriKind.Absolute, out var parsed) &&
            (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
        {
            uri = parsed;
            _urlBox.Text = uri.AbsoluteUri;
            return true;
        }

        uri = null!;
        MessageBox.Show(this, "请输入有效的网址，例如 https://www.example.com", "网址无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    private void ClearResults()
    {
        if (_running) return;
        _pages.Clear();
        _visiblePages.Clear();
        _edges.Clear();
        _sectionCounters.Clear();
        _pendingPages.Clear();
        _pendingLogs.Clear();
        _pendingProgress = null;
        _treeNodes.Clear();
        _tree.Nodes.Clear();
        _treeSyncedPageCount = 0;
        _sectionsDirty = false;
        _filterDirty = false;
        _pagesGrid.RowCount = 0;
        _sectionsGrid.Rows.Clear();
        _logBox.Clear();
        _treePrefixFilter = "";
        _filterLabel.Text = "当前：显示全部页面";
        _lastProgress = new CrawlProgressSnapshot();
        _progress.Value = 0;
        SetStat("已扫描", "0");
        SetStat("成功 / 失败", "0 / 0");
        SetStat("已发现 / 队列", "0 / 0");
        SetStat("扫描速度", "0.00 页/秒");
        SetStat("运行时间", "00:00:00");
        SetStat("预计剩余", "--");
        SetStat("平均请求", "0 ms");
        SetStat("平均解析", "0 ms");
        ClearSelectedPageDetail();
        _statusLabel.Text = "就绪 · 输入网址后点击“开始扫描”";
    }

    private void SetRunning(bool running)
    {
        _running = running;
        _startButton.Enabled = !running;
        _stopButton.Enabled = running;
        _clearButton.Enabled = !running;
        _browseButton.Enabled = !running;
        _urlBox.Enabled = !running;
        _outputBox.Enabled = !running;
        _maxPages.Enabled = !running;
        _maxDepth.Enabled = !running;
        _delayMs.Enabled = !running;
        _concurrency.Enabled = !running;
        _stripQuery.Enabled = !running;
        _respectRobots.Enabled = !running;
        _includeSubdomains.Enabled = !running;
        _exportButton.Enabled = _pages.Count > 0 || running;
    }

    private void AddLog(string line)
    {
        _logBox.AppendText(line + Environment.NewLine);
        if (_logBox.TextLength > 1_000_000)
        {
            _logBox.Select(0, Math.Min(200_000, _logBox.TextLength));
            if (_logBox.SelectionLength > 0) _logBox.SelectedText = "";
        }
        if (_tabs.SelectedTab == _logTab)
        {
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();
        }
    }

    private static string FormatDuration(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) return "--";
        var t = TimeSpan.FromSeconds(seconds);
        if (t.TotalDays >= 1) return $"{(int)t.TotalDays}d {t:hh\\:mm\\:ss}";
        return t.ToString(@"hh\:mm\:ss");
    }

    private void AddStat(string name, string initial)
    {
        var card=UiTheme.CardPanel(12);
        var label=new Label(); var value=new Label();
        SetLabel(label,name,8.8f,false,UiTheme.Muted);
        SetLabel(value,initial,11.4f,true,UiTheme.Text);
        card.Controls.AddRange(new Control[] { label,value });
        _statValues[name]=value;
        _statCards.Add((card,label,value));
        _statsLayout.Controls.Add(card);
        _toolTip.SetToolTip(value, initial);
    }

    private void SetStat(string name, string value)
    {
        if (_statValues.TryGetValue(name, out var label) && label.Text != value)
        { label.Text = value; _toolTip.SetToolTip(label, value); }
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.None,
        ForeColor = UiTheme.Muted,
        Font = UiTheme.UiFont(8.3f),
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true,
        Margin = new Padding(0)
    };

    private static Control CreatePill(string text, Color back, Color fore)
    {
        return new PillLabel
        {
            Text = text,
            FillColor = back,
            ForeColor = fore,
            Font = UiTheme.UiFont(8.5f, FontStyle.Bold),
            Padding = new Padding(8, 4, 8, 4),
            Margin = new Padding(0, 5, 8, 0)
        };
    }

    private static void ConfigureNumeric(DarkNumber box, decimal min, decimal max, decimal value)
    {
        box.Minimum = min;
        box.Maximum = max;
        box.Value = value;
        box.Font = UiTheme.UiFont(9.5f);
    }

    private static void ConfigureCheck(CheckBox box, string text, bool value)
    {
        box.Text = text;
        box.Checked = value;
        UiTheme.StyleCheckBox(box);
        box.AutoSize = false;
        box.Margin = new Padding(0);
    }

    private static DataGridViewTextBoxColumn Column(string name, string title, int width) => new()
    {
        Name = name,
        HeaderText = title,
        Width = width,
        MinimumWidth = Math.Min(width, 55),
        SortMode = DataGridViewColumnSortMode.NotSortable,
        AutoSizeMode = DataGridViewAutoSizeColumnMode.None
    };
}
