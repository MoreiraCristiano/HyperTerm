@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0bootstrap.ps1" %*
if errorlevel 1 (
  echo.
  echo Falha ao iniciar HyperTerm.
  pause
  exit /b 1
)
endlocal
