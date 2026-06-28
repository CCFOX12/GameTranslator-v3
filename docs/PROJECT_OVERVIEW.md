# Game Translator v3.0 — 项目内容概述文档

---

## 1. 项目概要

### 1.1 项目用途

**Game Translator** 是一款 Windows 平台的桌面游戏实时翻译工具。通过快捷键触发屏幕捕获 → OCR 文字识别 → 多引擎翻译 → 悬浮窗口覆盖渲染的流水线，为玩家提供所见即所得的实时游戏内文字翻译体验。

### 1.2 核心价值

- 支持**全屏翻译**和**框选翻译**两种捕获模式
- 三套翻译引擎自由切换：OpenAI 兼容 / DeepL / Google 翻译（免费）
- 双 OCR 引擎：Windows 系统自带 OCR + 腾讯云 OCR
- 纯透明悬浮覆盖层，不影响游戏操作
- **零外部依赖可执行文件**：一切功能内置，即装即用

---

## 2. 技术架构

### 2.1 技术栈

| 层次 | 技术 | 说明 |
|------|------|------|
| 运行时 | .NET 8.0 (net8.0-windows10.0.19041.0) | Windows 10 19041+ ，利用 WinRT API |
| UI 框架 | WPF (Windows Presentation Foundation) | XAML + Code-behind 经典模式 |
| WinForms 互操作 | System.Windows.Forms | 托盘图标 (NotifyIcon) |
| 屏幕捕获 | SharpDX 4.2.0 (DirectX 11 + DXGI) | GPU 加速桌面帧捕获 |
| OCR 引擎 | Windows.Media.Ocr (WinRT) | 系统内置多语言 OCR |
| HTTP 客户端 | System.Net.Http (标准库) | 调用外部 API |
| 配置存储 | System.Text.Json (标准库) | JSON 持久化到 AppData |

### 2.2 外部 NuGet 依赖

```
SharpDX                 4.2.0    // DXGI 桌面复制核心
SharpDX.DXGI           4.2.0    // DXGI 输出复制接口
SharpDX.Direct3D11     4.2.0    // D3D11 纹理操作
System.Drawing.Common   8.0.0    // GDI+ 位图处理
```

### 2.3 架构模式

```
┌─────────────────────────────────────────────────────────┐
│                    UI Layer (WPF)                        │
│  App.xaml(.cs)    MainWindow    OverlayWindow            │
│  应用入口+日志        配置面板       浮层渲染+交互             │
├─────────────────────────────────────────────────────────┤
│                  Services Layer                          │
│  CaptureService  OcrService  TranslationService          │
│  ConfigService   Logger (static)                        │
├─────────────────────────────────────────────────────────┤
│  Models          │  Native (P/Invoke)                    │
│  AppConfig       │  Win32.cs (Windows API 互操作)         │
└─────────────────────────────────────────────────────────┘
```

**设计特点：**
- 经典三层架构：UI → Service → Native/Model
- 服务间通过构造函数注入依赖 (纯手动 DI，无 IOC 容器)
- `ConfigService` 作为配置中枢，被多个 Service 共用
- `Logger` 作为静态工具类，贯穿全链路日志
- Native 层通过 `P/Invoke` 调用 Win32 API（显示器枚举、全局热键、鼠标钩子、窗口扩展样式）

---

## 3. 文件组织结构

### 3.1 源代码目录总览

```
GameTranslator/
├── GameTranslator.csproj          # 项目文件，定义目标框架和 NuGet 依赖
├── TuBiao.ico                     # 应用程序图标
│
├── App.xaml                       # 应用入口 XAML (资源定义，当前为空)
├── App.xaml.cs                    # 应用启动逻辑：日志初始化、显示器扫描、创建主窗口
│
├── MainWindow.xaml                # 主窗口 UI：引擎配置 + 设置面板
├── MainWindow.xaml.cs             # 主窗口逻辑：配置读写、启动/停止、快捷键注册
│
├── OverlayWindow.xaml             # 悬浮覆盖层 UI：框选蒙层 + 翻译渲染 Canvas + 状态栏
├── OverlayWindow.xaml.cs          # 覆盖层逻辑：框选交互、翻译调度、覆盖渲染
│
├── Models/
│   └── AppConfig.cs               # 配置数据模型 (POCO)
│
├── Native/
│   └── Win32.cs                   # Win32 API P/Invoke 封装 + MonitorInfo 记录类型
│
└── Services/
    ├── CaptureService.cs          # DXGI 桌面捕获服务 (IDisposable)
    ├── ConfigService.cs           # JSON 配置持久化服务 (AppData)
    ├── Logger.cs                  # 静态文件日志工具
    ├── OcrService.cs              # OCR 识别服务 (Windows OCR + 腾讯云)
    └── TranslationService.cs      # 翻译服务 (OpenAI / DeepL / Google)
```

