param(
    [ValidateSet("win-x64", "win-x86", "win-arm64")]
    [string]$Runtime = "win-x64",

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$Framework = "net8.0-windows10.0.19041.0",

    [Alias("NoZip")]
    [switch]$SkipZip,

    [Alias("NoReadme")]
    [switch]$SkipReadme
)

$ErrorActionPreference = "Stop"

function Write-Step([string]$Message) {
    Write-Host "[pack] $Message" -ForegroundColor Cyan
}

function Copy-IfExists([string]$Source, [string]$Destination) {
    if (Test-Path -LiteralPath $Source) {
        $parent = Split-Path -Parent $Destination
        if (-not [string]::IsNullOrWhiteSpace($parent)) {
            New-Item -ItemType Directory -Path $parent -Force | Out-Null
        }
        Copy-Item -LiteralPath $Source -Destination $Destination -Force
        return $true
    }
    return $false
}

function Find-FirstExistingPath([string[]]$Candidates) {
    foreach ($candidate in $Candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }
    return $null
}

function New-PackageReadme([string]$ReadmePath, [string]$BundleName, [string]$Runtime, [string]$Configuration, [string]$Framework, [string]$Version) {
    $generatedAt = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    @(
        "Castorice Launcher Package"
        "=========================="
        ""
        "Package: $BundleName"
        "Version: $Version"
        "Runtime: $Runtime"
        "Config : $Configuration"
        "TFM    : $Framework"
        "Built  : $generatedAt"
        ""
        "Contents"
        "--------"
        "Launcher: .\Launcher"
        "Server  : .\Server"
        "Patch   : .\Patch"
        "Tools   : .\Tools"
        ""
        "How To Run"
        "----------"
        "1. Ensure game path has been configured in launcher."
        "2. Run: .\Launcher\LauncherApp.exe"
        "3. Click start button in launcher."
        ""
        "Notes"
        "-----"
        "- This package is self-contained for launcher runtime."
        "- Keep Launcher/Server/Patch/Tools together in the same package directory."
    ) | Set-Content -LiteralPath $ReadmePath -Encoding UTF8
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$projectFile = Join-Path $projectRoot "LauncherApp.csproj"
$serverDir = Join-Path $projectRoot "Server"
$patchDir = Join-Path $projectRoot "Patch"
$toolsDir = Join-Path $projectRoot "Tools"
$artifactsDir = Join-Path $projectRoot "artifacts"
$publishDir = Join-Path $artifactsDir "publish\$Runtime"
$bundleName = "CastoriceLauncher-$Runtime-$Configuration"
$bundleDir = Join-Path $artifactsDir "bundle\$bundleName"
$zipPath = Join-Path $artifactsDir "$bundleName.zip"

if (-not (Test-Path $projectFile)) {
    throw "Project file not found: $projectFile"
}

if (-not (Test-Path $serverDir)) {
    throw "Server directory not found: $serverDir"
}

if (-not (Test-Path $patchDir)) {
    throw "Patch directory not found: $patchDir"
}

if (-not (Test-Path $toolsDir)) {
    throw "Tools directory not found: $toolsDir"
}

Write-Step "Cleaning old artifacts"
if (Test-Path $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }
if (Test-Path $bundleDir) { Remove-Item -LiteralPath $bundleDir -Recurse -Force }
if ((-not $SkipZip) -and (Test-Path $zipPath)) { Remove-Item -LiteralPath $zipPath -Force }

Write-Step "Publishing single-file launcher ($Runtime, $Configuration)"
$publishArgs = @(
    "publish", $projectFile,
    "-c", $Configuration,
    "-r", $Runtime,
    "-f", $Framework,
    "-o", $publishDir,
    "-p:PublishSingleFile=true",
    "-p:SelfContained=true",
    "-p:WindowsAppSDKSelfContained=true",
    "-p:PublishTrimmed=false",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:IncludeAllContentForSelfExtract=true"
)

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Step "Building bundle layout"
New-Item -ItemType Directory -Path $bundleDir -Force | Out-Null

$bundleLauncherDir = Join-Path $bundleDir "Launcher"
$bundleServerDir = Join-Path $bundleDir "Server"
$bundlePatchDir = Join-Path $bundleDir "Patch"
$bundleToolsDir = Join-Path $bundleDir "Tools"

Copy-Item -LiteralPath $publishDir -Destination $bundleLauncherDir -Recurse -Force
Copy-Item -LiteralPath $serverDir -Destination $bundleServerDir -Recurse -Force
Copy-Item -LiteralPath $patchDir -Destination $bundlePatchDir -Recurse -Force
Copy-Item -LiteralPath $toolsDir -Destination $bundleToolsDir -Recurse -Force

Write-Step "Ensuring required WinUI resources are present"
$launcherPriPath = Join-Path $bundleLauncherDir "LauncherApp.pri"
if (-not (Test-Path -LiteralPath $launcherPriPath)) {
    $priCandidates = @(
        (Join-Path $projectRoot "bin\$Configuration\$Framework\$Runtime\LauncherApp.pri")
        (Join-Path $projectRoot "bin\$Configuration\$Framework\LauncherApp.pri")
        (Join-Path $publishDir "LauncherApp.pri")
    )
    $foundPri = Find-FirstExistingPath $priCandidates
    if ($null -eq $foundPri) {
        $foundPri = Get-ChildItem -Path (Join-Path $projectRoot "bin") -Recurse -Filter "LauncherApp.pri" -ErrorAction SilentlyContinue |
            Select-Object -First 1 -ExpandProperty FullName
    }

    if ($null -eq $foundPri) {
        Write-Warning "LauncherApp.pri was not found. WinUI resource loading may fail at runtime."
    } else {
        Copy-IfExists -Source $foundPri -Destination $launcherPriPath | Out-Null
    }
}

$assetFiles = @(
    "Assets\icon.ico",
    "Assets\DefaultBackground.webp"
)
foreach ($assetRelPath in $assetFiles) {
    $sourcePath = Join-Path $projectRoot $assetRelPath
    $destPath = Join-Path $bundleLauncherDir $assetRelPath
    Copy-IfExists -Source $sourcePath -Destination $destPath | Out-Null
}

if (-not $SkipReadme) {
    $projectXml = [xml](Get-Content -LiteralPath $projectFile)
    $version = $projectXml.Project.PropertyGroup.Version | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($version)) { $version = "unknown" }
    $readmePath = Join-Path $bundleDir "README-PACKAGE.txt"
    New-PackageReadme -ReadmePath $readmePath -BundleName $bundleName -Runtime $Runtime -Configuration $Configuration -Framework $Framework -Version $version
}

if (-not $SkipZip) {
    Write-Step "Creating zip package"
    Compress-Archive -Path (Join-Path $bundleDir "*") -DestinationPath $zipPath -Force
}

Write-Step "Done"
Write-Host ""
Write-Host "Bundle directory: $bundleDir" -ForegroundColor Green
if (-not $SkipZip) {
    Write-Host "Zip package:      $zipPath" -ForegroundColor Green
}
