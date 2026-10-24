@echo off
setlocal
chcp 65001 >nul
"%~dp0..\..\artifacts\publish\win-x64-phase5-manual-store\WinMaint.Cli.exe" --pc-manager-store --no-ansi
pause
