@echo off
title ForgeFlow ERP Server Host
color 0B
echo ===================================================
echo           ForgeFlow ERP Server is Starting...
echo ===================================================

:: بستن نسخه قبلی در پس‌زمینه جهت آزادسازی پورت و قفل فایل‌ها
taskkill /F /IM ForgeFlow.Web.exe >nul 2>&1

:: تنظیم رشته اتصال پایگاه داده (اعتبارسنجی گواهی در محیط پروداکشن)
if "%ConnectionStrings__ForgeFlow%"=="" (
    set "ConnectionStrings__ForgeFlow=Server=localhost;Database=testforgflow;Integrated Security=True;Encrypt=True;"
)

:: تنظیم محیط اجرایی پروداکشن
if "%ASPNETCORE_ENVIRONMENT%"=="" (
    set ASPNETCORE_ENVIRONMENT=Production
)

:: پیکربندی مسیر ذخیره کلیدهای DataProtection برای پروداکشن
if "%DataProtection__KeyRingPath%"=="" (
    set "DataProtection__KeyRingPath=%~dp0.local\production-keys"
)

:: گوش دادن صرفاً روی پروتکل امن HTTPS
if "%ASPNETCORE_URLS%"=="" (
    set ASPNETCORE_URLS=https://0.0.0.0:5001
)

cd /d "%~dp0src\ForgeFlow.Web"

if exist "bin\Release\net10.0\ForgeFlow.Web.exe" (
    echo [OK] Launching ForgeFlow Production Server...
    echo Available at:
    echo   - HTTPS: https://localhost:5001
    echo ===================================================
    "bin\Release\net10.0\ForgeFlow.Web.exe"
) else (
    echo Building and launching ForgeFlow Server in Release...
    dotnet run -c Release
)

pause
