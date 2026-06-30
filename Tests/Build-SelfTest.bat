@echo off
rem Kompiliert und startet den deterministischen Offline-Selbsttest.
rem Exit-Code 0 = alle Tests gruen.

setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [FEHLER] csc.exe nicht gefunden. .NET Framework 4.x erforderlich.
    exit /b 1
)

set "T=%~dp0"
set "SRC=%~dp0.."
set "OUT=%~dp0SelfTest.exe"

"%CSC%" /nologo /target:exe /out:"%OUT%" ^
    /reference:System.dll ^
    /reference:System.Core.dll ^
    /reference:System.Xml.dll ^
    /reference:System.Security.dll ^
    /reference:System.Web.dll ^
    /reference:System.Web.Extensions.dll ^
    "%T%SelfTest.cs" ^
    "%SRC%\TestModel.cs" ^
    "%SRC%\AppConfig.cs" ^
    "%SRC%\ErrorLogger.cs" ^
    "%SRC%\HttpHelper.cs" ^
    "%SRC%\CryptoHelpers.cs" ^
    "%SRC%\SamlInspect.cs" ^
    "%SRC%\ReportBuilder.cs"

if errorlevel 1 (
    echo [FEHLER] Kompilierung fehlgeschlagen.
    exit /b 1
)

echo.
"%OUT%"
endlocal
exit /b %ERRORLEVEL%
