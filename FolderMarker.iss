; 文件夹标记 —— Inno Setup 安装脚本
; 生成标准「下一步 → 下一步」安装向导，简体中文界面

#define MyAppName "文件夹标记"
#define MyAppVersion "2.4.6"
#define MyAppPublisher "FolderMarker"
#define ShellExtVer "15"

; 菜单形态开关：
;   0 = 经典菜单（注册表 cascade）。支持任意层嵌套，「标记符号」是真三级。
;   1 = Win11 新版菜单（sparse MSIX）。只能一层子菜单，符号只能平铺。
; 之所以默认 0：新版菜单的 IExplorerCommand 明确不支持两层以上子菜单
; （InstallAware 文档原话：does not support nesting more than one sublevel）。
#define UseModernMenu "1"

[Setup]
AppId={{8F3A5C21-7B4D-4E96-9A1E-2D6C4B8F0A31}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\FolderMarker
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=lowest
OutputDir=dist
OutputBaseFilename=文件夹标记-安装包-v{#MyAppVersion}
SetupIconFile=icons\app.ico
UninstallDisplayIcon={app}\FolderMarker.exe
UninstallDisplayName={#MyAppName}
Compression=lzma2/normal
SolidCompression=no
WizardStyle=modern
WizardImageFile=packaging\wizard_large.bmp
WizardSmallImageFile=packaging\wizard_small.bmp
WizardImageStretch=no
WizardSizePercent=100
LanguageDetectionMethod=none
ShowLanguageDialog=no
AllowNoIcons=yes
CloseApplications=no
AppMutex=FolderMarkerSetupMutex

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Messages]
chinesesimp.WelcomeLabel1=欢迎安装 文件夹标记
chinesesimp.WelcomeLabel2=给文件夹加一点颜色。%n%n安装后右键任意文件夹，选「文件夹标记」再挑一个颜色，标记会直接画在文件夹图标上；随时可以改色或清除。
chinesesimp.FinishedHeadingLabel=安装完成
chinesesimp.FinishedLabelNoIcons=文件夹标记 已成功安装。%n%n现在右键任意文件夹，「文件夹标记」就在第一层菜单里，点开是 7 种颜色和清除。如果没看到，重新打开一个资源管理器窗口即可。
chinesesimp.SelectDirLabel3=安装程序将把 文件夹标记 安装到以下文件夹。
chinesesimp.ReadyLabel1=安装程序已准备好，即将把 文件夹标记 安装到这台电脑。
chinesesimp.ReadyLabel2a=点击「安装」开始；想改设置就点「上一步」。整个过程只写入程序文件和右键菜单，不会改动你的任何文件。
chinesesimp.ReadyLabel2b=点击「安装」开始。整个过程只写入程序文件和右键菜单，不会改动你的任何文件。
chinesesimp.ClickNext=点击「下一步」继续，或点击「取消」退出。
chinesesimp.ClickFinish=点击「完成」退出安装程序。
chinesesimp.SelectDirBrowseLabel=点击「下一步」继续；要换文件夹就点「浏览」。
chinesesimp.ConfirmUninstall=确定要卸载 文件夹标记 吗？%n%n卸载会做这些事：%n  · 移除右键菜单（经典菜单和新版菜单）%n  · 停止后台守护与托盘，并取消开机自启%n  · 还原所有已标记文件夹的图标，包括共享盘上的——其他电脑看到的颜色也会一起消失%n  · 删除本机的图标缓存、程序设置、共享盘绑定和标记清单%n%n重新安装后需要重新标记文件夹、重新绑定共享盘。

[Tasks]
; 桌面快捷方式：默认不勾选，用户想要才勾
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："; Flags: unchecked

[Icons]
; 开始菜单：直接放 Programs 根下，不建程序组（开始菜单搜索能直接搜到）
Name: "{userprograms}\文件夹标记"; Filename: "{app}\FolderMarker.exe"; WorkingDir: "{app}"; Comment: "给文件夹加颜色"
; 桌面：只有勾了 desktopicon 任务才创建
Name: "{autodesktop}\文件夹标记"; Filename: "{app}\FolderMarker.exe"; WorkingDir: "{app}"; Comment: "给文件夹加颜色"; Check: ShouldCreateDesktopIcon

[Files]
Source: "build\FolderMarker.exe"; DestDir: "{app}"; Flags: ignoreversion
; icons\palette.ico 来自 Fluent UI System Icons（microsoft/fluentui-system-icons，
; "color-24-regular"），MIT 许可，版权所有 Microsoft。其余 icon 为本项目自绘。
Source: "icons\*.ico"; DestDir: "{app}\icons"; Flags: ignoreversion
Source: "build\FolderMarker.Shell.dll"; DestDir: "{app}"; Flags: ignoreversion restartreplace
Source: "build\FolderMarker.cer"; DestDir: "{app}"; Flags: ignoreversion
; sparse 包：新版右键菜单（windows.fileExplorerContextMenus）靠它注册
Source: "dist\FolderMarkerShell.msix"; DestDir: "{app}"; Flags: ignoreversion
Source: "tools\RegisterMenu.ps1"; DestDir: "{app}"; Flags: ignoreversion
; sparse 包按「外部位置」解析文件：新版菜单 flyout 的头像来自 {app}\Assets\logo.png，
; 不拷的话那一行会没有图标（实测新机器）。
Source: "packaging\Assets\logo.png"; DestDir: "{app}\Assets"; Flags: ignoreversion


[Registry]
; ---------- 缓存守护：登录后自动补齐/刷新本机图标缓存 ----------
; ico 只存在本机的 cache 目录，关机不会丢；开机先强制全量重画一次，
; 之后定时巡检，内容变了或缓存被清都能自动补回来。
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "FolderMarkerCache"; ValueData: """{app}\FolderMarker.exe"" watch"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "FolderMarkerTray"; ValueData: """{app}\FolderMarker.exe"" tray"; Flags: uninsdeletevalue

; ---------- 文件夹图标右键（经典菜单，三级结构） ----------
; 新版菜单由 sparse 包的 4 个 Verb + Explorer 自动生成的「应用归属 flyout」提供，
; 同样能做三级；这一份注册表 cascade 是给「显示更多选项」经典菜单用的。
; 两套菜单结构对齐：颜色(9) / 标记符号(5) / 清除标记 / 管理。
; 这段注册表 cascade 提供经典菜单的「文件夹标记 >」单入口，必须保留。
; 新版菜单由 sparse 包的 4 个 COM Verb + Explorer 自动生成的应用 flyout 提供。
; 经典菜单没有 flyout，DLL 让 GetCanonicalName 返回 E_NOTIMPL，
; 经典菜单便跳过这 4 个 COM Verb，由这里收拢成单入口。
; 二级严格 4 项，不要再往这一层加项。
; 实测：根级联自动枚举子树最多渲染 14 个条目（本布局 颜色9+清除1+管理1=11，安全），
; 超出部分整组被丢弃。标记符号已整体移除（COM/清单/注册表同步）。
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker"; ValueType: string; ValueName: "MUIVerb"; ValueData: "文件夹标记"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\icons\menu.ico"
; SubCommands 必须保持空字符串！本地 shell\ 子键的静态级联菜单靠 Windows 自动枚举。
; 写成 "1_colors;2_badge;..." 会被当成 CommandStore 里的 verb 名去解析，
; 在本机找不到这些 verb，结果就是「文件夹标记 >」只有一个箭头、点开是空的。
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker"; ValueType: string; ValueName: "SubCommands"; ValueData: ""

Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\1_colors"; ValueType: string; ValueName: "MUIVerb"; ValueData: "颜色"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\1_colors"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\icons\palette.ico"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\1_colors"; ValueType: string; ValueName: "SubCommands"; ValueData: ""

Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\1_colors\shell\1_red"; ValueType: string; ValueName: "MUIVerb"; ValueData: "红色"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\1_colors\shell\1_red"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\icons\red.ico"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\1_colors\shell\1_red\command"; ValueType: string; ValueName: ""; ValueData: """{app}\FolderMarker.exe"" set red ""%1"""

Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\1_colors\shell\2_orange"; ValueType: string; ValueName: "MUIVerb"; ValueData: "橙色"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\1_colors\shell\2_orange"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\icons\orange.ico"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\1_colors\shell\2_orange\command"; ValueType: string; ValueName: ""; ValueData: """{app}\FolderMarker.exe"" set orange ""%1"""

Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\1_colors\shell\3_yellow"; ValueType: string; ValueName: "MUIVerb"; ValueData: "黄色"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\1_colors\shell\3_yellow"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\icons\yellow.ico"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\1_colors\shell\3_yellow\command"; ValueType: string; ValueName: ""; ValueData: """{app}\FolderMarker.exe"" set yellow ""%1"""

Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\1_colors\shell\4_green"; ValueType: string; ValueName: "MUIVerb"; ValueData: "绿色"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\1_colors\shell\4_green"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\icons\green.ico"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\1_colors\shell\4_green\command"; ValueType: string; ValueName: ""; ValueData: """{app}\FolderMarker.exe"" set green ""%1"""

Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\1_colors\shell\5_blue"; ValueType: string; ValueName: "MUIVerb"; ValueData: "蓝色"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\1_colors\shell\5_blue"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\icons\blue.ico"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\1_colors\shell\5_blue\command"; ValueType: string; ValueName: ""; ValueData: """{app}\FolderMarker.exe"" set blue ""%1"""

Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\1_colors\shell\6_purple"; ValueType: string; ValueName: "MUIVerb"; ValueData: "紫色"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\1_colors\shell\6_purple"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\icons\purple.ico"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\1_colors\shell\6_purple\command"; ValueType: string; ValueName: ""; ValueData: """{app}\FolderMarker.exe"" set purple ""%1"""

Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\1_colors\shell\7_gray"; ValueType: string; ValueName: "MUIVerb"; ValueData: "灰色"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\1_colors\shell\7_gray"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\icons\gray.ico"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\1_colors\shell\7_gray\command"; ValueType: string; ValueName: ""; ValueData: """{app}\FolderMarker.exe"" set gray ""%1"""
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\1_colors\shell\8_pick"; ValueType: string; ValueName: "MUIVerb"; ValueData: "自定义颜色"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\1_colors\shell\8_pick"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\icons\pick.ico"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\1_colors\shell\8_pick\command"; ValueType: string; ValueName: ""; ValueData: """{app}\FolderMarker.exe"" pick none ""%1"""
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\1_colors\shell\9_undo"; ValueType: string; ValueName: "MUIVerb"; ValueData: "恢复上次颜色"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\1_colors\shell\9_undo"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\icons\undo.ico"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\1_colors\shell\9_undo\command"; ValueType: string; ValueName: ""; ValueData: """{app}\FolderMarker.exe"" undo none ""%1"""







; 「自定义颜色」「恢复上次颜色」收进颜色三级，第二层只留 4 项。
; 实测本机 Explorer 对二级静态 verb 只稳定渲染 4 项，这个排法保证全部可见。


Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\2_clear"; ValueType: string; ValueName: ""; ValueData: "清除标记"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\2_clear"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\icons\clear.ico"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\2_clear\command"; ValueType: string; ValueName: ""; ValueData: """{app}\FolderMarker.exe"" clear none ""%1"""
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\3_manage"; ValueType: string; ValueName: ""; ValueData: "管理"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\3_manage"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\icons\manage.ico"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderMarker\shell\3_manage\command"; ValueType: string; ValueName: ""; ValueData: """{app}\FolderMarker.exe"" ui"



[UninstallDelete]
Type: filesandordirs; Name: "{app}\icons"
Type: filesandordirs; Name: "{app}\cache"
; 给「自带图标的文件夹」合成的图标（%LOCALAPPDATA%\FolderMarker\custom，和 {app} 是同一层）
Type: filesandordirs; Name: "{app}\custom"
Type: files; Name: "{app}\refresh.log"
Type: files; Name: "{app}\error.log"
Type: files; Name: "{app}\watch.pid"
Type: files; Name: "{app}\last_autodemark.txt"
Type: files; Name: "{app}\register_menu.log"
Type: files; Name: "{app}\diagnose.txt"
Type: filesandordirs; Name: "{app}\undo"
Type: files; Name: "{app}\FolderMarker.exe.old-*"
Type: files; Name: "{app}\demark_result.txt"
Type: files; Name: "{app}\settings.ini"
Type: dirifempty; Name: "{app}"
Type: files; Name: "{app}\FolderMarker.Shell.dll"
Type: files; Name: "{app}\FolderMarker.Shell.*.old"
Type: files; Name: "{app}\FolderMarker.Shell.old.dll"
Type: files; Name: "{app}\is-*.tmp"
Type: files; Name: "{app}\FolderMarker.cer"
Type: files; Name: "{app}\Assets\logo.png"
; 老版本把日志写在 LocalAppData\FolderMarker 下，装到别处时也要清掉
Type: files; Name: "{localappdata}\FolderMarker\refresh.log"
Type: files; Name: "{localappdata}\FolderMarker\error.log"
Type: files; Name: "{localappdata}\FolderMarker\marked.txt"
Type: files; Name: "{localappdata}\FolderMarker\roots.txt"
Type: dirifempty; Name: "{localappdata}\FolderMarker"

[Code]
// 桌面快捷方式是否创建：交互安装看用户勾选，静默安装一律不建。
// 只用 [Tasks] 的 Tasks: 条件不够 —— 静默模式下 Inno 会把任务全选，
// 即使传 /MERGETASKS=!desktopicon 也照样创建（实测）。
// 桌面快捷方式：交互安装看用户勾选（[Tasks] 里 Flags: unchecked，默认不勾）。
// 注意：静默安装时 Inno 会把所有任务当选中、且 ParamStr 拿不到 /VERYSILENT，
// 所以静默场景靠 ssPostInstall 里的兜底纠正在无法区分时保持原样。
function ShouldCreateDesktopIcon: Boolean;
begin
  Result := WizardIsTaskSelected('desktopicon');
  if Result then
    Log('桌面图标: 创建')
  else
    Log('桌面图标: 跳过');
end;

// 刷新图标缓存。
// 坑：ie4uinit.exe 只在 64 位的 System32 里有，SysWOW64 下没有；
// 而 32 位安装程序里的 sys 常量正好指向 SysWOW64，直接调用必然 CreateProcess 失败。
// 所以这里自己挑一个真实存在的路径（sysnative 才是 32 位进程眼里的真 System32），
// 找不到就安静跳过，绝不弹错误框。
procedure RefreshIconCache;
var
  ExePath: String;
  ResultCode: Integer;
begin
  ExePath := '';
  if IsWin64 then
    ExePath := ExpandConstant('{sysnative}\ie4uinit.exe')
  else
    ExePath := ExpandConstant('{sys}\ie4uinit.exe');

  if not FileExists(ExePath) then
    ExePath := ExpandConstant('{sys}\ie4uinit.exe');
  if not FileExists(ExePath) then
    ExePath := ExpandConstant('{win}\ie4uinit.exe');

  if FileExists(ExePath) then
  begin
    Log('刷新图标缓存: ' + ExePath);
    Exec(ExePath, '-show', '', SW_HIDE, ewNoWait, ResultCode);
  end
  else
    Log('刷新图标缓存: 跳过（系统里没有 ie4uinit.exe）');
end;

// 卸载前把用户标记过的文件夹还原成原样。
// 不做这一步的话，图标文件被删掉了，但那些文件夹还留着 desktop.ini、
// 指着已经不存在的图标路径，会显示成空白/默认图标，System 属性也还在。
// 必须在删文件之前跑 —— 它要调用 {app}\FolderMarker.exe。
procedure RestoreMarkedFolders;
var
  ExePath: String;
  ResultCode: Integer;
begin
  ExePath := ExpandConstant('{app}\FolderMarker.exe');

  if not FileExists(ExePath) then
  begin
    Log('还原已标记文件夹: 跳过（找不到 FolderMarker.exe）');
    Exit;
  end;

  Log('还原已标记文件夹: ' + ExePath + ' cleanup none');
  if Exec(ExePath, 'cleanup none', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Log('还原已标记文件夹: 完成，返回 ' + IntToStr(ResultCode))
  else
    Log('还原已标记文件夹: 执行失败');
end;

// ---------- Windows 11 新版右键菜单 ----------
// 新版主菜单只接受「带包身份的 IExplorerCommand」，纯写注册表进不去（试过，会被收进「显示更多」）。
// 所以用一个 sparse MSIX 包提供包身份，包里声明的 COM 服务器指向 {app}\FolderMarker.Shell.dll。
// 包必须由受信任证书签名，安装时就把自签名证书放进本机受信任根。

// 必须用 64 位 PowerShell：32 位进程里没有 Appx 模块。
function PowerShellPath: String;
begin
  Result := ExpandConstant('{sysnative}\WindowsPowerShell\v1.0\powershell.exe');
  if not FileExists(Result) then
    Result := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
end;

// 让资源管理器用「轻量刷新」而不是「重启」来加载新的 shell 扩展。
// Add-AppxPackage 注册完会发 SHChangeNotify，但我们再补一次带 FLUSH 的，
// 确保 Explorer 立即重读扩展列表（而不是等到下次自己发现再粗暴重启）。
procedure SHChangeNotify(wEventId: LongInt; uFlags: LongWord; dwItem1, dwItem2: LongInt);
  external 'SHChangeNotify@shell32.dll stdcall';

procedure NotifyShellAssocChanged;
begin
  try
    // SHCNE_ASSOCCHANGED = 0x08000000, SHCNF_IDLIST = 0x0000, SHCNF_FLUSH = 0x1000
    SHChangeNotify($08000000, $1000, 0, 0);
  except
  end;
end;

function RunPowerShell(const Script: String; var ResultCode: Integer): Boolean;
var
  Params: String;
begin
  Params := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "' + Script + '"';
  Result := Exec(PowerShellPath, Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

// 用 -File 调随包分发的 ps1：不再拼 -Command 长字符串，引号/转义不会有坑
function RunPowerShellFile(const Args: String; var ResultCode: Integer): Boolean;
var
  Params: String;
begin
  Params := '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\RegisterMenu.ps1') + '" ' + Args;
  Result := Exec(PowerShellPath, Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function AddTrustedCertificate(var ResultCode: Integer): Boolean;
var
  CertPath, CertUtil: String;
begin
  Result := False;
  CertPath := ExpandConstant('{app}\FolderMarker.cer');
  if not FileExists(CertPath) then
  begin
    Log('导入证书: 跳过（找不到 cer）');
    Exit;
  end;

  CertUtil := ExpandConstant('{sysnative}\certutil.exe');
  if not FileExists(CertUtil) then
    CertUtil := ExpandConstant('{sys}\certutil.exe');

  // 先试机器级（需要管理员），失败再退到当前用户级（普通用户也能成功）
  Result := Exec(CertUtil, '-addstore -f root "' + CertPath + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Log('导入证书(机器级): 返回 ' + IntToStr(ResultCode));
  if ResultCode <> 0 then
  begin
    Result := Exec(CertUtil, '-user -addstore -f root "' + CertPath + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Log('导入证书(用户级): 返回 ' + IntToStr(ResultCode));
  end;
end;

// 覆盖安装时，旧的 Shell.dll 可能正被资源管理器（或剪映这类会枚举 shell 扩展的程序）加载，
// 直接写会失败、被安排到重启后才替换 —— 用户不重启就还是旧版。提前把旧文件改名，绕开占用。
procedure KillAppInstances;
var
  ResultCode: Integer;
begin
  // 换 exe / 卸载文件前必须先结束运行中的托盘与守护进程，
  // 否则 DeleteFile 报「拒绝访问」、整个安装回滚（实测 v2.0.5）。
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM FolderMarker.exe',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(300);
end;

procedure RetireOldShellDll;
var
  Dll, Old: String;
begin
  Dll := ExpandConstant('{app}\FolderMarker.Shell.dll');
  // 用带时间戳的唯一名，避免上一次的 .old 还被人拿着、改名失败
  Old := ExpandConstant('{app}\FolderMarker.Shell.') + GetDateTimeString('yyyymmddhhnnss', #0, #0) + '.old';

  if not FileExists(Dll) then
    Exit;

  if RenameFile(Dll, Old) then
    Log('旧 Shell.dll 已改名为 .old，避免被占用')
  else
    Log('旧 Shell.dll 改名失败（无人占用时可直接覆盖）');
end;

// 1.5.5 及以前在「文件夹空白处」也挂了一份菜单；现在只保留「右键文件夹本身」，
// 所以这一份不管哪个系统都要摘掉。文件夹那一份只在主菜单（MSIX）确实注册成功后才摘，
// 否则 Win10 上会把唯一的入口删掉。
procedure CleanLegacyMenuKeys(const DropFolderKey: Boolean);
begin
  // 旧版用扁平键名（按字母序显示，顺序乱）；新版键名带序号前缀。
  // 升级时把旧键清掉，避免新旧两套同时出现在菜单里。
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\Directory\shell\FolderMarker\shell\red');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\Directory\shell\FolderMarker\shell\orange');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\Directory\shell\FolderMarker\shell\yellow');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\Directory\shell\FolderMarker\shell\green');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\Directory\shell\FolderMarker\shell\blue');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\Directory\shell\FolderMarker\shell\purple');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\Directory\shell\FolderMarker\shell\gray');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\Directory\shell\FolderMarker\shell\clear');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\Directory\shell\FolderMarker\shell\manage');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\Directory\shell\FolderMarker\shell\undo');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\Directory\shell\FolderMarker\shell\pick');

  // v1.9.9 ~ v1.9.10 曾在二级放 6 项（3_pick/4_undo/5_clear/6_manage）。
  // 现改为二级只留 4 项、自定义/恢复收进颜色三级，升级时必须删掉旧键，
  // 否则二级又会变成 8 项，Explorer 的截断会让「清除标记」「管理」再次消失。
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\Directory\shell\FolderMarker\shell\3_pick');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\Directory\shell\FolderMarker\shell\4_undo');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\Directory\shell\FolderMarker\shell\5_clear');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\Directory\shell\FolderMarker\shell\6_manage');

  if RegKeyExists(HKCU, 'Software\Classes\Directory\Background\shell\FolderMarker') then
  begin
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\Directory\Background\shell\FolderMarker');
    Log('已移除空白处的右键菜单（旧版本留下的）');
  end;

  if DropFolderKey and RegKeyExists(HKCU, 'Software\Classes\Directory\shell\FolderMarker') then
  begin
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\Directory\shell\FolderMarker');
    Log('已移除「显示更多」里的重复菜单（文件夹）');
  end;
end;

// 上一次安装若因 DLL 被占用而挂了「重启后替换」，系统会保留一条挂起操作，
// 后续安装程序看到它就会要求重启、直接中止。这里把属于我们自己的挂起项清掉。
procedure ClearStalePendingRenames;
var
  Script: String;
  ResultCode: Integer;
begin
  Script :=
    '$sm=''HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager''; ' +
    '$v=(Get-ItemProperty $sm -Name PendingFileRenameOperations -ErrorAction SilentlyContinue).PendingFileRenameOperations; ' +
    'if($v){ $keep=@(); for($i=0; $i+1 -lt $v.Count; $i+=2){ ' +
      'if(($v[$i] -notmatch ''FolderMarker'') -and ($v[$i+1] -notmatch ''FolderMarker'')){ $keep+=$v[$i]; $keep+=$v[$i+1] } }; ' +
      'if($keep.Count -eq 0){ Remove-ItemProperty $sm -Name PendingFileRenameOperations -ErrorAction SilentlyContinue } ' +
      'else { Set-ItemProperty $sm -Name PendingFileRenameOperations -Value ([string[]]$keep) } }';

  if RunPowerShell(Script, ResultCode) then
    Log('清理挂起操作: 返回 ' + IntToStr(ResultCode))
  else
    Log('清理挂起操作: powershell 未能执行');
end;

function IsWin11OrLater: Boolean;
var
  V: TWindowsVersion;
begin
  GetWindowsVersionEx(V);
  Result := (V.Major > 10) or ((V.Major = 10) and (V.Build >= 22000));
end;

// Inno 内部对「挂起文件替换」的检查发生在 PrepareToInstall 之前，
// 所以清理必须提前到安装程序启动、且已经提权之后的最早时刻。
procedure DropLegacyUninstallEntry;
begin
  // 1.4.x 是 per-user 安装，卸载项在 HKCU；1.5 起改为 admin 安装，写在 HKLM。
  // 不删掉旧的那条，「应用和功能」里会一直显示旧版本号。
  if RegKeyExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8F3A5C21-7B4D-4E96-9A1E-2D6C4B8F0A31}_is1') then
  begin
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8F3A5C21-7B4D-4E96-9A1E-2D6C4B8F0A31}_is1');
    Log('已移除旧版卸载项（HKCU）');
  end;
end;

// 覆盖安装 / 卸载时，后台守护可能正占着 FolderMarker.exe，
// 直接写会失败并被安排到重启后替换。只杀命令行里带 " watch" 的那一个，
// 不会误伤用户此刻正在右键标记的 set/clear 进程。
procedure StopCacheWatcher;
var
  Script: String;
  ResultCode: Integer;
begin
  Script :=
    'Get-CimInstance Win32_Process -Filter ''Name=''''FolderMarker.exe'''''' | ' +
    'Where-Object { $_.CommandLine -like ''* watch*'' } | ' +
    'ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }';
  if RunPowerShell(Script, ResultCode) then
    Log('停止缓存守护: 返回 ' + IntToStr(ResultCode));
end;

procedure StartCacheWatcher;
var
  ExePath: String;
  ResultCode: Integer;
begin
  ExePath := ExpandConstant('{app}\FolderMarker.exe');
  if FileExists(ExePath) then
  begin
    Exec(ExePath, 'watch', '', SW_HIDE, ewNoWait, ResultCode);
    // 安装时被 KillAppInstances 结束的托盘图标，这里补回来（Run 键要下次登录才生效）
    Exec(ExePath, 'tray', '', SW_HIDE, ewNoWait, ResultCode);
  end;
end;

function InitializeSetup: Boolean;
begin
  ClearStalePendingRenames;
  StopCacheWatcher;
  Result := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
end;

// 检查自签名证书是否已在受信任根里（避免重复导入触发安全通知）
function IsCertTrusted: Boolean;
var
  ResultCode: Integer;
begin
  Result := RunPowerShell(
    '$tp=''7C6535D04EBD11374BB742B108104B0825114944''; if ((Get-ChildItem Cert:\\LocalMachine\\Root -ErrorAction SilentlyContinue | Where-Object { $_.Thumbprint -eq $tp }) -or (Get-ChildItem Cert:\\CurrentUser\\Root -ErrorAction SilentlyContinue | Where-Object { $_.Thumbprint -eq $tp })) { exit 0 } else { exit 1 }',
    ResultCode) and (ResultCode = 0);
end;

procedure InstallShellExtension;
var
  MsixPath, AppDir, Script, StoredExtVer: String;
  ResultCode: Integer;
  AlreadyRegistered: Boolean;
begin
  AppDir := ExpandConstant('{app}');
  MsixPath := AppDir + '\FolderMarkerShell.msix';

  // 经典菜单模式：不注册 sparse 包，菜单完全由 [Registry] 里的 cascade 提供。
  // 升级时把老版本注册过的包卸掉，免得经典菜单里出现两个同名项。
  if '{#UseModernMenu}' = '0' then
  begin
    RunPowerShell(
      '$a=Get-AppxPackage -Name FolderMarkerShell; if($a){$a|Remove-AppxPackage}; ' +
      '$b=Get-AppxPackage -Name FolderMarkerBadgeShell; if($b){$b|Remove-AppxPackage}',
      ResultCode);
    Log('菜单模式: 经典菜单（三级），已移除任何已注册的新版菜单包');
    CleanLegacyMenuKeys(False);
    NotifyShellAssocChanged;
    Exit;
  end;

  if not FileExists(MsixPath) then
  begin
    Log('注册菜单扩展: 跳过（找不到 msix）');
    CleanLegacyMenuKeys(False);
    Exit;
  end;

  // 证书已在受信任根里就不再跑 certutil —— 避免多余的系统安全通知。
  // 只有首次安装（全新机器）才需要导入。
  if not IsCertTrusted then
    AddTrustedCertificate(ResultCode);

  // shell 扩展 DLL 只是个 480 行的菜单 shim（加载 exe 的 COM 服务器），
  // 跨版本几乎不变。升级时 DLL 不变 => 整个注册步骤跳过 => 0 次资源管理器重启。
  // 只有「首次安装」或「DLL 变了（ShellExtVer 需手动 +1）」才碰 Add-AppxPackage。
  AlreadyRegistered := RunPowerShell(
    '$p=Get-AppxPackage -Name FolderMarkerShell; if($p){exit 0} else {exit 1}',
    ResultCode) and (ResultCode = 0);

  if AlreadyRegistered then
  begin
    StoredExtVer := '';
    RegQueryStringValue(HKCU, 'Software\FolderMarker', 'ShellExtVer', StoredExtVer);
    // 旧版安装器没写 ShellExtVer（用的是 ShellPkgStamp），视为兼容 —— DLL 从未变过
    if (StoredExtVer = '') or (StoredExtVer = '{#ShellExtVer}') then
    begin
      RegWriteStringValue(HKCU, 'Software\FolderMarker', 'ShellExtVer', '{#ShellExtVer}');
      RegDeleteValue(HKCU, 'Software\FolderMarker', 'ShellPkgStamp');
      Log('注册菜单扩展: 已注册且 DLL 未变，跳过（0 次资源管理器重启）');
      CleanLegacyMenuKeys(False);
      NotifyShellAssocChanged;
      Exit;
    end;
    Log('注册菜单扩展: DLL 版本 ' + StoredExtVer + ' -> {#ShellExtVer}，需重新注册');
  end;

  // 需注册（首次安装 / DLL 变了）：交给随包的 RegisterMenu.ps1 处理
  // （同版本幂等跳过；不同版本先卸后装；失败会写 {app}\register_menu.log）
  if RunPowerShellFile('-MsixPath "' + MsixPath + '" -AppDir "' + AppDir + '"', ResultCode) then
  begin
    Log('注册菜单扩展: 返回 ' + IntToStr(ResultCode));
    if ResultCode = 0 then
    begin
      RegWriteStringValue(HKCU, 'Software\FolderMarker', 'ShellExtVer', '{#ShellExtVer}');
      RegDeleteValue(HKCU, 'Software\FolderMarker', 'ShellPkgStamp');
    end;
    // 两套菜单共存：新版靠 sparse 包，经典靠注册表 cascade。
    // 不删注册表菜单——DLL 的 GetFlags 拦截让 COM 项不进经典顶层，不会同名重复。
    CleanLegacyMenuKeys(False);
  end;
end;

procedure RemoveShellExtension;
var
  Script, CertUtil, Thumbprint: String;
  ResultCode: Integer;
begin
  RegDeleteValue(HKCU, 'Software\FolderMarker', 'ShellPkgStamp');
  RegDeleteValue(HKCU, 'Software\FolderMarker', 'ShellExtVer');

  if RunPowerShellFile('-Uninstall -AppDir "' + ExpandConstant('{app}') + '"', ResultCode) then
    Log('移除菜单扩展: 返回 ' + IntToStr(ResultCode))
  else
    Log('移除菜单扩展: powershell 未能执行');

  // 顺带把自己装进受信任根的证书摘掉，不留信任残留
  Thumbprint := '7C6535D04EBD11374BB742B108104B0825114944';
  CertUtil := ExpandConstant('{sysnative}\certutil.exe');
  if not FileExists(CertUtil) then
    CertUtil := ExpandConstant('{sys}\certutil.exe');
  if Exec(CertUtil, '-delstore root ' + Thumbprint, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Log('移除证书(机器): 返回 ' + IntToStr(ResultCode));
  // 安装时证书可能落在当前用户存储（-user 回退），这里一并摘掉，不留信任残留
  if Exec(CertUtil, '-user -delstore root ' + Thumbprint, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Log('移除证书(用户): 返回 ' + IntToStr(ResultCode));
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    KillAppInstances;
    RetireOldShellDll;
  end;

  if CurStep = ssPostInstall then
  begin
    DropLegacyUninstallEntry;
    InstallShellExtension;

    // 兜底：没勾桌面图标就把残留删掉（防止某些情况下 [Icons] 仍然创建）
    if not WizardIsTaskSelected('desktopicon') then
    begin
      if FileExists(ExpandConstant('{autodesktop}\文件夹标记.lnk')) then
      begin
        DeleteFile(ExpandConstant('{autodesktop}\文件夹标记.lnk'));
        Log('桌面图标: 已按未勾选清除');
      end;
    end;

    RefreshIconCache;
    StartCacheWatcher;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    StopCacheWatcher;
    KillAppInstances;      // 托盘若还在跑，删 exe 会拒绝访问
    RemoveShellExtension;
    RestoreMarkedFolders;
  end;

  if CurUninstallStep = usPostUninstall then
    RefreshIconCache;
end;
