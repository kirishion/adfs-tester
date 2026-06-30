@echo off
rem Kompiliert das ADFS-Test-Tool zu AdfsTester.exe
rem Benoetigt .NET Framework 4.5+ (auf jedem Windows 10/11 vorhanden)

setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [FEHLER] csc.exe nicht gefunden. .NET Framework 4.x ist erforderlich.
    exit /b 1
)

set "OUT=%~dp0AdfsTester.exe"
set "DIR=%~dp0"
set "ICO=%~dp0AdfsTester.ico"

set "ICON_OPT="
if exist "%ICO%" (
    set "ICON_OPT=/win32icon:"%ICO%""
    echo Icon       : %ICO%
) else (
    echo Icon       : ^(nicht gefunden - optional via MakeIcon.ps1 erzeugen^)
)

echo Kompiliere Sourcen aus %DIR%
echo   -^> %OUT%

"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu ^
    /out:"%OUT%" ^
    %ICON_OPT% ^
    /reference:System.dll ^
    /reference:System.Core.dll ^
    /reference:System.Drawing.dll ^
    /reference:System.Windows.Forms.dll ^
    /reference:System.Security.dll ^
    /reference:System.Xml.dll ^
    /reference:System.Web.dll ^
    /reference:System.Web.Extensions.dll ^
    "%DIR%Program.cs" ^
    "%DIR%TestModel.cs" ^
    "%DIR%AppConfig.cs" ^
    "%DIR%ErrorLogger.cs" ^
    "%DIR%HttpHelper.cs" ^
    "%DIR%CryptoHelpers.cs" ^
    "%DIR%CertificateInspector.cs" ^
    "%DIR%MetadataClient.cs" ^
    "%DIR%BrowserFlow.cs" ^
    "%DIR%SamlInspect.cs" ^
    "%DIR%WsFedTester.cs" ^
    "%DIR%WsTrustTester.cs" ^
    "%DIR%SamlTester.cs" ^
    "%DIR%OidcTester.cs" ^
    "%DIR%ReportBuilder.cs" ^
    "%DIR%ResultView.cs" ^
    "%DIR%MainForm.cs"

if errorlevel 1 (
    echo [FEHLER] Kompilierung fehlgeschlagen.
    exit /b 1
)

echo.
echo Fertig: %OUT%
endlocal
exit /b 0
