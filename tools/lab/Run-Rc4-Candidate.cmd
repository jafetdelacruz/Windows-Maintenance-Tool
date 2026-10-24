@echo off
setlocal
chcp 65001 >nul
if /I not "%USERNAME%"=="WDAGUtilityAccount" (
  echo BLOCKED: run only inside Windows Sandbox.
  pause
  exit /b 3
)
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "C:\WinMaintCandidate\VERIFY-CANDIDATE.ps1" -CandidateDirectory "C:\WinMaintCandidate" -OutputRoot "C:\WinMaintCandidateOutput"
set "RESULT=%ERRORLEVEL%"
echo.
echo Candidate verification exit code: %RESULT%
echo Reports exported automatically. Tell Codex: finished.
pause
exit /b %RESULT%
