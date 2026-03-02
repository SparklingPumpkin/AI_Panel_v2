using AI_Panel_v2.Contracts.Services;
using AI_Panel_v2.Helpers;
using AI_Panel_v2.Models;
using AI_Panel_v2.ViewModels;

using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;

using Windows.System;
using Windows.UI;

namespace AI_Panel_v2.Views;

// TODO: Update NavigationViewItem titles and icons in ShellPage.xaml.
public sealed partial class ShellPage : Page
{
    private static readonly string[] CustomAppResourceKeys =
    [
        "ButtonBackground",
        "ButtonBackgroundPointerOver",
        "ButtonBackgroundPressed",
        "ButtonForeground",
        "AccentFillColorDefaultBrush",
        "AccentFillColorSecondaryBrush",
        "TextOnAccentFillColorPrimaryBrush",
        "ControlFillColorDefaultBrush",
        "ControlFillColorSecondaryBrush",
        "ControlFillColorTertiaryBrush",
        "CardBackgroundFillColorDefaultBrush",
        "CardStrokeColorDefaultBrush"
    ];

    private const string FallbackWebUrl = "https://docs.microsoft.com/windows/apps/";
    private readonly ILocalSettingsService _localSettingsService;
    private Brush? _customSecondLightBrush;
    private Color? _customAccentColor;
    private string? _backgroundImagePath;
    private double _backgroundImageOpacity = 1.0;
    private double _backgroundImageBlur;
    private double _themeOpacity = 1.0;
    private double _themeBlur;
    private bool _isChromeHiddenByWebCard;
    private bool _isCustomChromeApplied;
    private readonly List<NavigationViewItem> _dynamicWebItems = new();
    private readonly List<NavigationViewItem> _dynamicPageItems = new();
    private readonly Dictionary<string, int> _pageTypeCounters = new()
    {
        [typeof(DataGridViewModel).FullName!] = 0,
        [typeof(ContentGridViewModel).FullName!] = 0,
        [typeof(ListDetailsViewModel).FullName!] = 0
    };
    private readonly List<PageTypeOption> _pageTypeOptions =
    [
        new PageTypeOption("Web", typeof(WebViewViewModel).FullName!, "\uE774"),
        new PageTypeOption("Data Grid", typeof(DataGridViewModel).FullName!, "\uE9D2"),
        new PageTypeOption("Content Grid", typeof(ContentGridViewModel).FullName!, "\uE14C"),
        new PageTypeOption("List Details", typeof(ListDetailsViewModel).FullName!, "\uE8FD")
    ];

    public ShellViewModel ViewModel
    {
        get;
    }

    public ShellPage(ShellViewModel viewModel)
    {
        ViewModel = viewModel;
        _localSettingsService = App.GetService<ILocalSettingsService>();
        InitializeComponent();

        ViewModel.NavigationService.Frame = NavigationFrame;
        ViewModel.NavigationViewService.Initialize(NavigationViewControl);

        // TODO: Set the title bar icon by updating /Assets/WindowIcon.ico.
        // A custom title bar is required for full window theme and Mica support.
        // https://docs.microsoft.com/windows/apps/develop/title-bar?tabs=winui3#full-customization
        App.MainWindow.ExtendsContentIntoTitleBar = true;
        App.MainWindow.SetTitleBar(AppTitleBar);
        App.MainWindow.Activated += MainWindow_Activated;
        AppTitleBarText.Text = "AppDisplayName".GetLocalized();
    }

