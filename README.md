# Game Translator v3.0

Real-time on-screen game translation overlay for Windows. Press a hotkey, OCR captures game text, and translations are rendered as overlays directly on screen.

## Features

- **Full-screen** or **region-select** capture modes
- **Three translation engines**: OpenAI-compatible (DeepSeek/Kimi/GPT), DeepL, Google Translate (free, no key needed)
- **Dual OCR engines**: Windows built-in OCR (offline) + Tencent Cloud OCR
- Transparent overlay renders translations in-place, matching the game's original colors
- Multi-monitor support with DPI awareness
- System tray minimize, global keyboard + mouse hotkeys

## Quick Start

### Prerequisites
- Windows 10 19041+ (x64)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Build & Run

```powershell
dotnet build src/GameTranslator/GameTranslator.csproj -c Release
dotnet run --project src/GameTranslator/GameTranslator.csproj -c Release
```

### Publish (standalone .exe)

```powershell
dotnet publish src/GameTranslator/GameTranslator.csproj -c Release --self-contained true -o publish
```

## Project Structure

```
.
├── src/GameTranslator/
│   ├── Models/           # AppConfig
│   ├── Native/           # Win32 P/Invoke wrappers
│   ├── Services/         # Capture, OCR, Translation, Config, Logger
│   ├── Views/            # MainWindow, OverlayWindow
│   ├── App.xaml(.cs)     # Entry point
│   └── GameTranslator.csproj
├── docs/                 # Documentation
└── tests/                # Unit tests
```

## Configuration

Config is stored at `%LocalAppData%\GameTranslator\config.json`.

| Key | Default | Description |
|-----|---------|-------------|
| ApiType | Google | Translation engine: Google, DeepL, OpenAI |
| OcrEngine | Windows | Windows (offline) or Tencent (cloud) |
| TargetLanguage | Chinese | Chinese, English, Japanese, Korean |
| CaptureMode | FullScreen | FullScreen or Region |

## License

For personal use.

See [docs/PROJECT_OVERVIEW.md](docs/PROJECT_OVERVIEW.md) for detailed architecture.
