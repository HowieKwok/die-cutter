# die-cutter（模切一体机视觉框架）

茜恩项目 · 模切一体机视觉软件源码（`Mef_VisionFrame`）及插件集合。

## 环境

| 项 | 说明 |
|---|---|
| IDE | Visual Studio 2022 及以上（需 .NET Framework 4.7.2 开发组件 + WPF 工作负载） |
| 主框架 | .NET Framework 4.7.2（多数项目，非 SDK 风格，`packages.config` 方式还原） |
| 其他 | `LightControl`、`ConvexPeakDemo` 为 net6.0(-windows)；`ViewROI` 为 4.5.2 |
| 平台 | 统一 `PlatformTarget=x64`（`Directory.Build.props` 设），Halcon xl / 相机 SDK / 光源 SDK 均为 AMD64 |
| 机器视觉 | HALCON（`halcon.dll` / `halcondotnet.dll`）、OpenCV（`OpenCvSharpExtern.dll`、`opencv_world451.dll`）、SkiaSharp |
| 相机 | Basler Pylon、DALSA Sapera LT、HIK MvCameraControl、大恒（IKap）、灰点 FlyCapture |

## 目录结构

```
Mef_VisionFrame.sln          解决方案入口（按 frame / infrastructure / plugin / ui 分解决方案文件夹）
build.bat                   构建入口（vswhere 定位 VS，输出 build_<配置>.log）
Directory.Build.props        全局构建约定：x64、al.exe 回退、载入门禁
build/                       分层与 XAML 门禁（Layer.props / Xaml*Guard.targets）
src/
  frame/                     Frame_Core（算法/流程/外设/标定）、FrameCommunity、Frame_PluginHost、Mef_VisionFrame（主程序）
  infrastructure/            ControlBoard、DevMgmt.Cameras、DevMgmt.Light、LogTool、DefectRuleClassifier
  plugin/                    各种检测/测量/匹配插件（27 个 Plugin.* 工程）
  ui/                        Frame_UI（令牌层）、Frame_UI.Wpf、Frame_UI.WinForms、LightControl、ViewROI
dll/                         第三方 DLL 汇总（Halcon / 相机 / UI / 其他）
packages/                    NuGet 包（packages.config 方式，已随仓库提供以便离线还原）
config/                      机型配置底本（不参与构建，装机/换机型时人工投放，见 docs/机型与配置.md）
docs/                        设计说明文档（.md）
```

工程内的相对引用遵循「工程目录 → 仓库根」三层：`..\..\..\dll\`、`..\..\..\packages\`；
跨层引用为 `..\..\frame\…`、`..\..\infrastructure\…`、`..\..\plugin\…`、`..\..\ui\…`。
新增工程时按此规律书写，`Directory.Build.props` 会自动生效，无需逐个配置。

## 构建

```bat
build.bat                  :: Debug|Any CPU，增量
build.bat debug            :: 同上
build.bat release          :: Release|Any CPU，增量
build.bat debug rebuild    :: Debug，先清理再构建
build.bat release rebuild  :: Release，先清理再构建
```

等价的手工命令：

```
msbuild Mef_VisionFrame.sln /p:Configuration=Release /p:Platform="Any CPU" /m
```

首次构建建议先还原 NuGet 包（VS 会自动完成，或 `nuget restore Mef_VisionFrame.sln`）。
`Debug|Any CPU` / `Release|Any CPU` 为当前验证通过的配置。

## 构建门禁

`Directory.Build.props` 导入 `build/` 下的四道门禁，违规直接编译失败：

- `Layer.props`：`LayerGuard` 校验引用方向（令牌层零 UI 框架依赖、核心不引渲染适配器、适配器互不引用）；`LayerPublishAudit` 校验插件输出目录不得出现宿主独占程序集副本。
- `XamlColorGuard` / `XamlStyleGuard` / `XamlFontGuard`：扫描仓库内 `*.xaml`，视图内联颜色、隐式样式、字面字体必须收敛到 `Frame_UI.Wpf/Theme`。
- 门禁的基线以「仓库根相对路径」登记（如 `src\plugin\Plugin.TemplateMatch\WPF架构\…`），**移动/重命名视图文件时需同步更新对应 target 的 Baseline 项**，否则会报 `XAMLSTYLE` / `XAMLCOLOR` 等硬错误。

## 关于 `bin` / `obj`

构建产物（`bin/`、`obj/`、`.vs/`）**未上传**，已在 `.gitignore` 中排除。插件输出直接落到宿主目录：
`src/frame/Mef_VisionFrame/bin/<Configuration>/Plugins/`，由宿主的 `DirectoryCatalog("Plugin.*.dll")` 加载。

> 注意：个别工程（如 `Plugin.CamerSDK`）仍有指向仓库外历史目录的绝对 `HintPath`，需按本机实际路径调整。

## 未纳入仓库的内容

原文件夹约 4.05 GB，其中 `bin/obj/.vs` 约 3.34 GB（含 `Mef_VisionFrame/bin/Release.7z` 约 349 MB、运行期标定/模板文件 `*.ncm` 约 607 MB）未上传；仓库内为源码 + 第三方依赖，约 700 MB。

## 已知问题

- 解决方案收录 35 个工程；`src/` 下共 42 个 `*.csproj`，未纳入的 7 个为 `Plugin.CamerSDK`、`Plugin.CropImage`、`Plugin.FindIntersectionAngle`、`Plugin.ModeMatch`、`LightControl`、`ConvexPeakDemo`、`ViewROI`，其中前四个本身已无法编译（接口变更后未跟进）。
- `Debug|x64` / `Release|x64` 未通过：`Frame_Core` 的 x64 配置组缺少 `AllowUnsafeBlocks`（`Debug|AnyCPU` 有），报 CS0227。