@echo off
rem ============================================================
rem  Lancador da porta virtual (pluggable discovery).
rem  A IDE fala com este processo por stdin/stdout, entao o .bat
rem  so repassa: nada de "start", nada de janela.
rem ============================================================
powershell -NoProfile -ExecutionPolicy Bypass -NonInteractive -File "%~dp0porta.ps1"
