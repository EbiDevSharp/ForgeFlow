@echo off
title ForgeFlow ERP Server Host
color 0B

echo ==============================================================================
echo           ForgeFlow ERP - Server Host
echo           Target URL : https://localhost:5001
echo           Target Port: 5001 (HTTPS)
echo ==============================================================================
echo.

:: Set working directory to repository root
cd /d "%~dp0"

:: Terminate previous instance to free listening port and binary locks
taskkill /F /IM ForgeFlow.Web.exe >nul 2>&1

:: Database connection string configuration
if "%ConnectionStrings__ForgeFlow%"=="" (
    set "ConnectionStrings__ForgeFlow=Server=localhost;Database=testforgflow;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;"
)

:: Set environment to Production
if "%ASPNETCORE_ENVIRONMENT%"=="" (
    set ASPNETCORE_ENVIRONMENT=Production
)

:: Configure DataProtection key storage path for production
if "%DataProtection__KeyRingPath%"=="" (
    set "DataProtection__KeyRingPath=%~dp0.local\production-keys"
)

:: Listen strictly on secure HTTPS
if "%ASPNETCORE_URLS%"=="" (
    set ASPNETCORE_URLS=https://0.0.0.0:5001
)

cd /d "%~dp0src\ForgeFlow.Web"

:: Launch background watcher that waits for HTTP 200 before opening browser
start "" /b powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0scripts\launch-browser-when-ready.ps1"

if exist "bin\Release\net10.0\ForgeFlow.Web.exe" (
    echo [OK] Launching ForgeFlow Production Server...
    echo Web Application: https://localhost:5001
    echo ==============================================================================
    "bin\Release\net10.0\ForgeFlow.Web.exe"
) else (
    echo Building and launching ForgeFlow Server in Release mode...
    dotnet run -c Release
)

pause
