@echo off
REM XamlCompiler.exe wrapper: Windows App SDK 1.7 XAML compiler generates all
REM output files successfully but returns exit code 1 on .NET 10 due to a
REM shutdown crash. This wrapper runs the compiler and returns 0 if the
REM output JSON file was created, regardless of the compiler's exit code.

REM Locate the real XamlCompiler.exe in the NuGet package cache:
REM prefer the highest installed microsoft.windowsappsdk version, and fall
REM back to the pinned version below if none is found.
set "XAMLC_EXE="
for /f "delims=" %%V in ('dir /b /ad "%USERPROFILE%\.nuget\packages\microsoft.windowsappsdk" 2^>nul') do (
    if exist "%USERPROFILE%\.nuget\packages\microsoft.windowsappsdk\%%V\tools\net472\XamlCompiler.exe" (
        set "XAMLC_EXE=%USERPROFILE%\.nuget\packages\microsoft.windowsappsdk\%%V\tools\net472\XamlCompiler.exe"
    )
)
if not defined XAMLC_EXE set "XAMLC_EXE=%USERPROFILE%\.nuget\packages\microsoft.windowsappsdk\1.7.250909003\tools\net472\XamlCompiler.exe"

"%XAMLC_EXE%" %*
set COMPILER_EXIT=%ERRORLEVEL%

REM Check if output.json (second argument) was created
if exist "%~2" (
    exit /b 0
)

exit /b %COMPILER_EXIT%
