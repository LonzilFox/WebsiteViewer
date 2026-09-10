using System.Text;
using System.Text.Json;

namespace WebsiteViewer;

internal static class ExportService
{
    public static void ExportAll(CrawlSummary summary, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        ExportUrls(summary.Pages, Path.Combine(outputDirectory, "urls.csv"));
        ExportEdges(summary.Edges, Path.Combine(outputDirectory, "edges.csv"));
        ExportSections(summary.Pages, Path.Combine(outputDirectory, "section_counts.csv"));
        ExportTree(summary.Pages, Path.Combine(outputDirectory, "tree.json"));
        ExportSummary(summary, Path.Combine(outputDirectory, "summary.json"));
    }

    private static void ExportUrls(IEnumerable<CrawlPageResult> pages, string path)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine("index,status,depth,section,fetch_ms,parse_ms,title,url,parent_url,discovered_from,content_type,canonical,noindex,error,breadcrumbs");
        foreach (var p in pages.OrderBy(x => x.Index))
        {
            writer.WriteLine(string.Join(',', new[]
            {
                Csv(p.Index), Csv(p.StatusCode), Csv(p.Depth), Csv(p.Section), Csv(p.FetchMs.ToString("F1")), Csv(p.ParseMs.ToString("F1")),
                Csv(p.Title), Csv(p.Url), Csv(p.ParentUrl), Csv(p.DiscoveredFrom), Csv(p.ContentType), Csv(p.Canonical), Csv(p.NoIndex), Csv(p.Error),
                Csv(string.Join(" > ", p.Breadcrumbs))
            }));
        }
    }

    private static void ExportEdges(IEnumerable<(string Source, string Target)> edges, string path)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine("source_url,target_url");
        foreach (var edge in edges.OrderBy(x => x.Source).ThenBy(x => x.Target))
            writer.WriteLine($"{Csv(edge.Source)},{Csv(edge.Target)}");
    }

    private static void ExportSections(IEnumerable<CrawlPageResult> pages, string path)
    {
        var groups = pages.GroupBy(p => p.Section, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                Section = g.Key,
                Total = g.Count(),
                Success = g.Count(p => p.StatusCode is >= 200 and < 400 && string.IsNullOrEmpty(p.Error)),
                Failed = g.Count(p => !(p.StatusCode is >= 200 and < 400) || !string.IsNullOrEmpty(p.Error)),
                AvgFetchMs = g.Any() ? g.Average(p => p.FetchMs) : 0,
                AvgParseMs = g.Any() ? g.Average(p => p.ParseMs) : 0
            })
            .OrderByDescending(x => x.Total)
            .ThenBy(x => x.Section)
            .ToList();

        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine("section,pages,success,failed,avg_fetch_ms,avg_parse_ms");
        foreach (var g in groups)
            writer.WriteLine($"{Csv(g.Section)},{g.Total},{g.Success},{g.Failed},{g.AvgFetchMs:F1},{g.AvgParseMs:F1}");
    }

    private static void ExportTree(IEnumerable<CrawlPageResult> pages, string path)
    {
        var pageMap = pages.ToDictionary(p => p.Url, StringComparer.OrdinalIgnoreCase);
        var children = new Dictionary<string, List<CrawlPageResult>>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in pages)
        {
            string parent = page.ParentUrl;
            if (string.IsNullOrWhiteSpace(parent) || !pageMap.ContainsKey(parent)) continue;
            if (!children.TryGetValue(parent, out var list)) children[parent] = list = new List<CrawlPageResult>();
            list.Add(page);
        }

        var roots = pages.Where(p => string.IsNullOrWhiteSpace(p.ParentUrl) || !pageMap.ContainsKey(p.ParentUrl))
            .OrderBy(p => p.Depth).ThenBy(p => p.Url).ToList();
        object Build(CrawlPageResult page) => new
        {
            name = string.IsNullOrWhiteSpace(page.Title) ? page.Url : page.Title,
            url = page.Url,
            status = page.StatusCode,
            depth = page.Depth,
            section = page.Section,
            children = children.TryGetValue(page.Url, out var list)
                ? list.OrderBy(x => x.Url).Select(Build).ToList()
                : new List<object>()
        };

        var forest = roots.Select(Build).ToList();
        File.WriteAllText(path, JsonSerializer.Serialize(forest, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    }

    private static void ExportSummary(CrawlSummary summary, string path)
    {
        var payload = new
        {
            version = AppInfo.Version,
            exportedAt = DateTimeOffset.Now,
            elapsedSeconds = summary.Elapsed.TotalSeconds,
            cancelled = summary.Cancelled,
            pages = summary.Pages.Count,
            success = summary.Pages.Count(p => p.StatusCode is >= 200 and < 400 && string.IsNullOrEmpty(p.Error)),
            failed = summary.Pages.Count(p => !(p.StatusCode is >= 200 and < 400) || !string.IsNullOrEmpty(p.Error)),
            avgFetchMs = summary.Pages.Count == 0 ? 0 : summary.Pages.Average(p => p.FetchMs),
            avgParseMs = summary.Pages.Count == 0 ? 0 : summary.Pages.Average(p => p.ParseMs),
            sections = summary.Pages.GroupBy(p => p.Section, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase)
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    }

    private static string Csv(object? value)
    {
        string s = value?.ToString() ?? "";
        return '"' + s.Replace("\"", "\"\"") + '"';
    }
}
