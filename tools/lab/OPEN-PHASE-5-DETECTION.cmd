@echo off
chcp 65001 >nul
title WinMaint - Phase 5 read-only detection
C:\WinMaintPhase5\WinMaint.Cli.exe --pc-manager-detect --no-ansi
set testExitCode=%ERRORLEVEL%
echo.
echo READ-ONLY DETECTION FINISHED. Exit code: %testExitCode%
pause
exit /b %testExitCode%
