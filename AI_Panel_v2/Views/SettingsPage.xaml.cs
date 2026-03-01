using AI_Panel_v2.Contracts.Services;
using AI_Panel_v2.Helpers;
using AI_Panel_v2.Models;
using AI_Panel_v2.ViewModels;

using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

using Windows.System;
using Windows.UI;
using Windows.UI.Core;

using ColorSpectrumShape = Microsoft.UI.Xaml.Controls.ColorSpectrumShape;

namespace AI_Panel_v2.Views;

// TODO: Set the URL for your privacy policy by updating SettingsPage_PrivacyTermsLink.NavigateUri in Resources.resw.
public sealed partial class SettingsPage : Page
{
    private const string FallbackWebUrl = "https://docs.microsoft.com/windows/apps/";
    private const string ThemeModeLight = "Light";
    private const string ThemeModeDark = "Dark";
    private const string ThemeModeDefault = "Default";
    private const string ThemeModeCustom = "Custom";
    private readonly ILocalSettingsService _localSettingsService;
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly List<(TextBox NameBox, TextBox UrlBox)> _webItemEditors = new();
    private List<WebItemSetting> _webItems = new();
    private HotKeySetting _pendingHotKey = new();
    private bool _isRecordingHotKey;
    private Border? _dragCard;
    private uint _dragPointerId;
    private Point _dragStartPointerInRoot;
    private Point _dragStartCardTranslation;
    private bool _isInitializingSettings;
    private bool _isCustomThemeSelected;

    public SettingsViewModel ViewModel
    {
        get;
    }

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        _localSettingsService = App.GetService<ILocalSettingsService>();
        _themeSelectorService = App.GetService<IThemeSelectorService>();
        InitializeComponent();
        Loaded += SettingsPage_Loaded;
    }

    private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadSettingsAsync();
        await RestoreCardLayoutAsync();
    }

    private async Task LoadSettingsAsync()
    {
        _isInitializingSettings = true;
        _pendingHotKey = await _localSettingsService.ReadSettingAsync<HotKeySetting>(AppSettingKeys.GlobalHotKey) ?? new HotKeySetting();
        HotKeyTextBlock.Text = $"Current: {ToDisplayText(_pendingHotKey)}";

        _webItems = await LoadWebItemsAsync();
        RenderWebItemEditors();

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
            ApplyCustomChromeToShell(AccentColorPicker.Color);
            AccentPaletteStatusTextBlock.Text = "Custom theme active";
        }
        else
        {
            _isCustomThemeSelected = false;
            ClearCustomChromeFromShell();
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

        _isInitializingSettings = false;
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
        _ = _localSettingsService.SaveSettingAsync(AppSettingKeys.AccentColor, AccentPaletteHelper.ToHex(AccentColorPicker.Color));
        AccentPaletteStatusTextBlock.Text = "Custom theme applied";
    }

    private async Task ApplyAndPersistCustomAccentAsync()
    {
        var color = AccentColorPicker.Color;
        ApplyCustomAccentVisual(color);
        await _localSettingsService.SaveSettingAsync(AppSettingKeys.AccentColor, AccentPaletteHelper.ToHex(color));
    }

    private static void ApplyCustomAccentVisual(Color color)
    {
        AccentPaletteHelper.ApplyAccentColor(color);
        ApplyCustomChromeToShell(color);
    }

    private static void ApplyCustomChromeToShell(Color accentColor)
    {
        if (App.MainWindow.Content is ShellPage shellPage)
        {
            shellPage.ApplyCustomChromeTheme(accentColor);
        }
    }

    private static void ClearCustomChromeFromShell()
    {
        if (App.MainWindow.Content is ShellPage shellPage)
        {
            shellPage.ClearCustomChromeTheme();
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

        if (border.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            border.RenderTransform = transform;
        }

        _dragStartCardTranslation = new Point(transform.X, transform.Y);
        border.Opacity = 0.95;
        Canvas.SetZIndex(border, 1000);
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

        _dragCard.ReleasePointerCapture(e.Pointer);
        _dragCard.Opacity = 1;
        Canvas.SetZIndex(_dragCard, 0);
        _ = SaveCardLayoutAsync();
        _dragCard = null;
        _dragPointerId = 0;
        e.Handled = true;
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
            [HotkeyCard.Name] = HotkeyCard,
            [AboutCard.Name] = AboutCard,
            [PageSettingsCard.Name] = PageSettingsCard
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
        AppendCardIfMissing(HotkeyCard, ContentArea, placed);
        AppendCardIfMissing(AboutCard, RightColumnStack, placed);
        AppendCardIfMissing(PageSettingsCard, RightColumnStack, placed);
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
}
