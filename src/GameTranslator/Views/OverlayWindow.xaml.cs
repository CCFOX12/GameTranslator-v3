using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using GameTranslator.Native;
using GameTranslator.Services;
using Color = System.Windows.Media.Color;
using WinFormsColor = System.Drawing.Color;

namespace GameTranslator.Views;

public partial class OverlayWindow : Window
{
    private readonly CaptureService _capture;
    private readonly OcrService _ocr;
    private readonly TranslationService _translator;
    private readonly ConfigService _config;
    private CancellationTokenSource? _cts;
    private double _dpiScaleX = 1.0;
    private double _dpiScaleY = 1.0;
    private bool _translating;
    private DateTime _lastTrigger = DateTime.MinValue;
    private const int DebounceMs = 1000;
    private System.Windows.Point _selStart;
    private bool _selecting;
    private System.Windows.Shapes.Rectangle? _selRect;

    private static readonly string[] ChineseFonts =
        ["Microsoft YaHei", "Noto Sans SC", "SimHei", "DengXian", "FangSong", "SimSun"];
    private string _chineseFont = "Microsoft YaHei";

    public OverlayWindow(CaptureService capture, OcrService ocr, TranslationService translator, ConfigService config)
    {
        InitializeComponent();
        _capture = capture;
        _ocr = ocr;
        _translator = translator;
        _config = config;
        Loaded += OnLoaded;
        _chineseFont = FindAvailableFont();
    }

