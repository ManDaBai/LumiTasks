# LumiTasks

**LumiTasks** 是一款适用于 Windows 的桌面任务管理工具，采用悬浮“灵动岛”主栏与任务球来展示日常任务、提醒和计时。

> 公开工程准备版：**V4.2.5**。这是源代码工程；用户可直接下载的安装版程序应另行在 GitHub Releases 发布。

## 功能

- 桌面悬浮灵动岛与任务球，可展开或收起任务展示
- 创建任务、标记完成、任务分类与自定义分类管理
- 任务计时和提醒、系统托盘入口
- `Ctrl + Alt + L` 全局快捷键隐藏或恢复灵动岛
- 灵动岛主栏“小／中／大”三级尺寸设置
- 主题外观及任务球显示选项
- Windows 登录时自动启动选项
- 本地 JSON 数据保存及备份／导入功能

## 系统要求

- **Windows 10 / Windows 11，64 位**（尚未对其他平台提供支持）
- 从源码构建：**.NET 8 SDK**

## 从源码构建

1. 安装 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。
2. 下载或克隆本仓库。
3. 在 Windows 上双击 `Build-Windows-x64.cmd`，或使用 PowerShell 运行 `Build-Windows-x64.ps1`。
4. 成功后在 `publish` 目录查找生成的 `LumiTasks.exe`。

如果希望自己手动编译：

```powershell
dotnet restore .\src\LumiTasks.csproj -r win-x64
dotnet publish .\src\LumiTasks.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o .\publish
```

> 本工程的构建脚本只输出到源码所在目录的 `publish`，不会主动升级已安装的程序。

## 使用说明

- 点击灵动岛的 **+** 新建任务。
- 点击展开按钮显示或收起任务球。
- 使用 **Ctrl + Alt + L** 隐藏或恢复灵动岛。
- 在设置中调整顶部灵动岛的大小与外观。
- 程序将任务保存在当前 Windows 用户的 `%LOCALAPPDATA%\LumiTasksWPFPreview` 路径中，请妥善备份。

## 开源发布前需要完成

1. **选择许可证**：MIT 许可更宽松；GPL-3.0 要求发布衍生作品时遵循相应开源义务。正式公开前，在仓库根目录添加 `LICENSE`。本准备包不擅自替作者决定许可证。
2. **确认素材权利**：检查 `src/Assets` 中的图标与任务球图片是否允许公开再分发。
3. **提供可执行下载**：在 GitHub 的 **Releases** 页面上传 Windows 已测试的正式 EXE 或安装程序，勿把早期升级补丁当作首次安装程序。
4. **添加截图**：使用不包含私人任务内容的图片，存入 `docs/screenshots/`（可自行创建）。
5. **执行隐私检查**：公开之前再次检查代码中有无令牌、账号、真实姓名、用户路径或个人数据。

## 问题反馈

可通过 GitHub Issues 提交 bug 复现步骤、Windows 版本、程序版本和报错日志。**上传日志前请删除用户名、真实路径、任务内容等个人信息。**

## License

尚待仓库所有者选择并添加正式开源许可证；当前仅为发布准备工程，不代表已授予任何特定开源许可。

---

**English:** LumiTasks is a Windows desktop task manager built with C# / WPF and .NET 8. See the Chinese sections above for build instructions. Choose a LICENSE and verify bundled assets before publishing the repository publicly.
