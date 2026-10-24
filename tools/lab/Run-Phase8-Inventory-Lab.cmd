@echo off
title WinMaint - Phase 8 inventory safety lab
if not "%USERNAME%"=="WDAGUtilityAccount" (
  echo BLOCKED: this launcher is only for Windows Sandbox.
  pause
  exit /b 3
)
"C:\WinMaintPhase8\WinMaint.InventoryLab.exe"
echo.
echo LAB EXIT CODE: %ERRORLEVEL%
echo No production cleanup is enabled.
pause
