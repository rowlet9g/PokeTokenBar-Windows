param(
    [string]$InstallerPath = (Join-Path $PSScriptRoot "artifacts\release\0.2.0\PokeTokenBar-0.2.0-win-x64-setup.exe"),
    [switch]$LaunchAfter
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$InstallerPath = [IO.Path]::GetFullPath($InstallerPath)
if (-not (Test-Path -LiteralPath $InstallerPath -PathType Leaf)) {
    throw "Installer was not found: $InstallerPath"
}

if ([IO.Path]::GetExtension($InstallerPath) -ne ".exe") {
    throw "InstallerPath must point to an executable: $InstallerPath"
}

$installDirectory = [IO.Path]::GetFullPath(
    (Join-Path $env:LOCALAPPDATA "Programs\PokeTokenBar"))
$installedExecutable = Join-Path $installDirectory "PokeTokenBar.Windows.exe"
$uninstaller = Join-Path $installDirectory "unins000.exe"
$dataDirectory = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('UserProfile')) ".poketokenbar"))
$backupDirectory = Join-Path $PSScriptRoot "artifacts\installer-validation-backup"
$startMenuShortcut = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)) "PokeTokenBar\PokeTokenBar.lnk"
$installerArguments = @(
    "/VERYSILENT",
    "/SUPPRESSMSGBOXES",
    "/NORESTART",
    "/SP-",
    "/MERGETASKS=!desktopicon")

function Get-ProtectedDataHashes {
    $result = [ordered]@{}
    foreach ($name in @("companion-state.json", "settings.json")) {
        $path = Join-Path $dataDirectory $name
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $result[$name] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        }
    }

    return $result
}

function Assert-DataHashes([System.Collections.IDictionary]$Expected, [string]$Stage) {
    $actual = Get-ProtectedDataHashes
    foreach ($name in $Expected.Keys) {
        if (-not $actual.Contains($name) -or $actual[$name] -ne $Expected[$name]) {
            throw "Protected user data changed during ${Stage}: $name"
        }
    }
}

function Invoke-CheckedProcess([string]$FilePath, [string[]]$ArgumentList) {
    $process = Start-Process -FilePath $FilePath -ArgumentList $ArgumentList -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) {
        throw "$FilePath failed with exit code $($process.ExitCode)"
    }
}

Get-Process -Name "PokeTokenBar.Windows" -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Path -and (
            $_.Path.StartsWith($installDirectory, [StringComparison]::OrdinalIgnoreCase) -or
            $_.Path.StartsWith($PSScriptRoot, [StringComparison]::OrdinalIgnoreCase))
    } |
    Stop-Process -Force

$originalHashes = Get-ProtectedDataHashes
if (Test-Path -LiteralPath $backupDirectory) {
    Remove-Item -LiteralPath $backupDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
foreach ($name in $originalHashes.Keys) {
    Copy-Item -LiteralPath (Join-Path $dataDirectory $name) -Destination (Join-Path $backupDirectory $name)
}

$needsRecoveryInstall = $false
try {
    Invoke-CheckedProcess $InstallerPath $installerArguments
    $needsRecoveryInstall = $true

    if (-not (Test-Path -LiteralPath $installedExecutable -PathType Leaf)) {
        throw "Installed executable was not found: $installedExecutable"
    }
    if (-not (Test-Path -LiteralPath $uninstaller -PathType Leaf)) {
        throw "Uninstaller was not found: $uninstaller"
    }
    if (-not (Test-Path -LiteralPath $startMenuShortcut -PathType Leaf)) {
        throw "Start menu shortcut was not created: $startMenuShortcut"
    }
    Assert-DataHashes $originalHashes "clean install"

    Invoke-CheckedProcess $InstallerPath $installerArguments
    Assert-DataHashes $originalHashes "in-place update"

    Invoke-CheckedProcess $uninstaller $installerArguments
    if (Test-Path -LiteralPath $installedExecutable) {
        throw "Installed executable remained after uninstall: $installedExecutable"
    }
    Assert-DataHashes $originalHashes "uninstall"

    Invoke-CheckedProcess $InstallerPath $installerArguments
    $needsRecoveryInstall = $false
    Assert-DataHashes $originalHashes "reinstall"

    if ($LaunchAfter) {
        Start-Process -FilePath $installedExecutable
    }

    [PSCustomObject]@{
        Installer = $InstallerPath
        InstalledExecutable = $installedExecutable
        FileVersion = (Get-Item -LiteralPath $installedExecutable).VersionInfo.FileVersion
        ProtectedFilesVerified = $originalHashes.Count
        StartMenuShortcut = $startMenuShortcut
        DataDirectoryPreserved = Test-Path -LiteralPath $dataDirectory
    } | Format-List
}
finally {
    if ($needsRecoveryInstall -and -not (Test-Path -LiteralPath $installedExecutable)) {
        Invoke-CheckedProcess $InstallerPath $installerArguments
    }
}
