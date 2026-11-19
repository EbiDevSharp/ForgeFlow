@echo off
title ForgeFlow ERP - User and Password Management
color 0b

cd /d "%~dp0"

:: 1. Check .NET SDK
where dotnet >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    color 0c
    echo [ERROR] .NET SDK is not installed or not in PATH!
    pause
    exit /b 1
)

:: 2. Set default connection string if not present
if "%ConnectionStrings__ForgeFlow%"=="" (
    set "ConnectionStrings__ForgeFlow=Server=localhost;Database=testforgflow;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;"
)

:MENU
cls
echo ==============================================================================
echo           ForgeFlow ERP - User and Password Management Tool
echo ==============================================================================
echo.
echo  Target Database: testforgflow (localhost)
echo.
echo  [1] List existing system users and their roles
echo  [2] Reset password for admin user (ebrahim) via ForgeFlow.LocalTestRecovery
echo  [3] Reset password for any user via ForgeFlow.Bootstrap
echo  [4] Create a fresh local test account (ForgeFlow.LocalTestAccount)
echo  [5] Exit
echo.
set /p CHOICE="Please select an option (1-5): "

if "%CHOICE%"=="1" goto LIST_USERS
if "%CHOICE%"=="2" goto RESET_EBRAHIM
if "%CHOICE%"=="3" goto RESET_ANY
if "%CHOICE%"=="4" goto CREATE_TEST
if "%CHOICE%"=="5" goto EXIT
goto MENU

:LIST_USERS
cls
echo ==============================================================================
echo  Existing System Users and Roles
echo ==============================================================================
echo.
powershell -NoProfile -Command "Invoke-Sqlcmd -ServerInstance 'localhost' -Database 'testforgflow' -Query 'SELECT u.Id, u.UserName, u.IsActive, ISNULL(rd.Name, ''(None)'') AS RoleName FROM [auth].[User] u LEFT JOIN [auth].[ScopedRoleAssignment] ra ON u.Id = ra.UserId LEFT JOIN [auth].[RoleDefinition] rd ON ra.RoleId = rd.Id ORDER BY u.Id' | Format-Table -AutoSize"
echo.
echo Press any key to return to menu...
pause >nul
goto MENU

:RESET_EBRAHIM
cls
echo ==============================================================================
echo  Reset Password for Admin (ebrahim)
echo ==============================================================================
echo.
echo This runs the built-in host recovery tool for account 'ebrahim'.
echo When prompted, type 'RECOVER' to attest and confirm.
echo A secure 28-character temporary password will be generated and displayed.
echo.
dotnet run --project tools\ForgeFlow.LocalTestRecovery\ForgeFlow.LocalTestRecovery.csproj -c Release --no-build --no-restore --environment DOTNET_ENVIRONMENT=Development -- --execute
echo.
echo Press any key to return to menu...
pause >nul
goto MENU

:RESET_ANY
cls
echo ==============================================================================
echo  Reset Password for Any User (ForgeFlow.Bootstrap)
echo ==============================================================================
echo.
echo Set recovery connection environment variable...
set "FORGEFLOW_RECOVERY_SQLSERVER=%ConnectionStrings__ForgeFlow%"
echo Starting ForgeFlow.Bootstrap in reset-password mode...
echo When prompted, type 'RECOVER' to confirm.
echo.
dotnet run --project tools\ForgeFlow.Bootstrap\ForgeFlow.Bootstrap.csproj -c Release --no-build --no-restore reset-password
echo.
echo Press any key to return to menu...
pause >nul
goto MENU

:CREATE_TEST
cls
echo ==============================================================================
echo  Create Local Test Account
echo ==============================================================================
echo.
echo This will create a fresh local test administrator account.
echo When prompted, type 'CREATE TEST ACCOUNT' to confirm.
echo.
dotnet run --project tools\ForgeFlow.LocalTestAccount\ForgeFlow.LocalTestAccount.csproj -c Release --no-build --no-restore --environment DOTNET_ENVIRONMENT=Development -- --execute
echo.
echo Press any key to return to menu...
pause >nul
goto MENU

:EXIT
echo.
echo Exiting...
exit /b 0
