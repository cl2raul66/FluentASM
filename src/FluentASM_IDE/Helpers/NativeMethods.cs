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
    // Constantes COM / Shell (selector nativo de carpetas)
    // =====================================================================

    /// <summary>Contexto de activación COM en el proceso llamador.</summary>
    internal const uint ClsctxInprocServer = 0x1;

    /// <summary>CLSID del diálogo de apertura de archivos y carpetas.</summary>
    internal static readonly Guid ClsidFileOpenDialog = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");

    /// <summary>IID de <see cref="IFileOpenDialog"/>.</summary>
    internal static readonly Guid IidFileOpenDialog = new("D57C7288-D4AD-4768-BE02-9D969532D960");

    /// <summary>IID de <see cref="IShellItem"/>.</summary>
    internal static readonly Guid IidShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    /// <summary>Permite seleccionar carpetas en lugar de archivos (<c>FOS_PICKFOLDERS</c>).</summary>
    internal const uint FosPickFolders = 0x20;

    /// <summary>Restringe la selección al sistema de archivos (<c>FOS_FORCEFILESYSTEM</c>).</summary>
    internal const uint FosForceFilesystem = 0x40;

    /// <summary>Exige que la ruta seleccionada exista (<c>FOS_PATHMUSTEXIST</c>).</summary>
    internal const uint FosPathMustExist = 0x800;

    /// <summary>Solicita la ruta física al consultar el nombre de un elemento (<c>SIGDN_FILESYSPATH</c>).</summary>
    internal const uint SigdnFilesysPath = 0x80058000;

    /// <summary>HRESULT que devuelve el diálogo cuando el usuario cancela (<c>ERROR_CANCELLED</c>).</summary>
    internal const int HResultCancelled = unchecked((int)0x800704C7);

    // =====================================================================
    // ole32.dll / shell32.dll — selector nativo de carpetas
    // =====================================================================

    /// <summary>
    /// Crea una instancia del objeto COM indicado. Se usa para obtener
    /// <c>IFileOpenDialog</c> sin depender de <c>Windows.Storage.Pickers</c>,
    /// que falla en procesos <em>unpackaged</em> elevados.
    /// </summary>
    [LibraryImport("ole32.dll")]
    internal static partial int CoCreateInstance(
        in Guid rclsid,
        nint pUnkOuter,
        uint dwClsContext,
        in Guid riid,
        out nint ppv);

    /// <summary>
    /// Crea un <c>IShellItem</c> a partir de una ruta, necesario para fijar la
    /// carpeta inicial del diálogo.
    /// </summary>
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int SHCreateItemFromParsingName(
        string pszPath,
        nint pbc,
        in Guid riid,
        out nint ppv);

    /// <summary>Libera memoria asignada con el asignador de tareas COM.</summary>
    [LibraryImport("ole32.dll")]
    internal static partial void CoTaskMemFree(nint pv);

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

