$ErrorActionPreference = "Stop"

& (Join-Path $PSScriptRoot "install.ps1") -Launch

$dataDirectory = Join-Path $env:LOCALAPPDATA "PokeTokenBar"
Write-Host "PokeTokenBar started with canonical data: $dataDirectory"
