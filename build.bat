@echo off
echo ========================================
echo ArtaloBot Build Script
echo ========================================
echo.

:: Set variables
set OUTPUT_DIR=publish
set PROJECT_PATH=src\ArtaloBot.App\ArtaloBot.App.csproj

:: Clean previous builds
echo [1/4] Cleaning previous builds...
if exist "%OUTPUT_DIR%" rmdir /s /q "%OUTPUT_DIR%"
mkdir "%OUTPUT_DIR%"

:: Restore packages
echo [2/4] Restoring NuGet packages...
dotnet restore

:: Build Release
echo [3/4] Building Release configuration...
dotnet build -c Release

:: Publish self-contained single file
echo [4/4] Publishing self-contained executable...
dotnet publish %PROJECT_PATH% ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:PublishReadyToRun=true ^
    -p:EnableCompressionInSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -o %OUTPUT_DIR%

echo.
echo ========================================
echo Build Complete!
echo ========================================
echo.
echo Output location: %OUTPUT_DIR%\ArtaloBot.App.exe
echo.
echo You can now:
echo   1. Run the executable directly from the publish folder
echo   2. Create an installer using: build-installer.bat
echo.
pause
