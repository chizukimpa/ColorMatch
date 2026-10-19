@echo off
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe

REM 1. gera o app (se ainda nao existir)
if not exist ColorMatch.exe call build.cmd
if not exist ColorMatch.exe (echo FALHA: ColorMatch.exe nao gerado & exit /b 1)

REM 2. gera o instalador com o app embutido como recurso
"%CSC%" /nologo /target:winexe /optimize^
 /out:ColorMatchSetup.exe^
 /win32icon:src\app.ico^
 /resource:ColorMatch.exe,ColorMatch.exe^
 /resource:src\app.ico,app.ico^
 /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.dll^
 src\Setup.cs
if errorlevel 1 (
  echo.
  echo COMPILACAO DO INSTALADOR FALHOU
  exit /b 1
)
echo OK: ColorMatchSetup.exe
