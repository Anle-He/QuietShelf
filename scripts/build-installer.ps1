param(
    [string]$Version = "",
    [string]$NumericVersion = "",
    [string]$DotnetPath = "dotnet",
    [string]$IsccPath = "",
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

$publishArguments = @(
    "publish", $projectPath,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
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

& $IsccPath "/DAppVersion=$Version" "/DAppNumericVersion=$NumericVersion" $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Installer compilation failed with exit code $LASTEXITCODE."
}

$installerPath = Join-Path $repositoryRoot "artifacts\installer\QuietShelf-Setup-$Version.exe"
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Installer output was not created."
}

Get-Item -LiteralPath $installerPath
