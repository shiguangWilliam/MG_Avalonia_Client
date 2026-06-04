# Avalonia 部署计划 — 环境检查与构建体系分析

日期：2026-05-07
状态：分析报告，待后续落地

---

## 0. 核心原则

- **不动现有 ClientGUI**，不与 Rampastring.XNAUI 产生任何耦合
- **单开一个 Avalonia 项目目录**，与现有项目并行存在
- Avalonia 只依赖 `ClientCore`（共享 INI 解析、翻译、配置等基础能力）
- 通过 `Directory.Build.props` 的 Engine 体系新增一个 `Avalonia` 引擎配置，不破坏现有四个引擎

---

## 1. dotnet 环境检查结果

| 项 | 值 |
|---|-----|
| dotnet CLI | **10.0.103** |
| 已安装 SDK | **8.0.417** 和 **10.0.103** |
| global.json | SDK 10.0.100 + rollForward=latestFeature |
| 当前项目 TFM | net8.0, net8.0-windows, net48 |

**结论：完全支持 Avalonia。** Avalonia 要求 net6.0+，项目已有的 net8.0 运行时 + SDK 10.0 可无缝构建。无需升级任何 SDK 或运行时。

---

## 2. 当前构建体系梳理

### 2.1 四个引擎配置（Directory.Build.props 第 21-23 行）

```
Configurations:
  UniversalGLDebug;WindowsDXDebug;WindowsGLDebug;WindowsXNADebug;
  UniversalGLRelease;WindowsDXRelease;WindowsGLRelease;WindowsXNARelease
```

引擎通过 `Configuration` 字符串识别（第 32 行）：
```xml
<Engine Condition="$(Configuration.Contains(WindowsDX))">WindowsDX</Engine>
<Engine Condition="$(Configuration.Contains(UniversalGL))">UniversalGL</Engine>
<Engine Condition="$(Configuration.Contains(WindowsGL))">WindowsGL</Engine>
<Engine Condition="$(Configuration.Contains(WindowsXNA))">WindowsXNA</Engine>
```

### 2.2 各引擎的 TFM 与平台

| 配置 | TFM | Platform | UseWindowsForms |
|------|-----|----------|-----------------|
| UniversalGL | net8.0 | AnyCPU | false |
| WindowsDX | net48; net8.0-windows | AnyCPU | true |
| WindowsGL | net48; net8.0-windows | AnyCPU | true |
| WindowsXNA | net48; net8.0-windows | x86 | true |

### 2.3 build.ps1 输出映射（第 58-68 行）

```powershell
$Script:EngineSubFolderMap = @{
    'UniversalGL' = 'UniversalGL'
    'WindowsDX'   = 'Windows'
    'WindowsGL'   = 'OpenGL'
    'WindowsXNA'  = 'XNA'
}
$Script:FrameworkBinariesFolderMap = @{
    'net48'          = 'Binaries'
    'net8.0'         = 'BinariesNET8'
    'net8.0-windows' = 'BinariesNET8'
}
```

最终输出结构：
```
Compiled/
├── Resources/
│   ├── Binaries/          ← net48 输出（Windows, OpenGL, XNA）
│   │   ├── Windows/
│   │   ├── OpenGL/
│   │   └── XNA/
│   └── BinariesNET8/      ← net8.0 输出（UniversalGL）
│       └── UniversalGL/
```

### 2.4 项目依赖关系（现有）

```
DXMainClient
  ├── ClientGUI ───── 依赖 Rampastring.XNAUI（MonoGame 控件体系）
  │   └── ClientCore ─ 纯逻辑，无 UI 依赖
  └── ClientUpdater
```

---

## 3. Avalonia 引擎需要修改的文件清单

### 3.1 必须新增的文件

| 文件 | 说明 |
|------|------|
| `AvaloniaUI/AvaloniaUI.csproj` | 新 Avalonia 项目，TargetFramework=net8.0 |
| `AvaloniaUI/` 目录下其他源文件 | App.axaml、Window、Views、IniUiLoader 等 |

### 3.2 必须修改的现有文件

#### (a) `Directory.Packages.props`

新增 Avalonia NuGet 包版本（Central Package Management 模式）：
```xml
<PackageVersion Include="Avalonia" Version="11.2.x" />
<PackageVersion Include="Avalonia.Desktop" Version="11.2.x" />
<PackageVersion Include="Avalonia.Themes.Fluent" Version="11.2.x" />
<!-- 可选 -->
<PackageVersion Include="Avalonia.Diagnostics" Version="11.2.x" />
<PackageVersion Include="Avalonia.ReactiveUI" Version="11.2.x" />
```

#### (b) `Directory.Build.props`

**第 21-23 行 — Configurations**：追加 `AvaloniaDebug;AvaloniaRelease`

**第 32 行下方 — Engine 识别**：追加
```xml
<Engine Condition="$(Configuration.Contains(Avalonia))">Avalonia</Engine>
```

**第 36-48 行 — TargetFrameworks 条件**：新增 Avalonia 的 TFM 配置
```xml
<PropertyGroup Condition="(条件需要调整为覆盖 Avalonia)">
  <!-- Avalonia 引擎 -->
  <TargetFrameworks Condition="$(Engine) == 'Avalonia'">net8.0</TargetFrameworks>
  <Platforms Condition="$(Engine) == 'Avalonia'">AnyCPU</Platforms>
  <Platform Condition="$(Engine) == 'Avalonia'">AnyCPU</Platform>
  <PlatformTarget Condition="$(Engine) == 'Avalonia'">AnyCPU</PlatformTarget>
  <UseWindowsForms Condition="$(Engine) == 'Avalonia'">false</UseWindowsForms>
</PropertyGroup>
```

