# Game Translator v3.0

Windows 桌面游戏翻译覆盖工具。按快捷键截取屏幕 → OCR 识别游戏文字 → 调用翻译 API → 用透明覆盖层将译文直接显示在原文位置上方。

## 技术栈

C# / .NET 8 / WPF，支持单文件自包含发布（无依赖）。

## 快速开始

### 环境要求
- Windows 10 19041+ (x64)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### 编译运行

```powershell
dotnet build src/GameTranslator/GameTranslator.csproj -c Release
dotnet run --project src/GameTranslator/GameTranslator.csproj -c Release
```

### 发布 (单 exe)

```powershell
dotnet publish src/GameTranslator/GameTranslator.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/
```

## 项目结构

```
src/GameTranslator/
├── Services/
│   ├── CaptureService.cs      # SharpDX DXGI 桌面复制截图
│   ├── OcrService.cs          # Windows OCR + 腾讯云 OCR，含锐化/对比度预处理
│   ├── TranslationService.cs  # 支持 OpenAI/DeepL/Google 三种翻译引擎
│   ├── ConfigService.cs       # JSON 配置持久化 (%LocalAppData%\GameTranslator\)
│   └── Logger.cs              # 文件日志 (exe 同目录 debug.log)
├── Native/
│   └── Win32.cs               # 全部 P/Invoke 声明
├── Models/
│   └── AppConfig.cs           # 配置模型
├── Views/
│   ├── OverlayWindow.xaml/cs  # 透明叠加窗口 + 框选 + 翻译渲染
│   └── MainWindow.xaml/cs     # 设置面板 (引擎/密钥/快捷键/显示器)
└── App.xaml/cs                # 启动入口
```

## 核心功能

| 功能 | 说明 |
|------|------|
| 手动快捷键翻译 | 全局热键触发，截取当前前台窗口 → OCR → 翻译 → 覆盖显示 |
| 双 OCR 引擎 | Windows 内置 OCR（免费）/ 腾讯云 OCR（1000次/月免费） |
| 三翻译引擎 | OpenAI 兼容 / DeepL / Google 翻译（免费免密钥） |
| 双捕获模式 | 全屏前台窗口 / 长按框选区域 |
| 智能渲染 | 自动采样原文颜色+背景+字号，翻译覆盖精确对位 |
| 系统托盘 | 最小化到托盘，全局快捷键后台运行 |
| DPI 自适应 | GetDpiForMonitor 读取真实缩放比，多显示器支持 |

## 已知问题

- 发布版偶发启动崩溃：单文件自包含 exe 在部分环境下静默崩溃（Debug 版正常），疑似 WPF 资源加载问题
- 腾讯云 OCR 延迟 1-1.5 秒：网络往返 + 签名耗时，比本地 Windows OCR 慢但准确率更高
- 框选模式刚重写为长按拖拽松开翻译，未经充分测试
- 文字预处理参数硬编码：Gamma/阈值/锐化强度不可调，不同类型游戏可能需要不同参数
- 特殊字体 OCR 仍不完美：漫画 SFX 手绘字、像素字体等极端情况是所有 OCR 引擎的共同瓶颈

## License

仅供个人使用。

详细架构文档见 [docs/PROJECT_OVERVIEW.md](docs/PROJECT_OVERVIEW.md)
