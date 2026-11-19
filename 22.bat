@echo off
setlocal enabledelayedexpansion

echo ========================================================
echo           ForgeFlow Repository Packaging Script
echo ========================================================

set "REPO_ROOT=%~dp0"
set "OUTPUT_ZIP=%REPO_ROOT%forgeflow-package.zip"

echo Packaging repository from: %REPO_ROOT%
echo Destination: %OUTPUT_ZIP%

if exist "%OUTPUT_ZIP%" del "%OUTPUT_ZIP%"

:: Use PowerShell to package repository files, explicitly excluding .local runtime keys,
:: bin, obj, test results, while keeping Migrations/ intact so schema history remains auditable.
powershell -NoProfile -Command ^
    "$repo = '%REPO_ROOT%'.TrimEnd('\');" ^
    "$zip = '%OUTPUT_ZIP%';" ^
    "Write-Host 'Compressing repository while excluding .local runtime keys and temporary files...';" ^
    "$exclude = @('.git', '.local', 'bin', 'obj', 'TestResults', '.vs', '.tools');" ^
    "$files = Get-ChildItem -Path $repo -Recurse -File | Where-Object {" ^
    "    $path = $_.FullName.Substring($repo.Length + 1);" ^
    "    $segments = $path.Split([System.IO.Path]::DirectorySeparatorChar);" ^
    "    $shouldExclude = $false;" ^
    "    foreach ($seg in $segments) {" ^
    "        if ($exclude -contains $seg) { $shouldExclude = $true; break; }" ^
    "    }" ^
    "    -not $shouldExclude" ^
    "};" ^
    "Write-Host ('Packaging {0} files (keeping Migrations/ intact)...' -f $files.Count);" ^
    "Compress-Archive -Path $files.FullName -DestinationPath $zip -Force;" ^
    "Write-Host 'Package created successfully: ' $zip;"

if %ERRORLEVEL% EQU 0 (
    echo [OK] Packaging complete.
) else (
    echo [ERROR] Packaging failed.
)
