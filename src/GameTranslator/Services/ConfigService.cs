using System.IO;
using System.Text.Json;
using GameTranslator.Models;

namespace GameTranslator.Services;

public class ConfigService
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GameTranslator", "config.json");

    public AppConfig Config { get; private set; } = new();

    public ConfigService() { Load(); }

    public void Load()
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                Config = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                Logger.Info("配置已加载 (AppData)");
            }
            else { Logger.Info("配置文件不存在, 使用默认配置"); }
        }
        catch (Exception ex) { Logger.Warn("配置加载失败: {0}", ex.Message); Config = new AppConfig(); }
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch { }
    }
}
