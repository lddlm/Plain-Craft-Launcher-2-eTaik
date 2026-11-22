# PCL eTaik 3.3.2

基于 **Plain Craft Launcher 2** 的第三方修改版 Minecraft 启动器（Windows / WPF / VB.NET）。
当前版本 **3.3.2.0**（VersionCode 418）。

> 普通用户请直接到 **Releases → v3.3.2** 下载成品，不需要编译本仓库。

> **推荐下载 `PCL-eTaik-3.3.2.zip`**，解压到任意文件夹即可运行，包内文件名已正确：

| 包内文件 | 说明 |
|---|---|
| `Plain Craft Launcher 2 eTaik.exe` | 主程序（已签名，3.3.2.0） |
| `Plain Craft Launcher 2 eTaik.exe.config` | 运行时配置（程序集绑定重定向），**必须与 exe 同名同目录** |
| `联机包.zip` | 联机功能分体包，解压到 exe 同目录 |

若只需要更新联机组件，可单独下载 `PCL-eTaik-Online-Pack.zip`（内容即 `联机包.zip`）。
本仓库存放的是完整源码，供查阅与自行构建。

## 3.3.2 更新内容

- **新增**：工具箱录屏 / 截屏：区域框选录制与截屏，录制红框与右上角计时器不入画
- **新增**：四个可改快捷键：录制 `Alt+Q`、区域录制 `Alt+1`、截屏 `Alt+W`、区域截屏 `Alt+2`
- **新增**：皮肤库 / 衣橱、主题个性化、EasyTier 异地联机、内存优化
- **新增**：MC 百科独立检索页、下载位置可自定义、一级导航栏可切换
- **修复**：切页动画竞态、Blend 字节溢出、eTaik 更新日志永不显示
- **其它**：本版为 Bata 快照号 418，版本显示带「 Bata」后缀

> 本版是 Bata（测试）版本号 418，下一正式版 3.4.2 已移除「 Bata」后缀。

完整源码见本仓库，构建方法见 `BUILD.md`，
逐文件校验值见 `MANIFEST.txt`。许可与第三方声明见 `LICENCE.txt` 与 `README.md`。
