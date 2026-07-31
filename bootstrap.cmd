@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0bootstrap.ps1" %*
if errorlevel 1 (
  echo.
  echo Falha ao iniciar SuperTerminal.
  pause
  exit /b 1
)
endlocal
