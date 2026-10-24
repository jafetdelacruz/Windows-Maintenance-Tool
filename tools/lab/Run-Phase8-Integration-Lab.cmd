@echo off
if /I not "%USERNAME%"=="WDAGUtilityAccount" (
  echo BLOCKED: run only inside Windows Sandbox.
  pause
  exit /b 3
)
"C:\WinMaintPhase8Cleanup\WinMaint.CleanupLab.exe" --integration-batch
echo Exit code: %ERRORLEVEL%
echo Reports exported automatically. Tell Codex: finished.
pause
