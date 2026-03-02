using AI_Panel_v2.ViewModels;

using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using AI_Panel_v2.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.UI;

namespace AI_Panel_v2.Views;

// To learn more about WebView2, see https://docs.microsoft.com/microsoft-edge/webview2/.
public sealed partial class WebViewPage : Page
{
    private enum ToolbarDockPosition
    {
        Floating,
        Top,
        Left,
        Right
    }

    private const double DockEdgeThreshold = 70;
    private const double ToolbarPeekSize = 10;
    private const double ToolbarMargin = 8;
    private const double DragStartThreshold = 6;

    private bool _isLoadingExtensions;
    private ToolbarDockPosition _toolbarDockPosition = ToolbarDockPosition.Floating;
    private bool _isToolbarRevealed = true;
    private bool _isToolbarDragPrimed;
    private bool _isDraggingToolbar;
    private uint _toolbarDragPointerId;
    private Point _toolbarDragStartPoint;
    private Point _toolbarStartOffset;

    public WebViewViewModel ViewModel
    {
        get;
    }

    public WebViewPage()
    {
        ViewModel = App.GetService<WebViewViewModel>();
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        Loaded += WebViewPage_Loaded;

        ViewModel.WebViewService.Initialize(WebView);
    }

    private void WebViewPage_Loaded(object sender, RoutedEventArgs e)
    {
        PlaceToolbarAtTopCenter();
    }

    private async void ExtensionsToggleButton_Click(object sender, RoutedEventArgs e)
    {
        ExtensionsBarHost.Visibility = ExtensionsBarHost.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (ExtensionsBarHost.Visibility != Visibility.Visible)
        {
            return;
        }

        await PopulateExtensionButtonsAsync();
    }

