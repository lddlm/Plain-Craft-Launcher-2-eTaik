# 构建 PCL eTaik 3.0

## 环境

- Windows 10 / 11
- Visual Studio 2022（Community 即可），勾选 **.NET 桌面开发** 与 **.NET Framework 4.8 目标包**
- MSBuild（VS2022 自带）

## 命令行构建

在本仓库根目录执行：

```bat
"%ProgramFiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" ^
  "Plain Craft Launcher 2\Plain Craft Launcher 2.vbproj" ^
  /t:Build /p:Configuration=Release /p:Platform=AnyCPU
```

输出：`Plain Craft Launcher 2\bin\Plain Craft Launcher 2.exe`（以及同名 `.exe.config`）。

## 说明

- `Resources\*.dll` 是工程 `HintPath` 直接引用的程序集，必须保留，不能用 NuGet 版本随意替换。
- 本仓库**不包含**构建机上使用的便携 .NET SDK / 参考程序集工具链；
  使用标准 VS2022 安装时不需要它。
- `Resources\EasyTier.zip` 是联机功能的分体包，不参与编译；运行时把 Release 里的
  `联机包.zip` 解压结果放到 exe 同目录即可（详见 Release 说明）。
- 源码中的新增 `.vb` 文件带 UTF-8 BOM，请勿以 ANSI 保存，否则中文注释会乱码。
- 交付前若需要改写 PE 版本资源或做 Authenticode 签名，请自行准备签名证书；
  本仓库不包含任何证书与私钥。
