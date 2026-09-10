using System.Collections.Concurrent;

namespace WebsiteViewer;

internal sealed class CrawlOptions
{
    public required Uri StartUri { get; init; }
    public int MaxPages { get; init; } = 3000;
    public int MaxDepth { get; init; } = 10;
    public int DelayMs { get; init; } = 400;
    public int Concurrency { get; init; } = 4;
    public bool StripQuery { get; init; } = true;
    public bool RespectRobots { get; init; } = true;
    public bool IncludeSubdomains { get; init; }
    public string OutputDirectory { get; init; } = "out";
}

internal sealed class CrawlPageResult
{
    public int Index { get; init; }
    public required string Url { get; init; }
    public string Title { get; init; } = "";
    public int Depth { get; init; }
    public string Section { get; init; } = "root";
    public string ParentUrl { get; init; } = "";
    public string DiscoveredFrom { get; init; } = "";
    public int StatusCode { get; init; }
    public string ContentType { get; init; } = "";
    public string Canonical { get; init; } = "";
    public bool NoIndex { get; init; }
    public double FetchMs { get; init; }
    public double ParseMs { get; init; }
    public string Error { get; init; } = "";
    public IReadOnlyList<string> Breadcrumbs { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Links { get; init; } = Array.Empty<string>();
}

internal sealed class CrawlProgressSnapshot
{
    public int Crawled { get; init; }
    public int Success { get; init; }
    public int Failed { get; init; }
    public int Discovered { get; init; }
    public int Queued { get; init; }
    public double ElapsedSeconds { get; init; }
    public double PagesPerSecond { get; init; }
    public double AverageFetchMs { get; init; }
    public double AverageParseMs { get; init; }
    public double EtaSeconds { get; init; }
}

internal sealed class CrawlSummary
{
    public List<CrawlPageResult> Pages { get; init; } = new();
    public List<(string Source, string Target)> Edges { get; init; } = new();
    public TimeSpan Elapsed { get; init; }
    public bool Cancelled { get; init; }
}

internal sealed class CrawlItem
{
    public required Uri Uri { get; init; }
    public int Depth { get; init; }
    public string DiscoveredFrom { get; init; } = "";
}

internal sealed class SectionCounter
{
    public string Section { get; init; } = "root";
    public int Total { get; set; }
    public int Success { get; set; }
    public int Failed { get; set; }
}
