@echo off
setlocal
cd /d "%~dp0"
title Dynamic Island bauen

echo.
echo  === Dynamic Island wird gebaut ===
echo.

set "HASSDK="
for /f "delims=" %%i in ('dotnet --list-sdks 2^>nul') do set "HASSDK=1"
if not defined HASSDK goto :nosdk

rem Laufende Insel beenden, sonst kann die .exe nicht ersetzt werden
taskkill /im DynamicIsland.exe /f >nul 2>nul

dotnet publish "src\DynamicIsland.csproj" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o "App" --nologo
if errorlevel 1 goto :failed

echo.
echo  Fertig! Die App liegt im Ordner "App".
echo  Sie startet jetzt. Rechtsklick auf die Insel = Menue.
echo.
start "" "App\DynamicIsland.exe"
pause
exit /b 0

:nosdk
echo  Das .NET SDK fehlt noch - gratis von Microsoft.
echo.
echo  Am einfachsten: Eingabeaufforderung oeffnen und eingeben:
echo     winget install Microsoft.DotNet.SDK.8
echo.
echo  Oder die Download-Seite benutzen, die sich jetzt oeffnet.
echo  Danach build.bat nochmal doppelklicken.
echo.
start "" "https://dotnet.microsoft.com/download/dotnet/8.0"
pause
exit /b 1

:failed
echo.
echo  FEHLER beim Bauen.
echo  Mach einen Screenshot von diesem Fenster und schick ihn an Claude.
echo.
pause
exit /b 1
