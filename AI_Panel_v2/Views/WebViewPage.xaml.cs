using AI_Panel_v2.ViewModels;

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using AI_Panel_v2.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace AI_Panel_v2.Views;

// To learn more about WebView2, see https://docs.microsoft.com/microsoft-edge/webview2/.
public sealed partial class WebViewPage : Page
{
    private bool _isLoadingExtensions;

    public WebViewViewModel ViewModel
    {
        get;
    }

    public WebViewPage()
    {
        ViewModel = App.GetService<WebViewViewModel>();
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;

        ViewModel.WebViewService.Initialize(WebView);
    }

    private async void ExtensionsBarHost_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        ExtensionsButtonsPanel.Visibility = Visibility.Visible;
        await PopulateExtensionButtonsAsync();
    }

    private void ExtensionsBarHost_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        ExtensionsButtonsPanel.Visibility = Visibility.Collapsed;
    }

    private async Task PopulateExtensionButtonsAsync()
    {
        if (_isLoadingExtensions)
        {
            return;
        }

        _isLoadingExtensions = true;
        var extensions = await ViewModel.WebViewService.GetExtensionsAsync();
        ExtensionsButtonsPanel.Children.Clear();

        if (extensions.Count == 0)
        {
            ExtensionsButtonsPanel.Children.Add(new TextBlock
            {
                Text = "No extensions",
                VerticalAlignment = VerticalAlignment.Center
            });
            _isLoadingExtensions = false;
            return;
        }

        foreach (var extension in extensions)
        {
            var button = new Button
            {
                Content = extension.Name,
                Tag = extension,
                Padding = new Thickness(10, 4, 10, 4),
                MinWidth = 86,
                CornerRadius = new CornerRadius(14)
            };

            if (!extension.IsEnabled)
            {
                button.Background = new SolidColorBrush(Color.FromArgb(0x33, 0x88, 0x88, 0x88));
            }

            button.Click += ExtensionItemButton_Click;
            ExtensionsButtonsPanel.Children.Add(button);
        }

        _isLoadingExtensions = false;
    }

    private async void ExtensionItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not BrowserExtensionInfo extension)
        {
            return;
        }

        if (!extension.IsEnabled)
        {
            var enabled = await ViewModel.WebViewService.SetExtensionEnabledAsync(extension.Id, true);
            if (!enabled)
            {
                await ShowSimpleMessageAsync("Extensions", $"Failed to enable {extension.Name}.");
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(extension.PopupUrl) && Uri.TryCreate(extension.PopupUrl, UriKind.Absolute, out var popupUri))
        {
            await ShowExtensionPopupDialogAsync(extension.Name, popupUri);
            return;
        }

        if (!string.IsNullOrWhiteSpace(extension.OptionsUrl) && Uri.TryCreate(extension.OptionsUrl, UriKind.Absolute, out var optionsUri))
        {
            ViewModel.WebViewService.Navigate(optionsUri);
            return;
        }

        await ShowSimpleMessageAsync("Extensions", $"{extension.Name} has no popup/options page.");
    }

    private async Task ShowExtensionPopupDialogAsync(string extensionName, Uri popupUri)
    {
        var popupWebView = new WebView2
        {
            Width = 360,
            Height = 620
        };
        var initialized = false;

        try
        {
            var options = new CoreWebView2EnvironmentOptions
            {
                AreBrowserExtensionsEnabled = true
            };
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, null, options);
            await popupWebView.EnsureCoreWebView2Async(environment);
            popupWebView.Source = popupUri;
            initialized = true;
        }
        catch
        {
            await ShowSimpleMessageAsync("Extensions", "Failed to open extension popup panel.");
            return;
        }

        if (!initialized)
        {
            await ShowSimpleMessageAsync("Extensions", "Failed to open extension popup panel.");
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = extensionName,
            Content = popupWebView,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };

        await dialog.ShowAsync();
    }

    private async Task ShowSimpleMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close
        };

        await dialog.ShowAsync();
    }
}
