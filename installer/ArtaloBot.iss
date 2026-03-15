; ArtaloBot Inno Setup Script
; Download Inno Setup from: https://jrsoftware.org/isinfo.php

#define MyAppName "ArtaloBot"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "ArtaloBot Team"
#define MyAppURL "https://github.com/anuragstpl/ArtaloAgent"
#define MyAppExeName "ArtaloBot.App.exe"

; .NET 8 Desktop Runtime download URL (update version as needed)
#define DotNetVersion "8.0.11"
#define DotNetInstallerURL "https://download.visualstudio.microsoft.com/download/pr/fc8c9dea-8180-4dad-bf1b-5f229cf47477/c3f0536639ab40f1470b6bad5e1b95b8/windowsdesktop-runtime-8.0.11-win-x64.exe"

[Setup]
; Application information
AppId={{A7B8C9D0-E1F2-4A5B-8C9D-0E1F2A3B4C5D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes

; Output settings
OutputDir=..\dist
OutputBaseFilename=ArtaloBot-Setup-{#MyAppVersion}

; Compression
Compression=lzma2/ultra64
SolidCompression=yes

; UI settings
WizardStyle=modern
WizardSizePercent=120
SetupIconFile=..\src\ArtaloBot.App\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

; Privileges
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; Architecture
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Versioning
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=ArtaloBot AI Assistant
VersionInfoCopyright=Copyright (C) 2024 {#MyAppPublisher}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

; Installer appearance
DisableWelcomePage=no
DisableDirPage=no
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel1=Welcome to the [name] Setup Wizard
WelcomeLabel2=This will install [name/ver] on your computer.%n%nArtaloBot is a powerful AI assistant with multi-LLM support, knowledge base agents, and multi-channel communication.%n%nIt is recommended that you close all other applications before continuing.

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"
Name: "quicklaunchicon"; Description: "Create a &Quick Launch shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
; Main application files (self-contained build)
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; Documentation
Source: "..\README.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\QUICK_START.md"; DestDir: "{app}\docs"; Flags: ignoreversion skipifsourcedoesntexist

; Logo
Source: "..\logo.png"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "Launch ArtaloBot AI Assistant"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{group}\Documentation"; Filename: "{app}\docs\README.md"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; Comment: "Launch ArtaloBot AI Assistant"
Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: quicklaunchicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent shellexec

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\ArtaloBot"

[Code]
var
  DownloadPage: TDownloadWizardPage;
  DotNetNeeded: Boolean;

// Check if .NET 8 Desktop Runtime is installed
function IsDotNet8DesktopInstalled(): Boolean;
var
  Version: String;
  Key: String;
begin
  Result := False;
  Key := 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';

  if RegKeyExists(HKLM, Key) then
  begin
    if RegQueryStringValue(HKLM, Key, '8.0.11', Version) or
       RegQueryStringValue(HKLM, Key, '8.0.10', Version) or
       RegQueryStringValue(HKLM, Key, '8.0.9', Version) or
       RegQueryStringValue(HKLM, Key, '8.0.8', Version) or
       RegQueryStringValue(HKLM, Key, '8.0.7', Version) or
       RegQueryStringValue(HKLM, Key, '8.0.6', Version) or
       RegQueryStringValue(HKLM, Key, '8.0.5', Version) or
       RegQueryStringValue(HKLM, Key, '8.0.4', Version) or
       RegQueryStringValue(HKLM, Key, '8.0.3', Version) or
       RegQueryStringValue(HKLM, Key, '8.0.2', Version) or
       RegQueryStringValue(HKLM, Key, '8.0.1', Version) or
       RegQueryStringValue(HKLM, Key, '8.0.0', Version) then
    begin
      Result := True;
    end;
  end;

  // Alternative check using file existence
  if not Result then
  begin
    Result := FileExists(ExpandConstant('{pf}\dotnet\shared\Microsoft.WindowsDesktop.App\8.0.11\WindowsBase.dll')) or
              FileExists(ExpandConstant('{pf}\dotnet\shared\Microsoft.WindowsDesktop.App\8.0.10\WindowsBase.dll')) or
              FileExists(ExpandConstant('{pf}\dotnet\shared\Microsoft.WindowsDesktop.App\8.0.9\WindowsBase.dll')) or
              FileExists(ExpandConstant('{pf}\dotnet\shared\Microsoft.WindowsDesktop.App\8.0.8\WindowsBase.dll')) or
              FileExists(ExpandConstant('{pf}\dotnet\shared\Microsoft.WindowsDesktop.App\8.0.7\WindowsBase.dll')) or
              FileExists(ExpandConstant('{pf}\dotnet\shared\Microsoft.WindowsDesktop.App\8.0.6\WindowsBase.dll')) or
              FileExists(ExpandConstant('{pf}\dotnet\shared\Microsoft.WindowsDesktop.App\8.0.5\WindowsBase.dll'));
  end;
end;

// Alternative check using command line
function CheckDotNetViaCmd(): Boolean;
var
  ResultCode: Integer;
  TempFile: String;
  Output: AnsiString;
begin
  Result := False;
  TempFile := ExpandConstant('{tmp}\dotnet_check.txt');

  if Exec('cmd.exe', '/c dotnet --list-runtimes > "' + TempFile + '" 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    if LoadStringFromFile(TempFile, Output) then
    begin
      Result := Pos('Microsoft.WindowsDesktop.App 8.', String(Output)) > 0;
    end;
    DeleteFile(TempFile);
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;

  // Check for .NET 8 Desktop Runtime
  DotNetNeeded := not IsDotNet8DesktopInstalled();

  if DotNetNeeded then
  begin
    // Double-check using command line method
    DotNetNeeded := not CheckDotNetViaCmd();
  end;

  if DotNetNeeded then
  begin
    if MsgBox('ArtaloBot requires .NET 8 Desktop Runtime which is not installed on your system.' + #13#10 + #13#10 +
              'The installer will download and install it automatically.' + #13#10 + #13#10 +
              'File size: approximately 55 MB' + #13#10 + #13#10 +
              'Do you want to continue?', mbConfirmation, MB_YESNO) = IDNO then
    begin
      Result := False;
    end;
  end;
end;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  if Progress = ProgressMax then
    Log(Format('Successfully downloaded file to {tmp}: %s', [FileName]));
  Result := True;
end;

procedure InitializeWizard();
begin
  // Create download page for .NET runtime
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), @OnDownloadProgress);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode: Integer;
  DotNetInstaller: String;
begin
  Result := True;

  if (CurPageID = wpReady) and DotNetNeeded then
  begin
    DownloadPage.Clear;
    DownloadPage.Add('{#DotNetInstallerURL}', 'windowsdesktop-runtime-{#DotNetVersion}-win-x64.exe', '');
    DownloadPage.Show;

    try
      try
        DownloadPage.Download;

        // Install .NET Runtime
        DotNetInstaller := ExpandConstant('{tmp}\windowsdesktop-runtime-{#DotNetVersion}-win-x64.exe');

        if FileExists(DotNetInstaller) then
        begin
          DownloadPage.SetText('Installing .NET 8 Desktop Runtime...', 'Please wait while .NET 8 is being installed. This may take a few minutes.');
          DownloadPage.SetProgress(0, 100);

          // Run installer with quiet mode
          if not Exec(DotNetInstaller, '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
          begin
            MsgBox('Failed to run .NET installer. Please install .NET 8 Desktop Runtime manually from:' + #13#10 +
                   'https://dotnet.microsoft.com/download/dotnet/8.0', mbError, MB_OK);
            Result := False;
          end
          else if ResultCode <> 0 then
          begin
            // Check common error codes
            if ResultCode = 3010 then
            begin
              // Reboot required
              MsgBox('.NET 8 Desktop Runtime was installed successfully, but a system restart is required.' + #13#10 + #13#10 +
                     'Please restart your computer after installation completes to use ArtaloBot.', mbInformation, MB_OK);
            end
            else if ResultCode = 1602 then
            begin
              MsgBox('.NET installation was cancelled by user. ArtaloBot requires .NET 8 to run.', mbError, MB_OK);
              Result := False;
            end
            else
            begin
              MsgBox('Warning: .NET installer returned code ' + IntToStr(ResultCode) + '.' + #13#10 +
                     'ArtaloBot may not work correctly. If you experience issues, please install .NET 8 Desktop Runtime manually.',
                     mbInformation, MB_OK);
            end;
          end;

          DotNetNeeded := False;
        end
        else
        begin
          MsgBox('Failed to download .NET 8 Desktop Runtime. Please check your internet connection and try again.', mbError, MB_OK);
          Result := False;
        end;
      except
        if DownloadPage.AbortedByUser then
          Log('Download aborted by user.')
        else
          MsgBox('Download failed: ' + GetExceptionMessage, mbError, MB_OK);
        Result := False;
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // Create app data directory
    ForceDirectories(ExpandConstant('{userappdata}\ArtaloBot'));
    ForceDirectories(ExpandConstant('{userappdata}\ArtaloBot\Logs'));
    ForceDirectories(ExpandConstant('{userappdata}\ArtaloBot\Data'));
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DeleteUserData: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    DeleteUserData := MsgBox('Do you want to delete all ArtaloBot user data (chat history, settings, agents)?',
                             mbConfirmation, MB_YESNO);
    if DeleteUserData = IDYES then
    begin
      DelTree(ExpandConstant('{userappdata}\ArtaloBot'), True, True, True);
    end;
  end;
end;
