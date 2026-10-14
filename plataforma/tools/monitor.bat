@echo off
rem ============================================================
rem  Lancador do monitor serial (pluggable monitor).
rem  A IDE fala com este processo por stdin/stdout, entao o .bat
rem  so repassa: nada de "start", nada de janela.
rem ============================================================
powershell -NoProfile -ExecutionPolicy Bypass -NonInteractive -File "%~dp0monitor.ps1"
