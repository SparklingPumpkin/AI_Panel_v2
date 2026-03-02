using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;

using AI_Panel_v2.Contracts.Services;
using AI_Panel_v2.Helpers;
using AI_Panel_v2.Models;

using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace AI_Panel_v2.Services;

public class WebViewService : IWebViewService
{
    private const string DefaultDownloadPath = @"D:\";
    private WebView2? _webView;
    private CoreWebView2? _coreWebView2;
    private Uri? _pendingSource;
    private readonly ILocalSettingsService _localSettingsService;

    public Uri? Source => _coreWebView2?.Source != null ? new Uri(_coreWebView2.Source) : _webView?.Source;

    [MemberNotNullWhen(true, nameof(_webView))]
    public bool CanGoBack => _webView != null && _webView.CanGoBack;

    [MemberNotNullWhen(true, nameof(_webView))]
    public bool CanGoForward => _webView != null && _webView.CanGoForward;

    public event EventHandler<CoreWebView2WebErrorStatus>? NavigationCompleted;

    public WebViewService(ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;
    }

    [MemberNotNull(nameof(_webView))]
    public void Initialize(WebView2 webView)
    {
        _webView = webView;
        _webView.NavigationCompleted += OnWebViewNavigationCompleted;
        _ = EnsureWebView2InitializedAsync();
    }

    public void Navigate(Uri source)
    {
        _pendingSource = source;
        if (_coreWebView2 != null)
        {
            _webView!.Source = source;
        }
    }

    public void GoBack() => _webView?.GoBack();

    public void GoForward() => _webView?.GoForward();

    public void Reload() => _webView?.Reload();

    public async Task<IReadOnlyList<BrowserExtensionInfo>> GetExtensionsAsync()
    {
        var profile = await GetProfileAsync();
        if (profile == null)
        {
            return [];
        }

        try
        {
            var optionsUrls = await ReadExtensionOptionsUrlsAsync();
            var popupUrls = await ReadExtensionPopupUrlsAsync();
            var extensions = await profile.GetBrowserExtensionsAsync();
            return extensions.Select(x => new BrowserExtensionInfo
            {
                Id = x.Id,
                Name = x.Name,
                IsEnabled = x.IsEnabled,
                OptionsUrl = optionsUrls.TryGetValue(x.Id, out var optionsUrl) ? optionsUrl : null,
                PopupUrl = popupUrls.TryGetValue(x.Id, out var popupUrl) ? popupUrl : null
            }).ToList();
        }
        catch
        {
            return [];
        }
    }

    public async Task<bool> EnsureInitializedAsync()
    {
        await EnsureWebView2InitializedAsync();
        return _coreWebView2 != null;
    }

