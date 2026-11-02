Unicode true
ManifestDPIAware true
SetCompressor /SOLID lzma
RequestExecutionLevel user

!include "MUI2.nsh"
!include "FileFunc.nsh"

!ifndef VERSION
  !define VERSION "1.0.0"
!endif
!ifndef VERSION_CORTA
  !define VERSION_CORTA "1.0"
!endif
!ifndef FUENTE
  !error "Falta /DFUENTE con la carpeta publicada"
!endif
!ifndef SALIDA
  !error "Falta /DSALIDA con la ruta del instalador"
!endif

!define PRODUCTO "Mi Gestor BD Accesible"
!define EJECUTABLE "MiGestorBDAccesible.exe"
!define AUTOR "Wilmer Rodríguez Vega"
!define CLAVE_DESINSTALAR "Software\Microsoft\Windows\CurrentVersion\Uninstall\MiGestorBDAccesible"
!define CLAVE_WEBVIEW2 "Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"

Name "${PRODUCTO}"
OutFile "${SALIDA}"
InstallDir "$LOCALAPPDATA\Programs\MiGestorBDAccesible"
InstallDirRegKey HKCU "${CLAVE_DESINSTALAR}" "InstallLocation"
BrandingText "${PRODUCTO} ${VERSION_CORTA}"

VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=1034 "ProductName" "${PRODUCTO}"
VIAddVersionKey /LANG=1034 "ProductVersion" "${VERSION_CORTA}"
VIAddVersionKey /LANG=1034 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1034 "FileDescription" "Instalador de ${PRODUCTO}"
VIAddVersionKey /LANG=1034 "CompanyName" "${AUTOR}"
VIAddVersionKey /LANG=1034 "LegalCopyright" "© 2026 ${AUTOR}"

!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "Instalación de ${PRODUCTO} ${VERSION_CORTA}"
!define MUI_WELCOMEPAGE_TEXT "Este asistente instalará ${PRODUCTO}, un gestor de bases de datos pensado para usarse por completo con teclado y lector de pantalla.$\r$\n$\r$\nSe instala solo para tu usuario y no necesita permisos de administrador.$\r$\n$\r$\nPulsa Siguiente para continuar."
!define MUI_FINISHPAGE_TITLE "${PRODUCTO} está instalado"
!define MUI_FINISHPAGE_TEXT "En el menú Inicio encontrarás ${PRODUCTO} y su manual de usuario."
!define MUI_FINISHPAGE_RUN "$INSTDIR\${EJECUTABLE}"
!define MUI_FINISHPAGE_RUN_TEXT "Abrir ${PRODUCTO}"
!define MUI_FINISHPAGE_SHOWREADME ""
!define MUI_FINISHPAGE_SHOWREADME_TEXT "Crear un acceso directo en el escritorio"
!define MUI_FINISHPAGE_SHOWREADME_NOTCHECKED
!define MUI_FINISHPAGE_SHOWREADME_FUNCTION CrearAccesoEscritorio

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "Spanish"

Function .onInit
  Call ComprobarAbierto
FunctionEnd

Function un.onInit
  Call un.ComprobarAbierto
FunctionEnd

!macro COMPROBAR_ABIERTO PREFIJO
Function ${PREFIJO}ComprobarAbierto
  abierto:
    FindWindow $0 "" "${PRODUCTO}"
    StrCmp $0 0 listo
    MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "${PRODUCTO} está abierto. Ciérralo y pulsa Reintentar para continuar." /SD IDCANCEL IDRETRY abierto
    Abort
  listo:
FunctionEnd
!macroend

!insertmacro COMPROBAR_ABIERTO ""
!insertmacro COMPROBAR_ABIERTO "un."

Function ComprobarWebView2
  ReadRegStr $0 HKLM "SOFTWARE\WOW6432Node\${CLAVE_WEBVIEW2}" "pv"
  StrCmp $0 "" 0 hay
  ReadRegStr $0 HKCU "Software\${CLAVE_WEBVIEW2}" "pv"
  StrCmp $0 "" falta
  hay:
    StrCmp $0 "0.0.0.0" falta listo
  falta:
    MessageBox MB_YESNO|MB_ICONINFORMATION "Para funcionar, ${PRODUCTO} necesita WebView2, un componente gratuito de Microsoft que no está en este equipo.$\r$\n$\r$\n¿Quieres abrir la página de descarga? Instálalo y después abre ${PRODUCTO}." /SD IDNO IDNO listo
    ExecShell "open" "https://developer.microsoft.com/microsoft-edge/webview2/"
  listo:
FunctionEnd

Function CrearAccesoEscritorio
  CreateShortcut "$DESKTOP\${PRODUCTO}.lnk" "$INSTDIR\${EJECUTABLE}"
FunctionEnd

Section "Programa"
  IfFileExists "$INSTDIR\${EJECUTABLE}" 0 copiar
    RMDir /r "$INSTDIR"
  copiar:
  SetOutPath "$INSTDIR"
  File /r "${FUENTE}\*.*"

  WriteUninstaller "$INSTDIR\Desinstalar.exe"

  CreateShortcut "$SMPROGRAMS\${PRODUCTO}.lnk" "$INSTDIR\${EJECUTABLE}" "" "" 0 SW_SHOWNORMAL "" "Gestor de bases de datos accesible"
  CreateShortcut "$SMPROGRAMS\Manual de ${PRODUCTO}.lnk" "$INSTDIR\Manual.html"

  WriteRegStr HKCU "${CLAVE_DESINSTALAR}" "DisplayName" "${PRODUCTO}"
  WriteRegStr HKCU "${CLAVE_DESINSTALAR}" "DisplayVersion" "${VERSION_CORTA}"
  WriteRegStr HKCU "${CLAVE_DESINSTALAR}" "Publisher" "${AUTOR}"
  WriteRegStr HKCU "${CLAVE_DESINSTALAR}" "URLInfoAbout" "https://wilmer-rv.com"
  WriteRegStr HKCU "${CLAVE_DESINSTALAR}" "DisplayIcon" "$INSTDIR\${EJECUTABLE}"
  WriteRegStr HKCU "${CLAVE_DESINSTALAR}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${CLAVE_DESINSTALAR}" "UninstallString" '"$INSTDIR\Desinstalar.exe"'
  WriteRegStr HKCU "${CLAVE_DESINSTALAR}" "QuietUninstallString" '"$INSTDIR\Desinstalar.exe" /S'
  WriteRegDWORD HKCU "${CLAVE_DESINSTALAR}" "NoModify" 1
  WriteRegDWORD HKCU "${CLAVE_DESINSTALAR}" "NoRepair" 1

  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKCU "${CLAVE_DESINSTALAR}" "EstimatedSize" "$0"

  Call ComprobarWebView2
SectionEnd

Section "Uninstall"
  Delete "$SMPROGRAMS\${PRODUCTO}.lnk"
  Delete "$SMPROGRAMS\Manual de ${PRODUCTO}.lnk"
  Delete "$DESKTOP\${PRODUCTO}.lnk"
  DeleteRegKey HKCU "${CLAVE_DESINSTALAR}"

  IfFileExists "$INSTDIR\${EJECUTABLE}" 0 fin
    RMDir /r "$INSTDIR"
  fin:
SectionEnd
