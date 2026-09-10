using System.Collections.Concurrent;

namespace WebsiteViewer;

internal sealed class RobotsPolicy
{
    private readonly List<(bool Allow, string Path)> _rules = new();

    public static RobotsPolicy AllowAll { get; } = new();

    public static RobotsPolicy Parse(string content, string userAgent)
    {
        var policy = new RobotsPolicy();
        var groups = new List<(List<string> Agents, List<(bool Allow, string Path)> Rules)>();
        List<string>? currentAgents = null;
        List<(bool Allow, string Path)>? currentRules = null;
        bool rulesStarted = false;

        foreach (string raw in content.Split('\n'))
        {
            string line = raw.Split('#', 2)[0].Trim();
            if (line.Length == 0) continue;
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            string key = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();

            if (key.Equals("user-agent", StringComparison.OrdinalIgnoreCase))
            {
                if (currentAgents is null || rulesStarted)
                {
                    currentAgents = new List<string>();
                    currentRules = new List<(bool Allow, string Path)>();
                    groups.Add((currentAgents, currentRules));
                    rulesStarted = false;
                }
                currentAgents.Add(value);
            }
            else if (currentAgents is not null && currentRules is not null &&
                     (key.Equals("allow", StringComparison.OrdinalIgnoreCase) || key.Equals("disallow", StringComparison.OrdinalIgnoreCase)))
            {
                rulesStarted = true;
                if (value.Length == 0) continue;
                currentRules.Add((key.Equals("allow", StringComparison.OrdinalIgnoreCase), value));
            }
        }

        var exact = groups.Where(g => g.Agents.Any(a => a != "*" && userAgent.Contains(a, StringComparison.OrdinalIgnoreCase))).ToList();
        var selected = exact.Count > 0 ? exact : groups.Where(g => g.Agents.Any(a => a == "*")).ToList();
        foreach (var group in selected) policy._rules.AddRange(group.Rules);
        return policy;
    }

    public bool CanFetch(Uri uri)
    {
        if (_rules.Count == 0) return true;
        string path = uri.PathAndQuery;
        var matching = _rules
            .Where(r => RuleMatches(r.Path, path))
            .OrderByDescending(r => EffectiveLength(r.Path))
            .ToList();
        if (matching.Count == 0) return true;
        int maxLen = EffectiveLength(matching[0].Path);
        var strongest = matching.Where(r => EffectiveLength(r.Path) == maxLen).ToList();
        return strongest.Any(r => r.Allow);
    }

    private static int EffectiveLength(string rule) => rule.Replace("*", "").TrimEnd('$').Length;

    private static bool RuleMatches(string rule, string path)
    {
        bool endAnchored = rule.EndsWith('$');
        if (endAnchored) rule = rule[..^1];
        string[] pieces = rule.Split('*');
        int position = 0;
        bool first = true;
        foreach (string piece in pieces)
        {
            if (piece.Length == 0) continue;
            int found = path.IndexOf(piece, position, StringComparison.Ordinal);
            if (found < 0) return false;
            if (first && !rule.StartsWith('*') && found != 0) return false;
            first = false;
            position = found + piece.Length;
        }
        if (endAnchored && pieces.Length > 0)
        {
            string last = pieces[^1];
            if (last.Length > 0 && !path.EndsWith(last, StringComparison.Ordinal)) return false;
        }
        return true;
    }
}

internal sealed class RobotsCache
{
    private const string AgentToken = "WebsiteViewer";
    private readonly HttpClient _client;
    private readonly ConcurrentDictionary<string, Lazy<Task<RobotsPolicy>>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public RobotsCache(HttpClient client) => _client = client;

    public Task<RobotsPolicy> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        string key = uri.GetLeftPart(UriPartial.Authority);
        var lazy = _cache.GetOrAdd(key, _ => new Lazy<Task<RobotsPolicy>>(() => LoadAsync(uri, cancellationToken)));
        return lazy.Value;
    }

    private async Task<RobotsPolicy> LoadAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            var robotsUri = new Uri(uri.GetLeftPart(UriPartial.Authority) + "/robots.txt");
            using var response = await _client.GetAsync(robotsUri, cancellationToken);
            if (!response.IsSuccessStatusCode) return RobotsPolicy.AllowAll;
            string text = await response.Content.ReadAsStringAsync(cancellationToken);
            return RobotsPolicy.Parse(text, AgentToken);
        }
        catch
        {
            return RobotsPolicy.AllowAll;
        }
    }
}
