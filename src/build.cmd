@echo off
rem Builds "..\app\XviewsPaperlessAiOcr.exe" (.NET 10, Avalonia + ASP.NET Core, self-contained single file).
rem Needs the .NET 10 SDK: https://dotnet.microsoft.com/download
rem To redraw the icon first: powershell -ExecutionPolicy Bypass -File ..\assets\make-icons.ps1
setlocal
cd /d "%~dp0"
dotnet publish XviewsPaperlessAiOcr.csproj -c Release -o bin\publish --nologo
if errorlevel 1 (echo Build fehlgeschlagen & exit /b 1)
if not exist "..\app" mkdir "..\app"
copy /y "bin\publish\XviewsPaperlessAiOcr.exe" "..\app" >nul
if errorlevel 1 (echo Kopieren fehlgeschlagen - laeuft das Tool noch? & exit /b 1)
echo Gebaut: %~dp0..\app\XviewsPaperlessAiOcr.exe
