# config —— 机型配置（仅供开发者）

这个目录**不参与构建**，不会复制进 `bin\Debug` / `bin\Release`。
它是“调好的配置”的版本库副本，用于给新机器或换机型时人工投放。

> 完整说明（配置三层归属、缺配置的症状对照、代码位置）见 `docs/机型与配置.md`。

## 为什么不在编译产物里

机型在装机时就定了，只有开发者有权决定配置从哪来。
把配置随程序一起发到现场，等于给了操作员一条改机型的路径，因此构建产物里不含本目录。

## 目录结构

```
config/
  AiModels/                      缺陷检测模型，正负极各一份，构建两份都投放
    正极/ model.onnx model_info.txt test.json
    负极/
  General/                       通用层：拷到 ZSystemConfigurationFile 根下
    SystemParam.json             出厂机型（CraftType）与运行模式（RunMode）
    UserParam.xml                账号表
    ControlCordParam.json
  ＜机型名＞/                     机型层：拷到 ZSystemConfigurationFile/＜机型名＞/
    AllCamParam.json             相机布局与名称
    AllLightControllerParam.json 光源映射
    ImageSaveParamer.json        按相机数生成的存图设置
    PointInspectionParamer.json  工位点检项
    PLCDevParamr.json            PLC 协议与地址表（两个机型目录里同名，内容不同）
```

目录名必须与 `MaterialCrafEnum.ToString()` 完全一致：`激光模切一体机`、`五金模切`。

## 换机型的操作步骤

1. 退出程序；
2. 改 `ZSystemConfigurationFile\SystemParam.json` 的 `CraftType`，`RecipeName` 改成该机型下真实存在的配方；
3. 把 `config\＜机型名＞\*` 拷到 `ZSystemConfigurationFile\＜机型名＞\`；
4. （首次装机）把 `config\General\*` 拷到 `ZSystemConfigurationFile\` 根下；
5. 启动程序。

## 绝对不要放在这里

- 相机标定（`CalibrationPlateParam*`、`CheckerboardParam*`、`PretargetingCalibParam*`、`Msa*`、`CameraParam*`）：
  只对某台机器的某台相机有效，复制到别的机器比留空更危险。标定留在各机器自己的输出目录里。
- `PurviewParam.xml`：每次启动都会被代码重写。
- `ZFormulaFile/`：配方属于生产数据，不做出厂配置。

## 缺配置时的表现

程序不再用空壳文件启动，缺失项会由 `Init()` 生成默认值并立即落盘。
若 `AllCamParam` 里没有相机名（或名字重复），流程加载会被拒绝并提示检查相机名——
这是刻意的：宁可停在启动阶段，也不要带着错的流程跑。