@echo off
rem Builds and runs the standalone game. Pass --assets <repo-root> if the Assets folder is not auto-detected.
cd /d "%~dp0"
dotnet run -c Release --project src\RipNRage.Game -- %*
