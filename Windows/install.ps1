param(
    [switch]$Launch
)

$ErrorActionPreference = "Stop"

$projectPath = Join-Path $PSScriptRoot "src\PokeTokenBar.Windows\PokeTokenBar.Windows.csproj"
$publishDirectory = Join-Path $PSScriptRoot "artifacts\win-x64"
$installDirectory = Join-Path $env:LOCALAPPDATA "Programs\PokeTokenBar"
$dataDirectory = Join-Path $env:LOCALAPPDATA "PokeTokenBar"
$installedStatePath = Join-Path $dataDirectory "companion-state.json"
$developmentStatePath = Join-Path (Split-Path -Parent $PSScriptRoot) ".ptb-data\companion-state.json"
$executableName = "PokeTokenBar.Windows.exe"
$installedExecutable = Join-Path $installDirectory $executableName

Get-Process -Name "PokeTokenBar.Windows" -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Path -and (
            $_.Path.StartsWith($PSScriptRoot, [StringComparison]::OrdinalIgnoreCase) -or
            $_.Path.StartsWith($installDirectory, [StringComparison]::OrdinalIgnoreCase))
    } |
    Stop-Process -Force

dotnet publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDirectory `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false

New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
Copy-Item -Path (Join-Path $publishDirectory "*") -Destination $installDirectory -Force

if (-not (Test-Path -LiteralPath $installedExecutable)) {
    throw "Published executable was not installed: $installedExecutable"
}

if ((Test-Path -LiteralPath $developmentStatePath) -and
    -not (Test-Path -LiteralPath $installedStatePath)) {
    New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null
    Copy-Item -LiteralPath $developmentStatePath -Destination $installedStatePath
    Write-Host "Migrated development companion state: $installedStatePath"
}

function New-PokeTokenBarShortcut {
    param(
        [Parameter(Mandatory)]
        [string]$ShortcutPath
    )

    $shell = New-Object -ComObject WScript.Shell
    try {
        $shortcut = $shell.CreateShortcut($ShortcutPath)
        $shortcut.TargetPath = $installedExecutable
        $shortcut.WorkingDirectory = $installDirectory
        $shortcut.IconLocation = "$installedExecutable,0"
        $shortcut.Description = "PokeTokenBar"
        $shortcut.Save()
    }
    finally {
        if ($null -ne $shortcut) {
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
        }
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
    }
}

$desktopDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
$programsDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
New-PokeTokenBarShortcut -ShortcutPath (Join-Path $desktopDirectory "PokeTokenBar.lnk")
New-PokeTokenBarShortcut -ShortcutPath (Join-Path $programsDirectory "PokeTokenBar.lnk")

Write-Host "Installed PokeTokenBar: $installedExecutable"
Write-Host "Desktop shortcut: $(Join-Path $desktopDirectory 'PokeTokenBar.lnk')"

if ($Launch) {
    Start-Process -FilePath $installedExecutable
}
