using System.Runtime.InteropServices;

namespace WebsiteViewer;

internal static class NativeDarkTheme
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);

    private static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);
    private static int Set(IntPtr hwnd, int attr, int value) => DwmSetWindowAttribute(hwnd, attr, ref value, sizeof(int));

    // System-drawn title bar: dark on supported versions of Windows, native resize/Snap retained.
    public static void ApplyFrame(IntPtr hwnd)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            if (Set(hwnd, 20, 1) != 0) _ = Set(hwnd, 19, 1);
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                _ = Set(hwnd, 35, ColorRef(UiTheme.Bg)); // caption
                _ = Set(hwnd, 36, ColorRef(UiTheme.Text)); // caption text
                _ = Set(hwnd, 34, unchecked((int)0xFFFFFFFE)); // no bright DWM border
            }
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    // Best-effort system scrollbar style; availability depends on Windows theme/build.
    public static void Attach(Control control)
    {
        void Apply()
        {
            if (!control.IsHandleCreated || !OperatingSystem.IsWindows()) return;
            try { _ = SetWindowTheme(control.Handle, "DarkMode_Explorer", null); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
        control.HandleCreated += (_, _) => Apply();
        if (control is DataGridView)
        {
            void ApplyChildren()
            {
                foreach (Control child in control.Controls)
                    if (child is ScrollBar) Attach(child);
            }
            ApplyChildren();
            control.ControlAdded += (_, e) => { if (e.Control is ScrollBar bar) Attach(bar); };
        }
        Apply();
    }
}
