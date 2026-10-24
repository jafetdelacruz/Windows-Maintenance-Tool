@echo off
chcp 65001 >nul
title WinMaint - Phase 5 recoverable network lab
C:\WinMaintPhase5\WinMaint.Cli.exe --no-ansi
set testExitCode=%ERRORLEVEL%
echo.
echo PHASE 5 NETWORK LAB FINISHED. Exit code: %testExitCode%
pause
exit /b %testExitCode%
