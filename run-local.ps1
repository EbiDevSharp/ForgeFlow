# ForgeFlow Local Setup & Runner Script
$OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# Set working directory to repository root
Set-Location $PSScriptRoot

Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host "          ForgeFlow ERP - Local Setup and Runner" -ForegroundColor Cyan
Write-Host "          Target URL : https://localhost:5001" -ForegroundColor Yellow
Write-Host "          Target Port: 5001 (HTTPS)" -ForegroundColor Yellow
Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host ""

# Terminate previous instance to free listening port and binary locks
Get-Process -Name "ForgeFlow.Web" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# 1. Check .NET SDK
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "[ERROR] .NET SDK is not installed or not in PATH!" -ForegroundColor Red
    Write-Host "Please download and install .NET 10 SDK or higher from:" -ForegroundColor Yellow
    Write-Host "  https://dotnet.microsoft.com/download/dotnet/10.0"
    Write-Host "`nAfter installing, close this window and run the script again."
    exit 1
}

# 2. Connection String
if (-not $env:ConnectionStrings__ForgeFlow) {
    $env:ConnectionStrings__ForgeFlow = "Server=localhost;Database=testforgflow;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;"
}

Write-Host "[Step 1/3] Ensuring HTTPS development certificate trust..." -ForegroundColor Gray
dotnet dev-certs https --trust 2>$null | Out-Null

Write-Host "[Step 2/3] Preparing database, applying migrations, and bootstrapping..." -ForegroundColor Yellow
dotnet run --project tools\ForgeFlow.Setup\ForgeFlow.Setup.csproj -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "`n[ERROR] Database setup failed!" -ForegroundColor Red
    Write-Host "Troubleshooting tips:" -ForegroundColor Yellow
    Write-Host " 1. Is the SQL Server service running?"
    Write-Host " 2. If using SQLEXPRESS, set the connection string in PowerShell:"
    Write-Host '    $env:ConnectionStrings__ForgeFlow = "Server=localhost\SQLEXPRESS;Database=testforgflow;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;"'
    exit $LASTEXITCODE
}

Write-Host "`n[Step 3/3] Starting Web Application at:" -ForegroundColor Green
Write-Host "           URL : https://localhost:5001" -ForegroundColor Cyan
Write-Host "           Port: 5001 (HTTPS)" -ForegroundColor Cyan
Write-Host "`nBrowser will automatically open as soon as the server is ready."
Write-Host "Press Ctrl + C to stop the application.`n"

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "https://localhost:5001"

# Launch background watcher that waits for HTTP 200 before opening browser
Start-Process powershell -WindowStyle Hidden -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSScriptRoot\scripts\launch-browser-when-ready.ps1`""

dotnet run --project src\ForgeFlow.Web\ForgeFlow.Web.csproj -c Release
