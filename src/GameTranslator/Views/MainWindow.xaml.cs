using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameTranslator.Native;
using GameTranslator.Services;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using WinFormsDrawing = System.Drawing;
using WpfColor = System.Windows.Media.Color;

namespace GameTranslator.Views;

public partial class MainWindow : Window
{
    private readonly ConfigService _config;
    private OverlayWindow? _overlay;
    private CaptureService? _capture;
    private bool _isRunning;
    private List<MonitorInfo> _monitors = new();
    private WpfColor _accentColor = WpfColor.FromRgb(0, 120, 212);
    private WpfColor _stopColor = WpfColor.FromRgb(220, 50, 50);
    private int _hotkeyId = 9001;
    private bool _hotkeyRegistered;
    private HwndSource? _hwndSource;

    // Keybinding capture
    private bool _listening;
    private string _capturedHotkey = "";

    // Low-level mouse hook for global mouse hotkeys
    private IntPtr _mouseHook;
    private Win32.LowLevelHookProc? _mouseHookDelegate;

    // System tray
    private Forms.NotifyIcon? _trayIcon;

    public MainWindow(ConfigService config)
    {
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            Logger.Error("InitializeComponent失败: {0}", ex.ToString());
            throw;
        }
        _config = config;
        try
        {
            LoadSystemAccent();
            ApplyAccentColor();
            LoadConfig();
            BuildMonitorSelector();
            try { InitTrayIcon(); } catch (Exception ex) { Logger.Warn("托盘: {0}", ex.Message); }
            TxtVersion.Text = "v3.0";
            try { SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged; } catch { }
            SourceInitialized += OnSourceInitialized;
            PreviewKeyDown += OnPreviewKeyDown;
            Closing += OnWindowClosing;
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow构造失败: {0}", ex.ToString());
            throw;
        }
    }

    private void InitTrayIcon()
    {
        try
        {
            // Extract icon from the exe itself (no external file needed)
            var exePath = Environment.ProcessPath ?? "";
            var exeIcon = File.Exists(exePath)
                ? WinFormsDrawing.Icon.ExtractAssociatedIcon(exePath)
                : null;

            _trayIcon = new Forms.NotifyIcon
            {
                Text = "Game Translator",
                Icon = exeIcon ?? WinFormsDrawing.SystemIcons.Application,
                Visible = false,
            };

            _trayIcon.DoubleClick += (_, _) => ShowWindow();
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("显示窗口", null, (_, _) => ShowWindow());
            menu.Items.Add("-");
            menu.Items.Add("退出", null, (_, _) => ForceExit());
            _trayIcon.ContextMenuStrip = menu;
        }
        catch (Exception ex)
        {
            Logger.Warn("托盘图标创建失败: {0}", ex.Message);
            _trayIcon?.Dispose();
            _trayIcon = null;
        }
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_isRunning)
        {
            // Minimize to tray instead of closing
            e.Cancel = true;
            Hide();
            if (_trayIcon != null) _trayIcon.Visible = true;
            Logger.Info("最小化到系统托盘");
        }
    }

    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        if (_trayIcon != null) _trayIcon.Visible = false;
    }

    private void ForceExit()
    {
        Closing -= OnWindowClosing; // Allow normal close
        if (_isRunning) Stop();
        if (_trayIcon != null) { _trayIcon.Visible = false; _trayIcon.Dispose(); _trayIcon = null; }
        System.Windows.Application.Current.Shutdown();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (_trayIcon != null) { _trayIcon.Visible = false; _trayIcon.Dispose(); _trayIcon = null; }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _hwndSource?.AddHook(WndProcHook);
    }

    private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Global keyboard hotkey
        if (msg == Win32.WM_HOTKEY && wParam == (IntPtr)_hotkeyId)
        {
            _overlay?.TriggerOnce();
            handled = true;
            return IntPtr.Zero;
        }

        // Mouse 4/5 during listening mode (capture for keybinding UI)
        if (_listening && msg == Win32.WM_XBUTTONDOWN)
        {
            var button = (wParam.ToInt64() >> 16) & 0xFFFF;
            string key = button == Win32.XBUTTON1 ? "Mouse4" : button == Win32.XBUTTON2 ? "Mouse5" : null;
            if (key != null) { ApplyCapturedKey(key); handled = true; }
        }

        return IntPtr.Zero;
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_listening) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Escape cancels
        if (key == Key.Escape)
        {
            CancelListening();
            e.Handled = true;
            return;
        }

        // Modifier-only keys ignored
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return;

        var mods = Keyboard.Modifiers;
        var parts = new List<string>();
        if ((mods & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((mods & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((mods & ModifierKeys.Alt) != 0) parts.Add("Alt");
        parts.Add(KeyToString(key));

        ApplyCapturedKey(string.Join("+", parts));
        e.Handled = true;
    }

    private void BtnHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (_listening)
        {
            CancelListening();
            return;
        }

        // Start listening
        _listening = true;
        BtnHotkey.Content = "按下按键... (右键取消)";
        BtnHotkey.Background = new SolidColorBrush(WpfColor.FromRgb(255, 240, 200));
        BtnHotkey.Focus();

        // Also handle right-click on the button to cancel
        BtnHotkey.MouseRightButtonDown += OnBtnHotkeyRightClick;
    }

    private void OnBtnHotkeyRightClick(object sender, MouseButtonEventArgs e)
    {
        CancelListening();
        e.Handled = true;
    }

    private void ApplyCapturedKey(string hotkey)
    {
        _capturedHotkey = hotkey;
        _listening = false;
        BtnHotkey.Content = hotkey;
        BtnHotkey.ClearValue(BackgroundProperty);
        BtnHotkey.MouseRightButtonDown -= OnBtnHotkeyRightClick;
        Logger.Info("快捷键已设置: {0}", hotkey);
    }

    private void CancelListening()
    {
        _listening = false;
        BtnHotkey.Content = _capturedHotkey.Length > 0 ? _capturedHotkey : _config.Config.ManualHotKey;
        BtnHotkey.ClearValue(BackgroundProperty);
        BtnHotkey.MouseRightButtonDown -= OnBtnHotkeyRightClick;
    }

    private static string KeyToString(Key key) => key switch
    {
        Key.D0 => "0", Key.D1 => "1", Key.D2 => "2", Key.D3 => "3", Key.D4 => "4",
        Key.D5 => "5", Key.D6 => "6", Key.D7 => "7", Key.D8 => "8", Key.D9 => "9",
        Key.OemPlus => "=", Key.OemMinus => "-", Key.OemComma => ",", Key.OemPeriod => ".",
        Key.OemQuestion => "/", Key.OemSemicolon => ";", Key.OemQuotes => "'",
        Key.OemOpenBrackets => "[", Key.OemCloseBrackets => "]", Key.OemPipe => "\\",
        Key.OemTilde => "`",
        _ => key.ToString()
    };

    private void LoadSystemAccent()
    {
        try
        {
            var glassColor = SystemParameters.WindowGlassColor;
            if (glassColor.A > 0) _accentColor = WpfColor.FromRgb(glassColor.R, glassColor.G, glassColor.B);
        }
        catch { }
    }

    private void ApplyAccentColor() => BtnToggle.Background = new SolidColorBrush(_accentColor);

    private void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
            Dispatcher.Invoke(() => { LoadSystemAccent(); if (!_isRunning) ApplyAccentColor(); });
    }

    private void LoadConfig()
    {
        var c = _config.Config;
        foreach (ComboBoxItem item in CmbApiType.Items)
            if (item.Tag?.ToString() == c.ApiType) { CmbApiType.SelectedItem = item; break; }
        CmbApiType.SelectedItem ??= CmbApiType.Items[0];

        foreach (ComboBoxItem item in CmbCaptureMode.Items)
            if (item.Tag?.ToString() == c.CaptureMode) { CmbCaptureMode.SelectedItem = item; break; }
        CmbCaptureMode.SelectedItem ??= CmbCaptureMode.Items[0];

        foreach (ComboBoxItem item in CmbOcrEngine.Items)
            if (item.Tag?.ToString() == c.OcrEngine) { CmbOcrEngine.SelectedItem = item; break; }
        CmbOcrEngine.SelectedItem ??= CmbOcrEngine.Items[0];

        TxtApiEndpoint.Text = c.ApiEndpoint;
        TxtApiKey.Text = c.ApiKey;
        TxtModelName.Text = c.ModelName;
        TxtTencentId.Text = c.TencentSecretId;
        TxtTencentKey.Text = c.TencentSecretKey;
        _capturedHotkey = c.ManualHotKey;
        BtnHotkey.Content = _capturedHotkey;

        foreach (ComboBoxItem item in CmbTargetLang.Items)
            if (item.Tag?.ToString() == c.TargetLanguage) { CmbTargetLang.SelectedItem = item; break; }
        CmbTargetLang.SelectedItem ??= CmbTargetLang.Items[0];

        ApplyApiTypeUI();
        ApplyOcrEngineUI();
    }

    private void SaveConfig()
    {
        var c = _config.Config;
        if (CmbApiType.SelectedItem is ComboBoxItem apiItem) c.ApiType = apiItem.Tag?.ToString() ?? "Google";
        if (CmbCaptureMode.SelectedItem is ComboBoxItem cmItem) c.CaptureMode = cmItem.Tag?.ToString() ?? "FullScreen";
        if (CmbOcrEngine.SelectedItem is ComboBoxItem ocrItem) c.OcrEngine = ocrItem.Tag?.ToString() ?? "Windows";
        c.ApiEndpoint = TxtApiEndpoint.Text.Trim();
        c.ApiKey = TxtApiKey.Text.Trim();
        c.ModelName = TxtModelName.Text.Trim();
        c.TencentSecretId = TxtTencentId.Text.Trim();
        c.TencentSecretKey = TxtTencentKey.Text.Trim();
        c.ManualHotKey = _capturedHotkey.Length > 0 ? _capturedHotkey : "Ctrl+Shift+T";
        if (CmbTargetLang.SelectedItem is ComboBoxItem item) c.TargetLanguage = item.Tag?.ToString() ?? "Chinese";
        _config.Save();
    }

    private void CmbOcrEngine_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyOcrEngineUI();

    private void ApplyOcrEngineUI()
    {
        if (CmbOcrEngine.SelectedItem is not ComboBoxItem item) return;
        var isTencent = item.Tag?.ToString() == "Tencent";
        LblTencentId.Visibility = isTencent ? Visibility.Visible : Visibility.Collapsed;
        TxtTencentId.Visibility = isTencent ? Visibility.Visible : Visibility.Collapsed;
        LblTencentKey.Visibility = isTencent ? Visibility.Visible : Visibility.Collapsed;
        TxtTencentKey.Visibility = isTencent ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CmbApiType_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyApiTypeUI();

    private void ApplyApiTypeUI()
    {
        if (CmbApiType.SelectedItem is not ComboBoxItem item) return;
        var tag = item.Tag?.ToString();
        var isSimple = tag is "DeepL" or "Google";
        LblEndpoint.Visibility = isSimple ? Visibility.Collapsed : Visibility.Visible;
        TxtApiEndpoint.Visibility = isSimple ? Visibility.Collapsed : Visibility.Visible;
        LblModel.Visibility = isSimple ? Visibility.Collapsed : Visibility.Visible;
        TxtModelName.Visibility = isSimple ? Visibility.Collapsed : Visibility.Visible;

        if (tag == "Google")
        {
            TxtApiKey.Visibility = Visibility.Collapsed;
            LblApiKey.Visibility = Visibility.Collapsed;
        }
        else
        {
            TxtApiKey.Visibility = Visibility.Visible;
            LblApiKey.Visibility = Visibility.Visible;
            LblApiKey.Content = tag == "DeepL" ? "DeepL API Key" : "API Key";
            if (tag == "DeepL" && (string.IsNullOrWhiteSpace(TxtApiEndpoint.Text) || TxtApiEndpoint.Text.Contains("openai")))
                TxtApiEndpoint.Text = "https://api-free.deepl.com";
        }
    }

    private void BuildMonitorSelector()
    {
        _monitors = Win32.EnumerateMonitors();
        MonitorList.Items.Clear();
        var maxRight = _monitors.Max(m => m.Left + m.Width);
        var maxBottom = _monitors.Max(m => m.Top + m.Height);
        var totalWidth = maxRight - _monitors.Min(m => m.Left);
        var totalHeight = maxBottom - _monitors.Min(m => m.Top);
        var minLeft = _monitors.Min(m => m.Left);
        var minTop = _monitors.Min(m => m.Top);

        foreach (var monitor in _monitors)
        {
            double scale = Math.Min(380.0 / totalWidth, 120.0 / totalHeight);
            var dispLeft = (monitor.Left - minLeft) * scale;
            var dispTop = (monitor.Top - minTop) * scale;
            var dispW = monitor.Width * scale;
            var dispH = monitor.Height * scale;
            var isSelected = monitor.Index == _config.Config.TargetDisplayIndex;

            var container = new Grid
            {
                Width = Math.Max(400, dispLeft + dispW + 10), Height = dispTop + dispH + 10,
                Margin = new Thickness(0, 2, 0, 2),
                Cursor = System.Windows.Input.Cursors.Hand, Tag = monitor.Index
            };
            container.MouseLeftButtonDown += MonitorClicked;

            var rect = new Border
            {
                Width = dispW, Height = dispH,
                Background = new SolidColorBrush(isSelected
                    ? WpfColor.FromArgb(80, 0, 120, 212) : WpfColor.FromArgb(40, 100, 100, 100)),
                BorderBrush = new SolidColorBrush(isSelected
                    ? WpfColor.FromRgb(0, 120, 212) : WpfColor.FromRgb(180, 180, 180)),
                BorderThickness = new Thickness(isSelected ? 2 : 1), CornerRadius = new CornerRadius(3),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(dispLeft, dispTop, 0, 0)
            };

            var labelStack = new StackPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var primaryTag = monitor.IsPrimary ? " [主]" : "";
            labelStack.Children.Add(new TextBlock
            {
                Text = $"显示器 {monitor.Index + 1}{primaryTag}", FontSize = 11,
                FontWeight = isSelected ? FontWeights.Bold : FontWeights.Normal,
                Foreground = new SolidColorBrush(isSelected ? WpfColor.FromRgb(0, 80, 160) : WpfColor.FromRgb(80, 80, 80)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center, TextAlignment = TextAlignment.Center
            });
            labelStack.Children.Add(new TextBlock
            {
                Text = $"{monitor.Width}x{monitor.Height}", FontSize = 10,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(120, 120, 120)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center, TextAlignment = TextAlignment.Center
            });
            rect.Child = labelStack;
            container.Children.Add(rect);
            MonitorList.Items.Add(container);
        }
    }

    private void MonitorClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Grid grid && grid.Tag is int index)
        {
            _config.Config.TargetDisplayIndex = index;
            _config.Save();
            MonitorList.Items.Clear();
            BuildMonitorSelector();
            TxtStatus.Text = $"已选择: 显示器 {index + 1}";
        }
    }

    private void BtnToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning) Stop(); else Start();
    }

    private void Start()
    {
        SaveConfig();
        Logger.Info("===== 启动手动模式 =====");
        try
        {
            _capture = new CaptureService(_config.Config.TargetDisplayIndex);
            _capture.Initialize();
            _overlay = new OverlayWindow(_capture, new OcrService(_config), new TranslationService(_config), _config);
            _overlay.Show();
            _overlay.Start();
            RegisterHotkey();
            InstallMouseHook();
            _isRunning = true;
            BtnToggle.Content = "停止";
            BtnToggle.Background = new SolidColorBrush(_stopColor);
            TxtStatus.Text = $"运行中 — 按 {_config.Config.ManualHotKey} 翻译";
        }
        catch (Exception ex)
        {
            Logger.Error("启动失败: {0}", ex.ToString());
            System.Windows.MessageBox.Show($"启动失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Stop()
    {
        Logger.Info("===== 停止 =====");
        UnregisterHotkey();
        UninstallMouseHook();
        _overlay?.Stop();
        _overlay?.Close();
        _overlay = null;
        _capture?.Dispose();
        _capture = null;
        _isRunning = false;
        BtnToggle.Content = "开始";
        BtnToggle.Background = new SolidColorBrush(_accentColor);
        TxtStatus.Text = "已停止";
    }

    private void RegisterHotkey()
    {
        UnregisterHotkey();
        var hotkey = _config.Config.ManualHotKey;

        // Mouse-only hotkeys handled by WndProc, not RegisterHotKey
        if (hotkey is "Mouse4" or "Mouse5")
        {
            Logger.Info("鼠标快捷键 (WndProc): {0}", hotkey);
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        var (mod, key) = ParseHotkey(hotkey);
        if (key == 0) return;
        _hotkeyRegistered = Win32.RegisterHotKey(hwnd, _hotkeyId, mod, key);
        Logger.Info("快捷键注册: {0} {1}", hotkey, _hotkeyRegistered ? "成功" : "失败");
    }

    private void InstallMouseHook()
    {
        var hotkey = _config.Config.ManualHotKey;
        if (hotkey is not ("Mouse4" or "Mouse5")) return;

        _mouseHookDelegate = (nCode, wParam, lParam) =>
        {
            if (nCode >= 0)
            {
                var data = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
                var button = (data.mouseData >> 16) & 0xFFFF;
                bool match = (button == Win32.XBUTTON1 && hotkey == "Mouse4")
                          || (button == Win32.XBUTTON2 && hotkey == "Mouse5");
                if (match)
                {
                    if (wParam == (IntPtr)Win32.WM_XBUTTONDOWN)
                        Dispatcher.InvokeAsync(() => _overlay?.PressBegin());
                    else if (wParam == (IntPtr)Win32.WM_XBUTTONUP)
                        Dispatcher.InvokeAsync(() => _overlay?.PressEnd());
                }
            }
            return Win32.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        };

        _mouseHook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _mouseHookDelegate,
            Win32.GetModuleHandle(null!), 0);
        Logger.Info("全局鼠标钩子已安装: {0} {1}", hotkey, _mouseHook != IntPtr.Zero ? "成功" : "失败");
    }

    private void UninstallMouseHook()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
            _mouseHookDelegate = null;
            Logger.Info("全局鼠标钩子已卸载");
        }
    }

    private void UnregisterHotkey()
    {
        if (_hotkeyRegistered)
        {
            Win32.UnregisterHotKey(new WindowInteropHelper(this).Handle, _hotkeyId);
            _hotkeyRegistered = false;
        }
    }

    private static (uint Mod, uint Key) ParseHotkey(string hotkey)
    {
        uint mod = 0, key = 0;
        foreach (var p in hotkey.Split('+', StringSplitOptions.TrimEntries))
        {
            if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) mod |= Win32.MOD_CONTROL;
            else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase)) mod |= Win32.MOD_SHIFT;
            else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase)) mod |= Win32.MOD_ALT;
            else if (p.Equals("Mouse4", StringComparison.OrdinalIgnoreCase)) key = 0x05; // VK_XBUTTON1
            else if (p.Equals("Mouse5", StringComparison.OrdinalIgnoreCase)) key = 0x06; // VK_XBUTTON2
            else { var kc = new KeyConverter().ConvertFromString(p); if (kc is Key k) key = (uint)KeyInterop.VirtualKeyFromKey(k); }
        }
        return (mod, key);
    }
}
