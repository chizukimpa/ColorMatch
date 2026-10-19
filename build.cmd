@echo off
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe

"%CSC%" /nologo /target:winexe /optimize^
 /out:ColorMatch.exe^
 /win32icon:src\app.ico^
 /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.dll^
 src\Api.cs src\Profiles.cs src\MonitorDb.cs src\Ui.cs src\Osd.cs src\App.cs
if errorlevel 1 (
  echo.
  echo COMPILHACAO FALHOU
  exit /b 1
)

echo OK: ColorMatch.exe