    private static string FindAvailableFont()
    {
        foreach (var font in ChineseFonts)
            if (Fonts.SystemFontFamilies.Any(f => f.Source.Equals(font, StringComparison.OrdinalIgnoreCase)))
                return font;
        return "Microsoft YaHei";
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
        exStyle |= Win32.WS_EX_TRANSPARENT | Win32.WS_EX_LAYERED | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE;
        Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, exStyle);
        var monitors = Win32.EnumerateMonitors();
        var idx = Math.Min(_config.Config.TargetDisplayIndex, monitors.Count - 1);
        var screen = monitors[idx];
        _dpiScaleX = screen.DpiScaleX;
        _dpiScaleY = screen.DpiScaleY;
        Left = screen.Left / _dpiScaleX;
        Top = screen.Top / _dpiScaleY;
        Width = screen.Width / _dpiScaleX;
        Height = screen.Height / _dpiScaleY;
        Logger.Info("叠加窗口已加载: {0}x{1}, DPI={2:F0}%", screen.Width, screen.Height, _dpiScaleX * 100);
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        Dispatcher.Invoke(() =>
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0, 200, 80));
            StatusText.Text = "等待快捷键...";
        });
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _translating = false;
        Dispatcher.Invoke(() =>
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(255, 85, 85));
            StatusText.Text = "已停止";
            OverlayCanvas.Children.Clear();
        });
    }

    public void TriggerOnce()
    {
        var now = DateTime.Now;
        if ((now - _lastTrigger).TotalMilliseconds < DebounceMs) return;
        _lastTrigger = now;
        if (_translating) ClearOverlays();
        else if (_config.Config.CaptureMode == "Region") EnterSelectionModeUI();
        else StartTranslate();
    }

    public void PressBegin()
    {
        var now = DateTime.Now;
        if ((now - _lastTrigger).TotalMilliseconds < DebounceMs) return;
        _lastTrigger = now;
        if (_config.Config.CaptureMode == "Region")
        {
            if (_translating) { ClearOverlays(); return; }
            Dispatcher.Invoke(() => EnterSelectionModeUI());
        }
    }

    public void PressEnd()
    {
        if (_config.Config.CaptureMode == "Region" && _selecting) return;
        if (_config.Config.CaptureMode != "Region" && !_selecting) TriggerOnce();
    }

    private void ClearOverlays()
    {
        _translating = false;
        Dispatcher.Invoke(() =>
        {
            OverlayCanvas.Children.Clear();
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0, 200, 80));
            StatusText.Text = _config.Config.CaptureMode == "Region" ? "框选模式" : "等待快捷键...";
        });
    }

    private void StartTranslate()
    {
        _translating = true;
        Dispatcher.Invoke(() => { StatusDot.Fill = new SolidColorBrush(Color.FromRgb(255, 200, 0)); StatusText.Text = "翻译中..."; });
        _ocr.ResetDedupCache();
        _ = CaptureAndTranslate();
    }

    private void EnterSelectionModeUI()
    {
        Dispatcher.Invoke(() =>
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(255, 200, 0));
            StatusText.Text = "拖拽框选...";
            EnterSelectionMode();
        });
    }

    private void EnterSelectionMode()
    {
        _selecting = true;
        SelectionMask.Visibility = Visibility.Visible;
        SelectionCanvas.Visibility = Visibility.Visible;
        SelectionCanvas.Children.Clear();
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
        exStyle &= ~Win32.WS_EX_TRANSPARENT;
        Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, exStyle);
    }

    private void ExitSelectionMode()
    {
        _selecting = false;
        SelectionMask.Visibility = Visibility.Collapsed;
        SelectionCanvas.Visibility = Visibility.Collapsed;
        SelectionCanvas.Children.Clear();
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
        exStyle |= Win32.WS_EX_TRANSPARENT;
        Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, exStyle);
    }

    private void SelectionMask_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_selecting) return;
        _selStart = e.GetPosition(SelectionMask);
        SelectionCanvas.Children.Clear();
        _selRect = new System.Windows.Shapes.Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0, 200, 255)),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(30, 0, 200, 255)),
            StrokeDashArray = new DoubleCollection([4, 2]),
        };
        Canvas.SetLeft(_selRect, _selStart.X);
        Canvas.SetTop(_selRect, _selStart.Y);
        _selRect.Width = 0;
        _selRect.Height = 0;
        SelectionCanvas.Children.Add(_selRect);
    }

    private void SelectionMask_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_selecting || _selRect == null) return;
        var pos = e.GetPosition(SelectionMask);
        var x = Math.Min(pos.X, _selStart.X);
        var y = Math.Min(pos.Y, _selStart.Y);
        Canvas.SetLeft(_selRect, x);
        Canvas.SetTop(_selRect, y);
        _selRect.Width = Math.Abs(pos.X - _selStart.X);
        _selRect.Height = Math.Abs(pos.Y - _selStart.Y);
    }

    private void SelectionMask_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_selecting || _selRect == null) return;
        var pos = e.GetPosition(SelectionMask);
        var x = (int)(Math.Min(pos.X, _selStart.X) * _dpiScaleX);
        var y = (int)(Math.Min(pos.Y, _selStart.Y) * _dpiScaleY);
        var w = (int)(Math.Abs(pos.X - _selStart.X) * _dpiScaleX);
        var h = (int)(Math.Abs(pos.Y - _selStart.Y) * _dpiScaleY);
        ExitSelectionMode();
        _ocr.ResetDedupCache();
        Logger.Info("框选区域: ({0},{1}) {2}x{3}", x, y, w, h);
        if (w < 10 || h < 10) { ClearOverlays(); return; }
        _translating = true;
        Dispatcher.Invoke(() => StatusText.Text = "翻译中...");
        _ = CaptureRegionAndTranslate(x, y, w, h);
    }

    private async Task CaptureRegionAndTranslate(int x, int y, int w, int h)
    {
        try
        {
            using var fullBitmap = _capture.CaptureFrame();
            if (fullBitmap == null || w < 10 || h < 10) { ResetAfterEmpty(); return; }
            var cropRect = new Rectangle(
                Math.Max(0, x), Math.Max(0, y),
                Math.Min(w, fullBitmap.Width - Math.Max(0, x)),
                Math.Min(h, fullBitmap.Height - Math.Max(0, y)));
            if (cropRect.Width < 10 || cropRect.Height < 10) { ResetAfterEmpty(); return; }
            using var bitmap = fullBitmap.Clone(cropRect, fullBitmap.PixelFormat);
            if (!await TranslateAndRender(bitmap, cropRect.Left, cropRect.Top)) ResetAfterEmpty();
        }
        catch (Exception ex) { Logger.Error("框选翻译: {0}", ex.Message); ResetAfterEmpty(); }
    }

    private async Task CaptureAndTranslate()
    {
        try
        {
            using var fullBitmap = _capture.CaptureFrame();
            if (fullBitmap == null) { ResetAfterEmpty(); return; }
            var fgHwnd = Win32.GetForegroundWindow();
            Win32.GetWindowRect(fgHwnd, out var fgRect);
            var fgBounds = new Rectangle(
                Math.Max(0, fgRect.Left), Math.Max(0, fgRect.Top),
                Math.Min(fullBitmap.Width, fgRect.Right) - Math.Max(0, fgRect.Left),
                Math.Min(fullBitmap.Height, fgRect.Bottom) - Math.Max(0, fgRect.Top));
            if (fgBounds.Width < 100 || fgBounds.Height < 100) { ResetAfterEmpty(); return; }
            using var bitmap = fullBitmap.Clone(fgBounds, fullBitmap.PixelFormat);
            if (!await TranslateAndRender(bitmap, fgBounds.Left, fgBounds.Top)) ResetAfterEmpty();
        }
        catch (Exception ex) { Logger.Error("翻译: {0}", ex.Message); ResetAfterEmpty(); }
    }

    private void ResetAfterEmpty()
    {
        _translating = false;
        Dispatcher.Invoke(() => { StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0, 200, 80)); StatusText.Text = "等待快捷键..."; });
    }

    private async Task<bool> TranslateAndRender(Bitmap bitmap, int offsetX, int offsetY)
    {
        var t0 = DateTime.Now;
        var ocrResults = await _ocr.RecognizeAsync(bitmap);
        var tOcr = (DateTime.Now - t0).TotalMilliseconds;
        if (ocrResults.Count == 0) { Logger.Info("OCR 无文字"); return false; }
        var toTranslate = ocrResults
            .Where(r => r.Text.Length >= 2)
            .Where(r => !r.Text.All(c => char.IsDigit(c) || char.IsPunctuation(c) || char.IsWhiteSpace(c)))
            .DistinctBy(r => r.Text).ToList();
        Logger.Info("OCR={0:F0}ms, 文字={1}->过滤={2}", tOcr, ocrResults.Count, toTranslate.Count);
        if (toTranslate.Count == 0) return false;
        var colorSamples = toTranslate.Select(r => (r.Text, r.Bounds, C: SampleColors(bitmap, r.Bounds))).ToList();
        using var semaphore = new SemaphoreSlim(6);
        var tasks = colorSamples.Select(async s =>
        {
            await semaphore.WaitAsync();
            try { var t = await _translator.TranslateAsync(s.Text); return (s.Text, s.Bounds, Translated: t, s.C.BgColor, s.C.TextColor, s.C.FontSize); }
            finally { semaphore.Release(); }
        }).ToList();
        foreach (var task in tasks)
        {
            var (sourceText, bounds, translated, bgColor, textColor, fontSize) = await task;
            if (string.IsNullOrEmpty(translated)) continue;
            var absX = bounds.X + offsetX; var absY = bounds.Y + offsetY;
            var (dipX, dipY) = ToDip(absX - 3, absY - 2);
            var dipW = Math.Max(bounds.Width + 12, 40) / _dpiScaleX;
            var dipH = Math.Max(bounds.Height + 6, 20) / _dpiScaleY;
            var dipFontSize = fontSize / _dpiScaleY;
            await Dispatcher.InvokeAsync(() =>
            {
                var container = new Grid();
                container.Children.Add(new System.Windows.Shapes.Rectangle
                {
                    Fill = new SolidColorBrush(ToWpfColor(bgColor)), RadiusX = 2, RadiusY = 2, Width = dipW, Height = dipH,
                });
                container.Children.Add(new TextBlock
                {
                    Text = translated, Foreground = new SolidColorBrush(ToWpfColor(textColor)),
                    FontSize = dipFontSize, FontFamily = new System.Windows.Media.FontFamily(_chineseFont),
                    TextWrapping = TextWrapping.Wrap, MaxWidth = Math.Max(dipW, 200),
                });
                Canvas.SetLeft(container, dipX); Canvas.SetTop(container, dipY); OverlayCanvas.Children.Add(container);
            });
        }
        return true;
    }

    private (double X, double Y) ToDip(int px, int py) => (px / _dpiScaleX, py / _dpiScaleY);

    private static (WinFormsColor BgColor, WinFormsColor TextColor, double FontSize) SampleColors(Bitmap bitmap, Rectangle bounds)
    {
        var left = Math.Max(0, bounds.Left); var top = Math.Max(0, bounds.Top);
        var right = Math.Min(bitmap.Width - 1, bounds.Right); var bottom = Math.Min(bitmap.Height - 1, bounds.Bottom);
        if (right <= left || bottom <= top) return (WinFormsColor.FromArgb(220, 0, 0, 0), WinFormsColor.White, 16);
        var fontSize = Math.Max(11, Math.Min(42, bounds.Height * 0.8));
        var samples = new List<WinFormsColor>();
        int step = Math.Max(1, (right - left) / 8);
        for (int x = left + step; x < right - step; x += step) { samples.Add(bitmap.GetPixel(x, top)); samples.Add(bitmap.GetPixel(x, bottom - 1)); }
        for (int y = top + step; y < bottom - step; y += step) { samples.Add(bitmap.GetPixel(left, y)); samples.Add(bitmap.GetPixel(right - 1, y)); }
        var bgColor = DominantColor(samples) ?? WinFormsColor.FromArgb(220, 0, 0, 0);
        var bgBright = (bgColor.R + bgColor.G + bgColor.B) / 3.0;
        var fgSamples = new List<WinFormsColor>();
        for (int y = top + 2; y < bottom - 2; y += 2)
            for (int x = left + 2; x < right - 2; x += 2)
            { var pixel = bitmap.GetPixel(x, y); if (Math.Abs((pixel.R + pixel.G + pixel.B) / 3.0 - bgBright) > 35) fgSamples.Add(pixel); }
        WinFormsColor textColor = fgSamples.Count > 2
            ? DominantColor(fgSamples) ?? (bgBright < 128 ? WinFormsColor.White : WinFormsColor.Black)
            : bgBright < 128 ? WinFormsColor.White : WinFormsColor.Black;
        return (bgColor, textColor, fontSize);
    }

    private static WinFormsColor? DominantColor(List<WinFormsColor> colors)
    {
        if (colors.Count == 0) return null;
        return colors.GroupBy(c => ((c.R / 32) << 10) | ((c.G / 32) << 5) | (c.B / 32)).OrderByDescending(g => g.Count()).First().First();
    }

    private static Color ToWpfColor(WinFormsColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);
}
