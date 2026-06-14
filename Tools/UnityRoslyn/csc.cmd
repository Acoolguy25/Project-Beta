@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0csc.ps1" --%% %*
exit /b %ERRORLEVEL%
