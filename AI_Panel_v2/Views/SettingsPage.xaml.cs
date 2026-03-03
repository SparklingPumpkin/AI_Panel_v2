using AI_Panel_v2.Contracts.Services;
using AI_Panel_v2.Helpers;
using AI_Panel_v2.Models;
using AI_Panel_v2.ViewModels;

using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using Windows.Foundation;
using System.IO;
using System.Diagnostics;
using Microsoft.Win32;

using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using WinRT.Interop;
using System.Globalization;

using ColorSpectrumShape = Microsoft.UI.Xaml.Controls.ColorSpectrumShape;

namespace AI_Panel_v2.Views;

// TODO: Set the URL for your privacy policy by updating SettingsPage_PrivacyTermsLink.NavigateUri in Resources.resw.
public sealed partial class SettingsPage : Page
{
    private const string FallbackWebUrl = "https://docs.microsoft.com/windows/apps/";
    private const string DefaultDownloadPath = @"D:\";
    private const string ThemeModeLight = "Light";
    private const string ThemeModeDark = "Dark";
    private const string ThemeModeDefault = "Default";
    private const string ThemeModeCustom = "Custom";
    private const double DefaultOpacity = 1.0;
    private const double DragStartThreshold = 8;
    private const string DefaultChatGptBeautifyCssFile = "chatgpt-header.css";
    private readonly ILocalSettingsService _localSettingsService;
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly INavigationService _navigationService;
    private readonly IWebViewService _webViewService;
    private readonly List<(TextBox NameBox, TextBox UrlBox)> _webItemEditors = new();
    private readonly List<(TextBox UrlBox, TextBox FilePathBox, ToggleSwitch EnabledSwitch)> _webBeautifyRuleEditors = new();
    private List<WebItemSetting> _webItems = new();
    private List<WebBeautifyRule> _webBeautifyRules = new();
    private HotKeySetting _pendingHotKey = new();
    private bool _isRecordingHotKey;
    private Border? _dragCard;
    private uint _dragPointerId;
    private Point _dragStartPointerInRoot;
    private Point _dragStartCardTranslation;
    private bool _isDragPrimed;
    private bool _isDraggingCard;
    private bool _isInitializingSettings;
    private bool _isUpdatingCardColorControls;
    private bool _isCustomThemeSelected;
    private Brush? _customMiddleBrush;
    private Brush? _customSecondDarkBrush;
    private Color? _lastAutoReadableTextColor;
    private double _settingsCardBlur;
    private readonly List<FontDisplayItem> _programFonts = new();
    private bool _isUpdatingProgramFontSelection;
    private bool _isUpdatingProgramFontStyleControls;
    private bool _hasLoadedSettingsOnce;

