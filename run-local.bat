@echo off
title ForgeFlow ERP - Local Runner
color 0b

:: Set working directory to repository root
cd /d "%~dp0"

echo ==============================================================================
echo           ForgeFlow ERP - Local Setup and Runner
echo           Target URL : https://localhost:5001
echo           Target Port: 5001 (HTTPS)
echo ==============================================================================
echo.

:: Terminate previous instance to free listening port and binary locks
taskkill /F /IM ForgeFlow.Web.exe >nul 2>&1

:: 1. Check .NET SDK installation
where dotnet >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    color 0c
    echo [ERROR] .NET SDK is not installed or not in PATH!
    echo Please download and install .NET 10 SDK or higher from:
    echo   https://dotnet.microsoft.com/download/dotnet/10.0
    echo.
    echo After installing, close this window and run the script again.
    echo.
    pause
    exit /b 1
)

:: 2. Set default SQL Server connection string
if "%ConnectionStrings__ForgeFlow%"=="" (
    set "ConnectionStrings__ForgeFlow=Server=localhost;Database=testforgflow;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;"
)

echo [Step 1/3] Ensuring HTTPS development certificate trust...
dotnet dev-certs https --trust >nul 2>nul

echo [Step 2/3] Preparing database, applying migrations, and bootstrapping...
dotnet run --project tools\ForgeFlow.Setup\ForgeFlow.Setup.csproj -c Release
if %ERRORLEVEL% NEQ 0 (
    color 0c
    echo.
    echo [ERROR] Database setup failed!
    echo Troubleshooting tips:
    echo  1. Is the SQL Server service running?
    echo  2. If using SQLEXPRESS, set the connection string in CMD:
    echo     set "ConnectionStrings__ForgeFlow=Server=localhost\SQLEXPRESS;Database=testforgflow;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;"
    echo.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [Step 3/3] Starting Web Application at:
echo           URL : https://localhost:5001
echo           Port: 5001 (HTTPS)
echo.
echo Browser will automatically open as soon as the server is ready.
echo Press Ctrl + C in this window to stop the application.
echo.

set "ASPNETCORE_ENVIRONMENT=Development"
set "ASPNETCORE_URLS=https://localhost:5001"

:: Launch independent background watcher that waits for HTTP 200 before opening browser
start "" powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0scripts\launch-browser-when-ready.ps1"

dotnet run --project src\ForgeFlow.Web\ForgeFlow.Web.csproj -c Release

echo.
echo ==============================================================================
echo ForgeFlow Web Server has stopped. Press any key to exit.
echo ==============================================================================
pause >nul
