namespace WebsiteViewer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ShowFatalError(e.Exception, "UI thread");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                try { CrashLogger.Log(ex, "AppDomain unhandled exception"); }
                catch { }
            }
        };

        try
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            ShowFatalError(ex, "Application startup");
        }
    }

    private static void ShowFatalError(Exception ex, string context)
    {
        string logPath = "";
        try { logPath = CrashLogger.Log(ex, context); }
        catch { }

        string detail = string.IsNullOrWhiteSpace(logPath)
            ? ex.ToString()
            : $"{ex.Message}\n\nCrash log:\n{logPath}";

        try
        {
            MessageBox.Show(
                detail,
                $"WebsiteViewer {AppInfo.Version} - Runtime Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch { }
    }
}