### 3.2 辅助文件 (根目录)

| 文件 | 类型 | 说明 |
|------|------|------|
| `.gitignore` | 配置 | Git 忽略规则 (bin/obj/logs/config) |
| `TuBiao.ico` | 资源 | 根目录图标副本 |
| `build_log.txt` / `build_output.txt` | 日志 | 构建输出日志 |
| `pub_*.txt` / `publish_*.txt` | 日志 | 发布流程日志 (lite=轻量化, self=自包含) |
| `run_log.txt` / `end_log.txt` | 日志 | 运行日志 |
| `dircheck.txt` / `size_*.txt` / `sdk*.txt` / `dotnet_ls.txt` / `which.txt` | 调试日志 | 开发和调试辅助命令输出 |
| `.claude/` | IDE 配置 | Claude AI 技能定义文件 (skill-creator 等) |

### 3.3 文件类型分布

| 类型 | 数量 | 占比 |
|------|------|------|
| `.cs` (C# 源码) | 8 | 29% |
| `.xaml` (WPF UI) | 3 | 11% |
| `.csproj` (项目文件) | 1 | 4% |
| `.ico` (图标) | 2 | 7% |
| `.txt` (日志/调试) | 14 | 50% |
| **总计** | **28** | **100%** |

> 核心代码文件 14 个（含 XAML + C#），根目录 14 个文本文件均为开发和发布过程的辅助日志。

### 3.4 命名规范

- **命名空间**: `GameTranslator` 统一根命名空间，子空间按文件夹分层 (`Models`, `Native`, `Services`)
- **类命名**: PascalCase (`MainWindow`, `CaptureService`, `OcrService`)
- **方法命名**: PascalCase (`CaptureFrame`, `RegisterHotkey`, `TranslateAndRender`)
- **私有字段**: `_camelCase` 下划线前缀 (`_config`, `_isRunning`, `_cts`)
- **XAML 控件**: 匈牙利前缀缩写 (`BtnToggle`, `TxtStatus`, `CmbApiType`, `LblApiKey`)
- **常量**: PascalCase (`MaxCacheSize`, `DebounceMs`)
- **记录类型**: `MonitorInfo` 使用 positional record 语法

---

## 4. 核心功能模块详解

### 4.1 屏幕捕获模块 (`CaptureService`)

**技术方案**: DXGI Desktop Duplication API，通过 SharpDX 封装实现 GPU 级桌面帧捕获。

```
流程: Factory1 → Adapter1 → Device(D3D11) → Output1 → DuplicateOutput
                                                    ↓
                                         TryAcquireNextFrame() 获取桌面纹理
                                                    ↓
                                         CopyResource → Staging Texture
                                                    ↓
                                         MapSubresource → GDI+ Bitmap
```

**关键设计:**
- 支持多显示器，通过 `_outputIndex` 选择目标显示器
- `AccessLost` 异常自动重连机制
- 使用 `IDisposable` 模式管理 GPU 资源生命周期
- 首帧验证日志 (防止黑屏捕获)

### 4.2 OCR 识别模块 (`OcrService`)

**双引擎架构:**

| 引擎 | 实现方式 | 特点 |
|------|----------|------|
| Windows OCR | `Windows.Media.Ocr.OcrEngine` (WinRT) | 系统内置，离线可用，支持多语言 |
| 腾讯云 OCR | HTTP API (TC3-HMAC-SHA256 签名) | 云端高精度，支持复杂排版 |

**图像预处理流水线:**
```
原始截图 → 锐化 (SharpenPixels: 2x center - neighbor mean)
        → 对比度拉伸 (ContrastStretch: 暗区更暗/亮区更亮)
        → PNG 编码 → WinRT BitmapDecoder → SoftwareBitmap → RecognizeAsync
```

**去重机制:**
- `HashSet<string> _recentTexts` (最大 500 条) 避免重复翻译同一段文字
- 过滤纯数字/标点文本
- 最小文字长度限制 (≥2 字符)

### 4.3 翻译服务模块 (`TranslationService`)

**三引擎策略:**

| 引擎 | API | 密钥 | 启用条件 |
|------|-----|------|----------|
| OpenAI 兼容 | `/v1/chat/completions` | 需 API Key | Model + Endpoint 可配置 |
| DeepL | `/v2/translate` | 需 API Key | 免费 50 万字符/月 |
| Google 翻译 | `translate.googleapis.com` | 无需 | 直接可用 |

**翻译策略:**
- OpenAI 模式使用 System Prompt 定制翻译风格 ("简洁自然游戏对话/UI")
- `temperature=0.3` 保持翻译一致性
- 内存缓存 (`Dictionary<string,string>`, 最大 200 条) 按 `引擎+文本+目标语言` 索引
- 端点自动补全 (`/v1/chat/completions` 后缀)

### 4.4 覆盖层渲染模块 (`OverlayWindow`)

**窗口属性 (通过 Win32 API 设置):**
- `WS_EX_TRANSPARENT` — 鼠标点击穿透
- `WS_EX_LAYERED` — 分层窗口支持透明
- `WS_EX_TOOLWINDOW` — 不显示在任务栏
- `WS_EX_TOPMOST` — 始终置顶
- `WS_EX_NOACTIVATE` — 不抢焦点

**颜色采样算法:**
```
1. 在 OCR 边界区域边缘采样背景色
2. 亮度差 >35 的像素归为前景色
3. 使用桶聚类 (R/32, G/32, B/32) 找主色调
4. 翻译覆盖层: 背景=原背景色, 文字=原文字色, 完美融入画面
```

**渲染流程:**
```
OCR结果 → 颜色采样 → 并行翻译(SemaphoreSlim=6并发)
        → DPI坐标转换 → Dispatcher.Invoke → Canvas 添加 Grid 覆盖层
```

### 4.5 主窗口交互模块 (`MainWindow`)

**功能清单:**

| 功能 | 实现 | 说明 |
|------|------|------|
| 引擎配置 | TabControl 面板 | 捕获模式/OCR引擎/翻译引擎三合一 |
| 快捷键绑定 | `RegisterHotKey` + `LowLevelMouseHook` | 支持键盘组合键 + 鼠标侧键 |
| 多显示器选择 | 动态可视化布局 | 等比缩放显示器矩形，点击切换 |
| 系统托盘 | `NotifyIcon` | 关闭时最小化到托盘 |
| 系统主题适配 | `WindowGlassColor` 读取系统强调色 | 按钮颜色跟随系统 |
| 配置持久化 | `%LocalAppData%/GameTranslator/config.json` | 自动保存/加载 |
| 版本标识 | v3.0 | 显示在底部状态栏 |

### 4.6 Win32 原生互操作 (`Win32.cs`)

**封装的 API 集合:**

| 类别 | API | 用途 |
|------|-----|------|
| 显示器 | `EnumDisplayMonitors`, `GetMonitorInfo`, `GetDpiForMonitor` | 多显示器检测和 DPI 感知 |
| 窗口管理 | `GetWindowLong/SetWindowLong`, `SetWindowPos` | 覆盖层透明/穿透/置顶 |
| 前台窗口 | `GetForegroundWindow`, `GetWindowRect`, `GetWindowText` | 全屏模式目标检测 |
| 热键 | `RegisterHotKey`, `UnregisterHotKey` | 全局键盘快捷键 |
| 鼠标钩子 | `SetWindowsHookEx(WH_MOUSE_LL)`, `CallNextHookEx` | 全局鼠标侧键捕获 |
| 数据结构 | `RECT`, `MONITORINFOEX`, `MSLLHOOKSTRUCT`, `POINT` | Win32 结构体映射 |

---

## 5. 数据流与处理流程

### 5.1 全屏翻译模式

```
用户按下快捷键
    ↓
MainWindow.WndProcHook → OverlayWindow.TriggerOnce()
    ↓
CaptureService.CaptureFrame()           ← DXGI 全屏截图
    ↓
GetForegroundWindow → GetWindowRect     ← 裁剪到前台窗口
    ↓
OcrService.RecognizeAsync(bitmap)       ← OCR 识别
    ↓ (图像增强 → Windows/腾讯云 OCR)
TranslationService.TranslateAsync()     ← 并行翻译 (6 并发)
    ↓ (内存去重缓存)
OverlayWindow.TranslateAndRender()      ← 颜色采样 + 覆盖渲染
    ↓
Canvas 添加 Grid (背景+翻译文字)         ← WPF Dispatcher UI 线程
```

### 5.2 框选翻译模式

```
用户按下快捷键 (键盘) 或 按下鼠标侧键
    ↓
OverlayWindow.EnterSelectionMode()
    ↓ (移除 WS_EX_TRANSPARENT 以捕获鼠标)
用户拖拽框选 → SelectionMask 绘制虚线矩形
    ↓
MouseUp → 获取框选坐标 (DPI 感知)
    ↓
CaptureService.CaptureFrame() → Clone(cropRect)  ← 裁剪到选区
    ↓
OcrService → TranslateAndRender (同上全屏流程)
    ↓
ExitSelectionMode() (恢复透明穿透)
```

---

## 6. 关键依赖关系图

```
App.xaml.cs
  ├── Logger (static, 全局)
  ├── Win32.EnumerateMonitors()
  └── MainWindow(configService) ──────────────────────────┐
       ├── ConfigService ───── Config 持久化 (AppData)      │
       ├── Win32 P/Invoke ──── 热键/鼠标钩子/显示器          │
       └── BtnToggle_Click                                  │
            ├── Start()                                     │
            │    ├── CaptureService ─── SharpDX/DXGI        │
            │    ├── OcrService                             │
            │    │    ├── Windows.Media.Ocr (WinRT)         │
            │    │    └── HttpClient → 腾讯云 API            │
            │    ├── TranslationService                     │
            │    │    └── HttpClient → OpenAI/DeepL/Google   │
            │    └── OverlayWindow(capture, ocr, translator) │
            │         ├── 框选交互 (鼠标事件)                  │
            │         ├── 颜色采样 (GDI+ 逐像素)              │
            │         └── Canvas 覆盖渲染 (WPF Grid)         │
            └── Stop()                                      │
                 └── 释放所有 GPU/网络资源                     │
```

---

## 7. 设计亮点与代码质量

### 优点

1. **UNIX 哲学实践** — 每个 Service 单文件，职责单一明确
2. **零 IOC 容器** — 手动构造函数注入，依赖关系一目了然
3. **完善的错误处理** — 每个外部调用都有 try-catch 包裹，`AccessLost` 自动重连
4. **全链路日志** — 从启动到 API 响应全程可追踪
5. **去重缓存** — OCR 和翻译两层去重，避免重复请求
6. **智能颜色采样** — 翻译覆盖层完美融入原始画面色彩
7. **图像预处理** — 锐化 + 对比度拉伸提升游戏字体 OCR 准确率

### 可改进点

1. 翻译服务可用 `IHttpClientFactory` 替代手动 `new HttpClient()`，避免 socket 耗尽
2. `OcrService` 和 `TranslationService` 的日志调用较频繁，可考虑结构化日志
3. `MainWindow.xaml.cs` 约 560 行，快捷键绑定逻辑可抽离为独立组件
4. 框选模式当前不支持多区域同时翻译

---

## 8. 构建与发布

### 构建要求

- .NET 8.0 SDK
- Windows 10 19041+ (x64)
- 目标框架: `net8.0-windows10.0.19041.0`

### 发布命令 (从日志推测)

```powershell
# 常规发布
dotnet publish -c Release -o publish

# 轻量化发布 (框架依赖)
dotnet publish -c Release -o publish_lite --self-contained false

# 自包含发布
dotnet publish -c Release -o publish_self --self-contained true
```

### 配置位置

```
%LocalAppData%\GameTranslator\config.json
```

---

## 9. 版本历史

| 版本 | 标识 | 主要变更 |
|------|------|----------|
| v2.0 | `TxtVersion` 初始值 | 基础翻译功能 |
| v3.0 | 当前版本 | 新增框选模式、鼠标侧键支持、腾讯云OCR、多显示器可视化选择、系统托盘 |

---

*文档生成日期: 2026-06-29*
