@echo off
if /I not "%USERNAME%"=="WDAGUtilityAccount" (
  echo BLOCKED: run this launcher only inside Windows Sandbox.
  pause
  exit /b 3
)
"C:\WinMaintPhase8Cleanup\WinMaint.CleanupLab.exe"
echo Exit code: %ERRORLEVEL%
pause
