param(
    [string]$Version,
    [switch]$SkipInstaller,
    [switch]$RequireInstaller,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($SkipInstaller -and $RequireInstaller) {
    throw "SkipInstaller and RequireInstaller cannot be used together."
}

$projectPath = Join-Path $PSScriptRoot "src\PokeTokenBar.Windows\PokeTokenBar.Windows.csproj"
$versionFile = Join-Path $PSScriptRoot "Directory.Build.props"
$installerScript = Join-Path $PSScriptRoot "installer\PokeTokenBar.iss"
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "artifacts"))

[xml]$versionDocument = Get-Content -LiteralPath $versionFile
$declaredVersion = [string]$versionDocument.Project.PropertyGroup.VersionPrefix
if ([string]::IsNullOrWhiteSpace($declaredVersion)) {
    throw "VersionPrefix is missing from $versionFile"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $declaredVersion
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Release version must use major.minor.patch format: $Version"
}

if ($Version -ne $declaredVersion) {
    throw "Requested version $Version does not match Directory.Build.props version $declaredVersion"
}

$publishDirectory = [IO.Path]::GetFullPath(
    (Join-Path $artifactsRoot "publish\$Version\win-x64"))
$releaseDirectory = [IO.Path]::GetFullPath(
    (Join-Path $artifactsRoot "release\$Version"))

foreach ($path in @($publishDirectory, $releaseDirectory)) {
    if (-not $path.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the Windows artifacts directory: $path"
    }

    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }

    New-Item -ItemType Directory -Path $path -Force | Out-Null
}

$publishArguments = @(
    "publish",
    $projectPath,
    "--configuration", "Release",
    "--runtime", "win-x64",
    "--self-contained", "true",
    "--output", $publishDirectory,
    "-p:Version=$Version",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false"
)
if ($NoRestore) {
    $publishArguments += "--no-restore"
}

& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$executablePath = Join-Path $publishDirectory "PokeTokenBar.Windows.exe"
if (-not (Test-Path -LiteralPath $executablePath)) {
    throw "Published executable was not found: $executablePath"
}

$forbiddenFiles = Get-ChildItem -LiteralPath $publishDirectory -Recurse -File |
    Where-Object { $_.Name -in @("companion-state.json", "settings.json") }
if ($forbiddenFiles) {
    throw "User state was found in the publish directory: $($forbiddenFiles.FullName -join ', ')"
}

$portableArchive = Join-Path $releaseDirectory "PokeTokenBar-$Version-win-x64.zip"
Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $portableArchive -CompressionLevel Optimal

function Find-InnoCompiler {
    if (-not [string]::IsNullOrWhiteSpace($env:ISCC_PATH)) {
        if (-not (Test-Path -LiteralPath $env:ISCC_PATH)) {
            throw "ISCC_PATH does not exist: $env:ISCC_PATH"
        }

        return [IO.Path]::GetFullPath($env:ISCC_PATH)
    }

    $command = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 7\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 7\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 7\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe")
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    return $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

$installerPath = $null
if (-not $SkipInstaller) {
    $innoCompiler = Find-InnoCompiler
    if ([string]::IsNullOrWhiteSpace($innoCompiler)) {
        if ($RequireInstaller) {
            throw "Inno Setup compiler was not found. Install Inno Setup 7 or set ISCC_PATH."
        }

        Write-Warning "Inno Setup compiler was not found; the portable archive was created without an installer."
    }
    else {
        $previousVersion = $env:PTB_RELEASE_VERSION
        $previousPublishDirectory = $env:PTB_PUBLISH_DIR
        try {
            $env:PTB_RELEASE_VERSION = $Version
            $env:PTB_PUBLISH_DIR = $publishDirectory
            $installerBaseName = "PokeTokenBar-$Version-win-x64-setup"
            & $innoCompiler `
                "--output-dir=$releaseDirectory" `
                "--output-filename=$installerBaseName" `
                $installerScript
            if ($LASTEXITCODE -ne 0) {
                throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
            }

            $installerPath = Join-Path $releaseDirectory "$installerBaseName.exe"
            if (-not (Test-Path -LiteralPath $installerPath)) {
                throw "Compiled installer was not found: $installerPath"
            }
        }
        finally {
            $env:PTB_RELEASE_VERSION = $previousVersion
            $env:PTB_PUBLISH_DIR = $previousPublishDirectory
        }
    }
}

$releaseFiles = Get-ChildItem -LiteralPath $releaseDirectory -File |
    Where-Object { $_.Extension -in @(".zip", ".exe") } |
    Sort-Object Name
$checksumLines = foreach ($file in $releaseFiles) {
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash *$($file.Name)"
}
$checksumPath = Join-Path $releaseDirectory "SHA256SUMS.txt"
Set-Content -LiteralPath $checksumPath -Value $checksumLines -Encoding utf8NoBOM

$manifest = [ordered]@{
    product = "PokeTokenBar"
    version = $Version
    runtime = "win-x64"
    selfContained = $true
    portableArchive = [IO.Path]::GetFileName($portableArchive)
    installer = if ($null -ne $installerPath) { [IO.Path]::GetFileName($installerPath) } else { $null }
}
$manifestPath = Join-Path $releaseDirectory "release-manifest.json"
$manifest | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM

Write-Host "Release artifacts: $releaseDirectory"
@($releaseFiles) + @(
    Get-Item -LiteralPath $checksumPath
    Get-Item -LiteralPath $manifestPath
) |
    Select-Object Name, Length |
    Format-Table -AutoSize
