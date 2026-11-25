param(
    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\BazaarTracker\BazaarTracker.csproj'

# regular build -> dist\ (requires .NET 10 installed),
# self-contained -> dist-portable\ (nothing needs to be installed)
$output = Join-Path $root $(if ($SelfContained) { 'dist-portable' } else { 'dist' })

if (Test-Path $output) {
    Remove-Item $output -Recurse -Force
}

$publishArgs = @(
    'publish', $project,
    '-c', 'Release',
    '-r', 'win-x64',
    '-p:PublishSingleFile=true',
    '-p:DebugType=None',
    '-o', $output
)

if ($SelfContained) {
    $publishArgs += '--self-contained'
} else {
    $publishArgs += '--no-self-contained'
}

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish exited with code $LASTEXITCODE"
}

Write-Host ""
Write-Host "Done: $output\BazaarTracker.exe" -ForegroundColor Green
