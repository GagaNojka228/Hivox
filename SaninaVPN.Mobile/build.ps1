# SaninaVPN Mobile Build Script
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "=== SaninaVPN Mobile Build ===" -ForegroundColor Cyan

# Check workload
$wl = dotnet workload list 2>&1 | Out-String
if ($wl -notmatch "maui-android" -and $wl -notmatch "android") {
    Write-Host "WARNING: maui-android workload not found!" -ForegroundColor Yellow
    Write-Host "Install with: dotnet workload install maui-android" -ForegroundColor Yellow
}

Write-Host "[1/3] Restoring..." -ForegroundColor Green
dotnet restore "$root\SaninaVPN.Mobile\SaninaVPN.Mobile.csproj"

Write-Host "[2/3] Building..." -ForegroundColor Green
dotnet build "$root\SaninaVPN.Mobile\SaninaVPN.Mobile.csproj" -f net10.0-android -c Debug

Write-Host "[3/3] Done!" -ForegroundColor Green
