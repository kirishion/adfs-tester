@echo off
rem ===========================================================================
rem Publiziert ADFS_Testing nach GitHub und haelt die Historie schlank:
rem   1) EXE bauen  2) Selbsttest  3) Pruefung auf uncommittete Aenderungen
rem   4) EINEN dokumentierten Commit oben auf den bestehenden main-Stand setzen
rem      (kein subtree-split-Aufblaehen der Historie)
rem   5) Tag + Release mit angehaengter EXE
rem
rem Aufruf:  Publish-Adfs.bat <version> [<message-datei>]
rem   <version>       z.B. v1.2.0
rem   <message-datei> optional: Text fuer Commit-Message UND Release-Notes.
rem                   Fehlt sie, wird "ADFS-Tester <version>" verwendet.
rem
rem Voraussetzungen: git, gh (angemeldet), .NET Framework 4.x.
rem ===========================================================================
setlocal
set "REPO=kirishion/adfs-tester"
set "PREFIX=ADFS_Testing"
set "TOOLDIR=%~dp0"
set "GHURL=https://github.com/%REPO%.git"

set "VERSION=%~1"
if "%VERSION%"=="" (
    echo [FEHLER] Bitte Versions-Tag angeben, z.B.:  Publish-Adfs.bat v1.2.0
    exit /b 1
)

rem Message-/Notes-Datei bestimmen (Arg 2 oder Default erzeugen)
set "MSGFILE=%~2"
if "%MSGFILE%"=="" (
    set "MSGFILE=%TEMP%\_adfs_msg.txt"
    > "%TEMP%\_adfs_msg.txt" echo ADFS-Tester %VERSION%
)
if not exist "%MSGFILE%" ( echo [FEHLER] Message-Datei nicht gefunden: %MSGFILE% & exit /b 1 )

pushd "%~dp0.." || (echo [FEHLER] Repo-Wurzel nicht gefunden. & exit /b 1)

echo === 1/5 EXE bauen ===
call "%TOOLDIR%Build-AdfsTester.bat"
if errorlevel 1 ( echo [FEHLER] Build fehlgeschlagen. & goto :fail )

echo.
echo === 2/5 Selbsttest ===
call "%TOOLDIR%Tests\Build-SelfTest.bat"
if errorlevel 1 ( echo [FEHLER] Selbsttest rot - Abbruch. & goto :fail )

echo.
echo === 3/5 Pruefe auf uncommittete Aenderungen in %PREFIX% ===
git diff --quiet HEAD -- "%PREFIX%"
if errorlevel 1 ( echo [FEHLER] Uncommittete Aenderungen in %PREFIX%. Bitte zuerst committen. & goto :fail )

echo.
echo === 4/5 Dokumentierten Commit auf main aufsetzen ===
rem aktuellen Remote-main-Stand als Parent ermitteln (leer = Repo noch leer)
git ls-remote "%GHURL%" refs/heads/main > "%TEMP%\_adfs_parent.txt" 2>nul
set "PARENT="
for /f "tokens=1" %%i in (%TEMP%\_adfs_parent.txt) do set "PARENT=%%i"

rem Inhalt von ADFS_Testing als Root-Tree extrahieren
git branch -D _pub 1>nul 2>nul
git subtree split --prefix=%PREFIX% -b _pub
if errorlevel 1 ( echo [FEHLER] subtree split fehlgeschlagen. & goto :fail )
git rev-parse "_pub^{tree}" > "%TEMP%\_adfs_tree.txt"
set /p TREE=<"%TEMP%\_adfs_tree.txt"

rem genau EINEN Commit erzeugen: neuer Tree, Parent = bisheriger main
if defined PARENT (
    git commit-tree %TREE% -p %PARENT% -F "%MSGFILE%" > "%TEMP%\_adfs_new.txt"
) else (
    git commit-tree %TREE% -F "%MSGFILE%" > "%TEMP%\_adfs_new.txt"
)
if errorlevel 1 ( echo [FEHLER] commit-tree fehlgeschlagen. & goto :fail )
set /p NEW=<"%TEMP%\_adfs_new.txt"

echo Neuer Commit: %NEW%  (Parent: %PARENT%)
git push "%GHURL%" %NEW%:refs/heads/main
if errorlevel 1 ( echo [FEHLER] Push fehlgeschlagen ^(Remote weiter als erwartet?^). & goto :fail )
git branch -D _pub 1>nul 2>nul

echo.
echo === 5/5 Tag %VERSION% + Release mit EXE ===
git tag -f %VERSION% %NEW%
git push -f "%GHURL%" %VERSION%
if errorlevel 1 ( echo [FEHLER] Tag-Push fehlgeschlagen. & goto :fail )
gh release create %VERSION% --repo %REPO% --title "ADFS-Tester %VERSION:v=%" -F "%MSGFILE%" "%TOOLDIR%AdfsTester.exe"
if errorlevel 1 ( echo [FEHLER] Release-Erstellung fehlgeschlagen ^(Version schon vorhanden?^). & goto :fail )

echo.
echo FERTIG: https://github.com/%REPO%/releases/tag/%VERSION%
del "%TEMP%\_adfs_parent.txt" "%TEMP%\_adfs_tree.txt" "%TEMP%\_adfs_new.txt" 1>nul 2>nul
popd
endlocal
exit /b 0

:fail
git branch -D _pub 1>nul 2>nul
popd
endlocal
exit /b 1
