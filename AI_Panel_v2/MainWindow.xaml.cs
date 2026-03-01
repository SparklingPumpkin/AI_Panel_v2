using System.Runtime.InteropServices;

using AI_Panel_v2.Contracts.Services;
using AI_Panel_v2.Helpers;
using AI_Panel_v2.Models;
using AI_Panel_v2.ViewModels;
using AI_Panel_v2.Views;

using Microsoft.UI.Xaml.Controls;

using WinUIEx.Messaging;

using Windows.System;
using Windows.UI.ViewManagement;

namespace AI_Panel_v2;

public sealed partial class MainWindow : WindowEx
{
    private const int WmCommand = 0x0111;
    private const int WmHotKey = 0x0312;
    private const int WmApp = 0x8000;
    private const int WmTrayIcon = WmApp + 0x32;
    private const int WmLButtonUp = 0x0202;
    private const int WmLButtonDblClk = 0x0203;
    private const int WmRButtonUp = 0x0205;
    private const int WmContextMenu = 0x007B;
    private const int NinSelect = 0x0400;
    private const int HotKeyId = 0xA11;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const int TrayOpenCommand = 0x1001;
    private const int TrayLowPowerCommand = 0x1002;
    private const int TrayExitCommand = 0x1003;
    private const uint MfString = 0x00000000;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetVersion = 0x00000004;
    private const uint NotifyIconVersion4 = 4;
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x00000010;
    private const uint TpmLeftAlign = 0x0000;
    private const uint TpmBottomAlign = 0x0020;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;
    private static readonly nint HwndTopMost = new(-1);
    private static readonly nint HwndNoTopMost = new(-2);
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;

    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly nint _hWnd;
    private readonly nint _trayMenu;
    private readonly nint _trayIconHandle;
    private WindowMessageMonitor? _messageMonitor;
    private HotKeySetting _hotKeySetting = new();
    private bool _isHiddenToTray;
    private bool _isPinned;
    private bool _isLowPowerMode;
    private bool _trayIconAdded;

    private readonly UISettings _settings;
    public bool IsPinned => _isPinned;

    public MainWindow()
    {
        InitializeComponent();
        _localSettingsService = App.GetService<ILocalSettingsService>();
        _hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico"));
        Content = null;
        Title = "AppDisplayName".GetLocalized();

        _trayIconHandle = LoadTrayIconHandle();
        _trayMenu = CreatePopupMenu();
        AppendMenu(_trayMenu, MfString, TrayOpenCommand, "Open");
        AppendMenu(_trayMenu, MfString, TrayLowPowerCommand, "Enter Low Power Mode");
        AppendMenu(_trayMenu, MfString, TrayExitCommand, "Exit");

        _settings = new UISettings();
        _settings.ColorValuesChanged += Settings_ColorValuesChanged;

        InitializeWindowMessageMonitor();
        Closed += MainWindow_Closed;
        _ = ReloadHotKeyAsync();
        _ = LoadPinnedStateAsync();
        _ = LoadAccentPaletteAsync();
    }

    private void Settings_ColorValuesChanged(UISettings sender, object args)
    {
        _dispatcherQueue.TryEnqueue(() => TitleBarHelper.ApplySystemThemeToCaptionButtons());
    }

    public async Task ReloadHotKeyAsync()
    {
        var savedHotKey = await _localSettingsService.ReadSettingAsync<HotKeySetting>(AppSettingKeys.GlobalHotKey);
        _hotKeySetting = savedHotKey ?? new HotKeySetting();

        UnregisterHotKey(_hWnd, HotKeyId);

        var modifiers = GetModifierFlags(_hotKeySetting);
        if (modifiers == 0)
        {
            modifiers = ModControl;
        }

        RegisterHotKey(_hWnd, HotKeyId, modifiers, (uint)_hotKeySetting.Key);
    }

    public async Task ReloadWebItemsAsync()
    {
        if (Content is Frame rootFrame && rootFrame.Content is ShellPage shellPage)
        {
            await shellPage.ReloadWebItemsAsync();
        }
    }

    public void EnterLowPowerMode()
    {
        _isLowPowerMode = true;
        HideToTray();

        // Release XAML tree and page instances; restore on next hotkey/open.
        Content = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    private void InitializeWindowMessageMonitor()
    {
        _messageMonitor = new WindowMessageMonitor(this);
        _messageMonitor.WindowMessageReceived += MessageMonitor_WindowMessageReceived;
    }

    private void MessageMonitor_WindowMessageReceived(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message.MessageId == WmHotKey && unchecked((int)e.Message.WParam) == HotKeyId)
        {
            ToggleWindowVisibility();
            e.Handled = true;
            return;
        }

        if (e.Message.MessageId == WmTrayIcon)
        {
            HandleTrayMessage(unchecked((int)((long)e.Message.LParam & 0xFFFF)));
            e.Handled = true;
            return;
        }

        if (e.Message.MessageId == WmCommand)
        {
            var commandId = unchecked((int)e.Message.WParam) & 0xFFFF;
            HandleTrayCommand(commandId);
        }
    }

    private void ToggleWindowVisibility()
    {
        var isForeground = GetForegroundWindow() == _hWnd;
        var shouldRestore = _isHiddenToTray || !isForeground || WindowState == WindowState.Minimized;

        if (shouldRestore)
        {
            ShowFromTray();
        }
        else
        {
            HideToTray();
        }
    }

    private void HideToTray()
    {
        _isHiddenToTray = true;
        EnsureTrayIconVisible();
        IsShownInSwitchers = false;
        HwndExtensions.HideWindow(_hWnd);
    }

