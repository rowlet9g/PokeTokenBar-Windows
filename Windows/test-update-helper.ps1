$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactDirectory = Join-Path $PSScriptRoot 'artifacts/update-integration'
New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
Push-Location $repoRoot
try {
    foreach ($project in @('Target','Installer','Harness')) {
        dotnet build "Windows/tests/UpdateIntegration/$project/$project.csproj" --configuration Release --verbosity minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false
        if ($LASTEXITCODE -ne 0) { throw "Update test fixture build failed: $project" }
    }
    $harnessPath = Join-Path $repoRoot 'Windows/tests/UpdateIntegration/Harness/bin/Release/net10.0-windows/Harness.exe'
    $harness = Start-Process -FilePath $harnessPath -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $artifactDirectory 'stdout.txt') -RedirectStandardError (Join-Path $artifactDirectory 'stderr.txt')
    if (-not $harness.WaitForExit(60000)) {
        $harness.Kill()
        throw 'Update helper integration timed out.'
    }
    $harness.WaitForExit()
    if ($harness.ExitCode -ne 0) {
        Get-Content -LiteralPath (Join-Path $artifactDirectory 'stderr.txt')
        throw 'Update helper integration failed.'
    }
    Get-Content -LiteralPath (Join-Path $artifactDirectory 'result.txt')
} finally {
    # Only our fixture helper is eligible for cleanup, including an error-dialog process.
    $lastRunFile = Join-Path $artifactDirectory 'last-run.txt'
    if (Test-Path -LiteralPath $lastRunFile) {
        $lastRun = Get-Content -LiteralPath $lastRunFile -Raw
        Get-CimInstance Win32_Process | Where-Object {
            $_.Name -eq 'powershell.exe' -and $_.CommandLine -like "*$lastRun*apply-update.ps1*"
        } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    }
    Pop-Location
}
