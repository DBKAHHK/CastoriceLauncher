# CastoriceLauncher

一个基于 WinUI 3 的本地化游戏启动器项目，包含：
- 启动器主程序
- 本地补丁目录支持
- 工具链集成

## 开发环境

- Windows 10/11
- .NET SDK 8+
- WinUI 3 依赖（通过 NuGet 还原）

## 运行与调试

```powershell
dotnet build .\LauncherApp.csproj -c Debug
dotnet run --project .\LauncherApp.csproj
```

## 发布打包

使用脚本自动发布单文件、组装目录并压缩：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish-SingleFileBundle.ps1 -Runtime win-x64 -Configuration Release
```

默认输出：
- `artifacts/bundle/CastoriceLauncher-win-x64-Release/`
- `artifacts/CastoriceLauncher-win-x64-Release.zip`

## 目录说明

- `Views/`：主界面与交互逻辑
- `Strings/`：多语言资源
- `Assets/`：图标与壁纸等静态资源
- `scripts/`：发布与辅助脚本

以下目录主要用于运行时/分发：
- `Patch/`
- `Server/`
- `Tools/`
- `artifacts/`
