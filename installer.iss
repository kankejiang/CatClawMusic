[Setup]
AppName=猫爪音乐
AppVersion=1.8.9
AppPublisher=CatClaw Music
AppPublisherURL=https://github.com/kankejiang/CatClawMusic
DefaultDirName={autopf}\CatClawMusic
DefaultGroupName=猫爪音乐
UninstallDisplayIcon={app}\CatClawMusic.Maui.exe
Compression=lzma2/max
SolidCompression=yes
OutputDir=.
OutputBaseFilename=猫爪音乐_Setup_1.8.9
SetupIconFile=D:\Code\CatClawMusic\CatClawMusic.Maui\Platforms\Windows\installer-icon.ico
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes

[Languages]
Name: "chinese"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "D:\Code\CatClawMusic\CatClawMusic.Maui\bin\winpub6\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; 覆盖安装时清理旧版本残留（旧 resources.pri / libffmpeg.so / Platforms 等会导致
; 新 exe 加载旧资源崩溃 0xC000027B），先删旧文件再装新文件
[InstallDelete]
Type: filesandordirs; Name: "{app}\*"

[Icons]
Name: "{group}\猫爪音乐"; Filename: "{app}\CatClawMusic.Maui.exe"; WorkingDir: "{app}"
Name: "{group}\卸载猫爪音乐"; Filename: "{uninstallexe}"
Name: "{commondesktop}\猫爪音乐"; Filename: "{app}\CatClawMusic.Maui.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\CatClawMusic.Maui.exe"; Description: "运行猫爪音乐"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "{app}\CatClawMusic.Maui.exe"; Parameters: "--uninstall"; Flags: runhidden