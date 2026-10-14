@echo off
rem ============================================================
rem  Falso compilador.
rem  O arduino-cli ANEXA flags proprias na receita (-MMD -MF ...),
rem  entao nao da para usar "copy" direto: ele quebra com argumento
rem  desconhecido. Este script cria o arquivo de saida vazio e
rem  ignora o resto, criando tambem o arquivo de dependencia
rem  quando o -MF for pedido.
rem ============================================================
setlocal

if "%~1"=="" exit /b 0
if /i "%~1"=="nul" goto varrer
copy /y nul "%~1" >nul 2>&1

:varrer
shift
if "%~1"=="" exit /b 0
if /i "%~1"=="-MF" goto depfile
goto varrer

:depfile
shift
if "%~1"=="" exit /b 0
copy /y nul "%~1" >nul 2>&1
goto varrer
