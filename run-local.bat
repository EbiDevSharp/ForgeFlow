@echo off
chcp 65001 >nul
title ForgeFlow - سامانه مدیریت یکپارچه کارخانه‌ای فورج‌فلو
color 0b

echo ==============================================================================
echo           ForgeFlow ERP - سامانه یکپارچه مدیریت کارخانه و تولید
echo                 راه اندازی و اجرای خودکار سامانه (لوکال)
echo ==============================================================================
echo.

:: 1. بررسی نصب بودن دات‌نت SDK
where dotnet >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    color 0c
    echo [خطا] دات نت (.NET SDK) روی سیستم شما نصب نیست یا در PATH تعریف نشده است!
    echo لطفاً دات‌نت 10 (یا بالاتر) را از نشانی زیر دریافت و نصب فرمایید:
    echo   https://dotnet.microsoft.com/download/dotnet/10.0
    echo.
    echo پس از نصب، این پنجره را ببندید و فایل را مجدداً اجرا کنید.
    echo.
    pause
    exit /b 1
)

:: 2. تنظیم رشته اتصال پیش‌فرض SQL Server
if "%ConnectionStrings__ForgeFlow%"=="" (
    set "ConnectionStrings__ForgeFlow=Server=localhost;Database=testforgflow;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;"
)

echo [مرحله ۱/۳] اطمینان از اعتماد به گواهی امنیتی لوکال (HTTPS Certificate)...
dotnet dev-certs https --trust >nul 2>nul

echo [مرحله ۲/۳] آماده‌سازی دیتابیس، اعمال Migrationها و راه‌اندازی اولیه...
dotnet run --project tools\ForgeFlow.Setup\ForgeFlow.Setup.csproj -c Release
if %ERRORLEVEL% NEQ 0 (
    color 0c
    echo.
    echo [خطا] عملیات آماده‌سازی پایگاه‌داده با خطا متوقف شد!
    echo نکات عیب‌یابی:
    echo  ۱. آیا سرویس SQL Server در سیستم شما Start است؟
    echo  ۲. اگر SQL Server با نام مشخص نصب شده (مثلاً SQLEXPRESS)، دستور زیر را در CMD بزنید:
    echo     set "ConnectionStrings__ForgeFlow=Server=localhost\SQLEXPRESS;Database=testforgflow;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;"
    echo.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [مرحله ۳/۳] در حال اجرای وب‌اپلیکیشن روی نشانی:
echo            https://localhost:5001
echo.
echo مرورگر تا لحظاتی دیگر به صورت خودکار باز خواهد شد...
echo (برای توقف برنامه، کلیدهای Ctrl + C را در این پنجره فشار دهید)
echo.

set "ASPNETCORE_ENVIRONMENT=Development"
set "ASPNETCORE_URLS=https://localhost:5001"

start "" "https://localhost:5001"

dotnet run --project src\ForgeFlow.Web\ForgeFlow.Web.csproj -c Release --no-build

if %ERRORLEVEL% NEQ 0 (
    dotnet run --project src\ForgeFlow.Web\ForgeFlow.Web.csproj -c Release
)

pause
