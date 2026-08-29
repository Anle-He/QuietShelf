param(
    [string]$Version = "",
    [string]$NumericVersion = "",
    [string]$DotnetPath = "dotnet",
    [string]$IsccPath = "",
    [string]$ExpectedIsccVersion = "6.7.3",
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "src\QuietShelf.App\QuietShelf.App.csproj"
$publishDirectory = Join-Path $repositoryRoot "artifacts\win-x64"
$installerDirectory = Join-Path $repositoryRoot "artifacts\installer"
$installerScript = Join-Path $repositoryRoot "installer\QuietShelf.iss"

function Get-ProjectProperty([string]$Name) {
    $value = & $DotnetPath msbuild $projectPath -nologo "-getProperty:$Name"
    if ($LASTEXITCODE -ne 0) {
        throw "Could not read project property '$Name'."
    }
    return ($value | Select-Object -Last 1).Trim()
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-ProjectProperty "Version"
}
if ([string]::IsNullOrWhiteSpace($NumericVersion)) {
    $NumericVersion = Get-ProjectProperty "FileVersion"
}
if ($Version -notmatch '^[0-9A-Za-z][0-9A-Za-z.+-]*$') {
    throw "Version '$Version' is not safe for an artifact file name."
}
if ($NumericVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "Numeric version '$NumericVersion' must contain four numeric components."
}

$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts"))
foreach ($outputDirectory in @($publishDirectory, $installerDirectory)) {
    $resolvedOutput = [System.IO.Path]::GetFullPath($outputDirectory)
    if ([System.IO.Path]::GetDirectoryName($resolvedOutput) -ne $artifactsRoot) {
        throw "Refusing to clear output outside the repository artifacts directory."
    }
    if (Test-Path -LiteralPath $resolvedOutput) {
        Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
    }
}

if ([string]::IsNullOrWhiteSpace($IsccPath)) {
    $isccCandidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )
    $IsccPath = $isccCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($IsccPath) -or -not (Test-Path -LiteralPath $IsccPath)) {
    throw "Inno Setup 6 compiler was not found."
}
# Some Inno Setup builds have a 0.0.0.0 PE version; query the compiler engine itself.
$versionProbe = @'
[Setup]
AppName=QuietShelfToolchainProbe
AppVersion=0
DefaultDirName={tmp}\QuietShelfToolchainProbe
CreateAppDir=no
Uninstallable=no
Output=no
'@
$probeOutput = $versionProbe | & $IsccPath "/O$installerDirectory" /O- - 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Could not query Inno Setup compiler version: $($probeOutput -join [Environment]::NewLine)"
}
$versionMatch = [Regex]::Match(($probeOutput -join "`n"), '(?m)^Compiler engine version: Inno Setup (\d+\.\d+\.\d+(?:\.\d+)?)\s*$')
if (-not $versionMatch.Success) {
    throw "Inno Setup compiler did not report its engine version."
}
$actualIsccVersion = $versionMatch.Groups[1].Value
if ($actualIsccVersion -notmatch "^$([Regex]::Escape($ExpectedIsccVersion))(?:\.|$)") {
    throw "Inno Setup compiler version '$actualIsccVersion' does not match required version '$ExpectedIsccVersion'."
}

$publishArguments = @(
    "publish", $projectPath,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "false",
    "-p:PublishSingleFile=false",
    "-p:Version=$Version",
    "-p:FileVersion=$NumericVersion",
    "-p:AssemblyVersion=$NumericVersion",
    "-o", $publishDirectory
)
if ($NoRestore) {
    $publishArguments += "--no-restore"
}

& $DotnetPath @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "QuietShelf publish failed with exit code $LASTEXITCODE."
}

$requiredPublishFiles = @(
    "QuietShelf.App.exe",
    "QuietShelf.App.dll",
    "QuietShelf.App.deps.json",
    "QuietShelf.App.runtimeconfig.json",
    "Microsoft.Data.Sqlite.dll",
    "Wpf.Ui.dll"
)
$missingPublishFiles = $requiredPublishFiles | Where-Object {
    -not (Test-Path -LiteralPath (Join-Path $publishDirectory $_) -PathType Leaf)
}
if ($missingPublishFiles.Count -gt 0) {
    throw "Publish output is missing required files: $($missingPublishFiles -join ', ')."
}
if (-not (Get-ChildItem -LiteralPath $publishDirectory -Recurse -File -Filter "e_sqlite3.dll" | Select-Object -First 1)) {
    throw "Publish output is missing the SQLite native runtime."
}

& $IsccPath "/DAppVersion=$Version" "/DAppNumericVersion=$NumericVersion" $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Installer compilation failed with exit code $LASTEXITCODE."
}

$installerPath = Join-Path $repositoryRoot "artifacts\installer\QuietShelf-Setup-$Version.exe"
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Installer output was not created."
}

Get-Item -LiteralPath $installerPath
