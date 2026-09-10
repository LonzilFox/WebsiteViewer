using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WebsiteViewer;

internal sealed class HtmlInspection
{
    public string Title { get; init; } = "";
    public string Canonical { get; init; } = "";
    public bool NoIndex { get; init; }
    public IReadOnlyList<string> Breadcrumbs { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Links { get; init; } = Array.Empty<string>();
}

internal static partial class HtmlInspector
{
    [GeneratedRegex("""<title\b[^>]*>(.*?)</title>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleRegex();

    [GeneratedRegex("""<a\b[^>]*?\bhref\s*=\s*(?:"([^"]*)"|'([^']*)')""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LinkRegex();

    [GeneratedRegex("""<link\b[^>]*?\brel\s*=\s*(?:"[^"]*canonical[^"]*"|'[^']*canonical[^']*')[^>]*?\bhref\s*=\s*(?:"([^"]*)"|'([^']*)')""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CanonicalRegexA();

    [GeneratedRegex("""<link\b[^>]*?\bhref\s*=\s*(?:"([^"]*)"|'([^']*)')[^>]*?\brel\s*=\s*(?:"[^"]*canonical[^"]*"|'[^']*canonical[^']*')""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CanonicalRegexB();

    [GeneratedRegex("""<meta\b[^>]*?\bname\s*=\s*(?:"robots"|'robots')[^>]*?\bcontent\s*=\s*(?:"([^"]*)"|'([^']*)')""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex RobotsMetaRegexA();

    [GeneratedRegex("""<meta\b[^>]*?\bcontent\s*=\s*(?:"([^"]*)"|'([^']*)')[^>]*?\bname\s*=\s*(?:"robots"|'robots')""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex RobotsMetaRegexB();

    [GeneratedRegex("""<script\b[^>]*?type\s*=\s*(?:"application/ld\+json"|'application/ld\+json')[^>]*>(.*?)</script>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex JsonLdRegex();

    [GeneratedRegex("""<[^>]+>""", RegexOptions.Singleline)]
    private static partial Regex TagsRegex();

    public static HtmlInspection Inspect(string html, Uri baseUri, bool stripQuery, Func<Uri, bool> isAllowedHost)
    {
        string title = ExtractTitle(html);
        string canonical = ExtractCanonical(html, baseUri, stripQuery);
        bool noIndex = ExtractNoIndex(html);
        var breadcrumbs = ExtractBreadcrumbs(html, baseUri, stripQuery);
        var links = ExtractLinks(html, baseUri, stripQuery, isAllowedHost);

        return new HtmlInspection
        {
            Title = title,
            Canonical = canonical,
            NoIndex = noIndex,
            Breadcrumbs = breadcrumbs,
            Links = links
        };
    }

    private static string ExtractTitle(string html)
    {
        var match = TitleRegex().Match(html);
        if (!match.Success) return "";
        string value = TagsRegex().Replace(match.Groups[1].Value, " ");
        value = WebUtility.HtmlDecode(value);
        return Regex.Replace(value, @"\s+", " ").Trim();
    }

    private static string ExtractCanonical(string html, Uri baseUri, bool stripQuery)
    {
        var match = CanonicalRegexA().Match(html);
        if (!match.Success) match = CanonicalRegexB().Match(html);
        if (!match.Success) return "";
        string href = FirstNonEmpty(match.Groups[1].Value, match.Groups[2].Value);
        if (!TryResolve(baseUri, href, out var uri)) return "";
        return UrlTools.Normalize(uri, stripQuery).AbsoluteUri;
    }

    private static bool ExtractNoIndex(string html)
    {
        var match = RobotsMetaRegexA().Match(html);
        if (!match.Success) match = RobotsMetaRegexB().Match(html);
        if (!match.Success) return false;
        string content = FirstNonEmpty(match.Groups[1].Value, match.Groups[2].Value);
        return content.Contains("noindex", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ExtractLinks(string html, Uri baseUri, bool stripQuery, Func<Uri, bool> isAllowedHost)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in LinkRegex().Matches(html))
        {
            string href = FirstNonEmpty(match.Groups[1].Value, match.Groups[2].Value).Trim();
            if (string.IsNullOrWhiteSpace(href)) continue;
            if (href.StartsWith('#') || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
                href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!TryResolve(baseUri, WebUtility.HtmlDecode(href), out var uri)) continue;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) continue;
            if (!isAllowedHost(uri)) continue;

            var normalized = UrlTools.Normalize(uri, stripQuery);
            if (UrlTools.IsLikelyHtml(normalized)) result.Add(normalized.AbsoluteUri);
        }
        return result.ToList();
    }

    private static IReadOnlyList<string> ExtractBreadcrumbs(string html, Uri baseUri, bool stripQuery)
    {
        foreach (Match match in JsonLdRegex().Matches(html))
        {
            string json = WebUtility.HtmlDecode(match.Groups[1].Value.Trim());
            if (string.IsNullOrWhiteSpace(json)) continue;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var found = FindBreadcrumbList(doc.RootElement, baseUri, stripQuery);
                if (found.Count > 0) return found;
            }
            catch
            {
                // 部分网站的 JSON-LD 并非严格 JSON，忽略即可。
            }
        }
        return Array.Empty<string>();
    }

    private static List<string> FindBreadcrumbList(JsonElement element, Uri baseUri, bool stripQuery)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("@type", out var typeElement) && IsBreadcrumbType(typeElement) &&
                element.TryGetProperty("itemListElement", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                var result = new List<string>();
                foreach (var item in items.EnumerateArray())
                {
                    string? raw = ExtractBreadcrumbItemUrl(item);
                    if (raw is null || !TryResolve(baseUri, raw, out var uri)) continue;
                    result.Add(UrlTools.Normalize(uri, stripQuery).AbsoluteUri);
                }
                if (result.Count > 0) return result;
            }

            foreach (var prop in element.EnumerateObject())
            {
                var child = FindBreadcrumbList(prop.Value, baseUri, stripQuery);
                if (child.Count > 0) return child;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var childElement in element.EnumerateArray())
            {
                var child = FindBreadcrumbList(childElement, baseUri, stripQuery);
                if (child.Count > 0) return child;
            }
        }
        return new List<string>();
    }

    private static bool IsBreadcrumbType(JsonElement typeElement)
    {
        if (typeElement.ValueKind == JsonValueKind.String)
            return string.Equals(typeElement.GetString(), "BreadcrumbList", StringComparison.OrdinalIgnoreCase);
        if (typeElement.ValueKind == JsonValueKind.Array)
            return typeElement.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String &&
                string.Equals(x.GetString(), "BreadcrumbList", StringComparison.OrdinalIgnoreCase));
        return false;
    }

    private static string? ExtractBreadcrumbItemUrl(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;
        if (!item.TryGetProperty("item", out var value)) return null;
        if (value.ValueKind == JsonValueKind.String) return value.GetString();
        if (value.ValueKind != JsonValueKind.Object) return null;
        if (value.TryGetProperty("@id", out var id) && id.ValueKind == JsonValueKind.String) return id.GetString();
        if (value.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String) return url.GetString();
        return null;
    }

    private static bool TryResolve(Uri baseUri, string href, out Uri uri)
    {
        try
        {
            uri = new Uri(baseUri, href);
            return true;
        }
        catch
        {
            uri = baseUri;
            return false;
        }
    }

    private static string FirstNonEmpty(string a, string b) => !string.IsNullOrEmpty(a) ? a : b;
}

internal static class UrlTools
{
    private static readonly HashSet<string> BannedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg", ".ico", ".bmp", ".avif",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".zip", ".rar", ".7z", ".gz", ".tar",
        ".mp4", ".mp3", ".avi", ".mov", ".wmv", ".m4a", ".wav", ".flac",
        ".css", ".js", ".json", ".xml", ".txt", ".woff", ".woff2", ".ttf", ".eot", ".map"
    };

    public static Uri Normalize(Uri uri, bool stripQuery)
    {
        var builder = new UriBuilder(uri) { Fragment = "" };
        if (stripQuery) builder.Query = "";
        string path = builder.Path;
        if (path.Length > 1 && path.EndsWith('/')) builder.Path = path.TrimEnd('/');
        builder.Host = builder.Host.ToLowerInvariant();
        if ((builder.Scheme == "http" && builder.Port == 80) || (builder.Scheme == "https" && builder.Port == 443))
            builder.Port = -1;
        return builder.Uri;
    }

    public static bool IsLikelyHtml(Uri uri)
    {
        string ext = Path.GetExtension(uri.AbsolutePath);
        return string.IsNullOrEmpty(ext) || ext.Equals(".html", StringComparison.OrdinalIgnoreCase) || ext.Equals(".htm", StringComparison.OrdinalIgnoreCase) || !BannedExtensions.Contains(ext);
    }

    public static string GetSection(Uri uri)
    {
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "root" : Uri.UnescapeDataString(parts[0]);
    }

    public static string GetPathParent(Uri uri, bool stripQuery)
    {
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "";
        if (parts.Length == 1)
        {
            var root = new UriBuilder(uri) { Path = "/", Query = "", Fragment = "" };
            return Normalize(root.Uri, stripQuery).AbsoluteUri;
        }
        var parent = new UriBuilder(uri)
        {
            Path = "/" + string.Join('/', parts.Take(parts.Length - 1)),
            Query = "",
            Fragment = ""
        };
        return Normalize(parent.Uri, stripQuery).AbsoluteUri;
    }
}
