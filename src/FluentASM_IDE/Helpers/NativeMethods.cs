// Copyright (c) FluentASM. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;

namespace FluentASM_IDE.Helpers;

/// <summary>
/// Proporciona firmas P/Invoke para APIs de Windows usadas por el IDE.
/// Migrado de <c>DllImport</c> a <c>LibraryImport</c> (.NET 7+) para mejor rendimiento y diagnósticos en tiempo de compilación.
/// </summary>
internal static partial class NativeMethods
{
    // =====================================================================
    // Constantes (System Metrics / DWM)
    // =====================================================================

    // GetSystemMetrics indices
    internal const int SmCyCaption = 4;
    internal const int SmCxBorder = 5;
    internal const int SmCyBorder = 6;
    internal const int SmCxMin = 28;
    internal const int SmCxSize = 30;
    internal const int SmCxSizeFrame = 32;
    internal const int SmCySizeFrame = 33;
    internal const int SmCxMinTrack = 34;
    internal const int SmCyMinTrack = 35;
    internal const int SmCxPaddedBorder = 92;

    // Monitor DPI Type
    internal const int MdtEffectiveDpi = 0;

    // DWM Window Attributes
    internal const int DwmwaExtendedFrameBounds = 9;

    // =====================================================================
    // Estructuras
    // =====================================================================

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    // =====================================================================
    // user32.dll
    // =====================================================================

    /// <summary>
    /// Recupera la posición del cursor en coordenadas de pantalla.
    /// </summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out NativePoint point);

    /// <summary>
    /// Recupera las dimensiones del rectángulo delimitador de la ventana especificada.
    /// </summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(nint hwnd, out NativeRect rect);

    /// <summary>
    /// Obtiene el DPI de la ventana especificada.
    /// </summary>
    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForWindow(nint hwnd);

    /// <summary>
    /// Recupera el valor de una métrica del sistema para el DPI especificado.
    /// </summary>
    [LibraryImport("user32.dll")]
    internal static partial int GetSystemMetricsForDpi(int nIndex, uint dpi);

    // =====================================================================
    // shcore.dll
    // =====================================================================

    /// <summary>
    /// Obtiene el DPI del monitor especificado.
    /// </summary>
    [LibraryImport("shcore.dll")]
    internal static partial int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    // =====================================================================
    // dwmapi.dll
    // =====================================================================

    /// <summary>
    /// Recupera el valor de un atributo de ventana DWM.
    /// </summary>
    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out NativeRect pvAttribute, int cbAttribute);
}