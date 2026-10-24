@echo off
setlocal
chcp 65001 >nul
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0VERIFY-CANDIDATE.ps1"
set "RESULT=%ERRORLEVEL%"
echo.
echo Verification exit code: %RESULT%
pause
exit /b %RESULT%
