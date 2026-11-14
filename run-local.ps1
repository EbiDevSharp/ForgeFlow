# ForgeFlow Local Setup & Runner Script
$OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host "          ForgeFlow ERP - سامانه یکپارچه مدیریت کارخانه و تولید" -ForegroundColor Cyan
Write-Host "                راه اندازی و اجرای خودکار سامانه (لوکال)" -ForegroundColor Cyan
Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host ""

# 1. Check .NET SDK
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "[خطا] دات نت (.NET SDK) روی سیستم شما نصب نیست یا در PATH تعریف نشده است!" -ForegroundColor Red
    Write-Host "لطفاً دات‌نت 10 (یا بالاتر) را از نشانی زیر دریافت و نصب فرمایید:" -ForegroundColor Yellow
    Write-Host "  https://dotnet.microsoft.com/download/dotnet/10.0"
    Write-Host "`nپس از نصب، این پنجره را ببندید و اسکریپت را مجدداً اجرا فرمایید."
    exit 1
}

# 2. Connection String
if (-not $env:ConnectionStrings__ForgeFlow) {
    $env:ConnectionStrings__ForgeFlow = "Server=localhost;Database=testforgflow;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;"
}

Write-Host "[مرحله ۱/۳] اطمینان از اعتماد به گواهی امنیتی لوکال (HTTPS Certificate)..." -ForegroundColor Gray
dotnet dev-certs https --trust 2>$null | Out-Null

Write-Host "[مرحله ۲/۳] آماده‌سازی دیتابیس، اعمال Migrationها و راه‌اندازی اولیه..." -ForegroundColor Yellow
dotnet run --project tools\ForgeFlow.Setup\ForgeFlow.Setup.csproj -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "`n[خطا] عملیات آماده‌سازی پایگاه‌داده با خطا متوقف شد!" -ForegroundColor Red
    Write-Host "نکات عیب‌یابی:" -ForegroundColor Yellow
    Write-Host " ۱. آیا سرویس SQL Server در سیستم شما Start است؟"
    Write-Host " ۲. در صورت استفاده از SQLEXPRESS، متغیر زیر را در پاورشل تنظیم کنید:"
    Write-Host '    $env:ConnectionStrings__ForgeFlow = "Server=localhost\SQLEXPRESS;Database=testforgflow;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;"'
    exit $LASTEXITCODE
}

Write-Host "`n[مرحله ۳/۳] در حال اجرای وب‌اپلیکیشن روی نشانی:" -ForegroundColor Green
Write-Host "           https://localhost:5001" -ForegroundColor Cyan
Write-Host "`nمرورگر به صورت خودکار باز خواهد شد..."
Write-Host "(برای توقف برنامه، کلیدهای Ctrl + C را فشار دهید)`n"

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "https://localhost:5001"

Start-Process "https://localhost:5001"

dotnet run --project src\ForgeFlow.Web\ForgeFlow.Web.csproj -c Release
