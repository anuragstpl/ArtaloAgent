@echo off
setlocal enabledelayedexpansion

echo ============================================
echo    ArtaloBot Installer Build Script
echo ============================================
echo.

:: Set variables
set "PROJECT_DIR=%~dp0"
set "PUBLISH_DIR=%PROJECT_DIR%publish"
set "DIST_DIR=%PROJECT_DIR%dist"

:: Check if Inno Setup is installed (try common paths)
set "ISCC_PATH="
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" (
    set "ISCC_PATH=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
)
if exist "C:\Program Files\Inno Setup 6\ISCC.exe" (
    set "ISCC_PATH=C:\Program Files\Inno Setup 6\ISCC.exe"
)

if "%ISCC_PATH%"=="" (
    echo WARNING: Inno Setup 6 not found!
    echo.
    echo Please install Inno Setup 6 from:
    echo https://jrsoftware.org/isdl.php
    echo.
    echo Continuing to build application and portable ZIP only...
    echo.
)

:: Build the application first
echo [1/4] Building application...
echo.
call "%PROJECT_DIR%build.bat"
if errorlevel 1 (
    echo ERROR: Build failed
    pause
    exit /b 1
)

:: Verify publish folder exists
if not exist "%PUBLISH_DIR%\ArtaloBot.App.exe" (
    echo ERROR: Published application not found!
    pause
    exit /b 1
)

:: Create dist folder
echo.
echo [2/4] Preparing distribution folder...
if not exist "%DIST_DIR%" mkdir "%DIST_DIR%"

:: Create portable ZIP
echo.
echo [3/4] Creating portable ZIP...
powershell -Command "Compress-Archive -Path '%PUBLISH_DIR%\*' -DestinationPath '%DIST_DIR%\ArtaloBot-Portable-v1.0.0.zip' -Force"
if errorlevel 1 (
    echo WARNING: Failed to create portable ZIP
) else (
    echo Created: %DIST_DIR%\ArtaloBot-Portable-v1.0.0.zip
)

:: Create installer if Inno Setup is available
echo.
if not "%ISCC_PATH%"=="" (
    echo [4/4] Creating installer with Inno Setup...
    "%ISCC_PATH%" "%PROJECT_DIR%installer\ArtaloBot.iss"
    if errorlevel 1 (
        echo WARNING: Installer creation failed
    ) else (
        echo Created: %DIST_DIR%\ArtaloBot-Setup-1.0.0.exe
    )
) else (
    echo [4/4] Skipping installer (Inno Setup not installed)
)

echo.
echo ============================================
echo    BUILD COMPLETED!
echo ============================================
echo.
echo Distribution files:
if exist "%DIST_DIR%\ArtaloBot-Setup-1.0.0.exe" echo   - Installer: %DIST_DIR%\ArtaloBot-Setup-1.0.0.exe
if exist "%DIST_DIR%\ArtaloBot-Portable-v1.0.0.zip" echo   - Portable:  %DIST_DIR%\ArtaloBot-Portable-v1.0.0.zip
echo.
echo The application is self-contained and includes all required .NET files.
echo.
pause