**第 68-71 行 — DefineConstants**：新增
```xml
<DefineConstants Condition="$(Engine) == 'Avalonia'">$(DefineConstants);AVALONIA</DefineConstants>
```

**注意**：Avalonia 是跨平台 UI 框架，不需要 `ISWINDOWS` 宏。`net8.0` 即可（非 `net8.0-windows`），这样可以跑 Linux/macOS。

#### (c) `Scripts/build.ps1`

**第 58-63 行 — EngineSubFolderMap**：追加
```powershell
'Avalonia' = 'Avalonia'
```

**第 64-68 行 — FrameworkBinariesFolderMap**：确认 Avalonia 走 `BinariesNET8`（net8.0 已经在 map 中）

**第 121-132 行 — Invoke-BuildProject**：需要新增 Avalonia 的构建路径。目前默认分支会调用所有 4 个引擎，需加上第 5 个：
```powershell
else {
    Invoke-BuildProject -Engine 'UniversalGL' -Framework 'net8.0'
    if ($IsWindows) {
        @('WindowsDX', 'WindowsGL', 'WindowsXNA') | ForEach-Object {
            # ...
        }
    }
    # 新增：Avalonia 在所有平台都构建
    Invoke-BuildProject -Engine 'Avalonia' -Framework 'net8.0'
}
```

或者把 Avalonia 加入 Windows 引擎列表（如果暂时只发 Windows）。

#### (d) `Directory.Build.targets`

**第 93-105 行 — MoveClientExes**：如果 Avalonia 产出自包含的 exe（如 `clientavalonia.exe`），需在此加移动逻辑。但 Avalonia 通常以 `dotnet clientavalonia.dll` 运行，与 UniversalGL 类似，可能不需要特殊处理。

**第 107-123 行 — CopyResources**：已通过 `DEBUG` 条件覆盖所有引擎，无需改。

**第 78-91 行 — MoveCommonBinaries**：Avalonia 引入的新 dll（Avalonia.*.dll）只在 Avalonia 输出目录中，不会出现在其他引擎目录，因此不会进入 CommonAssemblies 列表。需要重新运行 `Get-CommonAssemblyList.ps1` 验证。

#### (e) `DXMainClient/DXMainClient.csproj`

- 不需要改。Avalonia 是独立项目，DXMainClient 继续用 XNAUI。
- 将来如果要切换到 Avalonia 作为主客户端，再考虑在此做条件编译的入口切换。

#### (f) `ClientGUI/ClientGUI.csproj`

- **不动。** 第 9 行的 `Rampastring.XNAUI` 引用保持原样。
- Avalonia 项目不引用 ClientGUI。

#### (g) `DXClient.slnx`

- 新增 `AvaloniaUI\AvaloniaUI.csproj` 到解决方案

#### (h) `CommonAssemblies.txt` / `CommonAssembliesNetFx.txt`

- Avalonia 专属 dll 不会出现在其他引擎目录，不会被误标为 common
- 但 Avalonia 引擎 build 后需要重新运行 `Get-CommonAssemblyList.ps1` 确认没有意外

---

## 4. Avalonia 项目的依赖边界

```
AvaloniaUI（新项目，独立）
  ├── ClientCore ── INI 解析、翻译、配置（复用）
  ├── Avalonia 包 ── UI 框架
  │   ├── Avalonia.Desktop
  │   └── Avalonia.Themes.Fluent
  ├── IniUiLoader ── INI → UiNodeTree（新建，见 ini-to-avalonia-xaml-design-draft.md）
  └── Views / Controls ── Avalonia 控件（新建）

不依赖：
  ❌ ClientGUI（含 Rampastring.XNAUI）
  ❌ Rampastring.XNAUI 整个项目
  ❌ MonoGame
  ❌ WinForms
  ❌ XNA
```

---

## 5. 新增的 Engine 配置速查

| 项目 | 值 |
|------|---|
| Engine 名称 | `Avalonia` |
| 配置名 | `AvaloniaDebug` / `AvaloniaRelease` |
| TargetFramework | `net8.0` |
| Platform | `AnyCPU` |
| UseWindowsForms | `false` |
| DefineConstants | `AVALONIA` |
| 输出目录 | `Compiled/Resources/BinariesNET8/Avalonia/` |
| 启动方式 | `dotnet AvaloniaUI.dll` |

---

## 6. 后续步骤

1. 在 `Directory.Packages.props` 中确定 Avalonia 的具体版本号
2. 修改 `Directory.Build.props` 加 `Avalonia` 引擎
3. 修改 `Scripts/build.ps1` 加 Avalonia 构建路径
4. 创建 `AvaloniaUI/AvaloniaUI.csproj`
5. 更新 `DXClient.slnx`
6. 跑一次 `build.ps1 -NoMove` 验证 Avalonia 引擎能成功编译（即使没有 UI 代码，空项目先跑通）
7. 跑 `Get-CommonAssemblyList.ps1` 验证 CommonAssemblies 不需要改动