    public async Task<(bool Success, string? ErrorMessage, BrowserExtensionInfo? Extension)> InstallExtensionFromFolderAsync(string folderPath)
    {
        var resolvedFolderPath = ResolveExtensionFolderPath(folderPath);
        if (string.IsNullOrWhiteSpace(resolvedFolderPath))
        {
            return (false, "Extension folder is invalid or manifest.json was not found.", null);
        }

        var profile = await GetProfileAsync();
        if (profile == null)
        {
            return (false, "WebView is not initialized yet.", null);
        }

        try
        {
            var extension = await profile.AddBrowserExtensionAsync(resolvedFolderPath);
            var optionsPagePath = ReadOptionsPagePathFromManifest(resolvedFolderPath);
            var optionsUrl = BuildOptionsUrl(extension.Id, optionsPagePath);
            if (!string.IsNullOrWhiteSpace(optionsUrl))
            {
                await SaveExtensionOptionsUrlAsync(extension.Id, optionsUrl);
            }
            var popupPagePath = ReadPopupPagePathFromManifest(resolvedFolderPath);
            var popupUrl = BuildOptionsUrl(extension.Id, popupPagePath);
            if (!string.IsNullOrWhiteSpace(popupUrl))
            {
                await SaveExtensionPopupUrlAsync(extension.Id, popupUrl);
            }

            return (true, null, new BrowserExtensionInfo
            {
                Id = extension.Id,
                Name = extension.Name,
                IsEnabled = extension.IsEnabled,
                OptionsUrl = optionsUrl,
                PopupUrl = popupUrl
            });
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
    }

    public async Task<bool> SetExtensionEnabledAsync(string extensionId, bool isEnabled)
    {
        var extension = await FindExtensionAsync(extensionId);
        if (extension == null)
        {
            return false;
        }

        try
        {
            await extension.EnableAsync(isEnabled);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> RemoveExtensionAsync(string extensionId)
    {
        var extension = await FindExtensionAsync(extensionId);
        if (extension == null)
        {
            return false;
        }

        try
        {
            await extension.RemoveAsync();
            await RemoveExtensionOptionsUrlAsync(extensionId);
            await RemoveExtensionPopupUrlAsync(extensionId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void UnregisterEvents()
    {
        if (_webView != null)
        {
            _webView.NavigationCompleted -= OnWebViewNavigationCompleted;
        }
    }

    private async Task EnsureWebView2InitializedAsync()
    {
        if (_webView == null)
        {
            return;
        }

        if (_coreWebView2 != null)
        {
            await ApplyDownloadPathAsync(_coreWebView2);
            return;
        }

        try
        {
            var options = new CoreWebView2EnvironmentOptions
            {
                AreBrowserExtensionsEnabled = true
            };
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, null, options);
            await _webView.EnsureCoreWebView2Async(environment);
            _coreWebView2 = _webView.CoreWebView2;
            if (_coreWebView2 != null)
            {
                await ApplyDownloadPathAsync(_coreWebView2);
            }
        }
        catch
        {
        }

        if (_webView != null && _pendingSource != null)
        {
            _webView.Source = _pendingSource;
        }
    }

    private async Task<CoreWebView2Profile?> GetProfileAsync()
    {
        await EnsureWebView2InitializedAsync();
        return _coreWebView2?.Profile;
    }

    private async Task<CoreWebView2BrowserExtension?> FindExtensionAsync(string extensionId)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            return null;
        }

        var profile = await GetProfileAsync();
        if (profile == null)
        {
            return null;
        }

        try
        {
            var extensions = await profile.GetBrowserExtensionsAsync();
            return extensions.FirstOrDefault(x => string.Equals(x.Id, extensionId, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveExtensionFolderPath(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return null;
        }

        var directManifest = Path.Combine(folderPath, "manifest.json");
        if (File.Exists(directManifest))
        {
            return folderPath;
        }

        var candidates = Directory.GetDirectories(folderPath)
            .Where(path => File.Exists(Path.Combine(path, "manifest.json")))
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return candidates.Count > 0 ? candidates[0] : null;
    }

    private static string? ReadOptionsPagePathFromManifest(string extensionFolderPath)
    {
        var manifestPath = Path.Combine(extensionFolderPath, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(manifestPath);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            if (root.TryGetProperty("options_page", out var optionsPage) && optionsPage.ValueKind == JsonValueKind.String)
            {
                return optionsPage.GetString();
            }

            if (root.TryGetProperty("options_ui", out var optionsUi) && optionsUi.ValueKind == JsonValueKind.Object &&
                optionsUi.TryGetProperty("page", out var optionsUiPage) && optionsUiPage.ValueKind == JsonValueKind.String)
            {
                return optionsUiPage.GetString();
            }
        }
        catch
        {
        }

        return null;
    }

    private static string? BuildOptionsUrl(string extensionId, string? optionsPagePath)
    {
        if (string.IsNullOrWhiteSpace(extensionId) || string.IsNullOrWhiteSpace(optionsPagePath))
        {
            return null;
        }

        var normalized = optionsPagePath.Replace('\\', '/').TrimStart('/');
        return $"chrome-extension://{extensionId}/{normalized}";
    }

    private static string? ReadPopupPagePathFromManifest(string extensionFolderPath)
    {
        var manifestPath = Path.Combine(extensionFolderPath, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(manifestPath);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            if (root.TryGetProperty("action", out var action) && action.ValueKind == JsonValueKind.Object &&
                action.TryGetProperty("default_popup", out var popup) && popup.ValueKind == JsonValueKind.String)
            {
                return popup.GetString();
            }

            if (root.TryGetProperty("browser_action", out var browserAction) && browserAction.ValueKind == JsonValueKind.Object &&
                browserAction.TryGetProperty("default_popup", out var popupValue) && popupValue.ValueKind == JsonValueKind.String)
            {
                return popupValue.GetString();
            }
        }
        catch
        {
        }

        return null;
    }

    private async Task<Dictionary<string, string>> ReadExtensionOptionsUrlsAsync()
    {
        return await _localSettingsService.ReadSettingAsync<Dictionary<string, string>>(AppSettingKeys.ExtensionOptionsUrls)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private async Task SaveExtensionOptionsUrlAsync(string extensionId, string optionsUrl)
    {
        var urls = await ReadExtensionOptionsUrlsAsync();
        urls[extensionId] = optionsUrl;
        await _localSettingsService.SaveSettingAsync(AppSettingKeys.ExtensionOptionsUrls, urls);
    }

    private async Task<Dictionary<string, string>> ReadExtensionPopupUrlsAsync()
    {
        return await _localSettingsService.ReadSettingAsync<Dictionary<string, string>>(AppSettingKeys.ExtensionPopupUrls)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private async Task SaveExtensionPopupUrlAsync(string extensionId, string popupUrl)
    {
        var urls = await ReadExtensionPopupUrlsAsync();
        urls[extensionId] = popupUrl;
        await _localSettingsService.SaveSettingAsync(AppSettingKeys.ExtensionPopupUrls, urls);
    }

    private async Task RemoveExtensionOptionsUrlAsync(string extensionId)
    {
        var urls = await ReadExtensionOptionsUrlsAsync();
        if (urls.Remove(extensionId))
        {
            await _localSettingsService.SaveSettingAsync(AppSettingKeys.ExtensionOptionsUrls, urls);
        }
    }

    private async Task RemoveExtensionPopupUrlAsync(string extensionId)
    {
        var urls = await ReadExtensionPopupUrlsAsync();
        if (urls.Remove(extensionId))
        {
            await _localSettingsService.SaveSettingAsync(AppSettingKeys.ExtensionPopupUrls, urls);
        }
    }

    private async Task ApplyDownloadPathAsync(CoreWebView2 coreWebView2)
    {
        var downloadPath = await _localSettingsService.ReadSettingAsync<string>(AppSettingKeys.WebViewDownloadPath);
        if (string.IsNullOrWhiteSpace(downloadPath))
        {
            downloadPath = DefaultDownloadPath;
            await _localSettingsService.SaveSettingAsync(AppSettingKeys.WebViewDownloadPath, downloadPath);
        }

        if (string.IsNullOrWhiteSpace(downloadPath) || !Path.IsPathRooted(downloadPath))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(downloadPath);
            coreWebView2.Profile.DefaultDownloadFolderPath = downloadPath;
        }
        catch
        {
        }
    }

    private void OnWebViewNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args) => NavigationCompleted?.Invoke(this, args.WebErrorStatus);
}
