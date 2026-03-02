using AI_Panel_v2.ViewModels;
using AI_Panel_v2.Contracts.Services;
using AI_Panel_v2.Helpers;

using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using AI_Panel_v2.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.UI;
using System.IO;
using System.Globalization;

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
    private bool _isUnloaded;
    private Storyboard? _toolbarDockStoryboard;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _dockHideTimer;
    private uint _toolbarDragPointerId;
    private Point _toolbarDragStartPoint;
    private Point _toolbarStartOffset;
    private readonly ILocalSettingsService _localSettingsService;
    private string? _backgroundImageCssUrl;
    private double _backgroundImageOpacity = 1.0;
    private double _webLayerOpacity = 0.5;
    private double _webLayerBlur;

    public WebViewViewModel ViewModel
    {
        get;
    }

    public WebViewPage()
    {
        ViewModel = App.GetService<WebViewViewModel>();
        _localSettingsService = App.GetService<ILocalSettingsService>();
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        _dockHideTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        _dockHideTimer.Interval = TimeSpan.FromMilliseconds(240);
        _dockHideTimer.Tick += (_, _) =>
        {
            _dockHideTimer.Stop();
            if (_toolbarDockPosition != ToolbarDockPosition.Floating)
            {
                ApplyDockReveal(false);
            }
        };
        Loaded += WebViewPage_Loaded;
        Unloaded += WebViewPage_Unloaded;

        ViewModel.WebViewService.Initialize(WebView);
    }

    private async void WebViewPage_Loaded(object sender, RoutedEventArgs e)
    {
        _isUnloaded = false;
        try
        {
            WebView.NavigationCompleted -= WebView_NavigationCompleted;
            WebView.NavigationCompleted += WebView_NavigationCompleted;
            PlaceToolbarAtTopCenter();
            await ApplyWebToolsThemeAsync();
            await LoadWebBackgroundImageAsync();
            await TryInjectWebBackgroundAsync();
            UpdateWebCardModeIcon();
        }
        catch
        {
        }
    }

    private void WebViewPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _isUnloaded = true;
        WebView.NavigationCompleted -= WebView_NavigationCompleted;
    }

    private async void WebView_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (_isUnloaded)
        {
            return;
        }

        try
        {
            await TryInjectWebBackgroundAsync();
        }
        catch
        {
        }
    }

    public async Task RefreshWebBackgroundVisualAsync()
    {
        _backgroundImageCssUrl = null;
        await LoadWebBackgroundImageAsync();
        await TryInjectWebBackgroundAsync();
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

    private void EdgeRevealArea_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _dockHideTimer.Stop();
        if (_toolbarDockPosition != ToolbarDockPosition.Floating && !_isToolbarRevealed)
        {
            ApplyDockReveal(true);
        }
    }

    private void EdgeRevealArea_PointerExited(object sender, PointerRoutedEventArgs e) => TryHideDockedToolbar();

    private void WebToolsHost_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _dockHideTimer.Stop();
        if (_toolbarDockPosition != ToolbarDockPosition.Floating && !_isToolbarRevealed)
        {
            ApplyDockReveal(true);
        }
    }

    private void WebToolsHost_PointerExited(object sender, PointerRoutedEventArgs e) => TryHideDockedToolbar();

    private void TryHideDockedToolbar()
    {
        if (_toolbarDockPosition == ToolbarDockPosition.Floating)
        {
            return;
        }

        _dockHideTimer.Stop();
        _dockHideTimer.Start();
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
                AnimateToolbarTo(
                    Math.Clamp(WebToolsTransform.X, ToolbarMargin, maxX),
                    reveal ? ToolbarMargin : -(WebToolsHost.ActualHeight - ToolbarPeekSize));
                break;
            case ToolbarDockPosition.Left:
                AnimateToolbarTo(
                    reveal ? ToolbarMargin : -(WebToolsHost.ActualWidth - ToolbarPeekSize),
                    Math.Clamp(WebToolsTransform.Y, ToolbarMargin, maxY));
                break;
            case ToolbarDockPosition.Right:
                AnimateToolbarTo(
                    reveal ? maxX : (ContentArea.ActualWidth - ToolbarPeekSize),
                    Math.Clamp(WebToolsTransform.Y, ToolbarMargin, maxY));
                break;
        }
    }

    private void AnimateToolbarTo(double targetX, double targetY)
    {
        _toolbarDockStoryboard?.Stop();
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var duration = new Duration(TimeSpan.FromMilliseconds(420));
        var storyboard = new Storyboard();
        var xAnimation = new DoubleAnimation
        {
            To = targetX,
            Duration = duration,
            EasingFunction = easing,
            EnableDependentAnimation = true
        };
        var yAnimation = new DoubleAnimation
        {
            To = targetY,
            Duration = duration,
            EasingFunction = easing,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(xAnimation, WebToolsTransform);
        Storyboard.SetTargetProperty(xAnimation, "X");
        Storyboard.SetTarget(yAnimation, WebToolsTransform);
        Storyboard.SetTargetProperty(yAnimation, "Y");
        storyboard.Children.Add(xAnimation);
        storyboard.Children.Add(yAnimation);
        _toolbarDockStoryboard = storyboard;
        storyboard.Begin();
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

    private async Task ApplyWebToolsThemeAsync()
    {
        var savedThemeOpacity = await _localSettingsService.ReadSettingAsync<double?>(AppSettingKeys.ThemeColorOpacity);
        var themeOpacity = Math.Clamp(savedThemeOpacity ?? 1.0, 0, 1);
        var savedThemeBlur = await _localSettingsService.ReadSettingAsync<double?>(AppSettingKeys.ThemeColorBlur);
        var themeBlur = Math.Clamp(savedThemeBlur ?? 0, 0, 30);
        var tint = await ResolveThemeColorAsync();
        var intensity = Math.Clamp(themeBlur / 30.0, 0, 1);
        var tintOpacity = Math.Clamp((0.78 - (0.58 * intensity)) * Math.Max(0.22, themeOpacity), 0.08, 0.92);

        if (themeBlur > 0)
        {
            WebToolsHost.Background = new AcrylicBrush
            {
                TintColor = tint,
                TintOpacity = tintOpacity,
                FallbackColor = Color.FromArgb((byte)Math.Round(255 * Math.Max(0.18, themeOpacity)), tint.R, tint.G, tint.B)
            };
        }
        else
        {
            WebToolsHost.Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * themeOpacity), tint.R, tint.G, tint.B));
        }

        var foregroundColor = GetReadableForegroundColor(tint);
        var foregroundBrush = new SolidColorBrush(foregroundColor);
        ApplyForegroundRecursively(WebToolsHost, foregroundBrush);
        WebToolsHost.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, foregroundColor == Colors.White ? (byte)255 : (byte)0, foregroundColor == Colors.White ? (byte)255 : (byte)0, foregroundColor == Colors.White ? (byte)255 : (byte)0));
    }

    private async Task<Color> ResolveThemeColorAsync()
    {
        var themeMode = await _localSettingsService.ReadSettingAsync<string>(AppSettingKeys.ThemeMode);
        if (string.Equals(themeMode, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            var accentColorText = await _localSettingsService.ReadSettingAsync<string>(AppSettingKeys.AccentColor);
            if (AccentPaletteHelper.TryParseHex(accentColorText, out var customAccent))
            {
                return customAccent;
            }

            var paletteName = await _localSettingsService.ReadSettingAsync<string>(AppSettingKeys.AccentPalette);
            return AccentPaletteHelper.GetByName(paletteName).Accent;
        }

        if (Application.Current.Resources.TryGetValue("LayerFillColorDefaultBrush", out var layer) && layer is SolidColorBrush layerBrush)
        {
            return layerBrush.Color;
        }

        return Color.FromArgb(0xFF, 0x20, 0x20, 0x20);
    }

    private static Color GetReadableForegroundColor(Color background)
    {
        var luminance =
            (0.2126 * background.R / 255.0) +
            (0.7152 * background.G / 255.0) +
            (0.0722 * background.B / 255.0);
        return luminance < 0.5 ? Colors.White : Colors.Black;
    }

    private static void ApplyForegroundRecursively(DependencyObject node, Brush foreground)
    {
        if (node is Control control)
        {
            control.Foreground = foreground;
        }
        else if (node is FontIcon icon)
        {
            icon.Foreground = foreground;
        }
        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            ApplyForegroundRecursively(VisualTreeHelper.GetChild(node, i), foreground);
        }
    }

    private void WebCardModeButton_Click(object sender, RoutedEventArgs e)
    {
        var shell = ResolveShellPage();
        if (shell == null)
        {
            return;
        }

        shell.SetChromeHiddenForWebCard(!shell.IsChromeHiddenForWebCard);
        UpdateWebCardModeIcon();
    }

    private void UpdateWebCardModeIcon()
    {
        var shell = ResolveShellPage();
        if (shell == null)
        {
            return;
        }

        WebCardModeIcon.Glyph = shell.IsChromeHiddenForWebCard ? "\uE73F" : "\uE740";
    }

    private static ShellPage? ResolveShellPage()
    {
        if (App.MainWindow.Content is ShellPage shellPage)
        {
            return shellPage;
        }

        if (App.MainWindow.Content is Frame rootFrame && rootFrame.Content is ShellPage frameShell)
        {
            return frameShell;
        }

        return null;
    }

    private async Task LoadWebBackgroundImageAsync()
    {
        _backgroundImageCssUrl = null;
        var savedOpacity = await _localSettingsService.ReadSettingAsync<double?>(AppSettingKeys.BackgroundImageOpacity);
        _backgroundImageOpacity = Math.Clamp(savedOpacity ?? 1.0, 0, 1);
        var savedWebLayerOpacity = await _localSettingsService.ReadSettingAsync<double?>(AppSettingKeys.WebLayerOpacity);
        _webLayerOpacity = Math.Clamp(savedWebLayerOpacity ?? 0.5, 0, 1);
        var savedWebLayerBlur = await _localSettingsService.ReadSettingAsync<double?>(AppSettingKeys.WebLayerBlur);
        _webLayerBlur = Math.Clamp(savedWebLayerBlur ?? 0, 0, 30);

        var imagePath = await _localSettingsService.ReadSettingAsync<string>(AppSettingKeys.BackgroundImagePath);
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return;
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(imagePath);
            if (bytes.Length == 0)
            {
                return;
            }

            var ext = Path.GetExtension(imagePath).ToLowerInvariant();
            var mimeType = ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".bmp" => "image/bmp",
                ".webp" => "image/webp",
                _ => "image/png"
            };

            _backgroundImageCssUrl = $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
        }
        catch
        {
            _backgroundImageCssUrl = null;
        }
    }

    private async Task TryInjectWebBackgroundAsync()
    {
        if (string.IsNullOrWhiteSpace(_backgroundImageCssUrl))
        {
            await LoadWebBackgroundImageAsync();
        }

        if (string.IsNullOrWhiteSpace(_backgroundImageCssUrl))
        {
            return;
        }

        var cssUrl = _backgroundImageCssUrl.Replace("\\", "\\\\").Replace("'", "\\'");
        var opacityText = _backgroundImageOpacity.ToString("0.###", CultureInfo.InvariantCulture);
        var webLayerOpacityText = _webLayerOpacity.ToString("0.###", CultureInfo.InvariantCulture);
        var webLayerBlurPx = _webLayerBlur * 0.8;
        var webLayerBlurText = webLayerBlurPx.ToString("0.###", CultureInfo.InvariantCulture);
        var script = "(function(){" +
                     "const styleId='ai-panel-bg-style';" +
                     "let style=document.getElementById(styleId);" +
                     "if(!style){style=document.createElement('style');style.id=styleId;document.documentElement.appendChild(style);}" +
                     "style.textContent=\"" +
                     "html,body{background:transparent !important;}" +
                     "body::before{content:'';position:fixed;inset:0;background-image:url('" + cssUrl + "');" +
                     "background-size:cover;background-position:center;background-repeat:no-repeat;" +
                     "background-attachment:fixed;filter:blur(" + webLayerBlurText + "px);transform:scale(1.03);" +
                     "opacity:" + opacityText + ";z-index:-2147483647;pointer-events:none;}" +
                     "body::after{content:'';position:fixed;inset:0;background:rgba(255,255,255," + webLayerOpacityText + ");backdrop-filter:blur(" + webLayerBlurText + "px);z-index:-2147483646;pointer-events:none;}" +
                     "#app,#root,main,.app,.container{background-color:transparent !important;}" +
                     "\";" +
                     "})();";

        try
        {
            await WebView.ExecuteScriptAsync(script);
        }
        catch
        {
        }
    }
}
