@echo off
setlocal enabledelayedexpansion
REM MakePri.exe wrapper: Windows App SDK 1.7's MakePri.exe crashes with
REM E_UNEXPECTED (0x8000FFFF) on .NET 10 after generating the PRI file.
REM This wrapper runs MakePri.exe and returns 0 if the output PRI file
REM exists, regardless of the tool's exit code.

REM Locate the real MakePri.exe in the NuGet package cache:
set "MAKEPRI_EXE="
for /f "delims=" %%V in ('dir /b /ad "%USERPROFILE%\.nuget\packages\microsoft.windows.sdk.buildtools" 2^>nul') do (
    if exist "%USERPROFILE%\.nuget\packages\microsoft.windows.sdk.buildtools\%%V\bin\10.0.22621.0\x64\makepri.exe" (
        set "MAKEPRI_EXE=%USERPROFILE%\.nuget\packages\microsoft.windows.sdk.buildtools\%%V\bin\10.0.22621.0\x64\makepri.exe"
    )
)
if not defined MAKEPRI_EXE set "MAKEPRI_EXE=%USERPROFILE%\.nuget\packages\microsoft.windows.sdk.buildtools\10.0.22621.756\bin\10.0.22621.0\x64\makepri.exe"

"%MAKEPRI_EXE%" %*
set MAKEPRI_EXIT=!ERRORLEVEL!

REM If MakePri exited with 0, return immediately
if !MAKEPRI_EXIT! equ 0 exit /b 0

REM MakePri crashed - check if the output PRI file was still generated.
REM The -of parameter specifies the output file path.
set "OUTPUT_FILE="
set "FOUND_OF=0"
for %%A in (%*) do (
    if "!FOUND_OF!"=="1" (
        set "OUTPUT_FILE=%%A"
        set "FOUND_OF=0"
    )
    if "%%A"=="-of" set "FOUND_OF=1"
    if "%%A"=="/of" set "FOUND_OF=1"
)

REM If we found the output file and it exists, return 0
if not "!OUTPUT_FILE!"=="" (
    if exist "!OUTPUT_FILE!" exit /b 0
)

exit /b !MAKEPRI_EXIT!
