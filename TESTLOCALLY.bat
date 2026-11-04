@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%RUNLOCALLY.ps1" -Action Test
set "RESULT=%ERRORLEVEL%"
if "%RESULT%"=="0" (echo OrderFlow test suite passed.) else (echo OrderFlow test suite failed with exit code %RESULT%.)
pause
exit /b %RESULT%
