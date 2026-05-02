@echo off
set ANDROID_HOME=C:\Users\lvjin\AppData\Local\Android\Sdk
set ANDROID_SDK_ROOT=C:\Users\lvjin\AppData\Local\Android\Sdk
set SLN=D:\WorkBuddy\CatClawMusic\CatClawMusic.sln
set MSDBUILD="C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe"

%MSDBUILD% %SLN% /t:Restore /p:Configuration=Release /p:Platform="Any CPU" /p:AndroidSdkDirectory="C:\Users\lvjin\AppData\Local\Android\Sdk" /p:JavaSdkDirectory="C:\Program Files\Android\openjdk\jdk-21.0.8" /noconsolelogger /fileLogger /flp:LogFile="C:\Temp\vs_restore.log";Verbosity=normal
echo RESTORE_EXIT=%ERRORLEVEL%

%MSDBUILD% %SLN% /t:Build /p:Configuration=Release /p:Platform="Any CPU" /p:AndroidSdkDirectory="C:\Users\lvjin\AppData\Local\Android\Sdk" /p:JavaSdkDirectory="C:\Program Files\Android\openjdk\jdk-21.0.8" /noconsolelogger /fileLogger /flp:LogFile="C:\Temp\vs_build.log";Verbosity=normal
echo BUILD_EXIT=%ERRORLEVEL%
