# PCL eTaik 3.4.5

基于 **Plain Craft Launcher 2** 的第三方修改版 Minecraft 启动器（Windows / WPF / VB.NET）。
当前版本 **3.4.5.0**（VersionCode 426）。

> 普通用户请直接到 **Releases → v3.4.5** 下载成品，不需要编译本仓库。

> **推荐下载 `PCL-eTaik-3.4.5.zip`**，解压到任意文件夹即可运行，包内文件名已正确：

| 包内文件 | 说明 |
|---|---|
| `Plain Craft Launcher 2 eTaik.exe` | 主程序（已签名，3.4.5.0） |
| `Plain Craft Launcher 2 eTaik.exe.config` | 运行时配置（程序集绑定重定向），**必须与 exe 同名同目录** |
| `联机包.zip` | 联机功能分体包，解压到 exe 同目录 |

若只需要更新联机组件，可单独下载 `PCL-eTaik-Online-Pack.zip`（内容即 `联机包.zip`）。
本仓库存放的是完整源码，供查阅与自行构建。

## 3.4.5 更新内容

- **新增**：存档管理：按种子生成的 4:3 封面预览、种子 / 版本 / 模式信息，可改名、换封面、删除
- **新增**：存档可改名（只改 level.dat 里的世界显示名，不改文件夹）
- **新增**：关于页展示联合开发者，并可前往其主页
- **新增**：圆角与按钮尺寸卡片支持一键恢复默认；按钮高度 / 宽度可全局调节
- **改进**：存档详情改为展开在被点存档正下方；预览换成 4:3 种子预览图（不读区块数据）
- **改进**：大存档的实体统计改为抽样扫描，几 GB 的世界也不会卡住界面
- **其它**：存档管理侧栏图标改为软盘样式

> 3.4.5 是当前最新版本（VersionCode 426）。

## 历史版本

每个历史版本都有独立的 Tag 与 Release，源码与二进制都能单独下载：

| 版本 | VersionCode | 说明 |
|---|---|---|
| [v3.0](releases/tag/v3.0) | — | 首个公开版：一键补全前置 Mod、Mod 兼容性检查、恢复原版游戏与 Java 下载、工具箱录屏 / 截屏 |
| [v3.1](releases/tag/v3.1) | 415 | 区域录屏与区域截屏、四个可改快捷键、录制红框与右上角计时器（不入画）、修复成品闪绿 |
| [v3.3.2](releases/tag/v3.3.2) | 418 | Bata 版：MC 百科独立检索、下载位置自定义、一级导航栏可切换、切页动画竞态修复 |
| [v3.4.2](releases/tag/v3.4.2) | 420 | 正式版：去掉「 Bata」后缀，社区资源分类描边，资源搜索镜像优先与 5 秒超时 |
| [v3.4.5](releases/tag/v3.4.5) | 426 | 存档管理与存档改名、联合开发者展示、圆角与按钮尺寸可调、4:3 种子预览、实体统计抽样扫描 |

> 各 Tag 的 **Source code (zip)** 即该版本源码；Release 附件里的 `PCL-eTaik-<版本>-source.zip`
> 是同一份源码的独立打包（含 `MANIFEST.txt` 逐文件校验值），二者可任选。

## 仓库结构

| 目录 | 说明 |
|---|---|
| `Plain Craft Launcher 2\` | 主工程（VB.NET / WPF） |
| `PCLCS\` | C# 辅助库（版本解析、Java 封装、启动等） |
| `MeloongCore\` | 上游 WPF 控件库（`Meloong-Git/MeloongCore`），已直接收录，clone 后无需 submodule 拉取 |
| `docs\images\` | 界面预览图 |

## 构建

环境与命令见 [`BUILD.md`](BUILD.md)。要点：

- Visual Studio 2022，勾选 **.NET 桌面开发** 与 **.NET Framework 4.8** 目标包
- `Resources\*.dll` 是工程 `HintPath` 直接引用的程序集，必须保留，不能用 NuGet 版本替换
- 源码中的新增 `.vb` 文件带 UTF-8 BOM，请勿以 ANSI 保存，否则中文注释会乱码
- 逐文件校验值见 `MANIFEST.txt`，许可与第三方声明见 `LICENCE.txt`

完整源码见本仓库，构建方法见 `BUILD.md`，
逐文件校验值见 `MANIFEST.txt`。许可与第三方声明见 `LICENCE.txt`。
