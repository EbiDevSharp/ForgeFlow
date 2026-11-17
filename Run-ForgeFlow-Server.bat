@echo off
title ForgeFlow ERP Server Host
color 0B
echo ===================================================
echo           ForgeFlow ERP Server is Starting...
echo ===================================================

:: بستن نسخه قبلی در پس‌زمینه جهت آزادسازی پورت و قفل فایل‌ها
taskkill /F /IM ForgeFlow.Web.exe >nul 2>&1

:: تنظیم رشته اتصال پایگاه داده
set "ConnectionStrings__ForgeFlow=Server=localhost;Database=testforgflow;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;"

:: تنظیم محیط اجرایی
set ASPNETCORE_ENVIRONMENT=Development

:: گوش دادن روی تمامی کارت‌های شبکه سیستم روی پورت 5000 و 5001
set ASPNETCORE_URLS=http://0.0.0.0:5000;https://0.0.0.0:5001

cd /d "%~dp0src\ForgeFlow.Web"

if exist "bin\Release\net10.0\ForgeFlow.Web.exe" (
    echo [OK] Launching ForgeFlow Server...
    echo Available at:
    echo   - Local:   http://localhost:5000  or  https://localhost:5001
    echo   - Network: http://[Your-IP]:5000
    echo ===================================================
    "bin\Release\net10.0\ForgeFlow.Web.exe"
) else (
    echo Building and launching ForgeFlow Server...
    dotnet run -c Release
)

pause