using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace WebsiteViewer;

internal sealed class MainForm : Form
{
    private readonly TextBox _urlBox = new();
    private readonly NumericUpDown _maxPages = new();
    private readonly NumericUpDown _maxDepth = new();
    private readonly NumericUpDown _delayMs = new();
    private readonly NumericUpDown _concurrency = new();
    private readonly CheckBox _stripQuery = new();
    private readonly CheckBox _respectRobots = new();
    private readonly CheckBox _includeSubdomains = new();
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
    private readonly TabPage _pagesTab = new("页面列表");
    private readonly TabPage _sectionsTab = new("栏目统计");
    private readonly TabPage _treeTab = new("结构树");
    private readonly TabPage _logTab = new("运行日志");

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
    private readonly TableLayoutPanel _statsLayout = new();
    private readonly List<Control> _statCards = new();
    private readonly RowStyle _statsRowStyle = new(SizeType.Absolute, 132);
    private readonly Label _headerHint = new();
    private readonly RowStyle _pageDetailRowStyle = new(SizeType.Absolute, 88);
    private Control? _pageDetailCard;

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
    private int _lastResponsiveColumns = -1;
    private bool _lastCompactHeight;
    private bool _lastHeaderHintVisible;
    private bool _responsiveInitialized;
    private bool _initialWindowSized;
    private bool _running;

    public MainForm()
    {
        Text = AppInfo.DisplayName;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 560);
        Size = new Size(1480, 920);
        BackColor = UiTheme.Bg;
        ForeColor = UiTheme.Text;
        Font = UiTheme.UiFont();
        AutoScaleMode = AutoScaleMode.Dpi;
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

        BuildUi();
        WireEvents();
        ApplyResponsiveLayout();
        SetRunning(false);
        ClearSelectedPageDetail();

