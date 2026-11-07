@echo off
rem Compiles the vendored Clay into native\win-x64\clay.dll. Run from an "x64 Native Tools" developer prompt.
cd /d "%~dp0\.."
if not exist native\win-x64 mkdir native\win-x64
cl /nologo /O2 /LD /TC native\src\clay\clay.c /Fenative\win-x64\clay.dll /Fo%TEMP%\clay.obj /link /NOIMPLIB /NOEXP
