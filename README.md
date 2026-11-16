# die-cutter（模切一体机视觉框架）

茜恩项目 · 激光模切一体机视觉软件源码（`Mef_VisionFrame`）及插件集合。

## 环境

| 项 | 说明 |
|---|---|
| IDE | Visual Studio 2022（需 .NET Framework 4.7.2 开发组件 + WPF 工作负载） |
| 主框架 | .NET Framework 4.7.2（多数项目） |
| 其他 | `LightControl`、`ConvexPeakDemo` 为 net6.0(-windows)；`ViewROI` 为 4.5.2 |
| 机器视觉 | HALCON（`halcon.dll` / `halcondotnet.dll`）、OpenCV（`OpenCvSharpExtern.dll`、`opencv_world451.dll`）、SkiaSharp |
| 相机 | Basler Pylon、DALSA Sapera LT、HIK MvCameraControl、大恒（IKap）、灰点 FlyCapture |

解决方案入口：`Mef_VisionFrame.sln`（含全部 39 个项目）。

## 目录结构

```
Mef_VisionFrame/            主程序：主界面、业务、工位、调试与工艺设置
Frame_Core/                 核心框架：算法/流程/外设/标定、DLL、控制卡、图表
FrameCommunity/             通用组件
ControlBoard / DevMgmt.*    控制板、相机与光源设备管理
LogTool                     日志
DefectRuleClassifier        ONNX 缺陷规则分类器
Plugin.*/                   各种检测/测量/匹配插件（约 25 个）
DLL/                        第三方 DLL 汇总（Halcon / 相机 / UI / 其他）
packages/                   NuGet 包（packages.config 方式，已随仓库提供以便离线还原）
Docs/                       设计说明文档（.md / 预览图）
```

## 关于 `bin` / `obj`

构建产物（`bin/`、`obj/`、`.vs/`）**未上传**，已在 `.gitignore` 中排除，请在本地 `git clone` 后重新生成：

```
msbuild Mef_VisionFrame.sln /p:Configuration=Release /p:Platform="Any CPU" /m
```

首次构建建议先还原 NuGet 包（VS 会自动完成，或 `nuget restore Mef_VisionFrame.sln`）。

> 注意：部分 `HintPath` 指向 `bin\Release` 下的产物及仓库外的历史目录，需按本机实际路径调整后再编译。

## 未纳入仓库的内容

原文件夹约 4.05 GB，其中 `bin/obj/.vs` 约 3.34 GB（含 `Mef_VisionFrame/bin/Release.7z` 约 349 MB、运行期标定/模板文件 `*.ncm` 约 607 MB）未上传；仓库内为源码 + 第三方依赖，约 700 MB。

## 版本记录

见 [修改记录.txt](修改记录.txt)。
