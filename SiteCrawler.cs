using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Channels;

namespace WebsiteViewer;

internal sealed class SiteCrawler : IDisposable
{
    private readonly HttpClient _client;
    private readonly RobotsCache _robots;
    private readonly object _statsLock = new();
    private double _fetchMsTotal;
    private double _parseMsTotal;
    private int _crawled;
    private int _success;
    private int _failed;
    private int _discovered;
    private int _pending;
    private Stopwatch _stopwatch = new();

    public event Action<CrawlPageResult>? PageCompleted;
    public event Action<CrawlProgressSnapshot>? ProgressChanged;
    public event Action<string>? Log;

    public SiteCrawler()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 8
        };
        _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) WebsiteViewer/0.0.4");
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xhtml+xml"));
        _client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.6");
        _robots = new RobotsCache(_client);
    }

    public async Task<CrawlSummary> CrawlAsync(CrawlOptions options, CancellationToken cancellationToken)
    {
        ResetStats();
        _stopwatch = Stopwatch.StartNew();

        var results = new ConcurrentBag<CrawlPageResult>();
        var edges = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var seen = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var channel = Channel.CreateUnbounded<CrawlItem>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        Uri normalizedStart = UrlTools.Normalize(options.StartUri, options.StripQuery);
        string rootHost = normalizedStart.Host;
        string baseHost = rootHost.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? rootHost[4..] : rootHost;

        bool IsAllowedHost(Uri uri)
        {
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
            if (uri.Host.Equals(rootHost, StringComparison.OrdinalIgnoreCase)) return true;
            return options.IncludeSubdomains && uri.Host.EndsWith("." + baseHost, StringComparison.OrdinalIgnoreCase);
        }

        bool TryRegister(Uri uri)
        {
            string key = uri.AbsoluteUri;
            if (!seen.TryAdd(key, 0)) return false;
            int n = Interlocked.Increment(ref _discovered);
            if (n > options.MaxPages)
            {
                seen.TryRemove(key, out _);
                Interlocked.Decrement(ref _discovered);
                return false;
            }
            return true;
        }

        async ValueTask EnqueueAsync(CrawlItem item)
        {
            Interlocked.Increment(ref _pending);
            await channel.Writer.WriteAsync(item, cancellationToken);
        }

        TryRegister(normalizedStart);
        await EnqueueAsync(new CrawlItem { Uri = normalizedStart, Depth = 0 });
        Log?.Invoke($"开始：{normalizedStart.AbsoluteUri}");
        Log?.Invoke($"上限 {options.MaxPages} 页 / 深度 {options.MaxDepth} / 并发 {options.Concurrency} / 间隔 {options.DelayMs}ms");

        var workers = Enumerable.Range(0, Math.Max(1, options.Concurrency))
            .Select(_ => Task.Run(async () =>
            {
                try
                {
                    await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
                    {
                        try
                        {
                            if (item.Depth <= options.MaxDepth)
                            {
                                var page = await ProcessPageAsync(item, options, IsAllowedHost, cancellationToken);
                                int crawled = Interlocked.Increment(ref _crawled);
                                var indexedPage = WithIndex(page, crawled);
                                results.Add(indexedPage);

                                if (!string.IsNullOrWhiteSpace(item.DiscoveredFrom))
                                    edges.TryAdd(item.DiscoveredFrom + "\n" + page.Url, 0);
                                if (page.StatusCode is >= 200 and < 400 && string.IsNullOrEmpty(page.Error)) Interlocked.Increment(ref _success);
                                else Interlocked.Increment(ref _failed);
                                lock (_statsLock)
                                {
                                    _fetchMsTotal += page.FetchMs;
                                    _parseMsTotal += page.ParseMs;
                                }

                                PageCompleted?.Invoke(indexedPage);
                                ReportProgress(options.MaxPages);

                                if (page.StatusCode == 200 && item.Depth < options.MaxDepth)
                                {
                                    foreach (string link in page.Links)
                                    {
                                        if (Volatile.Read(ref _discovered) >= options.MaxPages) break;
                                        if (!Uri.TryCreate(link, UriKind.Absolute, out var target)) continue;
                                        target = UrlTools.Normalize(target, options.StripQuery);
                                        if (!IsAllowedHost(target) || !UrlTools.IsLikelyHtml(target)) continue;
                                        if (!TryRegister(target)) continue;
                                        await EnqueueAsync(new CrawlItem
                                        {
                                            Uri = target,
                                            Depth = item.Depth + 1,
                                            DiscoveredFrom = page.Url
                                        });
                                    }
                                }
                            }
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            Log?.Invoke("处理失败：" + ex.Message);
                        }
                        finally
                        {
                            if (Interlocked.Decrement(ref _pending) == 0)
                                channel.Writer.TryComplete();
                        }

                        if (options.DelayMs > 0)
                            await Task.Delay(options.DelayMs, cancellationToken);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // 正常取消。
                }
            }, cancellationToken)).ToArray();

        bool cancelled = false;
        try
        {
            await Task.WhenAll(workers);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        finally
        {
            _stopwatch.Stop();
        }

        var ordered = results.OrderBy(p => p.Index).ToList();
        var edgeList = edges.Keys.Select(k =>
        {
            int split = k.IndexOf('\n');
            return split > 0 ? (k[..split], k[(split + 1)..]) : ("", "");
        }).Where(x => x.Item1.Length > 0 && x.Item2.Length > 0).ToList();

        return new CrawlSummary
        {
            Pages = ordered,
            Edges = edgeList,
            Elapsed = _stopwatch.Elapsed,
            Cancelled = cancelled || cancellationToken.IsCancellationRequested
        };

    }

    private static CrawlPageResult WithIndex(CrawlPageResult source, int index) => new()
    {
        Index = index,
        Url = source.Url,
        Title = source.Title,
        Depth = source.Depth,
        Section = source.Section,
        ParentUrl = source.ParentUrl,
        DiscoveredFrom = source.DiscoveredFrom,
        StatusCode = source.StatusCode,
        ContentType = source.ContentType,
        Canonical = source.Canonical,
        NoIndex = source.NoIndex,
        FetchMs = source.FetchMs,
        ParseMs = source.ParseMs,
        Error = source.Error,
        Breadcrumbs = source.Breadcrumbs,
        Links = source.Links
    };


    private async Task<CrawlPageResult> ProcessPageAsync(
        CrawlItem item,
        CrawlOptions options,
        Func<Uri, bool> isAllowedHost,
        CancellationToken cancellationToken)
    {
        Uri uri = item.Uri;
        if (options.RespectRobots)
        {
            var policy = await _robots.GetAsync(uri, cancellationToken);
            if (!policy.CanFetch(uri))
            {
                return new CrawlPageResult
                {
                    Url = uri.AbsoluteUri,
                    Depth = item.Depth,
                    Section = UrlTools.GetSection(uri),
                    ParentUrl = UrlTools.GetPathParent(uri, options.StripQuery),
                    DiscoveredFrom = item.DiscoveredFrom,
                    StatusCode = 0,
                    Error = "robots.txt 禁止访问"
                };
            }
        }

        var fetchWatch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            string contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            bool htmlLike = string.IsNullOrWhiteSpace(contentType) ||
                            contentType.Contains("html", StringComparison.OrdinalIgnoreCase) ||
                            contentType.Contains("xhtml", StringComparison.OrdinalIgnoreCase);
            if (!htmlLike)
            {
                fetchWatch.Stop();
                return new CrawlPageResult
                {
                    Url = uri.AbsoluteUri,
                    Depth = item.Depth,
                    Section = UrlTools.GetSection(uri),
                    ParentUrl = UrlTools.GetPathParent(uri, options.StripQuery),
                    DiscoveredFrom = item.DiscoveredFrom,
                    StatusCode = (int)response.StatusCode,
                    ContentType = contentType,
                    FetchMs = fetchWatch.Elapsed.TotalMilliseconds
                };
            }

            string html = await response.Content.ReadAsStringAsync(cancellationToken);
            fetchWatch.Stop();
            var parseWatch = Stopwatch.StartNew();
            HtmlInspection inspection = HtmlInspector.Inspect(html, uri, options.StripQuery, isAllowedHost);
            parseWatch.Stop();

            string breadcrumbParent = inspection.Breadcrumbs.Count >= 2 ? inspection.Breadcrumbs[^2] : "";
            return new CrawlPageResult
            {
                Url = uri.AbsoluteUri,
                Title = inspection.Title,
                Depth = item.Depth,
                Section = UrlTools.GetSection(uri),
                ParentUrl = string.IsNullOrWhiteSpace(breadcrumbParent) ? UrlTools.GetPathParent(uri, options.StripQuery) : breadcrumbParent,
                DiscoveredFrom = item.DiscoveredFrom,
                StatusCode = (int)response.StatusCode,
                ContentType = contentType,
                Canonical = inspection.Canonical,
                NoIndex = inspection.NoIndex,
                FetchMs = fetchWatch.Elapsed.TotalMilliseconds,
                ParseMs = parseWatch.Elapsed.TotalMilliseconds,
                Breadcrumbs = inspection.Breadcrumbs,
                Links = inspection.Links
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            fetchWatch.Stop();
            return new CrawlPageResult
            {
                Url = uri.AbsoluteUri,
                Depth = item.Depth,
                Section = UrlTools.GetSection(uri),
                ParentUrl = UrlTools.GetPathParent(uri, options.StripQuery),
                DiscoveredFrom = item.DiscoveredFrom,
                StatusCode = 0,
                FetchMs = fetchWatch.Elapsed.TotalMilliseconds,
                Error = ex.Message
            };
        }
    }

    private void ResetStats()
    {
        _fetchMsTotal = 0;
        _parseMsTotal = 0;
        _crawled = 0;
        _success = 0;
        _failed = 0;
        _discovered = 0;
        _pending = 0;
    }

    private void ReportProgress(int maxPages)
    {
        int crawled = Volatile.Read(ref _crawled);
        int discovered = Volatile.Read(ref _discovered);
        double elapsed = Math.Max(0.001, _stopwatch.Elapsed.TotalSeconds);
        double avgFetch;
        double avgParse;
        lock (_statsLock)
        {
            avgFetch = crawled == 0 ? 0 : _fetchMsTotal / crawled;
            avgParse = crawled == 0 ? 0 : _parseMsTotal / crawled;
        }
        double rate = crawled / elapsed;
        int remaining = Math.Max(0, maxPages - crawled);
        double eta = rate <= 0 ? 0 : remaining / rate;
        ProgressChanged?.Invoke(new CrawlProgressSnapshot
        {
            Crawled = crawled,
            Success = Volatile.Read(ref _success),
            Failed = Volatile.Read(ref _failed),
            Discovered = discovered,
            Queued = Math.Max(0, Volatile.Read(ref _pending)),
            ElapsedSeconds = elapsed,
            PagesPerSecond = rate,
            AverageFetchMs = avgFetch,
            AverageParseMs = avgParse,
            EtaSeconds = eta
        });
    }

    public void Dispose() => _client.Dispose();
}
