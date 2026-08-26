param(
    [string]$Version = "0.1.0-alpha",
    [string]$NumericVersion = "0.1.0.0",
    [string]$DotnetPath = "dotnet",
    [string]$IsccPath = ""
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "src\QuietShelf.App\QuietShelf.App.csproj"
$publishDirectory = Join-Path $repositoryRoot "artifacts\win-x64"
$installerScript = Join-Path $repositoryRoot "installer\QuietShelf.iss"

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

& $DotnetPath publish $projectPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=$Version `
    -o $publishDirectory
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