    private void ContentArea_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ClampOrRefreshToolbarPosition();
    }

    private void ContentArea_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_isDraggingToolbar || _toolbarDockPosition == ToolbarDockPosition.Floating)
        {
            return;
        }

        var p = e.GetCurrentPoint(ContentArea).Position;
        if (IsPointerInsideWebToolsHost(p))
        {
            if (!_isToolbarRevealed)
            {
                ApplyDockReveal(true);
            }

            return;
        }

        var shouldReveal = _toolbarDockPosition switch
        {
            ToolbarDockPosition.Top => p.Y <= 26,
            ToolbarDockPosition.Left => p.X <= 26,
            ToolbarDockPosition.Right => p.X >= ContentArea.ActualWidth - 26,
            _ => true
        };

        if (shouldReveal != _isToolbarRevealed)
        {
            ApplyDockReveal(shouldReveal);
        }
    }

    private bool IsPointerInsideWebToolsHost(Point pointerInContentArea)
    {
        if (WebToolsHost.ActualWidth <= 0 || WebToolsHost.ActualHeight <= 0)
        {
            return false;
        }

        var topLeft = WebToolsHost.TransformToVisual(ContentArea).TransformPoint(new Point(0, 0));
        return pointerInContentArea.X >= topLeft.X &&
               pointerInContentArea.X <= topLeft.X + WebToolsHost.ActualWidth &&
               pointerInContentArea.Y >= topLeft.Y &&
               pointerInContentArea.Y <= topLeft.Y + WebToolsHost.ActualHeight;
    }

    private void ToolDragHandle_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(ContentArea);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _isToolbarDragPrimed = true;
        _isDraggingToolbar = false;
        _toolbarDragPointerId = e.Pointer.PointerId;
        _toolbarDragStartPoint = point.Position;
        _toolbarStartOffset = new Point(WebToolsTransform.X, WebToolsTransform.Y);

        ToolDragHandle.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void ToolDragHandle_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isToolbarDragPrimed || e.Pointer.PointerId != _toolbarDragPointerId)
        {
            return;
        }

        var current = e.GetCurrentPoint(ContentArea).Position;
        if (!_isDraggingToolbar)
        {
            var dx = current.X - _toolbarDragStartPoint.X;
            var dy = current.Y - _toolbarDragStartPoint.Y;
            if ((dx * dx) + (dy * dy) < DragStartThreshold * DragStartThreshold)
            {
                return;
            }

            _isDraggingToolbar = true;
            _toolbarDockPosition = ToolbarDockPosition.Floating;
            _isToolbarRevealed = true;
            WebToolsHost.Opacity = 0.92;
        }

        WebToolsTransform.X = _toolbarStartOffset.X + (current.X - _toolbarDragStartPoint.X);
        WebToolsTransform.Y = _toolbarStartOffset.Y + (current.Y - _toolbarDragStartPoint.Y);
        e.Handled = true;
    }

    private void ToolDragHandle_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isToolbarDragPrimed || e.Pointer.PointerId != _toolbarDragPointerId)
        {
            return;
        }

        if (_isDraggingToolbar)
        {
            SnapToolbarToDockOrFloating();
        }

        ToolDragHandle.ReleasePointerCapture(e.Pointer);
        WebToolsHost.Opacity = 1;
        _isToolbarDragPrimed = false;
        _isDraggingToolbar = false;
        _toolbarDragPointerId = 0;
        e.Handled = true;
    }

    private void PlaceToolbarAtTopCenter()
    {
        UpdateLayout();
        var maxX = Math.Max(ToolbarMargin, ContentArea.ActualWidth - WebToolsHost.ActualWidth - ToolbarMargin);
        WebToolsTransform.X = Math.Clamp((ContentArea.ActualWidth - WebToolsHost.ActualWidth) / 2, ToolbarMargin, maxX);
        WebToolsTransform.Y = ToolbarMargin;
        _toolbarDockPosition = ToolbarDockPosition.Floating;
        _isToolbarRevealed = true;
    }

    private void SnapToolbarToDockOrFloating()
    {
        var maxX = Math.Max(ToolbarMargin, ContentArea.ActualWidth - WebToolsHost.ActualWidth - ToolbarMargin);
        var maxY = Math.Max(ToolbarMargin, ContentArea.ActualHeight - WebToolsHost.ActualHeight - ToolbarMargin);

        var x = Math.Clamp(WebToolsTransform.X, ToolbarMargin, maxX);
        var y = Math.Clamp(WebToolsTransform.Y, ToolbarMargin, maxY);

        if (y <= DockEdgeThreshold)
        {
            _toolbarDockPosition = ToolbarDockPosition.Top;
            WebToolsTransform.X = x;
            WebToolsTransform.Y = ToolbarMargin;
            ApplyDockReveal(false);
            return;
        }

        if (x <= DockEdgeThreshold)
        {
            _toolbarDockPosition = ToolbarDockPosition.Left;
            WebToolsTransform.X = ToolbarMargin;
            WebToolsTransform.Y = y;
            ApplyDockReveal(false);
            return;
        }

        if (x >= maxX - DockEdgeThreshold)
        {
            _toolbarDockPosition = ToolbarDockPosition.Right;
            WebToolsTransform.X = maxX;
            WebToolsTransform.Y = y;
            ApplyDockReveal(false);
            return;
        }

        _toolbarDockPosition = ToolbarDockPosition.Floating;
        _isToolbarRevealed = true;
        WebToolsTransform.X = x;
        WebToolsTransform.Y = y;
    }

    private void ApplyDockReveal(bool reveal)
    {
        if (_toolbarDockPosition == ToolbarDockPosition.Floating)
        {
            return;
        }

        _isToolbarRevealed = reveal;
        var maxX = Math.Max(ToolbarMargin, ContentArea.ActualWidth - WebToolsHost.ActualWidth - ToolbarMargin);
        var maxY = Math.Max(ToolbarMargin, ContentArea.ActualHeight - WebToolsHost.ActualHeight - ToolbarMargin);

        switch (_toolbarDockPosition)
        {
            case ToolbarDockPosition.Top:
                WebToolsTransform.X = Math.Clamp(WebToolsTransform.X, ToolbarMargin, maxX);
                WebToolsTransform.Y = reveal ? ToolbarMargin : -(WebToolsHost.ActualHeight - ToolbarPeekSize);
                break;
            case ToolbarDockPosition.Left:
                WebToolsTransform.X = reveal ? ToolbarMargin : -(WebToolsHost.ActualWidth - ToolbarPeekSize);
                WebToolsTransform.Y = Math.Clamp(WebToolsTransform.Y, ToolbarMargin, maxY);
                break;
            case ToolbarDockPosition.Right:
                WebToolsTransform.X = reveal ? maxX : (ContentArea.ActualWidth - ToolbarPeekSize);
                WebToolsTransform.Y = Math.Clamp(WebToolsTransform.Y, ToolbarMargin, maxY);
                break;
        }
    }

    private void ClampOrRefreshToolbarPosition()
    {
        if (ContentArea.ActualWidth <= 0 || ContentArea.ActualHeight <= 0)
        {
            return;
        }

        if (_toolbarDockPosition == ToolbarDockPosition.Floating)
        {
            var maxX = Math.Max(ToolbarMargin, ContentArea.ActualWidth - WebToolsHost.ActualWidth - ToolbarMargin);
            var maxY = Math.Max(ToolbarMargin, ContentArea.ActualHeight - WebToolsHost.ActualHeight - ToolbarMargin);
            WebToolsTransform.X = Math.Clamp(WebToolsTransform.X, ToolbarMargin, maxX);
            WebToolsTransform.Y = Math.Clamp(WebToolsTransform.Y, ToolbarMargin, maxY);
            return;
        }

        ApplyDockReveal(_isToolbarRevealed);
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
                Tag = extension,
                Width = 34,
                Height = 34,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(17),
                Content = CreateExtensionIcon(extension)
            };
            ToolTipService.SetToolTip(button, extension.Name);

            if (!extension.IsEnabled)
            {
                button.Background = new SolidColorBrush(Color.FromArgb(0x33, 0x88, 0x88, 0x88));
            }

            button.Click += ExtensionItemButton_Click;
            ExtensionsButtonsPanel.Children.Add(button);
        }

        _isLoadingExtensions = false;
    }

    private static UIElement CreateExtensionIcon(BrowserExtensionInfo extension)
    {
        if (Uri.TryCreate(extension.IconUrl, UriKind.Absolute, out var iconUri))
        {
            return new Image
            {
                Source = new BitmapImage(iconUri),
                Width = 18,
                Height = 18
            };
        }

        return new FontIcon
        {
            Glyph = "\uEA86",
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
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

        try
        {
            var options = new CoreWebView2EnvironmentOptions
            {
                AreBrowserExtensionsEnabled = true
            };
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, null, options);
            await popupWebView.EnsureCoreWebView2Async(environment);
            popupWebView.Source = popupUri;
        }
        catch
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
