@echo off
REM ===== SaninaVPN Mobile Build Script =====
REM
REM This script builds the Android APK for SaninaVPN Mobile
REM
REM Prerequisites:
REM 1. .NET 10 SDK with maui-android workload:
REM    dotnet workload install maui-android
REM
REM 2. Or use Visual Studio 2022+ with "Mobile development with .NET"
REM
REM ===========================================

echo [1/3] Restoring packages...
dotnet restore "%~dp0SaninaVPN.Mobile\SaninaVPN.Mobile.csproj"
if %ERRORLEVEL% NEQ 0 goto error

echo [2/3] Building Debug APK...
dotnet build "%~dp0SaninaVPN.Mobile\SaninaVPN.Mobile.csproj" -f net10.0-android -c Debug
if %ERRORLEVEL% NEQ 0 goto error

echo [3/3] Build SUCCESS!
echo APK location: SaninaVPN.Mobile\bin\Debug\net10.0-android\com.saninavpn.mobile-Signed.apk
goto end

:error
echo Build FAILED!
exit /b 1

:end
