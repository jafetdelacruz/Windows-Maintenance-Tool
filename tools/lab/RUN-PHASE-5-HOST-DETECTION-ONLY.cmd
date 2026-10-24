@echo off
chcp 65001 >nul
title WinMaint - Phase 5 host detection only
set "REPO_ROOT=%~dp0..\.."
"%REPO_ROOT%\artifacts\publish\win-x64-phase5-preview\WinMaint.Cli.exe" --pc-manager-detect --no-ansi
set testExitCode=%ERRORLEVEL%
echo.
echo HOST READ-ONLY DETECTION FINISHED. Exit code: %testExitCode%
pause
exit /b %testExitCode%