/// <summary>
/// Interfaz COM mínima de <c>IFileOpenDialog</c> (hereda de <c>IModalWindow</c> y
/// <c>IFileDialog</c>). El orden de los métodos replica la tabla virtual nativa:
/// no reordenar ni omitir miembros intermedios.
/// </summary>
[ComImport]
[Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFileOpenDialog
{
    /// <summary>Muestra el diálogo de forma modal sobre la ventana indicada.</summary>
    [PreserveSig]
    int Show(nint hwndOwner);

    /// <summary>Establece los filtros de tipo de archivo.</summary>
    [PreserveSig]
    int SetFileTypes(uint cFileTypes, nint rgFilterSpec);

    /// <summary>Selecciona un filtro por índice.</summary>
    [PreserveSig]
    int SetFileTypeIndex(uint iFileType);

    /// <summary>Recupera el índice del filtro activo.</summary>
    [PreserveSig]
    int GetFileTypeIndex(out uint piFileType);

    /// <summary>Registra un receptor de eventos del diálogo.</summary>
    [PreserveSig]
    int Advise(nint pfde, out uint pdwCookie);

    /// <summary>Anula el registro de un receptor de eventos.</summary>
    [PreserveSig]
    int Unadvise(uint dwCookie);

    /// <summary>Establece las opciones del diálogo.</summary>
    [PreserveSig]
    int SetOptions(uint fos);

    /// <summary>Recupera las opciones vigentes del diálogo.</summary>
    [PreserveSig]
    int GetOptions(out uint pfos);

    /// <summary>Establece la carpeta predeterminada.</summary>
    [PreserveSig]
    int SetDefaultFolder(nint psi);

    /// <summary>Establece la carpeta que se muestra al abrir el diálogo.</summary>
    [PreserveSig]
    int SetFolder(nint psi);

    /// <summary>Recupera la carpeta actual del diálogo.</summary>
    [PreserveSig]
    int GetFolder(out nint ppsi);

    /// <summary>Recupera la selección actual del diálogo.</summary>
    [PreserveSig]
    int GetCurrentSelection(out nint ppsi);

    /// <summary>Establece el nombre de archivo inicial.</summary>
    [PreserveSig]
    int SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);

    /// <summary>Recupera el nombre de archivo escrito por el usuario.</summary>
    [PreserveSig]
    int GetFileName(out nint pszName);

    /// <summary>Establece el título del diálogo.</summary>
    [PreserveSig]
    int SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);

    /// <summary>Establece la etiqueta del botón de aceptar.</summary>
    [PreserveSig]
    int SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);

    /// <summary>Establece la etiqueta del campo de nombre de archivo.</summary>
    [PreserveSig]
    int SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);

    /// <summary>Recupera el elemento elegido por el usuario.</summary>
    [PreserveSig]
    int GetResult(out nint ppsi);

    /// <summary>Añade una ubicación a la barra lateral del diálogo.</summary>
    [PreserveSig]
    int AddPlace(nint psi, int fdap);

    /// <summary>Establece la extensión predeterminada.</summary>
    [PreserveSig]
    int SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);

    /// <summary>Cierra el diálogo con el código indicado.</summary>
    [PreserveSig]
    int Close(int hr);

    /// <summary>Establece un GUID de cliente para recordar el estado del diálogo.</summary>
    [PreserveSig]
    int SetClientGuid(in Guid guid);

    /// <summary>Borra el estado persistido del cliente.</summary>
    [PreserveSig]
    int ClearClientData();

    /// <summary>Establece un filtro de elementos.</summary>
    [PreserveSig]
    int SetFilter(nint pFilter);

    /// <summary>Recupera todos los elementos seleccionados (solo <c>IFileOpenDialog</c>).</summary>
    [PreserveSig]
    int GetResults(out nint ppenum);

    /// <summary>Recupera los elementos seleccionados (solo <c>IFileOpenDialog</c>).</summary>
    [PreserveSig]
    int GetSelectedItems(out nint ppsai);
}

/// <summary>
/// Interfaz COM mínima de <c>IShellItem</c>, usada para obtener la ruta física
/// del elemento elegido en el diálogo.
/// </summary>
[ComImport]
[Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    /// <summary>Vincula el elemento a un manejador concreto.</summary>
    [PreserveSig]
    int BindToHandler(nint pbc, in Guid bhid, in Guid riid, out nint ppv);

    /// <summary>Recupera el elemento padre.</summary>
    [PreserveSig]
    int GetParent(out nint ppsi);

    /// <summary>Recupera el nombre para mostrar en el formato indicado.</summary>
    [PreserveSig]
    int GetDisplayName(uint sigdnName, out nint ppszName);

    /// <summary>Recupera los atributos del elemento.</summary>
    [PreserveSig]
    int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

    /// <summary>Compara el elemento con otro.</summary>
    [PreserveSig]
    int Compare(nint psi, uint hint, out int piOrder);
}