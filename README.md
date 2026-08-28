# FRAME ONE · 格斗输入练习室

轻量级 Windows 原生**格斗摇杆 / 键盘输入训练器**。不使用 Electron、不内置浏览器内核，纯 WPF，启动快、稳。

> 面向 KOF / 街霸等格斗游戏，用来练**方向指令、连段、输入节奏与成功率**。

---

## 功能

- **全局键盘监听**：不抢占游戏按键，直接读取按键状态。
- **手柄 / 摇杆**：支持 XInput 手柄，以及普通 USB / DirectInput 摇杆（方向帽开关 + 模拟轴，前 4 个按键映射为 A / B / C / D）。
- **实时八方向摇杆动画**：杆头平滑滑动，当前方向数字高亮。
- **招式连续指令判定**：命中后整段标绿并统计耗时。
- **多段连段**：一套连招可拆成多段，每段独立的总窗口 / 单步间隔；诊断会标出「断在第几段第几步」。
- **指令直接输入**：在编辑器「指令」框直接打 numpad 记法（如 `236A`、`623C`），实时解析成步骤。
- **禁用输入**：可把某方向设为禁用（如升龙禁 `上(8)`），按到即标红提示。
- **训练分析**（诊断面板）：
  - **numpad 记法串**（如 `2 3 6 A`）实时显示。
  - **时序横条**：每步实际间隔 vs 目标，绿/黄/红。
  - **目标 vs 实际** 比对：标出「错在第几步 / 混入多余方向 / 缺尾」。
  - **成功率 + 断点统计**：会话成功率、最弱环节。
- **四键记法**：A=轻拳、B=轻脚、C=重拳、D=重脚。
- **三档显示**：完整模式 / 360px 紧凑悬浮 / mini 横向输入流。

## 环境要求

### 目标环境（运行发布包）
- **64 位 Windows**：Win7 SP1 / Win8 / Win10 / Win11。
- **无需安装任何 .NET**：发布包自带 .NET 5 运行库，双击即用。

### 开发环境（从源码构建 / 运行）
| 项 | 要求 |
| --- | --- |
| 操作系统 | Windows 10 / 11（64 位） |
| 工具链 | **.NET 5 SDK（5.0.4xx 以上）** —— 验证：`dotnet --list-sdks` |
| IDE（可选） | Visual Studio 2019/2022，安装「.NET 桌面开发」工作负载；或用命令行即可 |
| 目标框架 | `net5.0-windows`（WPF），无第三方 NuGet 依赖，可离线还原 |
| 运行时 | 开发机器装了 .NET 5 Desktop Runtime 即可（`dotnet run` 用）；打免安装包会用到本机安装的 5.0.17 运行库文件 |

> 说明：项目使用 `net5.0-windows`（WPF，`UseWPF`）。1）开发跑直接用 `dotnet run`；2）打「免安装」发布包时不依赖 NuGet 联网，而是把本机已装的 .NET 5.0.17 运行库拷进发布目录（见下文「重新打包」）。

## 运行

**开发调试**（需 .NET 5 SDK）：

```powershell
dotnet build FightstickLab.csproj -c Release
dotnet run --project FightstickLab.csproj -c Release
```

**免安装发布包（推荐）**：`publish\` 下的产物自带 .NET 5 运行库，双击即用。

| 产物 | 说明 |
| --- | --- |
| `publish\win-x64\` | 目录版（约 136MB）：整个文件夹复制到任意位置，双击 `FightstickLab.exe` |
| `publish\FightstickLab-Portable.exe` | 单文件版（约 60MB）：双击自动解压到 `%LOCALAPPDATA%\FightstickLabPortable\` 后启动，再次启动秒开 |

> 目录版入口 `FightstickLab.exe` 是启动器，真正的程序是 `FightstickLab.Core.exe`，运行库在 `shared\` 与 `host\` 下。

## 从源码构建 / 重新打包

本机需装 .NET 5 SDK（打包为免安装版时还需要 .NET 5 Desktop Runtime 的安装文件，通常 `C:\Program Files\dotnet\` 下已自带）。

```powershell
# 1) 构建
dotnet build FightstickLab.csproj -c Release

# 2) 打包【目录版】—— 把运行库拷到发布目录即可免安装运行
dotnet publish FightstickLab.csproj -c Release -o publish\win-x64 --no-restore
# 然后把 C:\Program Files\dotnet\shared\{Microsoft.NETCore.App,Microsoft.WindowsDesktop.App}\5.0.17
# 分别拷到 publish\win-x64\shared\ 下（保留 zh-Hans），并把
# C:\Program Files\dotnet\host\fxr\5.0.17\hostfxr.dll 拷到 publish\win-x64\host\fxr\5.0.17\
# 最后把 apphost 改名为 FightstickLab.Core.exe，用 tools\PortableLauncher\launcher.exe 充当 FightstickLab.exe。
```

**单文件便携版**：`tools\PortableLauncher\Program.cs` 是启动器源码（面向 .NET Framework 4.5+，所有 Windows 自带），用任意 C# 编译器生成 exe 后，把整个 `publish\win-x64` 目录打成 zip 追加到启动器尾部（尾随 `FSLZIP01` 魔数 + 8 字节小端长度）即得单文件版。`FSL_PORTABLE_DIR` 环境变量可自定义解压位置。

## 使用说明（训练）

- **默认键位**（可在「输入设置」里改）：

  | 操作 | 键位 |
  | --- | --- |
  | 上 / 下 / 左 / 右 | W / S / A / D |
  | A 轻拳 / C 重拳 | J / K |
  | B 轻脚 / D 重脚 | U / I |

- **记法**：方向用 numpad（`236` = →↓↘，`623` = →↓↗ 升龙），攻击键用 `A/B/C/D`。编辑器「指令」框可直接输入 `236A` 这种串。
- **连段**：编辑器里「＋ 添加一段」可把多个动作连成一套连招，每段独立窗口/间隔；练习时诊断会显示「断在第几段第几步」。
- **禁用输入**：编辑器「禁用输入」里把某方向设为禁用（如升龙禁 `上(8)`），按到该方向会在最近历史标红 + 诊断提示。
- **分析面板**：练招时，诊断区显示 时序横条（每一步 vs 目标间隔）、目标vs实际比对（`混入`多余方向会标出）、成功率与最弱环节。

## 数据存储

- 所有设置保存在 `%LOCALAPPDATA%\FightstickLab\settings.json`。
- 输入历史 JSON，键位按从旧到新排列，每条格式：

```json
{
  "时间": "2022-08-24T10:20:30.120+08:00",
  "间隔": 80,
  "输入键位": "↙"
}
```

## 技术栈

C# / WPF，目标框架 `net5.0-windows`；打包采用「应用本地框架」免安装部署，单文件版为自解压启动器。
