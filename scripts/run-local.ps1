param(
    [switch]$SkipBuild,
    [switch]$NoBrowser
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$dllPath  = Join-Path $repoRoot 'src\ForgeFlow.Web\bin\Release\net10.0\ForgeFlow.Web.dll'
$url      = 'https://localhost:5001'

Push-Location $repoRoot
try {
    # Stop only an existing ForgeFlow.Web process from this workspace listening on port 5001
    $listeners = Get-NetTCPConnection -LocalPort 5001 -State Listen -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique
    foreach ($procId in $listeners) {
        $proc = Get-CimInstance Win32_Process -Filter "ProcessId = $procId" -ErrorAction SilentlyContinue
        if ($proc -and $proc.CommandLine -like "*$repoRoot*ForgeFlow.Web*") {
            Write-Host "Stopping existing ForgeFlow.Web (PID $procId)..." -ForegroundColor Yellow
            Stop-Process -Id $procId -Force
        }
    }

    if (-not $SkipBuild -or -not (Test-Path $dllPath)) {
        Write-Host 'Building ForgeFlow.slnx (Release)...' -ForegroundColor Cyan
        dotnet build (Join-Path $repoRoot 'ForgeFlow.slnx') -c Release --no-restore -m:1
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    # Process-only environment variables (not persisted globally)
    $env:ASPNETCORE_ENVIRONMENT       = 'Development'
    $env:ASPNETCORE_URLS              = $url
    $env:ConnectionStrings__ForgeFlow = 'Server=localhost;Database=testforgflow;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'

    if (-not $NoBrowser) {
        Start-Job -ScriptBlock {
            param($target)
            Start-Sleep -Seconds 2
            Start-Process $target
        } -ArgumentList "$url/account/login" | Out-Null
    }

    Write-Host "Starting ForgeFlow at $url/account/login (Ctrl+C to stop)..." -ForegroundColor Green
    dotnet $dllPath
}
finally {
    Pop-Location
}
