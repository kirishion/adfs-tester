@echo off
rem ===========================================================================
rem Publiziert ADFS_Testing nach GitHub in einem Schritt:
rem   1) EXE bauen   2) Selbsttest   3) Pruefung auf uncommittete Aenderungen
rem   4) Subtree extrahieren   5) Force-Push nach main   6) Release mit EXE
rem
rem Aufruf:  Publish-Adfs.bat <version>      Beispiel:  Publish-Adfs.bat v1.1.0
rem
rem Voraussetzungen: git, gh (angemeldet), .NET Framework 4.x.
rem ACHTUNG: Schritt 5 ueberschreibt die Remote-Historie von main (force-push).
rem ===========================================================================
setlocal
set "REPO=kirishion/adfs-tester"
set "PREFIX=ADFS_Testing"
set "TOOLDIR=%~dp0"

set "VERSION=%~1"
if "%VERSION%"=="" (
    echo [FEHLER] Bitte Versions-Tag angeben, z.B.:  Publish-Adfs.bat v1.1.0
    exit /b 1
)

rem In die Repo-Wurzel wechseln (eine Ebene ueber diesem Verzeichnis)
pushd "%~dp0.." || (echo [FEHLER] Repo-Wurzel nicht gefunden. & exit /b 1)

echo === 1/6 EXE bauen ===
call "%TOOLDIR%Build-AdfsTester.bat"
if errorlevel 1 ( echo [FEHLER] Build fehlgeschlagen. & goto :fail )

echo.
echo === 2/6 Selbsttest ===
call "%TOOLDIR%Tests\Build-SelfTest.bat"
if errorlevel 1 ( echo [FEHLER] Selbsttest rot - Abbruch. & goto :fail )

echo.
echo === 3/6 Pruefe auf uncommittete Aenderungen in %PREFIX% ===
git diff --quiet HEAD -- "%PREFIX%"
if errorlevel 1 ( echo [FEHLER] Es gibt uncommittete Aenderungen in %PREFIX%. Bitte zuerst committen. & goto :fail )

echo.
echo === 4/6 Subtree extrahieren ===
git branch -D adfs-export 1>nul 2>nul
git subtree split --prefix=%PREFIX% -b adfs-export
if errorlevel 1 ( echo [FEHLER] subtree split fehlgeschlagen. & goto :fail )

echo.
echo === 5/6 Force-Push nach main ===
git push -f "https://github.com/%REPO%.git" adfs-export:main
if errorlevel 1 ( echo [FEHLER] Push fehlgeschlagen. & goto :fail )
git branch -D adfs-export 1>nul 2>nul

echo.
echo === 6/6 Release %VERSION% mit EXE ===
gh release create %VERSION% --repo %REPO% --title "ADFS-Tester %VERSION:v=%" --notes "ADFS-Diagnosetool als einzelne EXE. AdfsTester.exe unten unter Assets herunterladen, auf Server/Client kopieren, starten. Voraussetzung .NET Framework 4.5+." "%TOOLDIR%AdfsTester.exe"
if errorlevel 1 ( echo [FEHLER] Release-Erstellung fehlgeschlagen. & goto :fail )

echo.
echo FERTIG: https://github.com/%REPO%/releases/tag/%VERSION%
popd
endlocal
exit /b 0

:fail
git branch -D adfs-export 1>nul 2>nul
popd
endlocal
exit /b 1
