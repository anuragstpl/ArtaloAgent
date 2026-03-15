@echo off
setlocal enabledelayedexpansion

echo ============================================
echo    ArtaloBot Build Script
echo ============================================
echo.

:: Set variables
set "PROJECT_DIR=%~dp0"
set "PUBLISH_DIR=%PROJECT_DIR%publish"
set "DIST_DIR=%PROJECT_DIR%dist"

:: Clean previous builds
echo [1/5] Cleaning previous builds...
if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"
mkdir "%PUBLISH_DIR%"

:: Restore packages
echo.
echo [2/5] Restoring NuGet packages...
dotnet restore "%PROJECT_DIR%ArtaloBot.sln"
if errorlevel 1 (
    echo ERROR: Failed to restore packages
    pause
    exit /b 1
)

:: Build solution
echo.
echo [3/5] Building solution in Release mode...
dotnet build "%PROJECT_DIR%ArtaloBot.sln" -c Release --no-restore
if errorlevel 1 (
    echo ERROR: Build failed
    pause
    exit /b 1
)

:: Publish self-contained application (NOT single-file for better compatibility)
echo.
echo [4/5] Publishing self-contained application...
dotnet publish "%PROJECT_DIR%src\ArtaloBot.App\ArtaloBot.App.csproj" ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=false ^
    -p:PublishReadyToRun=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -o "%PUBLISH_DIR%"
if errorlevel 1 (
    echo ERROR: Publish failed
    pause
    exit /b 1
)

:: Verify publish output
echo.
echo [5/5] Verifying publish output...
if not exist "%PUBLISH_DIR%\ArtaloBot.App.exe" (
    echo ERROR: ArtaloBot.App.exe not found in publish folder
    pause
    exit /b 1
)

:: Count files
set /a filecount=0
for %%f in ("%PUBLISH_DIR%\*.*") do set /a filecount+=1
echo Published files to %PUBLISH_DIR%

echo.
echo ============================================
echo    BUILD COMPLETED SUCCESSFULLY!
echo ============================================
echo.
echo Output: %PUBLISH_DIR%
echo.
echo Next steps:
echo   1. Run build-installer.bat to create the installer
echo   2. Or run ArtaloBot.App.exe directly from the publish folder
echo.
pause