    private void ShowFromTray()
    {
        if (_isLowPowerMode)
        {
            RecoverFromLowPowerMode();
        }

        _isHiddenToTray = false;
        EnsureTrayIconVisible();
        IsShownInSwitchers = true;
        HwndExtensions.ShowWindow(_hWnd);
        HwndExtensions.RestoreWindow(_hWnd);
        Activate();
        HwndExtensions.SetForegroundWindow(_hWnd);
    }

    private void RecoverFromLowPowerMode()
    {
        _isLowPowerMode = false;
        if (Content == null)
        {
            Content = App.GetService<ShellPage>();
            _ = App.GetService<IThemeSelectorService>().SetRequestedThemeAsync();
            App.GetService<INavigationService>().NavigateTo(typeof(WebViewViewModel).FullName!, 0);
        }
    }

    public void SetPinned(bool pinned)
    {
        _isPinned = pinned;
        SetWindowPos(_hWnd, pinned ? HwndTopMost : HwndNoTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        _ = _localSettingsService.SaveSettingAsync(AppSettingKeys.WindowPinned, pinned);
    }

    private async Task LoadPinnedStateAsync()
    {
        var pinned = await _localSettingsService.ReadSettingAsync<bool>(AppSettingKeys.WindowPinned);
        SetPinned(pinned);
    }

    private async Task LoadAccentPaletteAsync()
    {
        var themeMode = await _localSettingsService.ReadSettingAsync<string>(AppSettingKeys.ThemeMode);
        if (!string.Equals(themeMode, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var accentColorText = await _localSettingsService.ReadSettingAsync<string>(AppSettingKeys.AccentColor);
        if (AccentPaletteHelper.TryParseHex(accentColorText, out var customAccent))
        {
            AccentPaletteHelper.ApplyAccentColor(customAccent);
            return;
        }

        var paletteName = await _localSettingsService.ReadSettingAsync<string>(AppSettingKeys.AccentPalette);
        var palette = AccentPaletteHelper.GetByName(paletteName);
        AccentPaletteHelper.ApplyPalette(palette);
    }

    private void HandleTrayMessage(int lParam)
    {
        if (lParam == WmLButtonUp || lParam == WmLButtonDblClk || lParam == NinSelect)
        {
            ShowFromTray();
            return;
        }

        if (lParam == WmRButtonUp || lParam == WmContextMenu)
        {
            ShowTrayContextMenu();
        }
    }

    private void ShowTrayContextMenu()
    {
        if (!GetCursorPos(out var point))
        {
            return;
        }

        SetForegroundWindow(_hWnd);
        var commandId = TrackPopupMenuEx(_trayMenu, TpmLeftAlign | TpmBottomAlign | TpmRightButton | TpmReturnCmd, point.X, point.Y, _hWnd, nint.Zero);
        if (commandId != 0)
        {
            HandleTrayCommand((int)commandId);
        }
    }

    private void HandleTrayCommand(int commandId)
    {
        if (commandId == TrayOpenCommand)
        {
            ShowFromTray();
        }
        else if (commandId == TrayLowPowerCommand)
        {
            EnterLowPowerMode();
        }
        else if (commandId == TrayExitCommand)
        {
            Close();
        }
    }

    private void EnsureTrayIconVisible()
    {
        if (_trayIconHandle == nint.Zero)
        {
            return;
        }

        var data = CreateNotifyIconData();
        var command = _trayIconAdded ? NimModify : NimAdd;
        if (Shell_NotifyIcon(command, ref data))
        {
            if (!_trayIconAdded)
            {
                data.uVersion = NotifyIconVersion4;
                Shell_NotifyIcon(NimSetVersion, ref data);
            }

            _trayIconAdded = true;
        }
    }

    private static nint LoadTrayIconHandle()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "TrayIcon.ico"),
            Path.Combine(AppContext.BaseDirectory, "Assets", "AppPanelTray.ico"),
            Path.Combine(AppContext.BaseDirectory, "Assets", "WindowIcon.ico")
        };

        foreach (var path in candidates)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            var iconHandle = LoadImage(nint.Zero, path, ImageIcon, 0, 0, LrLoadFromFile);
            if (iconHandle != nint.Zero)
            {
                return iconHandle;
            }
        }

        return nint.Zero;
    }

    private NOTIFYICONDATA CreateNotifyIconData()
    {
        return new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hWnd,
            uID = 1,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = WmTrayIcon,
            hIcon = _trayIconHandle,
            szTip = "AI Panel"
        };
    }

    private void MainWindow_Closed(object sender, Microsoft.UI.Xaml.WindowEventArgs args)
    {
        UnregisterHotKey(_hWnd, HotKeyId);

        if (_trayIconAdded)
        {
            var data = CreateNotifyIconData();
            Shell_NotifyIcon(NimDelete, ref data);
        }

        if (_trayIconHandle != nint.Zero)
        {
            DestroyIcon(_trayIconHandle);
        }

        if (_trayMenu != nint.Zero)
        {
            DestroyMenu(_trayMenu);
        }

        if (_messageMonitor != null)
        {
            _messageMonitor.WindowMessageReceived -= MessageMonitor_WindowMessageReceived;
            _messageMonitor.Dispose();
            _messageMonitor = null;
        }
    }

    private static uint GetModifierFlags(HotKeySetting setting)
    {
        uint modifiers = 0;
        if (setting.Alt) modifiers |= ModAlt;
        if (setting.Ctrl) modifiers |= ModControl;
        if (setting.Shift) modifiers |= ModShift;
        if (setting.Win) modifiers |= ModWin;
        return modifiers;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadImage(nint hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(nint hMenu, uint uFlags, int uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(nint hmenu, uint fuFlags, int x, int y, nint hwnd, nint lptpm);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public int uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}
