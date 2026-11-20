#requires -Version 5.1
<#
.SYNOPSIS
    Instala o desinstala la entrada de menú contextual "Abrir con FluentASM IDE"
    para carpetas (Directory y Directory\Background) en HKCU, sin requerir
    permisos de administrador.

.DESCRIPTION
    Este script registra (o elimina) una entrada de menú clásico de Windows
    (accesible con Shift+F10 / "Mostrar más opciones" en Windows 11) que
    abre la carpeta seleccionada directamente en el IDE de FluentASM.

.PARAMETER Instalar
    Crea las claves de registro necesarias para que aparezca la entrada
    "Abrir con FluentASM IDE" al hacer clic derecho sobre una carpeta.

.PARAMETER Desinstalar
    Elimina las claves de registro creadas por este script.

.PARAMETER RutaExe
    Ruta al ejecutable de FluentASM IDE (.exe). Si se omite, el script
    intenta detectar automáticamente fluentasm-ide.exe en las ubicaciones
    habituales del paquete MSIX y en la salida de compilación local.

.EJEMPLO
    .\FluentASM-MenuContextual.ps1 -i
    .\FluentASM-MenuContextual.ps1 -i -path "C:\...\fluentasm-ide.exe"
    .\FluentASM-MenuContextual.ps1 -u
    .\FluentASM-MenuContextual.ps1          # muestra menú de opciones

.NOTAS
    - Usa HKCU (Software\Classes\Directory\shell y Directory\Background\shell),
      por lo que no requiere elevación de privilegios.
    - La entrada aparece en el menú clásico (clic derecho con Shift o
      "Mostrar más opciones" en Windows 11), no en el menú simplificado de
      primer nivel (ese requiere una DLL COM IExplorerCommand).
#>
[CmdletBinding(DefaultParameterSetName = "Menu")]
param(
    [Parameter(ParameterSetName = "Instalar", Mandatory = $false)]
    [Alias('i')]
    [switch] $Instalar,

    [Parameter(ParameterSetName = "Desinstalar", Mandatory = $true)]
    [Alias('u')]
    [switch] $Desinstalar,

    [Parameter(Mandatory = $false)]
    [Alias('path')]
    [string] $RutaExe
)

$ErrorActionPreference = "Stop"

# Rutas de registro (usuario actual, sin necesidad de administrador)
$regRoot   = "HKCU:\Software\Classes"
$dirShell  = "$regRoot\Directory\shell\FluentASM"
$dirBgShell = "$regRoot\Directory\Background\shell\FluentASM"

$menuText  = "Abrir con FluentASM IDE"
$iconValue = "%1"

function Obtener-RutaExePredeterminada {
    # Candidatos típicos del ejecutable empaquetado MSIX y de salida local.
    $candidatos = @(
        # Salida local de compilación (Debug/Release, x64/ARM64)
        (Join-Path $PSScriptRoot "..\src\FluentASM_IDE\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\FluentASM_IDE.exe"),
        (Join-Path $PSScriptRoot "..\src\FluentASM_IDE\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\FluentASM_IDE.exe"),
        # Paquete MSIX instalado (WindowsApps)
        (Join-Path $env:ProgramFiles "WindowsApps\093e96ba-343f-4926-9956-bd1a15e52b29_*\fluentasm-ide.exe"),
        (Join-Path $env:LOCALAPPDATA "Packages\093e96ba-343f-4926-9956-bd1a15e52b29_*\fluentasm-ide.exe"),
        # Alias de registro (app execution alias) si está registrado
        "fluentasm-ide.exe"
    )

    foreach ($c in $candidatos) {
        # Expandir comodines de WindowsApps
        if ($c -like '*\*') {
            $matches = Get-Item -Path $c -ErrorAction SilentlyContinue
            if ($matches) {
                return $matches.FullName
            }
        }
        elseif (Test-Path -Path $c -PathType Leaf) {
            return $c
        }
    }

    return $null
}

function Crear-EntradaMenu {
    param([string] $exePath)

    if (-not (Test-Path -Path $exePath -PathType Leaf)) {
        Write-Error "No se encontró el ejecutable: $exePath"
        return $false
    }

    # Valor por defecto: la ruta del ejecutable con "%1" como parámetro (carpeta)
    $command = "`"$exePath`" `"%1`""

    try {
        # Directory\shell\FluentASM
        if (-not (Test-Path -Path $dirShell)) {
            New-Item -Path $dirShell -Force | Out-Null
        }
        Set-ItemProperty -Path $dirShell -Name "" -Value $menuText -Force
        Set-ItemProperty -Path $dirShell -Name "Icon" -Value $iconValue -Force

        New-Item -Path "$dirShell\command" -Force | Out-Null
        Set-ItemProperty -Path "$dirShell\command" -Name "" -Value $command -Force

        # Directory\Background\shell\FluentASM (clic en fondo de carpeta)
        if (-not (Test-Path -Path $dirBgShell)) {
            New-Item -Path $dirBgShell -Force | Out-Null
        }
        Set-ItemProperty -Path $dirBgShell -Name "" -Value $menuText -Force
        Set-ItemProperty -Path $dirBgShell -Name "Icon" -Value $iconValue -Force

        New-Item -Path "$dirBgShell\command" -Force | Out-Null
        Set-ItemProperty -Path "$dirBgShell\command" -Name "" -Value $command -Force

        Write-Host "Entrada de menú contextual instalada para: $exePath" -ForegroundColor Green
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
        if (Test-Path -Path $dirShell) {
            Remove-Item -Path $dirShell -Recurse -Force -ErrorAction SilentlyContinue
            $eliminado = $true
        }
        if (Test-Path -Path $dirBgShell) {
            Remove-Item -Path $dirBgShell -Recurse -Force -ErrorAction SilentlyContinue
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
    Write-Host "  i) Instalar  - Agrega 'Abrir con FluentASM IDE' al menú contextual"
    Write-Host "  u) Desinstalar - Elimina la entrada del menú contextual"
    Write-Host "  s) Salir"
    Write-Host ""
    Write-Host "  También puede usar parámetros:"
    Write-Host "    -i  (o -Instalar)   Instalar entrada"
    Write-Host "    -u  (o -Desinstalar) Desinstalar entrada"
    Write-Host "    -path <ruta>        Ruta al .exe (opcional)"
    Write-Host ""
}

# --- Ejecución principal ---

# Si no se pasó ningún parámetro, mostrar menú interactivo
if (-not $PSBoundParameters.ContainsKey('Instalar') -and -not $PSBoundParameters.ContainsKey('Desinstalar')) {
    Mostrar-Menu
    $opcion = Read-Host "Seleccione una opción (i/u/s)"

    switch ($opcion) {
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

# Instalar
if ($Instalar) {
    $exe = $RutaExe
    if ([string]::IsNullOrWhiteSpace($exe)) {
        $exe = Obtener-RutaExePredeterminada
    }

    if ([string]::IsNullOrWhiteSpace($exe) -or -not (Test-Path -Path $exe -PathType Leaf)) {
        Write-Error "No se pudo determinar la ruta del ejecutable. Especifíquela con -path <ruta>."
        exit 1
    }

    $ok = Crear-EntradaMenu -exePath $exe
    if (-not $ok) {
        exit 1
    }
}
