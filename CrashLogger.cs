using System.Text;

namespace WebsiteViewer;

internal static class CrashLogger
{
    private static readonly object Sync = new();

    public static string Log(Exception exception, string context)
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WebsiteViewer",
            "logs");
        Directory.CreateDirectory(root);

        string latest = Path.Combine(root, "latest_crash.log");
        string timestamped = Path.Combine(root, $"crash_{DateTime.Now:yyyyMMdd_HHmmss_fff}.log");
        string content = BuildContent(exception, context);

        lock (Sync)
        {
            File.WriteAllText(latest, content, new UTF8Encoding(false));
            File.WriteAllText(timestamped, content, new UTF8Encoding(false));
        }

        return timestamped;
    }

    private static string BuildContent(Exception ex, string context)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"WebsiteViewer {AppInfo.Version}");
        sb.AppendLine($"Time: {DateTimeOffset.Now:O}");
        sb.AppendLine($"Context: {context}");
        sb.AppendLine($"OS: {Environment.OSVersion}");
        sb.AppendLine($"Runtime: {Environment.Version}");
        sb.AppendLine($"64-bit process: {Environment.Is64BitProcess}");
        sb.AppendLine($"BaseDirectory: {AppContext.BaseDirectory}");
        sb.AppendLine();
        sb.AppendLine(ex.ToString());
        return sb.ToString();
    }
}