    private async void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "PowerAction.png");
        if (File.Exists(iconPath))
        {
            LowPowerIconImage.Source = new BitmapImage(new Uri(iconPath));
        }

        TitleBarHelper.UpdateTitleBar(RequestedTheme);

        KeyboardAccelerators.Add(BuildKeyboardAccelerator(VirtualKey.Left, VirtualKeyModifiers.Menu));
        KeyboardAccelerators.Add(BuildKeyboardAccelerator(VirtualKey.GoBack));

        if (App.MainWindow is MainWindow mainWindow)
        {
            PinToggleButton.IsChecked = mainWindow.IsPinned;
        }

        await ReloadWebItemsAsync();
        NavigateToFirstWebItemOnStartup();
        UpdateCloseButtonsVisibility();
        await ApplySavedChromeThemeAsync();
        await ApplySavedBackgroundImageAsync();
        await ApplySavedProgramTypographyAsync();
        ApplyNavigationContrastBrush();
        ApplyPinPressedVisual();
    }

    public void ApplyCustomChromeTheme(Color accent, double themeOpacity = 1.0, double themeBlur = 0)
    {
        _customAccentColor = accent;
        _themeOpacity = Math.Clamp(themeOpacity, 0, 1);
        _themeBlur = Math.Clamp(themeBlur, 0, 30);
        var darkest = Blend(accent, Colors.Black, 0.55);
        var secondDark = Blend(accent, Colors.Black, 0.35);
        var middle = Color.FromArgb((byte)Math.Round(255 * _themeOpacity), accent.R, accent.G, accent.B);
        var secondLight = Blend(accent, Colors.White, 0.65);
        var middleBrush = CreateAcrylicTintBrush(middle, _themeBlur, _themeOpacity);

        AppTitleBar.Background = middleBrush;
        RootLayout.Background = middleBrush;
        ApplyPaneBrushOverrides(middleBrush);
        ApplyAppBrushOverrides(darkest, secondDark, secondLight, _themeOpacity);
        PinToggleButton.Foreground = new SolidColorBrush(secondDark);
        _customSecondLightBrush = new SolidColorBrush(secondLight);
        _isCustomChromeApplied = true;
        ApplyBackgroundImageOverlayIfAvailable();
        ApplyNavigationContrastBrush();
        ApplyPinPressedVisual();
    }

    public void ClearCustomChromeTheme()
    {
        _customAccentColor = null;
        _themeBlur = 0;
        if (Application.Current.Resources.TryGetValue("ApplicationPageBackgroundThemeBrush", out var pageBrush) && pageBrush is Brush pageBackground)
        {
            RootLayout.Background = pageBackground;
        }

        if (Application.Current.Resources.TryGetValue("LayerFillColorDefaultBrush", out var layerBrush) && layerBrush is Brush layerBackground)
        {
            AppTitleBar.Background = layerBackground;
        }

        RemovePaneBrushOverrides();
        RemoveAppBrushOverrides();
        PinToggleButton.ClearValue(Control.ForegroundProperty);
        PinToggleButton.ClearValue(Control.BackgroundProperty);
        _customSecondLightBrush = null;
        _isCustomChromeApplied = false;
        ApplyBackgroundImageOverlayIfAvailable();
        ApplyNavigationContrastBrush();
    }

    public void ApplyBackgroundImage(string? path, double opacity = 1.0, double blur = 0)
    {
        _backgroundImagePath = string.IsNullOrWhiteSpace(path) ? null : path;
        _backgroundImageOpacity = Math.Clamp(opacity, 0, 1);
        _backgroundImageBlur = Math.Clamp(blur, 0, 30);

        if (ApplyBackgroundImageOverlayIfAvailable())
        {
            return;
        }

        if (_isCustomChromeApplied && _customAccentColor.HasValue)
        {
            ApplyCustomChromeTheme(_customAccentColor.Value, _themeOpacity, _themeBlur);
            return;
        }

        if (Application.Current.Resources.TryGetValue("ApplicationPageBackgroundThemeBrush", out var pageBrush) && pageBrush is Brush pageBackground)
        {
            RootLayout.Background = pageBackground;
        }

        if (Application.Current.Resources.TryGetValue("LayerFillColorDefaultBrush", out var layerBrush) && layerBrush is Brush layerBackground)
        {
            AppTitleBar.Background = layerBackground;
        }

        RemovePaneBrushOverrides();
        ApplyNavigationContrastBrush();
    }

    private async Task ApplySavedChromeThemeAsync()
    {
        var savedThemeOpacity = await _localSettingsService.ReadSettingAsync<double?>(AppSettingKeys.ThemeColorOpacity);
        _themeOpacity = Math.Clamp(savedThemeOpacity ?? 1.0, 0, 1);
        var savedThemeBlur = await _localSettingsService.ReadSettingAsync<double?>(AppSettingKeys.ThemeColorBlur);
        _themeBlur = Math.Clamp(savedThemeBlur ?? 0, 0, 30);

        var themeMode = await _localSettingsService.ReadSettingAsync<string>(AppSettingKeys.ThemeMode);
        if (!string.Equals(themeMode, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            ClearCustomChromeTheme();
            return;
        }

        var accentColorText = await _localSettingsService.ReadSettingAsync<string>(AppSettingKeys.AccentColor);
        if (AccentPaletteHelper.TryParseHex(accentColorText, out var customAccent))
        {
            ApplyCustomChromeTheme(customAccent, _themeOpacity, _themeBlur);
            return;
        }

        var paletteName = await _localSettingsService.ReadSettingAsync<string>(AppSettingKeys.AccentPalette);
        ApplyCustomChromeTheme(AccentPaletteHelper.GetByName(paletteName).Accent, _themeOpacity, _themeBlur);
    }

    private async Task ApplySavedBackgroundImageAsync()
    {
        _backgroundImagePath = await _localSettingsService.ReadSettingAsync<string>(AppSettingKeys.BackgroundImagePath);
        var savedImageOpacity = await _localSettingsService.ReadSettingAsync<double?>(AppSettingKeys.BackgroundImageOpacity);
        _backgroundImageOpacity = Math.Clamp(savedImageOpacity ?? 1.0, 0, 1);
        var savedImageBlur = await _localSettingsService.ReadSettingAsync<double?>(AppSettingKeys.BackgroundImageBlur);
        _backgroundImageBlur = Math.Clamp(savedImageBlur ?? 0, 0, 30);
        ApplyBackgroundImageOverlayIfAvailable();
        ApplyNavigationContrastBrush();
    }

    public void ApplyProgramFontFamily(string? fontFamily)
    {
        var trimmed = string.IsNullOrWhiteSpace(fontFamily) ? string.Empty : fontFamily.Trim();
        var toApply = string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;

        if (toApply == null)
        {
            NavigationViewControl.ClearValue(Control.FontFamilyProperty);
            NavigationFrame.ClearValue(Control.FontFamilyProperty);
            AppTitleBarText.ClearValue(Control.FontFamilyProperty);
            return;
        }

        var font = new FontFamily(toApply);
        NavigationViewControl.FontFamily = font;
        NavigationFrame.FontFamily = font;
        AppTitleBarText.FontFamily = font;
    }

    public void ApplyProgramTextStyle(double? fontSize, bool isBold, bool isItalic)
    {
        if (fontSize.HasValue && fontSize.Value > 0)
        {
            NavigationViewControl.FontSize = fontSize.Value;
            NavigationFrame.FontSize = fontSize.Value;
            AppTitleBarText.FontSize = fontSize.Value;
        }
        else
        {
            NavigationViewControl.ClearValue(Control.FontSizeProperty);
            NavigationFrame.ClearValue(Control.FontSizeProperty);
            AppTitleBarText.ClearValue(Control.FontSizeProperty);
        }

        var fontWeight = isBold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;
        var fontStyle = isItalic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal;
        NavigationViewControl.FontWeight = fontWeight;
        NavigationFrame.FontWeight = fontWeight;
        AppTitleBarText.FontWeight = fontWeight;
        NavigationViewControl.FontStyle = fontStyle;
        NavigationFrame.FontStyle = fontStyle;
        AppTitleBarText.FontStyle = fontStyle;
    }

    private async Task ApplySavedProgramTypographyAsync()
    {
        var savedFontFamily = await _localSettingsService.ReadSettingAsync<string>(AppSettingKeys.ProgramFontFamily);
        var savedFontSize = await _localSettingsService.ReadSettingAsync<double?>(AppSettingKeys.ProgramFontSize);
        var savedBold = await _localSettingsService.ReadSettingAsync<bool?>(AppSettingKeys.ProgramFontBold);
        var savedItalic = await _localSettingsService.ReadSettingAsync<bool?>(AppSettingKeys.ProgramFontItalic);
        ApplyProgramFontFamily(savedFontFamily);
        ApplyProgramTextStyle(savedFontSize.HasValue && savedFontSize.Value > 0 ? savedFontSize : null, savedBold == true, savedItalic == true);
    }

    public bool IsChromeHiddenForWebCard => _isChromeHiddenByWebCard;

    public void SetChromeHiddenForWebCard(bool hide)
    {
        _isChromeHiddenByWebCard = hide;
        AppTitleBar.Visibility = hide ? Visibility.Collapsed : Visibility.Visible;
        AppTitleBar.Height = hide ? 0 : 32;
        AppTitleBar.IsHitTestVisible = !hide;
        NavigationViewControl.IsPaneVisible = !hide;
        NavigationViewControl.IsTitleBarAutoPaddingEnabled = !hide;
        NavigationViewControl.IsBackButtonVisible = hide ? NavigationViewBackButtonVisible.Collapsed : NavigationViewBackButtonVisible.Visible;
        NavigationViewControl.IsSettingsVisible = !hide;

        if (App.MainWindow is MainWindow mainWindow)
        {
            mainWindow.PresenterKind = hide ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Overlapped;
        }
    }

    private bool ApplyBackgroundImageOverlayIfAvailable()
    {
        if (string.IsNullOrWhiteSpace(_backgroundImagePath) || !File.Exists(_backgroundImagePath))
        {
            BackgroundBlurOverlay.Visibility = Visibility.Collapsed;
            return false;
        }

        var imageUri = new Uri(_backgroundImagePath);
        RootLayout.Background = new ImageBrush
        {
            ImageSource = new BitmapImage(imageUri),
            Opacity = _backgroundImageOpacity,
            Stretch = Stretch.UniformToFill
        };
        // Keep title bar transparent so it reuses the same root image and stays seamlessly aligned.
        AppTitleBar.Background = new SolidColorBrush(Colors.Transparent);
        UpdateBackgroundBlurOverlay();

        return true;
    }

    private void UpdateBackgroundBlurOverlay()
    {
        if (_backgroundImageBlur <= 0)
        {
            BackgroundBlurOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        var intensity = Math.Clamp(_backgroundImageBlur / 30.0, 0, 1);
        var tintOpacity = Math.Clamp(0.62 - (0.52 * intensity), 0.08, 0.62);
        BackgroundBlurOverlay.Background = new AcrylicBrush
        {
            TintColor = Color.FromArgb(0xFF, 0xF5, 0xF5, 0xF5),
            TintOpacity = tintOpacity,
            FallbackColor = Color.FromArgb((byte)Math.Round(255 * Math.Clamp(0.10 + (0.20 * (1 - intensity)), 0.08, 0.30)), 0xF5, 0xF5, 0xF5)
        };
        BackgroundBlurOverlay.Visibility = Visibility.Visible;
    }

    private static Brush CreateAcrylicTintBrush(Color tintColor, double blur, double opacity)
    {
        if (blur <= 0)
        {
            return new SolidColorBrush(tintColor);
        }

        var intensity = Math.Clamp(blur / 30.0, 0, 1);
        var baseOpacity = Math.Clamp(0.78 - (0.58 * intensity), 0.14, 0.78);
        return new AcrylicBrush
        {
            TintColor = Color.FromArgb(0xFF, tintColor.R, tintColor.G, tintColor.B),
            TintOpacity = Math.Clamp(baseOpacity * Math.Max(0.22, opacity), 0.08, 0.92),
            FallbackColor = tintColor
        };
    }

    private void ApplyPaneBrushOverrides(Brush paneBrush)
    {
        NavigationViewControl.Resources["NavigationViewDefaultPaneBackground"] = paneBrush;
        NavigationViewControl.Resources["NavigationViewExpandedPaneBackground"] = paneBrush;
        NavigationViewControl.Resources["NavigationViewCompactPaneBackground"] = paneBrush;
        NavigationViewControl.Resources["NavigationViewMinimalPaneBackground"] = paneBrush;
        NavigationViewControl.Resources["NavigationViewPaneBackground"] = paneBrush;
        NavigationViewControl.Resources["NavigationViewTopPaneBackground"] = paneBrush;
        NavigationViewControl.Resources["NavigationViewLeftPaneBackground"] = paneBrush;
    }

    private void RemovePaneBrushOverrides()
    {
        NavigationViewControl.Resources.Remove("NavigationViewDefaultPaneBackground");
        NavigationViewControl.Resources.Remove("NavigationViewExpandedPaneBackground");
        NavigationViewControl.Resources.Remove("NavigationViewCompactPaneBackground");
        NavigationViewControl.Resources.Remove("NavigationViewMinimalPaneBackground");
        NavigationViewControl.Resources.Remove("NavigationViewPaneBackground");
        NavigationViewControl.Resources.Remove("NavigationViewTopPaneBackground");
        NavigationViewControl.Resources.Remove("NavigationViewLeftPaneBackground");
    }

    private static void ApplyAppBrushOverrides(Color darkest, Color secondDark, Color secondLight, double opacity)
    {
        var alpha = (byte)Math.Round(255 * Math.Clamp(opacity, 0, 1));
        var withAlphaDarkest = Color.FromArgb(alpha, darkest.R, darkest.G, darkest.B);
        var withAlphaSecondDark = Color.FromArgb(alpha, secondDark.R, secondDark.G, secondDark.B);
        var withAlphaSecondLight = Color.FromArgb(alpha, secondLight.R, secondLight.G, secondLight.B);
        var resources = Application.Current.Resources;
        resources["ButtonBackground"] = new SolidColorBrush(withAlphaSecondDark);
        resources["ButtonBackgroundPointerOver"] = new SolidColorBrush(Color.FromArgb(alpha, Blend(secondDark, Colors.Black, 0.12).R, Blend(secondDark, Colors.Black, 0.12).G, Blend(secondDark, Colors.Black, 0.12).B));
        resources["ButtonBackgroundPressed"] = new SolidColorBrush(Color.FromArgb(alpha, Blend(secondDark, Colors.Black, 0.22).R, Blend(secondDark, Colors.Black, 0.22).G, Blend(secondDark, Colors.Black, 0.22).B));
        resources["ButtonForeground"] = new SolidColorBrush(Colors.White);
        resources["AccentFillColorDefaultBrush"] = new SolidColorBrush(withAlphaDarkest);
        resources["AccentFillColorSecondaryBrush"] = new SolidColorBrush(withAlphaDarkest);
        resources["TextOnAccentFillColorPrimaryBrush"] = new SolidColorBrush(Colors.White);
        resources["ControlFillColorDefaultBrush"] = new SolidColorBrush(withAlphaSecondLight);
        resources["ControlFillColorSecondaryBrush"] = new SolidColorBrush(withAlphaSecondLight);
        resources["ControlFillColorTertiaryBrush"] = new SolidColorBrush(withAlphaSecondLight);
        var stroke = Blend(secondDark, Colors.White, 0.4);
        resources["CardStrokeColorDefaultBrush"] = new SolidColorBrush(Color.FromArgb(alpha, stroke.R, stroke.G, stroke.B));
    }

    private static void RemoveAppBrushOverrides()
    {
        var resources = Application.Current.Resources;
        foreach (var key in CustomAppResourceKeys)
        {
            resources.Remove(key);
        }
    }

    private static Color Blend(Color from, Color to, double ratio)
    {
        ratio = Math.Clamp(ratio, 0, 1);
        var r = (byte)Math.Round(from.R + (to.R - from.R) * ratio);
        var g = (byte)Math.Round(from.G + (to.G - from.G) * ratio);
        var b = (byte)Math.Round(from.B + (to.B - from.B) * ratio);
        return Color.FromArgb(0xFF, r, g, b);
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        App.AppTitlebar = AppTitleBarText as UIElement;
    }

    private void NavigationViewControl_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        AppTitleBar.Margin = new Thickness()
        {
            Left = sender.CompactPaneLength * (sender.DisplayMode == NavigationViewDisplayMode.Minimal ? 2 : 1),
            Top = AppTitleBar.Margin.Top,
            Right = AppTitleBar.Margin.Right,
            Bottom = AppTitleBar.Margin.Bottom
        };
    }

    public async Task ReloadWebItemsAsync()
    {
        foreach (var dynamicItem in _dynamicWebItems)
        {
            NavigationViewControl.MenuItems.Remove(dynamicItem);
        }

        _dynamicWebItems.Clear();

        var webItems = await LoadWebItemsAsync();
        var insertIndex = NavigationViewControl.MenuItems.IndexOf(AddWebNavItem);

        for (var i = 0; i < webItems.Count; i++)
        {
            var webItem = webItems[i];
            var menuItem = CreateDynamicMenuItem(
                webItem.Name,
                typeof(WebViewViewModel).FullName!,
                i,
                "\uE774",
                CreateWebIconForUrl(webItem.Url));
            NavigationHelper.SetNavigateTo(menuItem, typeof(WebViewViewModel).FullName!);
            NavigationHelper.SetNavigationParameter(menuItem, i);
            NavigationViewControl.MenuItems.Insert(insertIndex + i, menuItem);
            _dynamicWebItems.Add(menuItem);
        }

        UpdateCloseButtonsVisibility();
        NavigateToMainPageIfNoPageItems();
        ApplyNavigationContrastBrush();
    }

    private async void NavigationViewControl_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer != AddWebNavItem)
        {
            return;
        }

        var selectedType = await ShowAddPageTypeDialogAsync();
        if (selectedType is null)
        {
            return;
        }

        if (selectedType.PageKey == typeof(WebViewViewModel).FullName!)
        {
            var webItems = await LoadWebItemsAsync();
            var nextIndex = webItems.Count + 1;
            webItems.Add(new WebItemSetting
            {
                Name = $"Web {nextIndex}",
                Url = "https://docs.microsoft.com/windows/apps/"
            });

            await _localSettingsService.SaveSettingAsync(AppSettingKeys.WebItems, webItems);

            await ReloadWebItemsAsync();

            ViewModel.NavigationService.NavigateTo(typeof(WebViewViewModel).FullName!, webItems.Count - 1);
            NavigationViewControl.SelectedItem = _dynamicWebItems.LastOrDefault();
            return;
        }

        var item = AddDynamicPageItem(selectedType);
        ViewModel.NavigationService.NavigateTo(selectedType.PageKey);
        NavigationViewControl.SelectedItem = item;
    }

    private async Task<List<WebItemSetting>> LoadWebItemsAsync()
    {
        var webItems = await _localSettingsService.ReadSettingAsync<List<WebItemSetting>>(AppSettingKeys.WebItems);
        if (webItems != null)
        {
            return webItems;
        }

        return
        [
            new WebItemSetting
            {
                Name = "Web 1",
                Url = FallbackWebUrl
            }
        ];
    }

    private static KeyboardAccelerator BuildKeyboardAccelerator(VirtualKey key, VirtualKeyModifiers? modifiers = null)
    {
        var keyboardAccelerator = new KeyboardAccelerator() { Key = key };

        if (modifiers.HasValue)
        {
            keyboardAccelerator.Modifiers = modifiers.Value;
        }

        keyboardAccelerator.Invoked += OnKeyboardAcceleratorInvoked;

        return keyboardAccelerator;
    }

    private static void OnKeyboardAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        var navigationService = App.GetService<INavigationService>();

        var result = navigationService.GoBack();

        args.Handled = result;
    }

    private void PinToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow is MainWindow mainWindow)
        {
            var isPinned = PinToggleButton.IsChecked == true;
            mainWindow.SetPinned(isPinned);
            ApplyPinPressedVisual();
        }
    }

    private void ApplyPinPressedVisual()
    {
        if (_isCustomChromeApplied && PinToggleButton.IsChecked == true && _customSecondLightBrush != null)
        {
            PinToggleButton.Background = _customSecondLightBrush;
            return;
        }

        PinToggleButton.Background = new SolidColorBrush(Colors.Transparent);
    }

    private void PinToggleButton_PointerEntered(object sender, PointerRoutedEventArgs e) => AnimatePinButtonScale(1.05);

    private void PinToggleButton_PointerExited(object sender, PointerRoutedEventArgs e) => AnimatePinButtonScale(1.0);

    private void LowPowerButton_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (App.MainWindow is MainWindow mainWindow)
        {
            mainWindow.EnterLowPowerMode();
        }
    }

    private void LowPowerButton_PointerEntered(object sender, PointerRoutedEventArgs e) => AnimateLowPowerButton(0.95, -8);

    private void LowPowerButton_PointerExited(object sender, PointerRoutedEventArgs e) => AnimateLowPowerButton(1.0, 0);

    private void AnimatePinButtonScale(double toScale)
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(140));
        var storyboard = new Storyboard();
        var animationX = new DoubleAnimation
        {
            To = toScale,
            Duration = duration,
            EnableDependentAnimation = true
        };
        var animationY = new DoubleAnimation
        {
            To = toScale,
            Duration = duration,
            EnableDependentAnimation = true
        };

        Storyboard.SetTarget(animationX, PinToggleScaleTransform);
        Storyboard.SetTargetProperty(animationX, nameof(ScaleTransform.ScaleX));
        Storyboard.SetTarget(animationY, PinToggleScaleTransform);
        Storyboard.SetTargetProperty(animationY, nameof(ScaleTransform.ScaleY));
        storyboard.Children.Add(animationX);
        storyboard.Children.Add(animationY);
        storyboard.Begin();
    }

    private void AnimateLowPowerButton(double scale, double rotation)
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(140));
        var storyboard = new Storyboard();
        var scaleX = new DoubleAnimation
        {
            To = scale,
            Duration = duration,
            EnableDependentAnimation = true
        };
        var scaleY = new DoubleAnimation
        {
            To = scale,
            Duration = duration,
            EnableDependentAnimation = true
        };
        var rotate = new DoubleAnimation
        {
            To = rotation,
            Duration = duration,
            EnableDependentAnimation = true
        };

        Storyboard.SetTarget(scaleX, LowPowerButtonTransform);
        Storyboard.SetTargetProperty(scaleX, nameof(CompositeTransform.ScaleX));
        Storyboard.SetTarget(scaleY, LowPowerButtonTransform);
        Storyboard.SetTargetProperty(scaleY, nameof(CompositeTransform.ScaleY));
        Storyboard.SetTarget(rotate, LowPowerButtonTransform);
        Storyboard.SetTargetProperty(rotate, nameof(CompositeTransform.Rotation));

        storyboard.Children.Add(scaleX);
        storyboard.Children.Add(scaleY);
        storyboard.Children.Add(rotate);
        storyboard.Begin();
    }

    private async Task<PageTypeOption?> ShowAddPageTypeDialogAsync()
    {
        var comboBox = new ComboBox
        {
            ItemsSource = _pageTypeOptions,
            SelectedIndex = 0,
            DisplayMemberPath = nameof(PageTypeOption.DisplayName)
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Add Page",
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = comboBox
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return null;
        }

        return comboBox.SelectedItem as PageTypeOption;
    }

    private NavigationViewItem AddDynamicPageItem(PageTypeOption selectedType)
    {
        _pageTypeCounters[selectedType.PageKey]++;
        var title = $"{selectedType.DisplayName} {_pageTypeCounters[selectedType.PageKey]}";
        var item = CreateDynamicMenuItem(title, selectedType.PageKey, null, selectedType.Glyph);

        var insertIndex = NavigationViewControl.MenuItems.IndexOf(AddWebNavItem);
        NavigationViewControl.MenuItems.Insert(insertIndex, item);
        _dynamicPageItems.Add(item);
        UpdateCloseButtonsVisibility();
        ApplyNavigationContrastBrush();
        return item;
    }

    private void ApplyNavigationContrastBrush()
    {
        var basis = GetCurrentVisualBasisColor();
        var foregroundColor = GetReadableForegroundColor(basis);
        var brush = new SolidColorBrush(foregroundColor);
        NavigationViewControl.Foreground = brush;
        AppTitleBarText.Foreground = brush;
        AddWebNavItem.Foreground = brush;
        if (AddWebNavItem.Icon is IconElement addIcon)
        {
            addIcon.Foreground = brush;
        }

        foreach (var item in _dynamicWebItems.Concat(_dynamicPageItems))
        {
            item.Foreground = brush;
            if (item.Icon is IconElement icon)
            {
                icon.Foreground = brush;
            }
        }

        PinToggleButton.Foreground = brush;

        // Increase contrast for built-in NavigationView glyphs (back/settings/pane buttons).
        NavigationViewControl.Resources["NavigationViewItemForeground"] = brush;
        NavigationViewControl.Resources["NavigationViewItemForegroundPointerOver"] = brush;
        NavigationViewControl.Resources["NavigationViewItemForegroundPressed"] = brush;
        NavigationViewControl.Resources["NavigationViewItemForegroundSelected"] = brush;
        NavigationViewControl.Resources["NavigationViewButtonForeground"] = brush;
        NavigationViewControl.Resources["NavigationViewButtonForegroundPointerOver"] = brush;
        NavigationViewControl.Resources["NavigationViewButtonForegroundPressed"] = brush;
        NavigationViewControl.Resources["NavigationViewBackButtonForeground"] = brush;
        NavigationViewControl.Resources["NavigationViewBackButtonForegroundPointerOver"] = brush;
        NavigationViewControl.Resources["NavigationViewBackButtonForegroundPressed"] = brush;
    }

    private Color GetCurrentVisualBasisColor()
    {
        if (_customAccentColor.HasValue)
        {
            return Color.FromArgb((byte)Math.Round(255 * _themeOpacity), _customAccentColor.Value.R, _customAccentColor.Value.G, _customAccentColor.Value.B);
        }

        if (!string.IsNullOrWhiteSpace(_backgroundImagePath))
        {
            // For complex image backgrounds, prefer a high-contrast white foreground baseline.
            return Color.FromArgb(0xFF, 0x20, 0x20, 0x20);
        }

        if (RootLayout.Background is SolidColorBrush rootBrush)
        {
            return rootBrush.Color;
        }

        if (AppTitleBar.Background is SolidColorBrush titleBrush)
        {
            return titleBrush.Color;
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

    private NavigationViewItem CreateDynamicMenuItem(string title, string pageKey, object? parameter, string glyph, IconElement? icon = null)
    {
        var closeButton = new Button
        {
            Content = new FontIcon
            {
                FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
                Glyph = "\uE711",
                FontSize = 14
            },
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            MinWidth = 24,
            MinHeight = 24,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };

        var titleText = new TextBlock
        {
            Text = title,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };

        var contentGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };

        Grid.SetColumn(titleText, 0);
        Grid.SetColumn(closeButton, 1);
        contentGrid.Children.Add(titleText);
        contentGrid.Children.Add(closeButton);

        var item = new NavigationViewItem
        {
            Content = contentGrid,
            Tag = closeButton,
            Icon = icon ?? new FontIcon
            {
                FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
                Glyph = glyph
            }
        };

        closeButton.Click += async (_, _) => await RemoveDynamicItemAsync(item);
        NavigationHelper.SetNavigateTo(item, pageKey);
        NavigationHelper.SetNavigationParameter(item, parameter);
        return item;
    }

    private static IconElement CreateWebIconForUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var targetUri) || string.IsNullOrWhiteSpace(targetUri.Host))
        {
            return CreateFallbackWebIcon();
        }

        try
        {
            var origin = Uri.EscapeDataString($"{targetUri.Scheme}://{targetUri.Host}");
            var faviconUri = new Uri($"https://www.google.com/s2/favicons?sz=64&domain_url={origin}");
            return new ImageIcon
            {
                Source = new BitmapImage(faviconUri),
                Width = 16,
                Height = 16
            };
        }
        catch
        {
            return CreateFallbackWebIcon();
        }
    }

    private static IconElement CreateFallbackWebIcon() =>
        new FontIcon
        {
            FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
            Glyph = "\uE774"
        };

    private async Task RemoveDynamicItemAsync(NavigationViewItem item)
    {
        var removedWeb = _dynamicWebItems.Remove(item);
        var removedPage = _dynamicPageItems.Remove(item);
        if (removedWeb || removedPage)
        {
            NavigationViewControl.MenuItems.Remove(item);
        }

        if (removedWeb)
        {
            await RemoveWebItemSettingAsync(item);
            await ReloadWebItemsAsync();
        }

        if (ReferenceEquals(NavigationViewControl.SelectedItem, item))
        {
            if (_dynamicWebItems.Count > 0)
            {
                ViewModel.NavigationService.NavigateTo(typeof(WebViewViewModel).FullName!, 0);
            }
            else
            {
                NavigateToMainPageIfNoPageItems();
            }
        }

        NavigateToMainPageIfNoPageItems();
    }

    private async Task RemoveWebItemSettingAsync(NavigationViewItem item)
    {
        if (NavigationHelper.GetNavigationParameter(item) is not int index)
        {
            return;
        }

        var webItems = await LoadWebItemsAsync();
        if (index < 0 || index >= webItems.Count)
        {
            return;
        }

        webItems.RemoveAt(index);
        await _localSettingsService.SaveSettingAsync(AppSettingKeys.WebItems, webItems);
    }

    private void NavigationViewControl_PaneOpened(NavigationView sender, object args) => UpdateCloseButtonsVisibility();

    private void NavigationViewControl_PaneClosed(NavigationView sender, object args) => UpdateCloseButtonsVisibility();

    private void UpdateCloseButtonsVisibility()
    {
        var isVisible = NavigationViewControl.IsPaneOpen;

        foreach (var item in _dynamicWebItems.Concat(_dynamicPageItems))
        {
            if (item.Tag is Button closeButton)
            {
                closeButton.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private void NavigateToMainPageIfNoPageItems()
    {
        if (_dynamicWebItems.Count > 0 || _dynamicPageItems.Count > 0)
        {
            return;
        }

        ViewModel.NavigationService.NavigateTo(typeof(MainViewModel).FullName!);
        NavigationViewControl.SelectedItem = null;
    }

    private void NavigateToFirstWebItemOnStartup()
    {
        if (_dynamicWebItems.Count == 0)
        {
            return;
        }

        ViewModel.NavigationService.NavigateTo(typeof(WebViewViewModel).FullName!, 0);
        NavigationViewControl.SelectedItem = _dynamicWebItems[0];
    }

    private sealed record PageTypeOption(string DisplayName, string PageKey, string Glyph);
}
