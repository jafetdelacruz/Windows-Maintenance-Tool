@echo off
chcp 65001 >nul
title WinMaint - Prueba segura de Fase 4
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Run-Phase4-Temp-Lab.ps1"
if errorlevel 1 (
  echo.
  echo The test ended with an error. Keep this window open and report the message shown.
  pause
)
