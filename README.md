# CastoriceLauncher

[CN](README_CN.md)

A WinUI 3-based project for a localized game launcher, featuring:
- The main launcher application
- Support for local patch directories
- Integrated toolchain

## Development Environment

- Windows 10/11
- .NET SDK 8+
- WinUI 3 dependencies (restored via NuGet)

## Running and Debugging

```powershell
dotnet build .\LauncherApp.csproj -c Debug
dotnet run --project .\LauncherApp.csproj
```

## Publishing and Packaging

Use the script to automatically publish a single-file executable, assemble the distribution directory, and compress the package:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish-SingleFileBundle.ps1 -Runtime win-x64 -Configuration Release
```

Default Output:
- `artifacts/bundle/CastoriceLauncher-win-x64-Release/`
- `artifacts/CastoriceLauncher-win-x64-Release.zip`

## Directory Structure

- `Views/`: Main interface and interaction logic
- `Strings/`: Multilingual resources
- `Assets/`: Static assets (icons, wallpapers, etc.)
- `scripts/`: Publishing and utility scripts

The following directories are primarily intended for runtime/distribution purposes:
- `Patch/`
- `Server/`
- `Tools/`
- `artifacts/`