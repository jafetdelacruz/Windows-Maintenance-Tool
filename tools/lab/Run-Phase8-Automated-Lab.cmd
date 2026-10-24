@echo off
if /I not "%USERNAME%"=="WDAGUtilityAccount" (
  echo BLOCKED: run this launcher only inside Windows Sandbox.
  pause
  exit /b 3
)
"C:\WinMaintPhase8Cleanup\WinMaint.CleanupLab.exe" --synthetic-batch
echo Exit code: %ERRORLEVEL%
echo Reports were exported automatically. You can tell Codex: finished.
pause
