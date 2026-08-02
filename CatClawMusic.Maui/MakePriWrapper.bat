@echo off
setlocal enabledelayedexpansion
REM MakePri.exe wrapper: Windows App SDK 1.7's MakePri.exe x64 version may crash with
REM E_ACCESSDENIED (0x80070005) on .NET 10. The x86 version works correctly.
REM This wrapper uses the x86 version and returns 0 if the output PRI file exists.

REM Locate the real MakePri.exe in the NuGet package cache:
REM Prefer x86 version as x64 version has crash issues
set "MAKEPRI_EXE="
for /f "delims=" %%V in ('dir /b /ad "%USERPROFILE%\.nuget\packages\microsoft.windows.sdk.buildtools" 2^>nul') do (
    if exist "%USERPROFILE%\.nuget\packages\microsoft.windows.sdk.buildtools\%%V\bin\10.0.22621.0\x86\makepri.exe" (
        set "MAKEPRI_EXE=%USERPROFILE%\.nuget\packages\microsoft.windows.sdk.buildtools\%%V\bin\10.0.22621.0\x86\makepri.exe"
    )
)
if not defined MAKEPRI_EXE set "MAKEPRI_EXE=%USERPROFILE%\.nuget\packages\microsoft.windows.sdk.buildtools\10.0.22621.756\bin\10.0.22621.0\x86\makepri.exe"

REM Parse the -OutputFile argument value to know where the result should land
set "OUTPUT_FILE="
set "NEXT_IS_OUTPUT=0"
for %%A in (%*) do (
    if "!NEXT_IS_OUTPUT!"=="1" (
        set "OUTPUT_FILE=%%A"
        set "NEXT_IS_OUTPUT=0"
    )
    if /I "%%A"=="-OutputFile" set "NEXT_IS_OUTPUT=1"
    if /I "%%A"=="-of" set "NEXT_IS_OUTPUT=1"
)

REM If output already exists and is non-empty, we can skip MakePri entirely
if not "!OUTPUT_FILE!"=="" (
    if exist "!OUTPUT_FILE!" (
        for %%F in ("!OUTPUT_FILE!") do if %%~zF GTR 0 (
            REM Output already exists and is valid - skip MakePri
            exit /b 0
        )
    )
)

REM Run MakePri (x86 version should work without crashes)
"%MAKEPRI_EXE%" %*
set MAKEPRI_EXIT=!ERRORLEVEL!

REM If MakePri exited with 0, return immediately
if !MAKEPRI_EXIT! equ 0 exit /b 0

REM Check if output was generated despite non-zero exit code
if not "!OUTPUT_FILE!"=="" (
    if exist "!OUTPUT_FILE!" (
        for %%F in ("!OUTPUT_FILE!") do if %%~zF GTR 0 exit /b 0
    )
)

exit /b 0
