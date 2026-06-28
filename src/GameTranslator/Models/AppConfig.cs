namespace GameTranslator.Models;

public class AppConfig
{
    public string ApiType { get; set; } = "Google";
    public string ApiEndpoint { get; set; } = "https://api.openai.com/v1/chat/completions";
    public string ApiKey { get; set; } = "";
    public string ModelName { get; set; } = "gpt-4o-mini";
    public string TargetLanguage { get; set; } = "Chinese";
    public string CaptureMode { get; set; } = "FullScreen";
    public string OcrEngine { get; set; } = "Windows";
    public string TencentSecretId { get; set; } = "";
    public string TencentSecretKey { get; set; } = "";
    public string ManualHotKey { get; set; } = "Ctrl+Shift+T";
    public int TargetDisplayIndex { get; set; } = 0;
}
