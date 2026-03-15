@echo off
echo ========================================
echo ArtaloBot Installer Build Script
echo ========================================
echo.

:: Check if Inno Setup is installed
set ISCC_PATH="C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if not exist %ISCC_PATH% (
    echo ERROR: Inno Setup 6 not found!
    echo.
    echo Please install Inno Setup 6 from:
    echo https://jrsoftware.org/isdl.php
    echo.
    pause
    exit /b 1
)

:: First build the application
echo [1/3] Building application...
call build.bat

:: Create dist folder if not exists
if not exist "dist" mkdir "dist"

:: Create installer
echo.
echo [2/3] Creating installer with Inno Setup...
%ISCC_PATH% "installer\ArtaloBot.iss"

echo.
echo [3/3] Creating portable ZIP...
powershell -Command "Compress-Archive -Path 'publish\*' -DestinationPath 'dist\ArtaloBot-Portable.zip' -Force"

echo.
echo ========================================
echo Installer Build Complete!
echo ========================================
echo.
echo Outputs:
echo   - Installer: dist\ArtaloBot-Setup-1.0.0.exe
echo   - Portable:  dist\ArtaloBot-Portable.zip
echo.
pause
