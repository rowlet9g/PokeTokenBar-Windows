$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$dataDirectory = Join-Path $repositoryRoot ".ptb-data"
$statePath = Join-Path $dataDirectory "companion-state.json"
$legacyStatePath = Join-Path $env:LOCALAPPDATA "PokeTokenBar\companion-state.json"
$recoveryStatePath = Join-Path $PSScriptRoot "companion-state.recovery.json"
$executablePath = Join-Path $PSScriptRoot "src\PokeTokenBar.Windows\bin\Release\net10.0-windows\PokeTokenBar.Windows.exe"

New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null

if (-not (Test-Path -LiteralPath $statePath)) {
    $migrationSource = if (Test-Path -LiteralPath $recoveryStatePath) {
        $recoveryStatePath
    } elseif (Test-Path -LiteralPath $legacyStatePath) {
        $legacyStatePath
    } else {
        $null
    }

    if ($null -ne $migrationSource) {
        Copy-Item -LiteralPath $migrationSource -Destination $statePath
    }
}

if (-not (Test-Path -LiteralPath $executablePath)) {
    dotnet build (Join-Path $PSScriptRoot "PokeTokenBar.Windows.sln") --configuration Release
}

$env:PTB_DATA_DIR = $dataDirectory
Start-Process -FilePath $executablePath

Write-Host "PokeTokenBar started with development data: $dataDirectory"
