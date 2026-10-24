@echo off
setlocal
chcp 65001 >nul
"%~dp0..\..\artifacts\publish\win-x64-phase6-readable\WinMaint.Cli.exe" --no-ansi --report-dir "%~dp0..\..\artifacts\reports\phase6-user"
pause
