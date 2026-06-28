using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace GameTranslator.Services;

public class OcrResult
{
    public string Text { get; set; } = "";
    public Rectangle Bounds { get; set; }
}

public class OcrService
{
    private readonly ConfigService _config;
    private readonly OcrEngine _engine;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HashSet<string> _recentTexts = new();
    private const int MaxCacheSize = 500;
    private static readonly string[] SupportedTags =
        ["en", "ja", "zh-Hans", "zh-Hant", "ko", "fr", "de", "es", "ru", "pt", "it"];

    public OcrService(ConfigService config)
    {
        _config = config;
        _engine = TryCreateBestEngine();
        Logger.Info("OCR 引擎已初始化, 引擎={0}, 语言={1}",
            config.Config.OcrEngine, _engine.RecognizerLanguage.LanguageTag);
    }

    public void ResetDedupCache() { _recentTexts.Clear(); }

    private static Bitmap EnhanceForOcr(Bitmap src)
    {
        var result = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.DrawImage(src, 0, 0);
        var rect = new Rectangle(0, 0, result.Width, result.Height);
        var data = result.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        int bytes = Math.Abs(data.Stride) * result.Height;
        var pixels = new byte[bytes];
        System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, bytes);
        SharpenPixels(pixels, data.Stride, result.Width, result.Height);
        ContrastStretch(pixels, bytes);
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data.Scan0, bytes);
        result.UnlockBits(data);
        return result;
    }

    private static void SharpenPixels(byte[] pixels, int stride, int w, int h)
    {
        var copy = (byte[])pixels.Clone();
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                int i = y * stride + x * 4;
                for (int c = 0; c < 3; c++)
                {
                    int center = copy[i + c];
                    int avg = (copy[i - stride + c] + copy[i + stride + c] + copy[i - 4 + c] + copy[i + 4 + c]) / 4;
                    int val = center * 2 - avg;
                    pixels[i + c] = (byte)Math.Clamp(val, 0, 255);
                }
            }
    }

    private static void ContrastStretch(byte[] pixels, int length)
    {
        for (int i = 0; i < length; i += 4)
            for (int c = 0; c < 3; c++)
            {
                int v = pixels[i + c];
                if (v < 80) pixels[i + c] = (byte)Math.Max(0, v - 20);
                else if (v > 180) pixels[i + c] = (byte)Math.Min(255, v + 20);
            }
    }

    private static OcrEngine TryCreateBestEngine()
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine != null) return engine;
        foreach (var tag in SupportedTags)
        {
            try { engine = OcrEngine.TryCreateFromLanguage(new Language(tag)); if (engine != null) return engine; }
            catch { }
        }
        throw new InvalidOperationException("No OCR engine available. Install a language pack in Windows Settings.");
    }

    public async Task<List<OcrResult>> RecognizeAsync(Bitmap bitmap)
    {
        if (_config.Config.OcrEngine == "Tencent") return await RecognizeTencent(bitmap);
        return await RecognizeWindows(bitmap);
    }

    private async Task<List<OcrResult>> RecognizeWindows(Bitmap bitmap)
    {
        var results = new List<OcrResult>();
        try
        {
            using var enhanced = EnhanceForOcr(bitmap);
            using var ms = new MemoryStream();
            enhanced.Save(ms, ImageFormat.Png);
            var bytes = ms.ToArray();
            var stream = new InMemoryRandomAccessStream();
            var writer = new DataWriter(stream);
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var softwareBitmap = await decoder.GetSoftwareBitmapAsync();
            if (softwareBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 || softwareBitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
                softwareBitmap = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            var ocrResult = await _engine.RecognizeAsync(softwareBitmap);
            foreach (var line in ocrResult.Lines)
            {
                var text = line.Text.Trim();
                if (string.IsNullOrEmpty(text) || text.Length < 2) continue;
                if (_recentTexts.Contains(text)) continue;
                _recentTexts.Add(text);
                if (_recentTexts.Count > MaxCacheSize) _recentTexts.Clear();
                var words = line.Words;
                var bounds = new Rectangle(
                    (int)words[0].BoundingRect.X, (int)words[0].BoundingRect.Y,
                    (int)(words[^1].BoundingRect.X + words[^1].BoundingRect.Width - words[0].BoundingRect.X),
                    (int)words.Max(w => w.BoundingRect.Height));
                results.Add(new OcrResult { Text = text, Bounds = bounds });
            }
            return results;
        }
        catch (ObjectDisposedException) { return results; }
        catch (ArgumentException) { return results; }
    }

    private async Task<List<OcrResult>> RecognizeTencent(Bitmap bitmap)
    {
        var results = new List<OcrResult>();
        try
        {
            var cfg = _config.Config;
            if (string.IsNullOrWhiteSpace(cfg.TencentSecretId) || string.IsNullOrWhiteSpace(cfg.TencentSecretKey))
            {
                Logger.Warn("腾讯云OCR未配置密钥");
                return results;
            }
            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Jpeg);
            var base64 = Convert.ToBase64String(ms.ToArray());
            var body = JsonSerializer.Serialize(new { ImageBase64 = base64 });
            var service = "ocr";
            var action = "GeneralBasicOCR";
            var version = "2018-11-19";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var endpoint = $"{service}.tencentcloudapi.com";
            var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var canonicalReq = $"POST\n/\n\ncontent-type:application/json\nhost:{endpoint}\n\ncontent-type;host\n{HashSha256(body)}";
            var stringToSign = $"TC3-HMAC-SHA256\n{timestamp}\n{date}/{service}/tc3_request\n{HashSha256(canonicalReq)}";
            var sd = HmacSha256(Encoding.UTF8.GetBytes("TC3" + cfg.TencentSecretKey), date);
            var ss = HmacSha256(sd, service);
            var sk = HmacSha256(ss, "tc3_request");
            var sig = BitConverter.ToString(HmacSha256(sk, stringToSign)).Replace("-", "").ToLowerInvariant();
            var auth = $"TC3-HMAC-SHA256 Credential={cfg.TencentSecretId}/{date}/{service}/tc3_request, SignedHeaders=content-type;host, Signature={sig}";
            var request = new HttpRequestMessage(HttpMethod.Post, $"https://{endpoint}")
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body))
            };
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            request.Headers.TryAddWithoutValidation("Authorization", auth);
            request.Headers.Add("X-TC-Action", action);
            request.Headers.Add("X-TC-Version", version);
            request.Headers.Add("X-TC-Timestamp", timestamp);
            request.Headers.Add("X-TC-Region", "ap-guangzhou");
            var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                Logger.Error("腾讯云OCR失败: {0}", json[..Math.Min(json.Length, 300)]);
                return results;
            }
            using var doc = JsonDocument.Parse(json);
            var resp = doc.RootElement.GetProperty("Response");
            if (resp.TryGetProperty("Error", out var error))
            {
                Logger.Error("腾讯云OCR: {0}", error.GetProperty("Message").GetString());
                return results;
            }
            var detections = resp.GetProperty("TextDetections");
            foreach (var d in detections.EnumerateArray())
            {
                var text = d.GetProperty("DetectedText").GetString()?.Trim();
                if (string.IsNullOrEmpty(text) || text.Length < 2) continue;
                if (_recentTexts.Contains(text)) continue;
                _recentTexts.Add(text);
                if (_recentTexts.Count > MaxCacheSize) _recentTexts.Clear();
                var poly = d.GetProperty("ItemPolygon");
                results.Add(new OcrResult
                {
                    Text = text,
                    Bounds = new Rectangle(poly.GetProperty("X").GetInt32(), poly.GetProperty("Y").GetInt32(),
                        poly.GetProperty("Width").GetInt32(), poly.GetProperty("Height").GetInt32())
                });
            }
        }
        catch (Exception ex) { Logger.Error("腾讯云OCR: {0}", ex.Message); }
        return results;
    }

    private static string HashSha256(string data)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(data));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private static byte[] HmacSha256(byte[] key, string data)
        => new HMACSHA256(key).ComputeHash(Encoding.UTF8.GetBytes(data));
}
