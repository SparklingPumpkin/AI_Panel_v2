using System.Diagnostics;
using System.IO;

namespace AI_Panel_v2.Helpers;

public static class WebViewPathHelper
{
    public static string GetAppWebViewUserDataDir()
    {
        var processName = Process.GetCurrentProcess().ProcessName;
        var userDataDir = Path.Combine(AppContext.BaseDirectory, $"{processName}.exe.WebView2", "EBWebView");
        Directory.CreateDirectory(userDataDir);
        return userDataDir;
    }
}