    public SettingsViewModel ViewModel
    {
        get;
    }

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        _localSettingsService = App.GetService<ILocalSettingsService>();
        _themeSelectorService = App.GetService<IThemeSelectorService>();
        _navigationService = App.GetService<INavigationService>();
        _webViewService = App.GetService<IWebViewService>();
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        CardColorPickerButton.RegisterPropertyChangedCallback(
            CommunityToolkit.WinUI.Controls.ColorPickerButton.SelectedColorProperty,
            (_, _) => OnCardColorPickerButtonColorChanged());
        Loaded += SettingsPage_Loaded;
        Unloaded += SettingsPage_Unloaded;
    }

    private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_hasLoadedSettingsOnce)
        {
            return;
        }

        try
        {
            await LoadSettingsAsync();
            await RestoreCardLayoutAsync();
            _hasLoadedSettingsOnce = true;
        }
        catch (Exception ex)
        {
            WebItemsStatusTextBlock.Text = $"Load failed: {ex.Message}";
        }
    }

    private async void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        await SaveProgramFontSettingsSnapshotAsync();
    }

    private async Task LoadSettingsAsync()
    {
        _isInitializingSettings = true;
        try
        {
            _pendingHotKey = await _localSettingsService.ReadSettingAsync<HotKeySetting>(AppSettingKeys.GlobalHotKey) ?? new HotKeySetting();
            HotKeyTextBlock.Text = $"Current: {ToDisplayText(_pendingHotKey)}";

            _webItems = await LoadWebItemsAsync();
            RenderWebItemEditors();

            var downloadPath = await _localSettingsService.ReadSettingAsync<string>(AppSettingKeys.WebViewDownloadPath);
            if (string.IsNullOrWhiteSpace(downloadPath))
            {
                downloadPath = DefaultDownloadPath;
                await _localSettingsService.SaveSettingAsync(AppSettingKeys.WebViewDownloadPath, downloadPath);
            }

            DownloadPathTextBox.Text = downloadPath;

            var backgroundImagePath = await _localSettingsService.ReadSettingAsync<string>(AppSettingKeys.BackgroundImagePath) ?? string.Empty;
            BackgroundImagePathTextBox.Text = backgroundImagePath;
            BackgroundImageStatusTextBlock.Text = string.IsNullOrWhiteSpace(backgroundImagePath) ? "Not set" : "Loaded";

            var savedBackgroundOpacity = await _localSettingsService.ReadSettingAsync<double?>(AppSettingKeys.BackgroundImageOpacity);
            BackgroundImageOpacitySlider.Value = ClampOpacity(savedBackgroundOpacity ?? DefaultOpacity);
            var savedBackgroundBlur = await _localSettingsService.ReadSettingAsync<double?>(AppSettingKeys.BackgroundImageBlur);
            BackgroundImageBlurSlider.Value = ClampBlur(savedBackgroundBlur ?? 0);

            var savedThemeOpacity = await _localSettingsService.ReadSettingAsync<double?>(AppSettingKeys.ThemeColorOpacity);
            ThemeOpacitySlider.Value = ClampOpacity(savedThemeOpacity ?? DefaultOpacity);
            var savedThemeBlur = await _localSettingsService.ReadSettingAsync<double?>(AppSettingKeys.ThemeColorBlur);
            ThemeBlurSlider.Value = ClampBlur(savedThemeBlur ?? 0);
            var savedWebLayerOpacity = await _localSettingsService.ReadSettingAsync<double?>(AppSettingKeys.WebLayerOpacity);
            WebLayerOpacitySlider.Value = ClampOpacity(savedWebLayerOpacity ?? 0.5);
            var savedWebLayerBlur = await _localSettingsService.ReadSettingAsync<double?>(AppSettingKeys.WebLayerBlur);
            WebLayerBlurSlider.Value = ClampBlur(savedWebLayerBlur ?? 0);
            await LoadWebBeautifySettingsAsync();

            LoadProgramFontOptions();
            await LoadProgramFontSelectionAsync();
            await LoadProgramFontStyleAsync();

            // Avoid heavy WebView extension enumeration during page activation to reduce flicker
            // and prevent instability when switching pages rapidly.
            if (InstallExtensionPanel.Visibility == Visibility.Visible)
            {
                await RefreshExtensionsAsync();
            }

            SpectrumShapeComboBox.SelectedItem = "Box";

            var savedColorText = await _localSettingsService.ReadSettingAsync<string>(AppSettingKeys.AccentColor);
            if (AccentPaletteHelper.TryParseHex(savedColorText, out var savedColor))
            {
                AccentColorPicker.Color = savedColor;
            }
            else
            {
                var paletteName = await _localSettingsService.ReadSettingAsync<string>(AppSettingKeys.AccentPalette);
                var palette = AccentPaletteHelper.GetByName(paletteName);
                AccentColorPicker.Color = palette.Accent;
            }

            var themeMode = await _localSettingsService.ReadSettingAsync<string>(AppSettingKeys.ThemeMode);
            if (string.Equals(themeMode, ThemeModeCustom, StringComparison.OrdinalIgnoreCase))
            {
                ThemeCustomRadioButton.IsChecked = true;
                _isCustomThemeSelected = true;
                ApplyCustomAccentVisual(AccentColorPicker.Color);
                AccentPaletteStatusTextBlock.Text = "Custom theme active";
            }
            else
            {
                _isCustomThemeSelected = false;
                ClearCustomChromeFromShell();
                ClearCustomControlBrushes();
                var currentTheme = _themeSelectorService.Theme;
                if (string.Equals(themeMode, ThemeModeLight, StringComparison.OrdinalIgnoreCase) || currentTheme == ElementTheme.Light)
                {
                    ThemeLightRadioButton.IsChecked = true;
                }
                else if (string.Equals(themeMode, ThemeModeDark, StringComparison.OrdinalIgnoreCase) || currentTheme == ElementTheme.Dark)
                {
                    ThemeDarkRadioButton.IsChecked = true;
                }
                else
                {
                    ThemeDefaultRadioButton.IsChecked = true;
                }

                AccentPaletteStatusTextBlock.Text = "Switch to Custom to apply color changes";
            }

            await LoadCardColorAsync();
        }
        finally
        {
            _isInitializingSettings = false;
        }
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

    private static string GetDefaultWebBeautifyRootPath()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(docs, "AI_Panel_v2", "WebBeautify");
    }

    private async Task LoadWebBeautifySettingsAsync()
    {
        var rootPath = await _localSettingsService.ReadSettingAsync<string>(AppSettingKeys.WebBeautifyRootPath);
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            rootPath = GetDefaultWebBeautifyRootPath();
            await _localSettingsService.SaveSettingAsync(AppSettingKeys.WebBeautifyRootPath, rootPath);
        }

        try
        {
            Directory.CreateDirectory(rootPath);
        }
        catch
        {
        }

        WebBeautifyRootPathTextBox.Text = rootPath;

        _webBeautifyRules = await _localSettingsService.ReadSettingAsync<List<WebBeautifyRule>>(AppSettingKeys.WebBeautifyRules) ?? new List<WebBeautifyRule>();
        EnsureDefaultChatGptBeautifyRule(rootPath, _webBeautifyRules);
        await _localSettingsService.SaveSettingAsync(AppSettingKeys.WebBeautifyRules, _webBeautifyRules);
        RenderWebBeautifyRuleEditors();
        WebBeautifyRulesStatusTextBlock.Text = "Loaded";
    }

    private static void EnsureDefaultChatGptBeautifyRule(string rootPath, List<WebBeautifyRule> rules)
    {
        try
        {
            Directory.CreateDirectory(rootPath);
            var cssPath = Path.Combine(rootPath, DefaultChatGptBeautifyCssFile);
            File.WriteAllText(cssPath,
@"#page-header,
header#page-header,
header[data-testid=""page-header""],
main #page-header {
  background: rgba(255, 255, 255, 0.10) !important;
  backdrop-filter: blur(16px) saturate(140%) !important;
  -webkit-backdrop-filter: blur(16px) saturate(140%) !important;
  border-radius: 999px !important;
  border: 1px solid rgba(255, 255, 255, 0.22) !important;
  overflow: hidden !important;
}

/* ChatGPT left sidebar container (open/collapsed variants) */
div.relative.flex.h-full.flex-col,
div.relative.flex.h-full.flex-col > div,
div.relative.flex.h-full.flex-col > aside,
div.relative.flex.h-full.flex-col nav,
div.relative.flex.h-full.flex-col [role=""navigation""],
div.relative.flex.h-full.flex-col [data-testid*=""sidebar""],
div.relative.flex.h-full.flex-col [data-panel*=""sidebar""] {
  background: rgba(255, 255, 255, 0.08) !important;
  backdrop-filter: blur(14px) saturate(135%) !important;
  -webkit-backdrop-filter: blur(14px) saturate(135%) !important;
  border-radius: 10px !important;
  border: 1px solid rgba(255, 255, 255, 0.16) !important;
}
");

            if (!rules.Any(r =>
                    string.Equals(r.UrlPattern?.Trim(), "www.chatgpt.com", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(r.UrlPattern?.Trim(), "chatgpt.com", StringComparison.OrdinalIgnoreCase)))
            {
                rules.Add(new WebBeautifyRule
                {
                    UrlPattern = "chatgpt.com",
                    FilePath = cssPath,
                    IsEnabled = true
                });
            }
        }
        catch
        {
        }
    }

    private void RenderWebBeautifyRuleEditors()
    {
        _webBeautifyRuleEditors.Clear();
        WebBeautifyRulesPanel.Children.Clear();

        if (_webBeautifyRules.Count == 0)
        {
            WebBeautifyRulesPanel.Children.Add(new TextBlock
            {
                Text = "No rules. Click Add Rule.",
                Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0x88, 0x88, 0x88))
            });
            return;
        }

        for (var i = 0; i < _webBeautifyRules.Count; i++)
        {
            var rule = _webBeautifyRules[i];
            var row = new Grid
            {
                ColumnSpacing = 8,
                Margin = new Thickness(0, 0, 0, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var urlBox = new TextBox
            {
                Text = rule.UrlPattern,
                PlaceholderText = "Host / URL pattern",
                Height = 32
            };
            var filePathBox = new TextBox
            {
                Text = rule.FilePath,
                PlaceholderText = "CSS/JS/HTML file path",
                Height = 32
            };
            var enabledSwitch = new ToggleSwitch
            {
                IsOn = rule.IsEnabled,
                OnContent = "On",
                OffContent = "Off",
                VerticalAlignment = VerticalAlignment.Center
            };

            var deleteButton = new Button
            {
                Content = "Delete",
                Height = 32,
                Tag = i
            };
            deleteButton.Click += DeleteWebBeautifyRuleButton_Click;

            Grid.SetColumn(urlBox, 0);
            Grid.SetColumn(filePathBox, 1);
            Grid.SetColumn(enabledSwitch, 2);
            Grid.SetColumn(deleteButton, 3);
            row.Children.Add(urlBox);
            row.Children.Add(filePathBox);
            row.Children.Add(enabledSwitch);
            row.Children.Add(deleteButton);
            WebBeautifyRulesPanel.Children.Add(row);
            _webBeautifyRuleEditors.Add((urlBox, filePathBox, enabledSwitch));
        }
    }

    private void RenderWebItemEditors()
    {
        _webItemEditors.Clear();
        WebItemsEditorPanel.Children.Clear();

        for (var i = 0; i < _webItems.Count; i++)
        {
            var item = _webItems[i];
            var row = new Grid
            {
                ColumnSpacing = 8,
                RowSpacing = 0,
                VerticalAlignment = VerticalAlignment.Center,
                MinHeight = 36,
                Margin = new Thickness(0, 0, 0, 4)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var badge = new Border
            {
                Width = 72,
                Height = 32,
                Padding = new Thickness(8, 4, 8, 4),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x7A, 0x7A, 0x7A)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = $"Item {i + 1}",
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            };
            var nameBox = new TextBox
            {
                PlaceholderText = "Page name",
                Text = item.Name,
                Height = 32,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var urlBox = new TextBox
            {
                PlaceholderText = "Web URL (for Web page type)",
                Text = item.Url,
                Height = 32,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var index = i;
            var deleteButton = new Button
            {
                Content = "Delete",
                Height = 32,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            if (_isCustomThemeSelected && _customSecondDarkBrush != null)
            {
                deleteButton.Background = _customMiddleBrush ?? _customSecondDarkBrush;
                deleteButton.Foreground = new SolidColorBrush(Colors.White);
                deleteButton.BorderThickness = new Thickness(0);
            }
            deleteButton.Click += async (_, _) => await RemoveWebItemAsync(index);

            Grid.SetColumn(badge, 0);
            Grid.SetColumn(nameBox, 1);
            Grid.SetColumn(urlBox, 2);
            Grid.SetColumn(deleteButton, 3);
            row.Children.Add(badge);
            row.Children.Add(nameBox);
            row.Children.Add(urlBox);
            row.Children.Add(deleteButton);
            WebItemsEditorPanel.Children.Add(row);
            _webItemEditors.Add((nameBox, urlBox));
        }
    }

    private async Task RemoveWebItemAsync(int index)
    {
        if (index < 0 || index >= _webItems.Count)
        {
            return;
        }

        _webItems.RemoveAt(index);
        RenderWebItemEditors();
        await _localSettingsService.SaveSettingAsync(AppSettingKeys.WebItems, _webItems);
        if (App.MainWindow is MainWindow window)
        {
            await window.ReloadWebItemsAsync();
        }

        WebItemsStatusTextBlock.Text = "Deleted and saved";
    }

    private async void AddWebItemButton_Click(object sender, RoutedEventArgs e)
    {
        _webItems.Add(new WebItemSetting
        {
            Name = $"Web {_webItems.Count + 1}",
            Url = "https://docs.microsoft.com/windows/apps/"
        });

        RenderWebItemEditors();
        await _localSettingsService.SaveSettingAsync(AppSettingKeys.WebItems, _webItems);
        if (App.MainWindow is MainWindow window)
        {
            await window.ReloadWebItemsAsync();
        }

        WebItemsStatusTextBlock.Text = "Added and saved";
    }

    private async void SaveWebItemsButton_Click(object sender, RoutedEventArgs e)
    {
        var itemsToSave = new List<WebItemSetting>();

        for (var i = 0; i < _webItemEditors.Count; i++)
        {
            var (nameBox, urlBox) = _webItemEditors[i];
            var name = string.IsNullOrWhiteSpace(nameBox.Text) ? $"Web {i + 1}" : nameBox.Text.Trim();
            var url = urlBox.Text?.Trim() ?? string.Empty;

            if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            {
                WebItemsStatusTextBlock.Text = $"Item {i + 1} URL invalid";
                return;
            }

            itemsToSave.Add(new WebItemSetting
            {
                Name = name,
                Url = url
            });
        }

        _webItems = itemsToSave;
        await _localSettingsService.SaveSettingAsync(AppSettingKeys.WebItems, _webItems);

        WebItemsStatusTextBlock.Text = "Saved";

        if (App.MainWindow is MainWindow window)
        {
            await window.ReloadWebItemsAsync();
        }
    }

    private async void SaveDownloadPathButton_Click(object sender, RoutedEventArgs e)
    {
        var downloadPath = DownloadPathTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(downloadPath) || !Path.IsPathRooted(downloadPath))
        {
            DownloadPathStatusTextBlock.Text = "Path invalid";
            return;
        }

        try
        {
            Directory.CreateDirectory(downloadPath);
        }
        catch
        {
            DownloadPathStatusTextBlock.Text = "Path invalid";
            return;
        }

        await _localSettingsService.SaveSettingAsync(AppSettingKeys.WebViewDownloadPath, downloadPath);
        DownloadPathStatusTextBlock.Text = "Saved";
    }

    private async void SaveWebBeautifyRootPathButton_Click(object sender, RoutedEventArgs e)
    {
        var path = WebBeautifyRootPathTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            WebBeautifyRootPathStatusTextBlock.Text = "Path invalid";
            return;
        }

        try
        {
            Directory.CreateDirectory(path);
        }
        catch
        {
            WebBeautifyRootPathStatusTextBlock.Text = "Path invalid";
            return;
        }

        await _localSettingsService.SaveSettingAsync(AppSettingKeys.WebBeautifyRootPath, path);
        WebBeautifyRootPathStatusTextBlock.Text = "Saved";
    }

    private void AddWebBeautifyRuleButton_Click(object sender, RoutedEventArgs e)
    {
        _webBeautifyRules.Add(new WebBeautifyRule
        {
            UrlPattern = string.Empty,
            FilePath = string.Empty,
            IsEnabled = true
        });
        RenderWebBeautifyRuleEditors();
        WebBeautifyRulesStatusTextBlock.Text = "Rule added";
    }

    private async void SaveWebBeautifyRulesButton_Click(object sender, RoutedEventArgs e)
    {
        var rules = new List<WebBeautifyRule>();
        foreach (var (urlBox, filePathBox, enabledSwitch) in _webBeautifyRuleEditors)
        {
            var pattern = urlBox.Text?.Trim() ?? string.Empty;
            var filePath = filePathBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(filePath))
            {
                continue;
            }

            rules.Add(new WebBeautifyRule
            {
                UrlPattern = pattern,
                FilePath = filePath,
                IsEnabled = enabledSwitch.IsOn
            });
        }

        _webBeautifyRules = rules;
        await _localSettingsService.SaveSettingAsync(AppSettingKeys.WebBeautifyRules, _webBeautifyRules);
        WebBeautifyRulesStatusTextBlock.Text = "Rules saved";
        await RefreshActiveWebPageBackgroundInjectionAsync();
    }

    private void DeleteWebBeautifyRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not int index)
        {
            return;
        }

        if (index < 0 || index >= _webBeautifyRules.Count)
        {
            return;
        }

        _webBeautifyRules.RemoveAt(index);
        RenderWebBeautifyRuleEditors();
        WebBeautifyRulesStatusTextBlock.Text = "Rule deleted";
    }

    private async void ChooseBackgroundImageButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".bmp");
        picker.FileTypeFilter.Add(".webp");

        var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file == null)
        {
            return;
        }

        BackgroundImagePathTextBox.Text = file.Path;
        await SaveBackgroundImagePathAsync(file.Path);
    }

    private async void ClearBackgroundImageButton_Click(object sender, RoutedEventArgs e)
    {
        BackgroundImagePathTextBox.Text = string.Empty;
        await SaveBackgroundImagePathAsync(string.Empty);
    }

    private async Task SaveBackgroundImagePathAsync(string path)
    {
        await _localSettingsService.SaveSettingAsync(AppSettingKeys.BackgroundImagePath, path);
        ApplyBackgroundImageToShell(path, BackgroundImageOpacitySlider.Value, ClampBlur(BackgroundImageBlurSlider.Value));

        BackgroundImageStatusTextBlock.Text = string.IsNullOrWhiteSpace(path) ? "Cleared" : "Saved";
    }

    private async void OpenEdgeAddonsButton_Click(object sender, RoutedEventArgs e)
    {
        var storeUri = new Uri("https://microsoftedge.microsoft.com/addons/Microsoft-Edge-Extensions-Home");
        _navigationService.NavigateTo(typeof(WebViewViewModel).FullName!, 0);
        _webViewService.Navigate(storeUri);
        await Task.CompletedTask;
        ExtensionStatusTextBlock.Text = "Opened Edge Add-ons in WebView";
    }

    private async void OpenExtensionDevModeButton_Click(object sender, RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Open App Profile Dev Settings",
            Content = "The app will close first, then Edge will open the app profile extensions page.",
            PrimaryButtonText = "Continue",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        var result = await confirm.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        var launched = TryScheduleEdgeExtensionsPageWithAppProfileLaunch();
        if (!launched)
        {
            await Launcher.LaunchUriAsync(new Uri("https://microsoftedge.microsoft.com/addons/Microsoft-Edge-Extensions-Home"));
            await ShowExtensionMessageAsync(
                "Enable Script Injection",
                "Edge executable was not found. Please open Edge manually with your app profile and navigate to edge://extensions/.");
            ExtensionStatusTextBlock.Text = "Failed to launch app profile extensions page";
            return;
        }

        App.MainWindow.Close();
    }

    private static bool TryScheduleEdgeExtensionsPageWithAppProfileLaunch()
    {
        var candidates = new[]
        {
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe"
        };
        var userDataDir = WebViewPathHelper.GetAppWebViewUserDataDir();

        foreach (var edgeExe in candidates)
        {
            if (!File.Exists(edgeExe))
            {
                continue;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c timeout /t 1 /nobreak >nul & start \"\" \"{edgeExe}\" --new-window --user-data-dir=\"{userDataDir}\" \"edge://extensions/\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                return true;
            }
            catch
            {
            }
        }

        return false;
    }

    private async void RefreshExtensionsButton_Click(object sender, RoutedEventArgs e)
    {
        await EnsureWebViewInitializedForExtensionsAsync();
        await RefreshExtensionsAsync();
    }

    private async void InstallExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        var initialized = await EnsureWebViewInitializedForExtensionsAsync();
        if (!initialized)
        {
            ExtensionStatusTextBlock.Text = "WebView not initialized";
            await ShowExtensionMessageAsync("Install Extension", "Please open any Web page once, then return to Settings and install again.");
            return;
        }

        var folderPath = ExtensionFolderPathTextBox.Text?.Trim() ?? string.Empty;
        var result = await _webViewService.InstallExtensionFromFolderAsync(folderPath);
        ExtensionStatusTextBlock.Text = result.Success ? "Installed" : $"Install failed: {result.ErrorMessage}";
        await ShowExtensionMessageAsync(
            "Install Extension",
            result.Success ? "Extension installed successfully." : $"Install failed: {result.ErrorMessage}");
        if (result.Success)
        {
            await AddExtensionOptionsPageToNavigationAsync(result.Extension);
            await RefreshExtensionsAsync();
        }
    }

    private async Task RefreshExtensionsAsync()
    {
        var extensions = await _webViewService.GetExtensionsAsync();
        RenderExtensionRows(extensions);
    }

    private void RenderExtensionRows(IReadOnlyList<BrowserExtensionInfo> extensions)
    {
        ExtensionsRowsPanel.Children.Clear();
        if (extensions.Count == 0)
        {
            ExtensionsRowsPanel.Children.Add(new TextBlock
            {
                Text = "No extensions installed.",
                Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0x88, 0x88, 0x88))
            });
            return;
        }

        foreach (var extension in extensions)
        {
            var rowContainer = new Border
            {
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(8, 7, 8, 7),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0x88, 0x88, 0x88)),
                Background = new SolidColorBrush(Color.FromArgb(0x12, 0x88, 0x88, 0x88))
            };

            var row = new Grid
            {
                ColumnSpacing = 8
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var actionPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center
            };

            var toggleButton = new Button
            {
                Content = extension.IsEnabled ? "Disable" : "Enable",
                Tag = extension,
                MinWidth = 76,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            toggleButton.Click += ToggleExtensionButton_Click;

            var uninstallButton = new Button
            {
                Content = "Uninstall",
                Tag = extension,
                MinWidth = 78,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            uninstallButton.Click += UninstallExtensionButton_Click;

            actionPanel.Children.Add(toggleButton);
            actionPanel.Children.Add(uninstallButton);

            var textPanel = new StackPanel
            {
                Spacing = 2
            };
            textPanel.Children.Add(new TextBlock
            {
                Text = extension.Name,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 220
            });

            textPanel.Children.Add(new TextBlock
            {
                Text = extension.IsEnabled ? "Enabled" : "Disabled",
                Foreground = new SolidColorBrush(extension.IsEnabled ? Color.FromArgb(0xFF, 0x29, 0x8A, 0x3B) : Color.FromArgb(0xCC, 0x88, 0x88, 0x88))
            });

            Grid.SetColumn(actionPanel, 0);
            Grid.SetColumn(textPanel, 1);
            row.Children.Add(actionPanel);
            row.Children.Add(textPanel);
            rowContainer.Child = row;
            ExtensionsRowsPanel.Children.Add(rowContainer);
        }
    }

    private async void ToggleExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not BrowserExtensionInfo extension)
        {
            return;
        }

        var targetState = !extension.IsEnabled;
        var success = await _webViewService.SetExtensionEnabledAsync(extension.Id, targetState);
        ExtensionStatusTextBlock.Text = success
            ? (targetState ? "Enabled" : "Disabled")
            : (targetState ? "Enable failed" : "Disable failed");
        if (success)
        {
            await RefreshExtensionsAsync();
        }
    }

    private async void UninstallExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not BrowserExtensionInfo extension)
        {
            return;
        }

        var success = await _webViewService.RemoveExtensionAsync(extension.Id);
        ExtensionStatusTextBlock.Text = success ? "Uninstalled" : "Uninstall failed";
        if (success)
        {
            await RefreshExtensionsAsync();
        }
    }

    private async Task AddExtensionOptionsPageToNavigationAsync(BrowserExtensionInfo? extension)
    {
        if (extension == null || string.IsNullOrWhiteSpace(extension.OptionsUrl))
        {
            return;
        }

        var webItems = await LoadWebItemsAsync();
        if (webItems.Any(item => string.Equals(item.Url, extension.OptionsUrl, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        webItems.Add(new WebItemSetting
        {
            Name = $"{extension.Name} Settings",
            Url = extension.OptionsUrl
        });
        await _localSettingsService.SaveSettingAsync(AppSettingKeys.WebItems, webItems);
        _webItems = webItems;
        RenderWebItemEditors();

        if (App.MainWindow is MainWindow window)
        {
            await window.ReloadWebItemsAsync();
        }
    }

    private async Task<bool> EnsureWebViewInitializedForExtensionsAsync()
    {
        if (await _webViewService.EnsureInitializedAsync())
        {
            return true;
        }

        return false;
    }

    private async Task ShowExtensionMessageAsync(string title, string content)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = content,
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close
        };

        await dialog.ShowAsync();
    }

    private void RecordHotKeyButton_Click(object sender, RoutedEventArgs e)
    {
        _isRecordingHotKey = true;
        HotKeyStatusTextBlock.Text = "Press a key combination";
        RecordHotKeyButton.Content = "Recording...";
        Focus(FocusState.Programmatic);
    }

    private async void SaveHotKeyButton_Click(object sender, RoutedEventArgs e)
    {
        await _localSettingsService.SaveSettingAsync(AppSettingKeys.GlobalHotKey, _pendingHotKey);
        HotKeyStatusTextBlock.Text = "Saved";

        if (App.MainWindow is MainWindow window)
        {
            await window.ReloadHotKeyAsync();
        }
    }

    private async void ThemeRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_isInitializingSettings || sender is not RadioButton radioButton)
        {
            return;
        }

        if (ReferenceEquals(radioButton, ThemeCustomRadioButton))
        {
            _isCustomThemeSelected = true;
            ViewModel.ElementTheme = ElementTheme.Default;
            await _themeSelectorService.SetThemeAsync(ElementTheme.Default);
            await _localSettingsService.SaveSettingAsync(AppSettingKeys.ThemeMode, ThemeModeCustom);
            await ApplyAndPersistCustomAccentAsync();
            AccentPaletteStatusTextBlock.Text = "Custom theme active";
            return;
        }

        _isCustomThemeSelected = false;
        var theme = ReferenceEquals(radioButton, ThemeLightRadioButton)
            ? ElementTheme.Light
            : ReferenceEquals(radioButton, ThemeDarkRadioButton)
                ? ElementTheme.Dark
                : ElementTheme.Default;

        ClearCustomChromeFromShell();
        ClearCustomControlBrushes();
        RenderWebItemEditors();
        ViewModel.ElementTheme = theme;
        await _themeSelectorService.SetThemeAsync(theme);
        await _localSettingsService.SaveSettingAsync(AppSettingKeys.ThemeMode, theme.ToString());
        AccentPaletteStatusTextBlock.Text = "Switch to Custom to apply color changes";
    }

    private void AccentColorPicker_ColorChanged(object sender, Microsoft.UI.Xaml.Controls.ColorChangedEventArgs e)
    {
        if (_isInitializingSettings || !_isCustomThemeSelected)
        {
            return;
        }

        ApplyCustomAccentVisual(AccentColorPicker.Color);
        RenderWebItemEditors();
        _ = _localSettingsService.SaveSettingAsync(AppSettingKeys.AccentColor, AccentPaletteHelper.ToHex(AccentColorPicker.Color));
        AccentPaletteStatusTextBlock.Text = "Custom theme applied";
    }

    private async Task ApplyAndPersistCustomAccentAsync()
    {
        var color = AccentColorPicker.Color;
        ApplyCustomAccentVisual(color);
        RenderWebItemEditors();
        await _localSettingsService.SaveSettingAsync(AppSettingKeys.AccentColor, AccentPaletteHelper.ToHex(color));
    }

    private void ApplyCustomAccentVisual(Color color)
    {
        var darkest = Blend(color, Colors.Black, 0.55);
        var secondDark = Blend(color, Colors.Black, 0.35);
        var themeOpacity = ClampOpacity(ThemeOpacitySlider.Value);
        var themeBlur = ClampBlur(ThemeBlurSlider.Value);
        var middleAlpha = (byte)Math.Round(255 * themeOpacity);
        var middle = new SolidColorBrush(Color.FromArgb(middleAlpha, color.R, color.G, color.B));
        SetCustomControlBrushes(new SolidColorBrush(darkest), new SolidColorBrush(secondDark), middle);
        AccentPaletteHelper.ApplyAccentColor(color);
        ApplyCustomChromeToShell(color, themeOpacity, themeBlur);
    }

    private static Color Blend(Color from, Color to, double ratio)
    {
        ratio = Math.Clamp(ratio, 0, 1);
        var r = (byte)Math.Round(from.R + (to.R - from.R) * ratio);
        var g = (byte)Math.Round(from.G + (to.G - from.G) * ratio);
        var b = (byte)Math.Round(from.B + (to.B - from.B) * ratio);
        return Color.FromArgb(0xFF, r, g, b);
    }

    private void SetCustomControlBrushes(Brush darkestBrush, Brush secondDarkBrush, Brush middleBrush)
    {
        _customMiddleBrush = middleBrush;
        _customSecondDarkBrush = secondDarkBrush;

        Resources["ToggleSwitchOnContentForeground"] = secondDarkBrush;
        Resources["ToggleSwitchOffContentForeground"] = secondDarkBrush;
        Resources["ToggleSwitchKnobFillOn"] = secondDarkBrush;
        Resources["ToggleSwitchFillOn"] = secondDarkBrush;

        ThemeLightRadioButton.Foreground = secondDarkBrush;
        ThemeDarkRadioButton.Foreground = secondDarkBrush;
        ThemeDefaultRadioButton.Foreground = secondDarkBrush;
        ThemeCustomRadioButton.Foreground = secondDarkBrush;

        AlphaEnabledSwitch.Foreground = secondDarkBrush;
        AlphaSliderSwitch.Foreground = secondDarkBrush;
        ColorSliderSwitch.Foreground = secondDarkBrush;
        ColorChannelSwitch.Foreground = secondDarkBrush;
        SpectrumVisibleSwitch.Foreground = secondDarkBrush;
        ColorPaletteSwitch.Foreground = secondDarkBrush;
        AccentColorsSwitch.Foreground = secondDarkBrush;
    }

    private void ClearCustomControlBrushes()
    {
        _customMiddleBrush = null;
        _customSecondDarkBrush = null;

        Resources.Remove("ToggleSwitchOnContentForeground");
        Resources.Remove("ToggleSwitchOffContentForeground");
        Resources.Remove("ToggleSwitchKnobFillOn");
        Resources.Remove("ToggleSwitchFillOn");

        ThemeLightRadioButton.ClearValue(Control.ForegroundProperty);
        ThemeDarkRadioButton.ClearValue(Control.ForegroundProperty);
        ThemeDefaultRadioButton.ClearValue(Control.ForegroundProperty);
        ThemeCustomRadioButton.ClearValue(Control.ForegroundProperty);

        AlphaEnabledSwitch.ClearValue(Control.ForegroundProperty);
        AlphaSliderSwitch.ClearValue(Control.ForegroundProperty);
        ColorSliderSwitch.ClearValue(Control.ForegroundProperty);
        ColorChannelSwitch.ClearValue(Control.ForegroundProperty);
        SpectrumVisibleSwitch.ClearValue(Control.ForegroundProperty);
        ColorPaletteSwitch.ClearValue(Control.ForegroundProperty);
        AccentColorsSwitch.ClearValue(Control.ForegroundProperty);
    }

    private static void ApplyCustomChromeToShell(Color accentColor, double themeOpacity, double themeBlur)
    {
        if (App.MainWindow.Content is ShellPage shellPage)
        {
            shellPage.ApplyCustomChromeTheme(accentColor, themeOpacity, themeBlur);
            return;
        }

        if (App.MainWindow.Content is Frame rootFrame && rootFrame.Content is ShellPage frameShell)
        {
            frameShell.ApplyCustomChromeTheme(accentColor, themeOpacity, themeBlur);
        }
    }

    private static void ClearCustomChromeFromShell()
    {
        if (App.MainWindow.Content is ShellPage shellPage)
        {
            shellPage.ClearCustomChromeTheme();
            return;
        }

        if (App.MainWindow.Content is Frame rootFrame && rootFrame.Content is ShellPage frameShell)
        {
            frameShell.ClearCustomChromeTheme();
        }
    }

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_isRecordingHotKey || IsModifierKey(e.Key))
        {
            return;
        }

        _pendingHotKey = new HotKeySetting
        {
            Key = e.Key,
            Ctrl = IsPressed(VirtualKey.Control),
            Alt = IsPressed(VirtualKey.Menu),
            Shift = IsPressed(VirtualKey.Shift),
            Win = IsPressed(VirtualKey.LeftWindows) || IsPressed(VirtualKey.RightWindows)
        };

        _isRecordingHotKey = false;
        HotKeyTextBlock.Text = $"Current: {ToDisplayText(_pendingHotKey)}";
        HotKeyStatusTextBlock.Text = "Recorded";
        RecordHotKeyButton.Content = "Record Hotkey";
        e.Handled = true;
    }

    private static bool IsPressed(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private static bool IsModifierKey(VirtualKey key) =>
        key == VirtualKey.Control ||
        key == VirtualKey.Menu ||
        key == VirtualKey.Shift ||
        key == VirtualKey.LeftWindows ||
        key == VirtualKey.RightWindows;

    private static string ToDisplayText(HotKeySetting hotKey)
    {
        var parts = new List<string>();
        if (hotKey.Ctrl) parts.Add("Ctrl");
        if (hotKey.Alt) parts.Add("Alt");
        if (hotKey.Shift) parts.Add("Shift");
        if (hotKey.Win) parts.Add("Win");
        parts.Add(hotKey.Key.ToString());
        return string.Join("+", parts);
    }

    private void AlphaEnabledSwitch_Toggled(object sender, RoutedEventArgs e) => AccentColorPicker.IsAlphaEnabled = AlphaEnabledSwitch.IsOn;

    private void AlphaSliderSwitch_Toggled(object sender, RoutedEventArgs e) => AccentColorPicker.IsAlphaSliderVisible = AlphaSliderSwitch.IsOn;

    private void ColorSliderSwitch_Toggled(object sender, RoutedEventArgs e) => AccentColorPicker.IsColorSliderVisible = ColorSliderSwitch.IsOn;

    private void ColorChannelSwitch_Toggled(object sender, RoutedEventArgs e) => AccentColorPicker.IsColorChannelTextInputVisible = ColorChannelSwitch.IsOn;

    private void SpectrumVisibleSwitch_Toggled(object sender, RoutedEventArgs e) => AccentColorPicker.IsColorSpectrumVisible = SpectrumVisibleSwitch.IsOn;

    private void ColorPaletteSwitch_Toggled(object sender, RoutedEventArgs e) => AccentColorPicker.IsColorPaletteVisible = ColorPaletteSwitch.IsOn;

    private void AccentColorsSwitch_Toggled(object sender, RoutedEventArgs e) => AccentColorPicker.ShowAccentColors = AccentColorsSwitch.IsOn;

    private void SpectrumShapeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var value = SpectrumShapeComboBox.SelectedItem as string;
        AccentColorPicker.ColorSpectrumShape = value == "Ring" ? ColorSpectrumShape.Ring : ColorSpectrumShape.Box;
    }

    private void DraggableCard_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border border)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source && HasInteractiveAncestor(source, border))
        {
            return;
        }

        var point = e.GetCurrentPoint(SettingsLayoutRoot);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragCard = border;
        _dragPointerId = e.Pointer.PointerId;
        _dragStartPointerInRoot = point.Position;
        _isDragPrimed = true;
        _isDraggingCard = false;

        if (border.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            border.RenderTransform = transform;
        }

        _dragStartCardTranslation = new Point(transform.X, transform.Y);
        border.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void DraggableCard_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragCard is null || e.Pointer.PointerId != _dragPointerId || _dragCard.RenderTransform is not TranslateTransform transform)
        {
            return;
        }

        var currentPoint = e.GetCurrentPoint(SettingsLayoutRoot).Position;
        if (_isDragPrimed && !_isDraggingCard)
        {
            var deltaX = currentPoint.X - _dragStartPointerInRoot.X;
            var deltaY = currentPoint.Y - _dragStartPointerInRoot.Y;
            if ((deltaX * deltaX) + (deltaY * deltaY) < DragStartThreshold * DragStartThreshold)
            {
                return;
            }

            _isDraggingCard = true;
            _dragCard.Opacity = 0.95;
            Canvas.SetZIndex(_dragCard, 1000);
        }

        if (!_isDraggingCard)
        {
            return;
        }

        transform.X = _dragStartCardTranslation.X + (currentPoint.X - _dragStartPointerInRoot.X);
        transform.Y = _dragStartCardTranslation.Y + (currentPoint.Y - _dragStartPointerInRoot.Y);
        e.Handled = true;
    }

    private void DraggableCard_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragCard is null || e.Pointer.PointerId != _dragPointerId)
        {
            return;
        }

        if (_isDraggingCard)
        {
            var releasePoint = e.GetCurrentPoint(SettingsLayoutRoot).Position;
            var targetColumn = releasePoint.X < SettingsLayoutRoot.ActualWidth / 2 ? ContentArea : RightColumnStack;

            if (_dragCard.Parent is Panel currentParent)
            {
                var insertIndex = 0;
                foreach (var child in targetColumn.Children.OfType<Border>())
                {
                    if (ReferenceEquals(child, _dragCard))
                    {
                        continue;
                    }

                    var origin = child.TransformToVisual(SettingsLayoutRoot).TransformPoint(new Point(0, 0));
                    var centerY = origin.Y + child.ActualHeight / 2;
                    if (releasePoint.Y > centerY)
                    {
                        insertIndex++;
                    }
                }

                currentParent.Children.Remove(_dragCard);
                targetColumn.Children.Insert(Math.Min(insertIndex, targetColumn.Children.Count), _dragCard);
            }

            if (_dragCard.RenderTransform is TranslateTransform transform)
            {
                transform.X = 0;
                transform.Y = 0;
            }

            _ = SaveCardLayoutAsync();
        }

        _dragCard.ReleasePointerCapture(e.Pointer);
        _dragCard.Opacity = 1;
        Canvas.SetZIndex(_dragCard, 0);
        _dragCard = null;
        _dragPointerId = 0;
        _isDragPrimed = false;
        _isDraggingCard = false;
        e.Handled = true;
    }

    private void SettingsLayoutRoot_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source || HasInteractiveTarget(source))
        {
            return;
        }

        if (!_navigationService.GoBack() && Frame?.CanGoBack == true)
        {
            Frame.GoBack();
        }

        e.Handled = true;
    }

    private static bool HasInteractiveTarget(DependencyObject source)
    {
        var current = source;
        while (current != null)
        {
            if (current is TextBox || current is Button || current is ToggleSwitch || current is ComboBox || current is RadioButton || current is HyperlinkButton || current is CommunityToolkit.WinUI.Controls.ColorPicker)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private async Task LoadCardColorAsync()
    {
        var savedCardColorText = await _localSettingsService.ReadSettingAsync<string>(AppSettingKeys.SettingsCardColor);
        var cardColor = AccentPaletteHelper.TryParseHex(savedCardColorText, out var savedCardColor)
            ? savedCardColor
            : Color.FromArgb(0xCC, 0x20, 0x20, 0x20);
        var savedCardBlur = await _localSettingsService.ReadSettingAsync<double?>(AppSettingKeys.SettingsCardBlur);
        _settingsCardBlur = ClampBlur(savedCardBlur ?? 0);

        _isUpdatingCardColorControls = true;
        CardColorPickerButton.SelectedColor = Color.FromArgb(0xFF, cardColor.R, cardColor.G, cardColor.B);
        CardOpacitySlider.Value = cardColor.A;
        CardBlurSlider.Value = _settingsCardBlur;
        _isUpdatingCardColorControls = false;

        ApplySettingsCardColor(cardColor);
    }

    private void OnCardColorPickerButtonColorChanged()
    {
        if (_isUpdatingCardColorControls)
        {
            return;
        }

        var selected = CardColorPickerButton.SelectedColor;
        var alpha = (byte)Math.Round(CardOpacitySlider.Value);
        var cardColor = Color.FromArgb(alpha, selected.R, selected.G, selected.B);
        ApplySettingsCardColor(cardColor);
        _ = _localSettingsService.SaveSettingAsync(AppSettingKeys.SettingsCardColor, AccentPaletteHelper.ToHex(cardColor));
    }

    private void CardOpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdatingCardColorControls)
        {
            return;
        }

        var selected = CardColorPickerButton.SelectedColor;
        var alpha = (byte)Math.Round(e.NewValue);
        var cardColor = Color.FromArgb(alpha, selected.R, selected.G, selected.B);
        ApplySettingsCardColor(cardColor);
        _ = _localSettingsService.SaveSettingAsync(AppSettingKeys.SettingsCardColor, AccentPaletteHelper.ToHex(cardColor));
    }

    private void CardBlurSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdatingCardColorControls)
        {
            return;
        }

        _settingsCardBlur = ClampBlur(e.NewValue);
        var selected = CardColorPickerButton.SelectedColor;
        var alpha = (byte)Math.Round(CardOpacitySlider.Value);
        var cardColor = Color.FromArgb(alpha, selected.R, selected.G, selected.B);
        ApplySettingsCardColor(cardColor);
        _ = _localSettingsService.SaveSettingAsync(AppSettingKeys.SettingsCardBlur, _settingsCardBlur);
    }

    private void ApplySettingsCardColor(Color cardColor)
    {
        var textColor = GetReadableTextColor(cardColor);
        var contrastTarget = textColor == Colors.White ? Colors.White : Colors.Black;
        var secondaryColor = BlendWithAlpha(cardColor, contrastTarget, 0.08);
        var strokeColor = BlendWithAlpha(cardColor, contrastTarget, 0.16);
        var useAcrylic = _settingsCardBlur > 0;
        if (useAcrylic)
        {
            var intensity = Math.Clamp(_settingsCardBlur / 30.0, 0, 1);
            var tintOpacity = Math.Clamp((cardColor.A / 255.0) * (0.86 - (0.62 * intensity)), 0.08, 0.9);
            Resources["CardBackgroundFillColorDefaultBrush"] = new AcrylicBrush
            {
                TintColor = Color.FromArgb(0xFF, cardColor.R, cardColor.G, cardColor.B),
                TintOpacity = tintOpacity,
                FallbackColor = cardColor
            };
            Resources["CardBackgroundFillColorSecondaryBrush"] = new AcrylicBrush
            {
                TintColor = Color.FromArgb(0xFF, secondaryColor.R, secondaryColor.G, secondaryColor.B),
                TintOpacity = Math.Clamp(tintOpacity - 0.06, 0.06, 0.86),
                FallbackColor = secondaryColor
            };
        }
        else
        {
            Resources["CardBackgroundFillColorDefaultBrush"] = new SolidColorBrush(cardColor);
            Resources["CardBackgroundFillColorSecondaryBrush"] = new SolidColorBrush(secondaryColor);
        }

        Resources["CardStrokeColorDefaultBrush"] = new SolidColorBrush(strokeColor);

        // Keep text readable against dark/light card colors.
        var readableBrush = new SolidColorBrush(textColor);
        Resources["TextFillColorPrimaryBrush"] = readableBrush;
        Resources["TextFillColorSecondaryBrush"] = new SolidColorBrush(Color.FromArgb(0xCC, textColor.R, textColor.G, textColor.B));
        ApplyReadableForegroundToSettings(readableBrush, _lastAutoReadableTextColor);
        _lastAutoReadableTextColor = textColor;
    }

    private static Color GetReadableTextColor(Color background)
    {
        var luminance =
            (0.2126 * background.R / 255.0) +
            (0.7152 * background.G / 255.0) +
            (0.0722 * background.B / 255.0);

        return luminance < 0.5 ? Colors.White : Colors.Black;
    }

    private static Color BlendWithAlpha(Color from, Color to, double ratio)
    {
        ratio = Math.Clamp(ratio, 0, 1);
        var r = (byte)Math.Round(from.R + (to.R - from.R) * ratio);
        var g = (byte)Math.Round(from.G + (to.G - from.G) * ratio);
        var b = (byte)Math.Round(from.B + (to.B - from.B) * ratio);
        return Color.FromArgb(from.A, r, g, b);
    }

    private void LoadProgramFontOptions()
    {
        if (_programFonts.Count > 0)
        {
            return;
        }

        var uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _programFonts.Add(new FontDisplayItem("Default (System)", true));

        try
        {
            using var fontsKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts");
            if (fontsKey != null)
            {
                foreach (var valueName in fontsKey.GetValueNames())
                {
                    var name = valueName;
                    var splitIndex = name.IndexOf('(');
                    if (splitIndex > 0)
                    {
                        name = name[..splitIndex];
                    }

                    name = name.Trim();
                    if (string.IsNullOrWhiteSpace(name) || !uniqueNames.Add(name))
                    {
                        continue;
                    }

                    _programFonts.Add(new FontDisplayItem(name, false));
                }
            }
        }
        catch
        {
        }

        _programFonts.Sort((a, b) =>
        {
            if (a.IsDefault && !b.IsDefault) return -1;
            if (!a.IsDefault && b.IsDefault) return 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        ProgramFontsGridView.ItemsSource = _programFonts;
    }

    private async Task LoadProgramFontSelectionAsync()
    {
        var savedFontFamily = await _localSettingsService.ReadSettingAsync<string>(AppSettingKeys.ProgramFontFamily);
        var selected = _programFonts.FirstOrDefault(x =>
            x.IsDefault
                ? string.IsNullOrWhiteSpace(savedFontFamily)
                : string.Equals(x.Name, savedFontFamily, StringComparison.OrdinalIgnoreCase))
            ?? _programFonts.FirstOrDefault(x => x.IsDefault);

        _isUpdatingProgramFontSelection = true;
        ProgramFontsGridView.SelectedItem = selected;
        _isUpdatingProgramFontSelection = false;
        UpdateProgramFontStatusText(savedFontFamily);
    }

    private async Task LoadProgramFontStyleAsync()
    {
        var savedFontSize = await _localSettingsService.ReadSettingAsync<double?>(AppSettingKeys.ProgramFontSize);
        var savedBold = await _localSettingsService.ReadSettingAsync<bool?>(AppSettingKeys.ProgramFontBold);
        var savedItalic = await _localSettingsService.ReadSettingAsync<bool?>(AppSettingKeys.ProgramFontItalic);
        var savedFamily = await _localSettingsService.ReadSettingAsync<string>(AppSettingKeys.ProgramFontFamily);

        _isUpdatingProgramFontStyleControls = true;
        ProgramFontSizeSlider.Value = savedFontSize.HasValue && savedFontSize.Value > 0 ? savedFontSize.Value : 14;
        ProgramFontBoldSwitch.IsOn = savedBold == true;
        ProgramFontItalicSwitch.IsOn = savedItalic == true;
        _isUpdatingProgramFontStyleControls = false;

        ApplyProgramFontToApp(savedFamily);
    }

    private async void ProgramFontsListExpandButton_Click(object sender, RoutedEventArgs e)
    {
        var isOpen = ProgramFontsListPanel.Visibility == Visibility.Visible;
        ProgramFontsListPanel.Visibility = isOpen ? Visibility.Collapsed : Visibility.Visible;
        ProgramFontsListExpandIcon.Glyph = isOpen ? "\uE70D" : "\uE70E";
        await Task.CompletedTask;
    }

    private async void ProgramFontsGridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializingSettings || _isUpdatingProgramFontSelection || ProgramFontsGridView.SelectedItem is not FontDisplayItem selected)
        {
            return;
        }

        var savedValue = selected.IsDefault ? string.Empty : selected.Name;
        await SaveProgramFontSettingsSnapshotAsync();
        ApplyProgramFontToApp(savedValue);
        UpdateProgramFontStatusText(savedValue);
    }

    private async void ProgramFontSizeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isInitializingSettings || _isUpdatingProgramFontStyleControls)
        {
            return;
        }

        await SaveProgramFontSettingsSnapshotAsync();
        ApplyProgramFontToApp(GetSelectedProgramFontFamilyValue());
    }

    private async void ProgramFontBoldSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializingSettings || _isUpdatingProgramFontStyleControls)
        {
            return;
        }

        await SaveProgramFontSettingsSnapshotAsync();
        ApplyProgramFontToApp(GetSelectedProgramFontFamilyValue());
    }

    private async void ProgramFontItalicSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializingSettings || _isUpdatingProgramFontStyleControls)
        {
            return;
        }

        await SaveProgramFontSettingsSnapshotAsync();
        ApplyProgramFontToApp(GetSelectedProgramFontFamilyValue());
    }

    private async void RestoreProgramFontButton_Click(object sender, RoutedEventArgs e)
    {
        _isUpdatingProgramFontSelection = true;
        ProgramFontsGridView.SelectedItem = _programFonts.FirstOrDefault(x => x.IsDefault);
        _isUpdatingProgramFontSelection = false;

        _isUpdatingProgramFontStyleControls = true;
        ProgramFontSizeSlider.Value = 14;
        ProgramFontBoldSwitch.IsOn = false;
        ProgramFontItalicSwitch.IsOn = false;
        _isUpdatingProgramFontStyleControls = false;

        await _localSettingsService.SaveSettingAsync(AppSettingKeys.ProgramFontFamily, string.Empty);
        await _localSettingsService.SaveSettingAsync(AppSettingKeys.ProgramFontSize, 0d);
        await _localSettingsService.SaveSettingAsync(AppSettingKeys.ProgramFontBold, false);
        await _localSettingsService.SaveSettingAsync(AppSettingKeys.ProgramFontItalic, false);

        ApplyProgramFontToApp(string.Empty);
        UpdateProgramFontStatusText(string.Empty);
    }

    private async void ThemeColorExpandButton_Click(object sender, RoutedEventArgs e)
    {
        var isOpen = ThemeColorPanel.Visibility == Visibility.Visible;
        ThemeColorPanel.Visibility = isOpen ? Visibility.Collapsed : Visibility.Visible;
        ThemeColorExpandIcon.Glyph = isOpen ? "\uE70D" : "\uE70E";
        await Task.CompletedTask;
    }

    private async void PageItemsExpandButton_Click(object sender, RoutedEventArgs e)
    {
        var isOpen = PageItemsPanel.Visibility == Visibility.Visible;
        PageItemsPanel.Visibility = isOpen ? Visibility.Collapsed : Visibility.Visible;
        PageItemsExpandIcon.Glyph = isOpen ? "\uE70D" : "\uE70E";
        await Task.CompletedTask;
    }

    private async void InstallExtensionExpandButton_Click(object sender, RoutedEventArgs e)
    {
        var isOpen = InstallExtensionPanel.Visibility == Visibility.Visible;
        InstallExtensionPanel.Visibility = isOpen ? Visibility.Collapsed : Visibility.Visible;
        InstallExtensionExpandIcon.Glyph = isOpen ? "\uE70D" : "\uE70E";
        if (!isOpen)
        {
            try
            {
                await RefreshExtensionsAsync();
            }
            catch
            {
                ExtensionStatusTextBlock.Text = "Failed to refresh extensions";
            }
        }

        await Task.CompletedTask;
    }

    private async void BackgroundImageOpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isInitializingSettings)
        {
            return;
        }

        var opacity = ClampOpacity(e.NewValue);
        await _localSettingsService.SaveSettingAsync(AppSettingKeys.BackgroundImageOpacity, opacity);
        ApplyBackgroundImageToShell(BackgroundImagePathTextBox.Text, opacity, ClampBlur(BackgroundImageBlurSlider.Value));
    }

    private async void BackgroundImageBlurSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isInitializingSettings)
        {
            return;
        }

        var blur = ClampBlur(e.NewValue);
        await _localSettingsService.SaveSettingAsync(AppSettingKeys.BackgroundImageBlur, blur);
        ApplyBackgroundImageToShell(BackgroundImagePathTextBox.Text, ClampOpacity(BackgroundImageOpacitySlider.Value), blur);
    }

    private async void ThemeOpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isInitializingSettings)
        {
            return;
        }

        var opacity = ClampOpacity(e.NewValue);
        await _localSettingsService.SaveSettingAsync(AppSettingKeys.ThemeColorOpacity, opacity);
        await _localSettingsService.SaveSettingAsync(AppSettingKeys.ThemeColorBlur, ClampBlur(ThemeBlurSlider.Value));

        if (_isCustomThemeSelected)
        {
            ApplyCustomAccentVisual(AccentColorPicker.Color);
        }
    }

    private async void ThemeBlurSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isInitializingSettings)
        {
            return;
        }

        var blur = ClampBlur(e.NewValue);
        await _localSettingsService.SaveSettingAsync(AppSettingKeys.ThemeColorBlur, blur);

        if (_isCustomThemeSelected)
        {
            ApplyCustomAccentVisual(AccentColorPicker.Color);
        }
    }

    private async void WebLayerOpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isInitializingSettings)
        {
            return;
        }

        var opacity = ClampOpacity(e.NewValue);
        await _localSettingsService.SaveSettingAsync(AppSettingKeys.WebLayerOpacity, opacity);
        await RefreshActiveWebPageBackgroundInjectionAsync();
    }

    private async void WebLayerBlurSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isInitializingSettings)
        {
            return;
        }

        var blur = ClampBlur(e.NewValue);
        await _localSettingsService.SaveSettingAsync(AppSettingKeys.WebLayerBlur, blur);
        await RefreshActiveWebPageBackgroundInjectionAsync();
    }

    private static double ClampOpacity(double value) => Math.Clamp(value, 0, 1);
    private static double ClampBlur(double value) => Math.Clamp(value, 0, 30);

    private void ApplyReadableForegroundToSettings(SolidColorBrush readableBrush, Color? previousAutoColor)
    {
        ApplyReadableForegroundToElement(SettingsLayoutRoot, readableBrush, previousAutoColor);
    }

    private static void ApplyReadableForegroundToElement(DependencyObject node, SolidColorBrush readableBrush, Color? previousAutoColor)
    {
        if (node is TextBlock textBlock)
        {
            var current = textBlock.Foreground as SolidColorBrush;
            var isUnset = textBlock.ReadLocalValue(TextBlock.ForegroundProperty) == DependencyProperty.UnsetValue;
            var isPreviouslyAuto = previousAutoColor.HasValue && current != null && current.Color == previousAutoColor.Value;
            if (isUnset || isPreviouslyAuto)
            {
                textBlock.Foreground = readableBrush;
            }
        }
        else if (node is Control control)
        {
            var current = control.Foreground as SolidColorBrush;
            var isUnset = control.ReadLocalValue(Control.ForegroundProperty) == DependencyProperty.UnsetValue;
            var isPreviouslyAuto = previousAutoColor.HasValue && current != null && current.Color == previousAutoColor.Value;
            if (isUnset || isPreviouslyAuto)
            {
                control.Foreground = readableBrush;
            }
        }

        var childCount = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < childCount; i++)
        {
            ApplyReadableForegroundToElement(VisualTreeHelper.GetChild(node, i), readableBrush, previousAutoColor);
        }
    }

    private static void ApplyBackgroundImageToShell(string? path, double opacity, double blur)
    {
        var clampedOpacity = ClampOpacity(opacity);
        var clampedBlur = ClampBlur(blur);
        if (App.MainWindow.Content is ShellPage shellPage)
        {
            shellPage.ApplyBackgroundImage(path, clampedOpacity, clampedBlur);
            return;
        }

        if (App.MainWindow.Content is Frame rootFrame && rootFrame.Content is ShellPage frameShell)
        {
            frameShell.ApplyBackgroundImage(path, clampedOpacity, clampedBlur);
        }
    }

    private static async Task RefreshActiveWebPageBackgroundInjectionAsync()
    {
        if (App.MainWindow.Content is ShellPage shellPage &&
            shellPage.ViewModel.NavigationService.Frame?.Content is WebViewPage webViewPage)
        {
            await webViewPage.RefreshWebBackgroundVisualAsync();
            return;
        }

        if (App.MainWindow.Content is Frame rootFrame &&
            rootFrame.Content is ShellPage frameShell &&
            frameShell.ViewModel.NavigationService.Frame?.Content is WebViewPage frameWebViewPage)
        {
            await frameWebViewPage.RefreshWebBackgroundVisualAsync();
        }
    }

    private void ApplyProgramFontToApp(string? fontFamily)
    {
        var trimmed = string.IsNullOrWhiteSpace(fontFamily) ? string.Empty : fontFamily.Trim();
        var toApply = string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
        var size = ProgramFontSizeSlider?.Value ?? 0;
        var applySize = size > 0 ? size : (double?)null;
        var isBold = ProgramFontBoldSwitch?.IsOn == true;
        var isItalic = ProgramFontItalicSwitch?.IsOn == true;
        ApplySettingsTitleFontSizes(applySize);

        if (toApply == null)
        {
            ClearValue(Control.FontFamilyProperty);
        }
        else
        {
            FontFamily = new FontFamily(toApply);
        }

        if (applySize.HasValue)
        {
            FontSize = applySize.Value;
        }
        else
        {
            ClearValue(Control.FontSizeProperty);
        }

        FontWeight = isBold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;
        FontStyle = isItalic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal;

        if (App.MainWindow.Content is ShellPage shellPage)
        {
            shellPage.ApplyProgramFontFamily(toApply);
            shellPage.ApplyProgramTextStyle(applySize, isBold, isItalic);
            return;
        }

        if (App.MainWindow.Content is Frame rootFrame && rootFrame.Content is ShellPage frameShell)
        {
            frameShell.ApplyProgramFontFamily(toApply);
            frameShell.ApplyProgramTextStyle(applySize, isBold, isItalic);
        }
    }

    private void ApplySettingsTitleFontSizes(double? baseSize)
    {
        var effectiveBase = baseSize.HasValue && baseSize.Value > 0 ? baseSize.Value : 14;
        var level1 = effectiveBase + 6;
        var level2 = effectiveBase + 1;
        var options = effectiveBase + 1;
        Resources["Level1TitleFontSize"] = level1;
        Resources["Level2TitleFontSize"] = level2;
        Resources["ThemeModeOptionFontSize"] = options;

        PersonalizationTitleTextBlock.FontSize = level1;
        GlobalSettingsTitleTextBlock.FontSize = level1;
        AboutTitleTextBlock.FontSize = level1;
        WebSettingsTitleTextBlock.FontSize = level1;

        ThemeTitleTextBlock.FontSize = level2;
        ProgramFontTitleTextBlock.FontSize = level2;
        BackgroundImageTitleTextBlock.FontSize = level2;
        GlobalHotkeyTitleTextBlock.FontSize = level2;
        PageItemsTitleTextBlock.FontSize = level2;
        DownloadPathTitleTextBlock.FontSize = level2;
        WebOpacityTitleTextBlock.FontSize = level2;
        ExtensionsTitleTextBlock.FontSize = level2;

        ThemeLightRadioButton.FontSize = options;
        ThemeDarkRadioButton.FontSize = options;
        ThemeDefaultRadioButton.FontSize = options;
        ThemeCustomRadioButton.FontSize = options;
    }

    private string GetSelectedProgramFontFamilyValue()
    {
        if (ProgramFontsGridView.SelectedItem is not FontDisplayItem selected)
        {
            return string.Empty;
        }

        return selected.IsDefault ? string.Empty : selected.Name;
    }

    private async Task SaveProgramFontSettingsSnapshotAsync()
    {
        var family = GetSelectedProgramFontFamilyValue();
        var size = ProgramFontSizeSlider?.Value ?? 14d;
        var bold = ProgramFontBoldSwitch?.IsOn == true;
        var italic = ProgramFontItalicSwitch?.IsOn == true;

        await _localSettingsService.SaveSettingAsync(AppSettingKeys.ProgramFontFamily, family);
        await _localSettingsService.SaveSettingAsync(AppSettingKeys.ProgramFontSize, size);
        await _localSettingsService.SaveSettingAsync(AppSettingKeys.ProgramFontBold, bold);
        await _localSettingsService.SaveSettingAsync(AppSettingKeys.ProgramFontItalic, italic);
    }

    private void UpdateProgramFontStatusText(string? fontFamily)
    {
        ProgramFontStatusTextBlock.Text = string.IsNullOrWhiteSpace(fontFamily) ? "Current: Default (System)" : $"Current: {fontFamily}";
    }

    private static bool HasInteractiveAncestor(DependencyObject source, Border boundary)
    {
        var current = source;
        while (current != null && !ReferenceEquals(current, boundary))
        {
            if (current is TextBox || current is Button || current is ToggleSwitch || current is ComboBox)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private async Task SaveCardLayoutAsync()
    {
        var layout = new List<string>();
        layout.AddRange(ContentArea.Children.OfType<Border>().Select(x => $"L:{x.Name}"));
        layout.AddRange(RightColumnStack.Children.OfType<Border>().Select(x => $"R:{x.Name}"));
        await _localSettingsService.SaveSettingAsync(AppSettingKeys.SettingsCardLayout, layout);
    }

    private async Task RestoreCardLayoutAsync()
    {
        var saved = await _localSettingsService.ReadSettingAsync<List<string>>(AppSettingKeys.SettingsCardLayout);
        if (saved == null || saved.Count == 0)
        {
            return;
        }

        var cards = new Dictionary<string, Border>
        {
            [PersonalizationCard.Name] = PersonalizationCard,
            [GlobalSettingsCard.Name] = GlobalSettingsCard,
            [AboutCard.Name] = AboutCard,
            [WebSettingsCard.Name] = WebSettingsCard
        };
        var placed = new HashSet<string>();

        foreach (var token in saved)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            var parts = token.Split(':', 2);
            if (parts.Length != 2 || !cards.TryGetValue(parts[1], out var card) || !placed.Add(parts[1]))
            {
                continue;
            }

            if (card.Parent is Panel currentParent)
            {
                currentParent.Children.Remove(card);
            }

            var target = parts[0] == "R" ? RightColumnStack : ContentArea;
            target.Children.Add(card);
        }

        // Add cards not present in saved layout using default columns.
        AppendCardIfMissing(PersonalizationCard, ContentArea, placed);
        AppendCardIfMissing(GlobalSettingsCard, ContentArea, placed);
        AppendCardIfMissing(AboutCard, RightColumnStack, placed);
        AppendCardIfMissing(WebSettingsCard, RightColumnStack, placed);
    }

    private static void AppendCardIfMissing(Border card, Panel target, HashSet<string> placed)
    {
        if (placed.Contains(card.Name))
        {
            return;
        }

        if (card.Parent is Panel currentParent)
        {
            currentParent.Children.Remove(card);
        }

        target.Children.Add(card);
        placed.Add(card.Name);
    }

    private sealed class FontDisplayItem
    {
        public FontDisplayItem(string name, bool isDefault)
        {
            Name = name;
            IsDefault = isDefault;
            DisplayFontFamily = isDefault ? "Segoe UI" : name;
        }

        public string Name { get; }

        public bool IsDefault { get; }

        public string DisplayFontFamily { get; }
    }
}
