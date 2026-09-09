$ErrorActionPreference = "Stop"

& (Join-Path $PSScriptRoot "install.ps1") -Launch

$dataDirectory = Join-Path ([Environment]::GetFolderPath('UserProfile')) ".poketokenbar"
Write-Host "PokeTokenBar data directory: $dataDirectory"
