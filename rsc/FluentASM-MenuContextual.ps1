#requires -Version 5.1
<#
.SYNOPSIS
    Instala o desinstala la entrada de menú contextual "Abrir con FluentASM IDE"
    para carpetas (Directory y Directory\Background) en HKCU, sin requerir
    permisos de administrador.

.PARAMETER Instalar
    Crea las claves de registro para abrir carpetas en FluentASM IDE.

.PARAMETER Desinstalar
    Elimina las claves de registro creadas por este script.

.PARAMETER RutaExe
    Ruta opcional al ejecutable (.exe) de FluentASM IDE.

.EXAMPLE
    .\FluentASM-MenuContextual.ps1 -i
    .\FluentASM-MenuContextual.ps1 -i -path "C:\...\FluentASM_IDE.exe"
    .\FluentASM-MenuContextual.ps1 -u
#>
[CmdletBinding(DefaultParameterSetName = "Menu")]
param(
    [Parameter(ParameterSetName = "Instalar", Mandatory = $false)]
    [Alias('i')]
    [switch] $Instalar,

    [Parameter(ParameterSetName = "Desinstalar", Mandatory = $true)]
    [Alias('u')]
    [switch] $Desinstalar,

    [Parameter(ParameterSetName = "Instalar", Mandatory = $false)]
    [Alias('path')]
    [string] $RutaExe
)

$ErrorActionPreference = "Stop"

$script:regRoot    = "HKCU:\Software\Classes"
$script:dirShell   = "$script:regRoot\Directory\shell\FluentASM"
$script:dirBgShell = "$script:regRoot\Directory\Background\shell\FluentASM"
$script:menuText   = "Abrir con FluentASM IDE"

function Test-ArchivoExiste {
    param([string] $Ruta)
    if ([string]::IsNullOrWhiteSpace($Ruta)) { return $false }
    return (Test-Path -LiteralPath $Ruta -PathType Leaf) -or [System.IO.File]::Exists($Ruta)
}

function Obtener-RutaExePredeterminada {
    # El alias de ejecución es el único lanzador predeterminado válido para la
    # aplicación empaquetada. El .exe de compilación no puede sustituirlo porque
    # no recibe la identidad de paquete que requiere WinUI 3.
    $cmd = Get-Command "fluentasm-ide.exe" -ErrorAction SilentlyContinue
    if ($cmd -and -not [string]::IsNullOrWhiteSpace($cmd.Source)) {
        return $cmd.Source
    }

    $windowsAppsAlias = Join-Path $env:LOCALAPPDATA "Microsoft\WindowsApps\fluentasm-ide.exe"
    if (Test-ArchivoExiste $windowsAppsAlias) {
        return $windowsAppsAlias
    }

    return $null
}

function Crear-EntradaMenu {
    param([string] $ExePath)

    if (-not (Test-ArchivoExiste $ExePath)) {
        Write-Error "No se encontró el ejecutable: $ExePath"
        return $false
    }

    $iconValue = "`"$ExePath`",0"
    $cmdDir    = "`"$ExePath`" `"%1`""
    $cmdBg     = "`"$ExePath`" `"%V`""

    try {
        # Clic derecho sobre una carpeta
        $keyShell = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("Software\Classes\Directory\shell\FluentASM")
        $keyShell.SetValue('', $script:menuText)
        $keyShell.SetValue('Icon', $iconValue)

        $keyCmd = $keyShell.CreateSubKey('command')
        $keyCmd.SetValue('', $cmdDir)
        $keyCmd.Close()
        $keyShell.Close()

        # Clic derecho en el fondo dentro de una carpeta (requiere %V)
        $keyBgShell = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("Software\Classes\Directory\Background\shell\FluentASM")
        $keyBgShell.SetValue('', $script:menuText)
        $keyBgShell.SetValue('Icon', $iconValue)

        $keyBgCmd = $keyBgShell.CreateSubKey('command')
        $keyBgCmd.SetValue('', $cmdBg)
        $keyBgCmd.Close()
        $keyBgShell.Close()

        Write-Host "Entrada de menú contextual instalada para: $ExePath" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Error "Error al instalar la entrada de menú: $_"
        return $false
    }
}

function Eliminar-EntradaMenu {
    $eliminado = $false

    try {
        if (Test-Path -LiteralPath $script:dirShell) {
            Remove-Item -LiteralPath $script:dirShell -Recurse -Force
            $eliminado = $true
        }
        if (Test-Path -LiteralPath $script:dirBgShell) {
            Remove-Item -LiteralPath $script:dirBgShell -Recurse -Force
            $eliminado = $true
        }

        if ($eliminado) {
            Write-Host "Entrada de menú contextual desinstalada." -ForegroundColor Green
        }
        else {
            Write-Host "No se encontró ninguna entrada de menú para desinstalar." -ForegroundColor Yellow
        }
    }
    catch {
        Write-Error "Error al desinstalar la entrada de menú: $_"
    }
}

function Mostrar-Menu {
    Write-Host ""
    Write-Host "================================================" -ForegroundColor Cyan
    Write-Host "  FluentASM IDE - Menú Contextual para Carpetas" -ForegroundColor Cyan
    Write-Host "================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  i) Instalar    - Agrega 'Abrir con FluentASM IDE' al menú contextual"
    Write-Host "  u) Desinstalar - Elimina la entrada del menú contextual"
    Write-Host "  s) Salir"
    Write-Host ""
    Write-Host "  Parámetros:"
    Write-Host "    -i  (o -Instalar)    Instalar entrada"
    Write-Host "    -u  (o -Desinstalar) Desinstalar entrada"
    Write-Host "    -path <ruta>         Ruta al .exe (opcional)"
    Write-Host ""
}

# --- Ejecución principal ---

if (-not $Instalar -and -not $Desinstalar) {
    Mostrar-Menu
    $opcion = Read-Host "Seleccione una opción (i/u/s)"

    switch ($opcion.Trim().ToLowerInvariant()) {
        'i' { $Instalar = $true }
        'u' { $Desinstalar = $true }
        's' { exit 0 }
        default {
            Write-Host "Opción no válida." -ForegroundColor Red
            exit 1
        }
    }
}

if ($Desinstalar) {
    Eliminar-EntradaMenu
    exit 0
}

if ($Instalar) {
    $exe = $RutaExe
    if ([string]::IsNullOrWhiteSpace($exe)) {
        $exe = Obtener-RutaExePredeterminada
    }

    if (-not (Test-ArchivoExiste $exe)) {
        Write-Error "No se encontró el alias 'fluentasm-ide.exe'. Despliega o instala el MSIX de FluentASM IDE o indica una ruta explícita con -path <ruta>."
        exit 1
    }

    if (-not (Crear-EntradaMenu -ExePath $exe)) {
        exit 1
    }
}
