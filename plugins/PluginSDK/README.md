# 🐾 猫爪音乐插件 SDK (CatClawMusic Plugin SDK)

猫爪音乐官方插件开发工具包，支持 **5 种主流编程语言** 开发插件。
**所有语言的插件最终都编译成统一的 `.ccp` / `.dll` 格式，通过 `Assembly.Load()` 加载，零 HTTP、零 Bridge。**

| 语言 | 编译验证 | 原理 |
|------|---------|------|
| C# | ✅ | 进程内直接执行 |
| Java | ✅ | IKVM.NET 转 CIL |
| Python | ✅ | IronPython 嵌入 |
| JavaScript | ✅ | Jint 引擎嵌入 |
| Go | ✅ | cgo c-shared → P/Invoke |

## 架构

```
开发者编写业务逻辑              模板 C# 适配器 + 编译工具链         最终产物
─────────────────────      ──────────────────────────────     ──────────
MyPlugin.java              javac → .class → ikvmc → .dll
                  ──→  ──→  JavaPluginAdapter.cs      ──→  .ccp ✅
                         dotnet build

my_plugin.py              IronPython 嵌入执行
                  ──→  ──→  PythonPluginAdapter.cs     ──→  .ccp ✅
                         dotnet build (EmbeddedResource)

my_plugin.js              Jint 引擎嵌入执行
                  ──→  ──→  JsPluginAdapter.cs          ──→  .ccp ✅
                         dotnet build (EmbeddedResource)

main.go                   go build -buildmode=c-shared → .so
                  ──→  ──→  GoPluginAdapter.cs + DllImport ──→ .ccp ✅
                         dotnet build (+ 原生 .so)

MyPlugin.cs               native C# 直接编译
                  ──→  ──→  PluginBase.cs               ──→  .ccp ✅
                         dotnet build
```

所有产物都是标准 .NET DLL → `.ccp`，通过 `Assembly.Load()` 统一加载。

## 支持的编程语言

| 语言 | 编译工具 | 运行原理 | 模板目录 |
|------|---------|---------|---------|
| **C#** | `dotnet` | 进程内直接执行 | `clients/csharp/` |
| **Java** | `javac` + `ikvmc` | IKVM.NET 转 CIL | `clients/java/template/` |
| **Python** | `dotnet` | IronPython 嵌入 | `clients/python/template/` |
| **JavaScript** | `dotnet` | Jint 引擎嵌入 | `clients/js/template/` |
| **Go** | `go` + `dotnet` | cgo c-shared → .so + P/Invoke | `clients/go/template/` |

## 插件接口

| 接口 | 用途 | 实现方式 |
|------|------|---------|
| `ILyricsProviderPlugin` | 歌词获取 | `getLyrics(title, artist)` |
| `ICoverProviderPlugin` | 封面获取 | `getCover(title, artist)` |
| `IProtocolProviderPlugin` | 网络协议 | `listFiles(path)` / `openRead(path)` |
| `IMenuContributorPlugin` | 右键菜单 | `getMenuItems(title, artist)` |
| `IAudioEnhancerPlugin` | 音频处理 | `processSamples(...)` |

## 快速开始

### C# 插件

```bash
cd clients/csharp
dotnet build -c Release → .dll → .ccp
```

### Java 插件

```bash
cd clients/java/template
# 1. 编写 MyPlugin.java
# 2. 编译:
javac MyPlugin.java
jar cf myplugin.jar *.class
ikvmc -target:library myplugin.jar -out:myplugin.dll
dotnet build -c Release  # 生成 .ccp
```

### Python 插件

```bash
cd clients/python/template
# 1. 编写 my_plugin.py
# 2. 编译:
dotnet build -c Release → .dll → .ccp
```

### JavaScript 插件

```bash
cd clients/js/template
# 1. 编写 my_plugin.js
# 2. 编译:
dotnet build -c Release → .dll → .ccp
```

### Go 插件

```bash
cd clients/go/template
# 1. 编写 main.go
# 2. 编译原生库:
go build -buildmode=c-shared -o libgoplugin.so
# (或使用 build.sh: ./build.sh android)
# 3. 编译 C# 适配器:
dotnet build -c Release → .dll → .ccp
```

## 目录结构

```
PluginSDK/
├── README.md
├── protocol/
│   └── plugin-protocol.json         # 接口协议 JSON Schema
└── clients/
    ├── csharp/                       # C# SDK 原生支持
    │   ├── CatClawMusic.PluginSDK.csproj
    │   ├── Models.cs                 # 数据模型
    │   └── PluginBase.cs             # 插件基类
    ├── java/
    │   └── template/                 # Java → .NET DLL 模板 (IKVM.NET)
    │       ├── JavaPlugin.csproj
    │       ├── MyPlugin.java         # 开发者编辑此文件
    │       └── JavaPluginAdapter.cs
    ├── python/
    │   └── template/                 # Python → .NET DLL 模板 (IronPython)
    │       ├── PythonPlugin.csproj
    │       ├── my_plugin.py          # 开发者编辑此文件
    │       └── PythonPluginAdapter.cs
    ├── js/
    │   └── template/                 # JavaScript → .NET DLL 模板 (Jint)
    │       ├── JsPlugin.csproj
    │       ├── my_plugin.js          # 开发者编辑此文件
    │       └── JsPluginAdapter.cs
    └── go/
        └── template/                 # Go → .NET DLL 模板 (cgo c-shared)
            ├── GoPlugin.csproj
            ├── main.go               # 开发者编辑此文件 (Go 源码)
            ├── GoPluginAdapter.cs    # C# P/Invoke 适配器
            └── build.sh              # Go 跨平台编译脚本
```

## 开源协议

MIT License
