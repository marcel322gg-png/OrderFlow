@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
where powershell.exe >nul 2>nul
if errorlevel 1 (
  echo Windows PowerShell is unavailable. Enable the built-in Windows PowerShell feature and retry.
  exit /b 1
)
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%RUNLOCALLY.ps1" %*
set "RESULT=%ERRORLEVEL%"
if not "%RESULT%"=="0" (
  echo.
  echo OrderFlow startup or command failed with exit code %RESULT%.
  pause
)
exit /b %RESULT%
