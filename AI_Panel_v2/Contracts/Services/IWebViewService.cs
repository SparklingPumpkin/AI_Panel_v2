using AI_Panel_v2.Models;

using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace AI_Panel_v2.Contracts.Services;

public interface IWebViewService
{
    Uri? Source
    {
        get;
    }

    bool CanGoBack
    {
        get;
    }

    bool CanGoForward
    {
        get;
    }

    event EventHandler<CoreWebView2WebErrorStatus>? NavigationCompleted;

    void Initialize(WebView2 webView);

    void Navigate(Uri source);

    void GoBack();

    void GoForward();

    void Reload();

    Task<IReadOnlyList<BrowserExtensionInfo>> GetExtensionsAsync();

    Task<bool> EnsureInitializedAsync();

    Task<(bool Success, string? ErrorMessage, BrowserExtensionInfo? Extension)> InstallExtensionFromFolderAsync(string folderPath);

    Task<bool> SetExtensionEnabledAsync(string extensionId, bool isEnabled);

    Task<bool> RemoveExtensionAsync(string extensionId);

    void UnregisterEvents();
}
