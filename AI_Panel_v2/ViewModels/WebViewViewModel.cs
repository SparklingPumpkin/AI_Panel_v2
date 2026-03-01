using AI_Panel_v2.Contracts.Services;
using AI_Panel_v2.Contracts.ViewModels;
using AI_Panel_v2.Helpers;
using AI_Panel_v2.Models;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Web.WebView2.Core;

namespace AI_Panel_v2.ViewModels;

// TODO: Review best practices and distribution guidelines for WebView2.
// https://docs.microsoft.com/microsoft-edge/webview2/get-started/winui
// https://docs.microsoft.com/microsoft-edge/webview2/concepts/developer-guide
// https://docs.microsoft.com/microsoft-edge/webview2/concepts/distribution
public partial class WebViewViewModel : ObservableRecipient, INavigationAware
{
    private const string FallbackWebUrl = "https://docs.microsoft.com/windows/apps/";
    // TODO: Set the default URL to display.
    [ObservableProperty]
    private Uri source = new(FallbackWebUrl);

    [ObservableProperty]
    private bool isLoading = true;

    [ObservableProperty]
    private bool hasFailures;

    private readonly ILocalSettingsService _localSettingsService;

    public IWebViewService WebViewService
    {
        get;
    }

    public WebViewViewModel(IWebViewService webViewService, ILocalSettingsService localSettingsService)
    {
        WebViewService = webViewService;
        _localSettingsService = localSettingsService;
    }

    [RelayCommand]
    private async Task OpenInBrowser()
    {
        if (WebViewService.Source != null)
        {
            await Windows.System.Launcher.LaunchUriAsync(WebViewService.Source);
        }
    }

    [RelayCommand]
    private void Reload()
    {
        WebViewService.Reload();
    }

    [RelayCommand(CanExecute = nameof(BrowserCanGoForward))]
    private void BrowserForward()
    {
        if (WebViewService.CanGoForward)
        {
            WebViewService.GoForward();
        }
    }

    private bool BrowserCanGoForward()
    {
        return WebViewService.CanGoForward;
    }

    [RelayCommand(CanExecute = nameof(BrowserCanGoBack))]
    private void BrowserBack()
    {
        if (WebViewService.CanGoBack)
        {
            WebViewService.GoBack();
        }
    }

    private bool BrowserCanGoBack()
    {
        return WebViewService.CanGoBack;
    }

    public async void OnNavigatedTo(object parameter)
    {
        try
        {
            var targetSource = await ResolveSourceAsync(parameter);
            if (Uri.Compare(Source, targetSource, UriComponents.AbsoluteUri, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0)
            {
                IsLoading = false;
                HasFailures = false;
                return;
            }

            IsLoading = true;
            HasFailures = false;
            Source = targetSource;
            WebViewService.NavigationCompleted -= OnNavigationCompleted;
            WebViewService.NavigationCompleted += OnNavigationCompleted;
        }
        catch
        {
            Source = new Uri(FallbackWebUrl);
            IsLoading = false;
            HasFailures = true;
        }
    }

    public void OnNavigatedFrom()
    {
        WebViewService.UnregisterEvents();
        WebViewService.NavigationCompleted -= OnNavigationCompleted;
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2WebErrorStatus webErrorStatus)
    {
        IsLoading = false;
        BrowserBackCommand.NotifyCanExecuteChanged();
        BrowserForwardCommand.NotifyCanExecuteChanged();

        if (webErrorStatus != default)
        {
            HasFailures = true;
        }
    }

    [RelayCommand]
    private void OnRetry()
    {
        HasFailures = false;
        IsLoading = true;
        WebViewService?.Reload();
    }

    private async Task<Uri> ResolveSourceAsync(object parameter)
    {
        var webItems = await _localSettingsService.ReadSettingAsync<List<WebItemSetting>>(AppSettingKeys.WebItems);
        if (parameter is int index && webItems != null && index >= 0 && index < webItems.Count)
        {
            var itemUrl = webItems[index].Url;
            if (Uri.TryCreate(itemUrl, UriKind.Absolute, out var itemUri))
            {
                return itemUri;
            }
        }

        if (webItems != null && webItems.Count > 0 && Uri.TryCreate(webItems[0].Url, UriKind.Absolute, out var firstItemUri))
        {
            return firstItemUri;
        }

        return new Uri(FallbackWebUrl);
    }
}
