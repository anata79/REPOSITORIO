@echo off
chcp 65001 >nul
net session >nul 2>&1
if errorlevel 1 (
  echo Clique com o botao direito em instalar.bat e escolha "Executar como administrador".
  pause
  exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0instalar.ps1" %*
pause
