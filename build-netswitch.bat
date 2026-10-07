@echo off
rem Builds NetSwitch.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo csc.exe not found. Install .NET Framework 4.8.
  exit /b 1
)

"%CSC%" /nologo /codepage:65001 /target:winexe /platform:anycpu /optimize+ ^
 /out:"%~dp0com.local.netswitch.sdPlugin\NetSwitch.exe" ^
 /r:System.Web.Extensions.dll /r:System.ServiceProcess.dll ^
 "%~dp0src\NetSwitch\*.cs"
if errorlevel 1 exit /b 1

"%~dp0com.local.netswitch.sdPlugin\NetSwitch.exe" --self-test
if errorlevel 1 (
  echo Self-test failed, see com.local.netswitch.sdPlugin\self-test.json
  exit /b 1
)
echo Built com.local.netswitch.sdPlugin\NetSwitch.exe