        _uiTimer.Interval = 100;
        _uiTimer.Tick += (_, _) => ProcessUiBatch();
        _uiTimer.Start();
        SizeChanged += (_, _) => _layoutDirty = true;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        FitWindowToWorkingAreaOnce();
        ApplyResponsiveLayout();
    }

    private void FitWindowToWorkingAreaOnce()
    {
        if (_initialWindowSized) return;
        _initialWindowSized = true;

        Rectangle work = Screen.FromControl(this).WorkingArea;
        int availableWidth = Math.Max(MinimumSize.Width, work.Width - 48);
        int availableHeight = Math.Max(MinimumSize.Height, work.Height - 48);
        int targetWidth = Math.Min(Width, availableWidth);
        int targetHeight = Math.Min(Height, availableHeight);
        Size = new Size(targetWidth, targetHeight);

        int x = work.Left + Math.Max(0, (work.Width - targetWidth) / 2);
        int y = work.Top + Math.Max(0, (work.Height - targetHeight) / 2);
        Location = new Point(x, y);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_running) _cts?.Cancel();
        _crawler?.Dispose();
        _cts?.Dispose();
        _uiTimer.Stop();
        _uiTimer.Dispose();
        base.OnFormClosing(e);
    }

    private void BuildUi()
    {
        SuspendLayout();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Bg,
            Padding = new Padding(14),
            Margin = new Padding(0),
            ColumnCount = 1,
            RowCount = 5
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(_statsRowStyle);
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        Controls.Add(root);

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildCrawlerCard(), 0, 1);
        root.Controls.Add(BuildStatsPanel(), 0, 2);
        root.Controls.Add(BuildWorkspace(), 0, 3);
        root.Controls.Add(BuildStatusBar(), 0, 4);

        ResumeLayout(true);
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Bg,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 8)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var textStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0)
        };
        textStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        textStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var titleRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0)
        };
        titleRow.Controls.Add(new Label
        {
            Text = "WebsiteViewer",
            AutoSize = true,
            Font = UiTheme.UiFont(18f, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Margin = new Padding(0, 1, 10, 0)
        });
        titleRow.Controls.Add(CreatePill("v" + AppInfo.Version, UiTheme.Blue, Color.FromArgb(7, 22, 43)));
        titleRow.Controls.Add(CreatePill("网站结构扫描", UiTheme.Card2, UiTheme.Muted));

        var subtitle = new Label
        {
            Text = "统计页面数量、栏目结构、父子关系，并实时记录请求与 HTML 解析耗时",
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            ForeColor = UiTheme.Muted,
            Font = UiTheme.UiFont(9.2f),
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Padding(1, 1, 0, 0)
        };

        textStack.Controls.Add(titleRow, 0, 0);
        textStack.Controls.Add(subtitle, 0, 1);
        header.Controls.Add(textStack, 0, 0);

        _headerHint.Text = "双击页面可在浏览器打开";
        _headerHint.AutoSize = true;
        _headerHint.ForeColor = UiTheme.Muted;
        _headerHint.Font = UiTheme.UiFont(8.8f);
        _headerHint.TextAlign = ContentAlignment.MiddleRight;
        _headerHint.Anchor = AnchorStyles.Right;
        _headerHint.Margin = new Padding(12, 10, 0, 0);
        header.Controls.Add(_headerHint, 1, 0);
        return header;
    }

    private Control BuildCrawlerCard()
    {
        var card = UiTheme.CardPanel();
        card.AutoSize = true;
        card.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        card.Dock = DockStyle.Top;
        card.Margin = new Padding(0, 0, 0, 10);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12, 11, 12, 11),
            Margin = new Padding(0),
            BackColor = Color.Transparent
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.Controls.Add(layout);

        layout.Controls.Add(BuildUrlRow(), 0, 0);
        layout.Controls.Add(BuildSettingsRow(), 0, 1);
        layout.Controls.Add(BuildOutputRow(), 0, 2);
        return card;
    }

    private Control BuildUrlRow()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 58,
            AutoSize = false,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 8),
            BackColor = Color.Transparent
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        var urlHost = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 10, 0),
            BackColor = Color.Transparent
        };
        urlHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        urlHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        urlHost.Controls.Add(FieldLabel("目标网站"), 0, 0);
        _urlBox.Text = "https://www.huaweicloud.com";
        _urlBox.Dock = DockStyle.Fill;
        _urlBox.Margin = new Padding(0);
        UiTheme.StyleTextBox(_urlBox);
        urlHost.Controls.Add(_urlBox, 0, 1);

        ConfigureTopActionButton(_startButton, new Padding(4, 20, 4, 0));
        ConfigureTopActionButton(_stopButton, new Padding(4, 20, 0, 0));

        row.Controls.Add(urlHost, 0, 0);
        row.Controls.Add(_startButton, 1, 0);
        row.Controls.Add(_stopButton, 2, 0);
        return row;
    }

    private Control BuildSettingsRow()
    {
        ConfigureNumeric(_maxPages, 10, 100000, 3000);
        ConfigureNumeric(_maxDepth, 0, 100, 10);
        ConfigureNumeric(_delayMs, 0, 10000, 400);
        ConfigureNumeric(_concurrency, 1, 16, 4);

        ConfigureCheck(_stripQuery, "忽略 URL 参数", true);
        ConfigureCheck(_respectRobots, "遵守 robots.txt", true);
        ConfigureCheck(_includeSubdomains, "包含子域名", false);

        // 固定为两层网格，不再使用 AutoSize + FlowLayoutPanel 自动换行。
        // WinForms 在高 DPI / 窄窗口下对 AutoSize FlowLayout 的 PreferredSize 计算容易出现覆盖。
        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 96,
            AutoSize = false,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 8),
            BackColor = Color.Transparent
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        var numericRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0),
            BackColor = Color.Transparent
        };
        for (int i = 0; i < 4; i++)
            numericRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        numericRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        numericRow.Controls.Add(BuildNumericField("最多页面", _maxPages), 0, 0);
        numericRow.Controls.Add(BuildNumericField("最大深度", _maxDepth), 1, 0);
        numericRow.Controls.Add(BuildNumericField("请求间隔 ms", _delayMs), 2, 0);
        numericRow.Controls.Add(BuildNumericField("并发", _concurrency, last: true), 3, 0);

        var optionRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0),
            BackColor = Color.Transparent
        };
        for (int i = 0; i < 3; i++)
            optionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        optionRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        optionRow.Controls.Add(BuildCheckHost(_stripQuery), 0, 0);
        optionRow.Controls.Add(BuildCheckHost(_respectRobots), 1, 0);
        optionRow.Controls.Add(BuildCheckHost(_includeSubdomains), 2, 0);

        outer.Controls.Add(numericRow, 0, 0);
        outer.Controls.Add(optionRow, 0, 1);
        return outer;
    }

    private Control BuildOutputRow()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 58,
            AutoSize = false,
            ColumnCount = 5,
            RowCount = 2,
            Margin = new Padding(0),
            BackColor = Color.Transparent
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        row.Controls.Add(FieldLabel("输出目录"), 0, 0);
        _outputBox.Text = Path.Combine(AppContext.BaseDirectory, "out");
        _outputBox.Dock = DockStyle.Fill;
        _outputBox.Margin = new Padding(0, 0, 8, 0);
        UiTheme.StyleTextBox(_outputBox);
        row.Controls.Add(_outputBox, 0, 1);

        ConfigureOutputButton(_browseButton, new Padding(0));
        ConfigureOutputButton(_exportButton, new Padding(4, 0, 0, 0));
        ConfigureOutputButton(_openOutputButton, new Padding(4, 0, 0, 0));
        ConfigureOutputButton(_clearButton, new Padding(4, 0, 0, 0));

        row.Controls.Add(_browseButton, 1, 1);
        row.Controls.Add(_exportButton, 2, 1);
        row.Controls.Add(_openOutputButton, 3, 1);
        row.Controls.Add(_clearButton, 4, 1);
        return row;
    }

    private Control BuildStatsPanel()
    {
        _statsLayout.Dock = DockStyle.Fill;
        _statsLayout.ColumnCount = 4;
        _statsLayout.RowCount = 2;
        _statsLayout.Margin = new Padding(0, 0, 0, 10);
        _statsLayout.Padding = new Padding(0);
        for (int i = 0; i < 4; i++) _statsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        _statsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        _statsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        AddStat(_statsLayout, 0, 0, "已扫描", "0");
        AddStat(_statsLayout, 1, 0, "成功 / 失败", "0 / 0");
        AddStat(_statsLayout, 2, 0, "已发现 / 队列", "0 / 0");
        AddStat(_statsLayout, 3, 0, "扫描速度", "0.00 页/秒");
        AddStat(_statsLayout, 0, 1, "运行时间", "00:00:00");
        AddStat(_statsLayout, 1, 1, "预计剩余", "--");
        AddStat(_statsLayout, 2, 1, "平均请求", "0 ms");
        AddStat(_statsLayout, 3, 1, "平均解析", "0 ms");
        return _statsLayout;
    }

    private void ApplyResponsiveLayout()
    {
        if (IsDisposed || _statsLayout.IsDisposed || _statCards.Count != 8) return;

        int contentWidth = Math.Max(0, ClientSize.Width - 28);
        bool compactHeight = ClientSize.Height < 760;
        bool veryCompactHeight = ClientSize.Height < 600;
        bool headerHintVisible = contentWidth >= 1120 && !compactHeight;
        int columns = contentWidth >= 1260 ? 8 : 4;
        int rows = 8 / columns;

        // SizeChanged 会非常频繁触发。布局模式没有变化时绝不重复清空/重建统计卡，
        // 否则拖动窗口本身就会造成明显卡顿和闪烁。
        if (_responsiveInitialized &&
            _lastResponsiveColumns == columns &&
            _lastCompactHeight == compactHeight &&
            _lastHeaderHintVisible == headerHintVisible &&
            _statsLayout.Visible == !veryCompactHeight)
        {
            _layoutDirty = false;
            return;
        }

        _responsiveInitialized = true;
        _lastResponsiveColumns = columns;
        _lastCompactHeight = compactHeight;
        _lastHeaderHintVisible = headerHintVisible;
        _layoutDirty = false;

        _statsLayout.Visible = !veryCompactHeight;
        _statsRowStyle.Height = veryCompactHeight ? 0 : columns == 8 ? 68 : (compactHeight ? 112 : 132);
        _headerHint.Visible = headerHintVisible;
        _pageDetailRowStyle.Height = compactHeight ? 0 : 88;
        if (_pageDetailCard is not null) _pageDetailCard.Visible = !compactHeight;

        _statsLayout.SuspendLayout();
        try
        {
            _statsLayout.Controls.Clear();
            _statsLayout.ColumnStyles.Clear();
            _statsLayout.RowStyles.Clear();
            _statsLayout.ColumnCount = columns;
            _statsLayout.RowCount = rows;
            for (int i = 0; i < columns; i++)
                _statsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
            for (int i = 0; i < rows; i++)
                _statsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));

            for (int i = 0; i < _statCards.Count; i++)
            {
                int col = i % columns;
                int row = i / columns;
                var card = _statCards[i];
                card.Margin = new Padding(
                    col == 0 ? 0 : 4,
                    row == 0 ? 0 : 4,
                    col == columns - 1 ? 0 : 4,
                    row == rows - 1 ? 0 : 4);
                _statsLayout.Controls.Add(card, col, row);
            }
        }
        finally
        {
            _statsLayout.ResumeLayout(true);
        }
        PerformLayout();
    }

    private Control BuildWorkspace()
    {
        var card = UiTheme.CardPanel();
        card.Dock = DockStyle.Fill;
        card.Padding = new Padding(1);

        _tabs.Dock = DockStyle.Fill;
        _tabs.Margin = new Padding(0);
        _pagesTab.BackColor = UiTheme.Card;
        _sectionsTab.BackColor = UiTheme.Card;
        _treeTab.BackColor = UiTheme.Card;
        _logTab.BackColor = UiTheme.Card;
        _pagesTab.ForeColor = UiTheme.Text;
        _sectionsTab.ForeColor = UiTheme.Text;
        _treeTab.ForeColor = UiTheme.Text;
        _logTab.ForeColor = UiTheme.Text;

        _pagesTab.Controls.Add(BuildPagesPanel());
        _sectionsTab.Controls.Add(BuildSectionsPanel());
        _treeTab.Controls.Add(BuildTreePanel());
        _logTab.Controls.Add(BuildLogPanel());

        _tabs.TabPages.Add(_pagesTab);
        _tabs.TabPages.Add(_sectionsTab);
        _tabs.TabPages.Add(_treeTab);
        _tabs.TabPages.Add(_logTab);
        card.Controls.Add(_tabs);
        return card;
    }

    private Control BuildPagesPanel()
    {
        ConfigurePagesGrid();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10),
            Margin = new Padding(0),
            BackColor = UiTheme.Card
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(_pageDetailRowStyle);

        var searchRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 7)
        };
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8));

        _searchBox.Dock = DockStyle.Fill;
        _searchBox.PlaceholderText = "筛选 URL / 标题 / 栏目；结构树筛选也会同步作用到这里";
        UiTheme.StyleTextBox(_searchBox);
        _clearSearchButton.Dock = DockStyle.Fill;
        _clearSearchButton.Margin = new Padding(4, 0, 0, 0);
        searchRow.Controls.Add(_searchBox, 0, 0);
        searchRow.Controls.Add(_clearSearchButton, 1, 0);

        layout.Controls.Add(searchRow, 0, 0);
        layout.Controls.Add(_pagesGrid, 0, 1);
        _pageDetailCard = BuildSelectedPageDetail();
        layout.Controls.Add(_pageDetailCard, 0, 2);
        return layout;
    }

    private Control BuildSelectedPageDetail()
    {
        var card = UiTheme.CardPanel(10);
        card.Dock = DockStyle.Fill;
        card.BackColor = UiTheme.Input;
        card.Margin = new Padding(0, 8, 0, 0);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            Padding = new Padding(10, 7, 10, 8),
            Margin = new Padding(0),
            BackColor = Color.Transparent
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.4f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(FieldLabel("选中页面"), 0, 0);
        layout.Controls.Add(FieldLabel("父级 URL"), 1, 0);
        layout.Controls.Add(FieldLabel("发现来源"), 2, 0);
        layout.Controls.Add(FieldLabel("Canonical / 错误"), 3, 0);

        _selectedStatusValue.Text = "尚未选择页面";
        _selectedStatusValue.Dock = DockStyle.Fill;
        _selectedStatusValue.AutoEllipsis = true;
        _selectedStatusValue.TextAlign = ContentAlignment.MiddleLeft;
        _selectedStatusValue.ForeColor = UiTheme.Muted;
        _selectedStatusValue.Font = UiTheme.UiFont(9f, FontStyle.Bold);
        _selectedStatusValue.Margin = new Padding(0, 0, 8, 0);

        PrepareDetailBox(_selectedParentBox);
        PrepareDetailBox(_selectedSourceBox);
        PrepareDetailBox(_selectedCanonicalBox);

        layout.Controls.Add(_selectedStatusValue, 0, 1);
        layout.Controls.Add(_selectedParentBox, 1, 1);
        layout.Controls.Add(_selectedSourceBox, 2, 1);
        layout.Controls.Add(_selectedCanonicalBox, 3, 1);
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildSectionsPanel()
    {
        ConfigureSectionsGrid();
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            BackColor = UiTheme.Card
        };
        panel.Controls.Add(_sectionsGrid);
        return panel;
    }

    private Control BuildTreePanel()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(10),
            BackColor = UiTheme.Card
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 8)
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142));

        _filterLabel.Text = "当前：显示全部页面";
        _filterLabel.Dock = DockStyle.Fill;
        _filterLabel.ForeColor = UiTheme.Muted;
        _filterLabel.Font = UiTheme.UiFont(9.1f);
        _filterLabel.TextAlign = ContentAlignment.MiddleLeft;
        _filterLabel.AutoEllipsis = true;
        _clearTreeFilterButton.Dock = DockStyle.Fill;
        _clearTreeFilterButton.Margin = new Padding(4, 0, 4, 0);
        _showBranchPagesButton.Dock = DockStyle.Fill;
        _showBranchPagesButton.Margin = new Padding(4, 0, 0, 0);

        toolbar.Controls.Add(_filterLabel, 0, 0);
        toolbar.Controls.Add(_clearTreeFilterButton, 1, 0);
        toolbar.Controls.Add(_showBranchPagesButton, 2, 0);

        _tree.Dock = DockStyle.Fill;
        _tree.BackColor = UiTheme.Input;
        _tree.ForeColor = UiTheme.Text;
        _tree.BorderStyle = BorderStyle.None;
        _tree.Font = UiTheme.UiFont(9.4f);
        _tree.HideSelection = false;
        _tree.ShowNodeToolTips = true;
        _tree.FullRowSelect = true;
        _tree.Indent = 22;
        _tree.ItemHeight = 28;

        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(_tree, 0, 1);
        return layout;
    }

    private Control BuildLogPanel()
    {
        ConfigureLogBox();
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            BackColor = UiTheme.Card
        };
        panel.Controls.Add(_logBox);
        return panel;
    }

    private Control BuildStatusBar()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 8, 0, 0),
            Padding = new Padding(2, 5, 2, 0),
            BackColor = UiTheme.Bg
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));

        _statusLabel.Text = "就绪 · 输入网址后点击“开始扫描”";
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.ForeColor = UiTheme.Muted;
        _statusLabel.Font = UiTheme.UiFont(8.9f);
        _statusLabel.AutoEllipsis = true;
        _progress.Dock = DockStyle.Fill;
        _progress.Maximum = 1000;
        _progress.Value = 0;
        _progress.Margin = new Padding(12, 10, 0, 10);

        panel.Controls.Add(_statusLabel, 0, 0);
        panel.Controls.Add(_progress, 1, 0);
        return panel;
    }

    private void ConfigurePagesGrid()
    {
        UiTheme.StyleGrid(_pagesGrid);
        _pagesGrid.VirtualMode = true;
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

    private void AddStat(TableLayoutPanel table, int column, int row, string name, string initial)
    {
        var card = UiTheme.CardPanel(12);
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(column == 0 ? 0 : 5, row == 0 ? 0 : 5, column == 3 ? 0 : 5, row == 1 ? 0 : 5);

        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(11, 7, 9, 6),
            BackColor = Color.Transparent
        };
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var label = new Label
        {
            Text = name,
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Muted,
            Font = UiTheme.UiFont(8.4f),
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var value = new Label
        {
            Text = initial,
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Text,
            Font = UiTheme.UiFont(12.5f, FontStyle.Bold),
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        stack.Controls.Add(label, 0, 0);
        stack.Controls.Add(value, 0, 1);
        card.Controls.Add(stack);
        _statValues[name] = value;
        _statCards.Add(card);
        table.Controls.Add(card, column, row);
    }

    private void SetStat(string name, string value)
    {
        if (_statValues.TryGetValue(name, out var label)) label.Text = value;
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
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

    private static Control BuildNumericField(string label, NumericUpDown box, bool last = false)
    {
        var host = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Height = 56,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, last ? 0 : 8, 0),
            BackColor = Color.Transparent
        };
        host.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        host.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        host.Controls.Add(FieldLabel(label), 0, 0);
        box.Dock = DockStyle.Fill;
        box.Margin = new Padding(0);
        host.Controls.Add(box, 0, 1);
        return host;
    }

    private static Control BuildCheckHost(CheckBox box)
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 8, 0),
            BackColor = Color.Transparent
        };
        box.AutoSize = false;
        box.Dock = DockStyle.Fill;
        box.TextAlign = ContentAlignment.MiddleLeft;
        box.Margin = new Padding(0);
        host.Controls.Add(box);
        return host;
    }

    private static void ConfigureTopActionButton(RoundedButton button, Padding margin)
    {
        button.Dock = DockStyle.Fill;
        button.Height = 38;
        button.Margin = margin;
        button.MinimumSize = new Size(0, 38);
    }

    private static void ConfigureOutputButton(RoundedButton button, Padding margin)
    {
        button.Dock = DockStyle.Fill;
        button.Height = 38;
        button.Margin = margin;
        button.MinimumSize = new Size(0, 38);
    }

    private static void ConfigureNumeric(NumericUpDown box, decimal min, decimal max, decimal value)
    {
        box.Minimum = min;
        box.Maximum = max;
        box.Value = value;
        box.DecimalPlaces = 0;
        box.ThousandsSeparator = true;
        UiTheme.StyleNumeric(box);
    }

    private static void ConfigureCheck(CheckBox box, string text, bool value)
    {
        box.Text = text;
        box.Checked = value;
        UiTheme.StyleCheckBox(box);
        box.Margin = new Padding(0);
    }

    private static void PrepareDetailBox(TextBox box)
    {
        box.Dock = DockStyle.Fill;
        box.Margin = new Padding(0, 0, 8, 0);
        UiTheme.StyleTextBox(box, readOnly: true);
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
