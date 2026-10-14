@echo off
rem Instala o plugin do CircuitSketch na Arduino IDE. Feche a IDE antes.
rem Detalhes no LEIA-ME.txt.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0instalar-plugin.ps1" %*
echo.
pause
