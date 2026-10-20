; Per-user, self-contained installation. The payload and removal list come from Publish.ps1.
Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
Name "Time Tracker"
OutFile "${OUTPUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\TimeTracker"
InstallDirRegKey HKCU "Software\TimeTracker" "InstallDirectory"
RequestExecutionLevel user
SetCompressor /SOLID lzma
VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey "ProductName" "Time Tracker"
VIAddVersionKey "FileDescription" "Time Tracker Windows installer"
VIAddVersionKey "FileVersion" "${APP_VERSION}"
VIAddVersionKey "LegalCopyright" "MIT licensed — R-Valdina"
!define MUI_ICON "..\src\TimeTracker.Desktop\Assets\time-tracker.ico"
!define MUI_UNICON "..\src\TimeTracker.Desktop\Assets\time-tracker.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TEXT "Track time on development cards with an offline Windows app.$\r$\n$\r$\nSetup includes the runtime, README, and installation guide. No Git, Visual Studio, .NET installation, account, or administrator access is needed."
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "${SOURCE_DIRECTORY}\LICENSE"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\TimeTracker.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Open Time Tracker"
!define MUI_FINISHPAGE_SHOWREADME "$INSTDIR\README.txt"
!define MUI_FINISHPAGE_SHOWREADME_TEXT "Read the usage and installation guide"
!define MUI_FINISHPAGE_SHOWREADME_NOTCHECKED
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

; Never terminate active timers as part of setup or removal.
!macro RequireClosedTracker
    FindWindow $0 "" "Time Tracker"
    ${If} $0 != 0
        MessageBox MB_OK|MB_ICONEXCLAMATION "Close Time Tracker before installing, updating, or uninstalling. Your saved trackers will be kept."
        Abort
    ${EndIf}
!macroend
Function .onInit
    ${IfNot} ${RunningX64}
        MessageBox MB_OK|MB_ICONSTOP "This release requires 64-bit Windows."
        Abort
    ${EndIf}
    !insertmacro RequireClosedTracker
FunctionEnd
Function un.onInit
    !insertmacro RequireClosedTracker
FunctionEnd

Section "Time Tracker" MainSection
    SetShellVarContext current
    SetRegView 64
    ; Let the previous version remove its exact payload before replacing it, retaining user data.
    ${If} ${FileExists} "$INSTDIR\Uninstall.exe"
        ExecWait '$"$INSTDIR\Uninstall.exe$" /S _?=$INSTDIR' $0
        ${If} $0 != 0
            MessageBox MB_OK|MB_ICONSTOP "The previous version could not be removed. Setup has stopped."
            Abort
        ${EndIf}
        Delete "$INSTDIR\Uninstall.exe"
    ${EndIf}
    SetOutPath "$INSTDIR"
    File /r "${SOURCE_DIRECTORY}\*.*"
    WriteUninstaller "$INSTDIR\Uninstall.exe"
    CreateShortcut "$DESKTOP\Time Tracker.lnk" "$INSTDIR\TimeTracker.exe" "" "$INSTDIR\TimeTracker.exe" 0
    CreateShortcut "$SMPROGRAMS\Time Tracker.lnk" "$INSTDIR\TimeTracker.exe" "" "$INSTDIR\TimeTracker.exe" 0
    WriteRegStr HKCU "Software\TimeTracker" "InstallDirectory" "$INSTDIR"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\TimeTracker" "DisplayName" "Time Tracker"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\TimeTracker" "DisplayVersion" "${APP_VERSION}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\TimeTracker" "Publisher" "R-Valdina"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\TimeTracker" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\TimeTracker" "DisplayIcon" "$INSTDIR\TimeTracker.exe"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\TimeTracker" "UninstallString" '$"$INSTDIR\Uninstall.exe$"'
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\TimeTracker" "QuietUninstallString" '$"$INSTDIR\Uninstall.exe$" /S'
    WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\TimeTracker" "NoModify" 1
    WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\TimeTracker" "NoRepair" 1
SectionEnd

Section "Uninstall"
    SetShellVarContext current
    SetRegView 64
    ; Remove only files shipped by this version, not arbitrary files in a chosen installation folder.
    !include "${UNINSTALL_MANIFEST}"
    Delete "$INSTDIR\Uninstall.exe"
    Delete "$DESKTOP\Time Tracker.lnk"
    Delete "$SMPROGRAMS\Time Tracker.lnk"
    DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\TimeTracker"
    DeleteRegKey HKCU "Software\TimeTracker"
    RMDir "$INSTDIR"
    ; Tracker data in $LOCALAPPDATA\TimeTracker is intentionally retained.
SectionEnd
