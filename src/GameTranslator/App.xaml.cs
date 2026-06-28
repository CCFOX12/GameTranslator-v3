using System.Windows;
using GameTranslator.Native;
using GameTranslator.Services;
using GameTranslator.Views;

namespace GameTranslator;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Logger.Info("===== Game Translator 启动 =====");
        Logger.Info("系统: Win{0}, .NET {1}, PID={2}",
            Environment.OSVersion.Version, Environment.Version, Environment.ProcessId);

        var monitors = Win32.EnumerateMonitors();
        Logger.Info("检测到 {0} 个显示器:", monitors.Count);
        foreach (var m in monitors)
        {
            Logger.Info("  显示器#{0}: 物理=({1},{2}) {3}x{4}, DPI={5:F0}%, {6}",
                m.Index, m.Left, m.Top, m.Width, m.Height,
                m.DpiScaleX * 100, m.IsPrimary ? "主显示器" : "副显示器");
        }

        var configService = new ConfigService();
        Logger.Info("配置: API={0}, 引擎={1}, 目标语言={2}",
            configService.Config.ApiEndpoint, configService.Config.ApiType,
            configService.Config.TargetLanguage);

        Logger.Info("正在创建主窗口...");
        var mainWindow = new MainWindow(configService);
        Logger.Info("正在显示主窗口...");
        mainWindow.Show();
        Logger.Info("主窗口已显示");
    }
}
