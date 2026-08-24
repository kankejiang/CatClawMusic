; 猫爪音乐 Windows 安装程序脚本（Inno Setup 6.3+/7）
; 用法: "C:\Program Files (x86)\Inno Setup 7\ISCC.exe" build-win-setup.iss
; 打包源: CatClawMusic.Maui\bin\win-release\publish（self-contained 绿色目录发布）

#define MyAppName "猫爪音乐"
#ifndef MyAppVersion
#define MyAppVersion "1.8.0"
#endif
#ifndef MyPublishDir
#define MyPublishDir "CatClawMusic.Maui\bin\win-release\publish"
#endif
#define MyAppPublisher "CatClawMusic"
#define MyAppExeName "CatClawMusic.Maui.exe"
#define MyAppId "5F2C8B6A-91D4-4E7C-B3A8-CatClawMusicWin"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppVerName={#MyAppName} {#MyAppVersion}
DefaultDirName={autopf}\CatClawMusic
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=CatClawMusic.Maui\Platforms\Windows\installer-icon.ico
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=release\windows
OutputBaseFilename=catclaw.music-{#MyAppVersion}-Setup
LicenseFile=LICENSE.txt
; 权限：绿色目录无需管理员即可写入 Program Files 时用 normal，
; 但写 {autopf} 需要管理员，故用 admin 权限
PrivilegesRequired=admin

[Languages]
; 中文界面（官方 Languages 目录需含 ChineseSimplified.isl，若缺失改用英文界面）
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式(&D)"; GroupDescription: "附加图标:"; Flags: unchecked

[Files]
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.xml"

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "立即运行 {#MyAppName}(&R)"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
