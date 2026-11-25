using System.Runtime.InteropServices;
using FluentASM_IDE.Helpers;

namespace FluentASM_IDE.Services;

/// <summary>
/// Selector de carpetas nativo de Win32 (<c>IFileOpenDialog</c> con
/// <c>FOS_PICKFOLDERS</c>).
/// </summary>
/// <remarks>
/// Sustituye a <c>Windows.Storage.Pickers.FolderPicker</c>, que lanza una
/// <c>COMException</c> no controlada en procesos <em>unpackaged</em> elevados
/// (<c>WindowsPackageType=None</c> ejecutado como Administrador). El diálogo
/// nativo funciona igual con o sin identidad de paquete y con cualquier nivel
/// de privilegio, y se muestra modal sobre la ventana que se indique.
/// </remarks>
internal static class NativeFolderPicker
{
    /// <summary>
    /// Margen máximo de espera al delegar el diálogo en el hilo de interfaz.
    /// Es holgado porque el usuario puede permanecer en el diálogo todo el
    /// tiempo que necesite; solo evita un bloqueo infinito si el hilo de
    /// interfaz deja de bombear mensajes.
    /// </summary>
    private static readonly TimeSpan UiDispatchTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Muestra el selector nativo de carpetas y devuelve la ruta física elegida,
    /// o <c>null</c> si el usuario cancela.
    /// </summary>
    /// <param name="ownerHwnd">
    /// Ventana propietaria del diálogo; lo hace modal y lo mantiene por encima
    /// de la ventana principal.
    /// </param>
    /// <param name="initialDirectory">Carpeta inicial opcional; debe existir.</param>
    /// <param name="title">Título opcional del diálogo.</param>
    /// <remarks>
    /// El diálogo se muestra siempre en el hilo de interfaz del IDE (UI/STA).
    /// Si la llamada llega desde otro hilo, se delega en el despachador de la
    /// aplicación y se espera al resultado, de modo que el modelo de hilos
    /// nunca provoque un fallo del proceso.
    /// </remarks>
    /// <exception cref="COMException">El diálogo falló con un HRESULT de error.</exception>
    internal static string? PickFolder(nint ownerHwnd, string? initialDirectory = null, string? title = null)
    {
        Microsoft.UI.Dispatching.DispatcherQueue dispatcher = App.DispatcherQueue;
        if (dispatcher is null || dispatcher.HasThreadAccess)
        {
            return PickFolderCore(ownerHwnd, initialDirectory, title);
        }

        return PickFolderOnDispatcher(dispatcher, ownerHwnd, initialDirectory, title);
    }

    /// <summary>
    /// Delega la creación y el Showing del diálogo en el hilo de interfaz y
    /// devuelve la ruta seleccionada.
    /// </summary>
    private static string? PickFolderOnDispatcher(
        Microsoft.UI.Dispatching.DispatcherQueue dispatcher,
        nint ownerHwnd,
        string? initialDirectory,
        string? title)
    {
        string? selectedPath = null;
        Exception? failure = null;

        using var completed = new ManualResetEventSlim(false);
        bool queued = dispatcher.TryEnqueue(() =>
        {
            try
            {
                selectedPath = PickFolderCore(ownerHwnd, initialDirectory, title);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                completed.Set();
            }
        });

        if (!queued)
        {
            throw new InvalidOperationException(
                "No se pudo encolar el selector nativo de carpetas en el hilo de interfaz.");
        }

        if (!completed.Wait(UiDispatchTimeout))
        {
            throw new TimeoutException(
                "El selector nativo de carpetas no respondió a tiempo desde el hilo de interfaz.");
        }

        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return selectedPath;
    }

    /// <summary>
    /// Implementación del selector: crea el diálogo COM, lo muestra de forma
    /// modal y devuelve la ruta física elegida.
    /// </summary>
    private static string? PickFolderCore(nint ownerHwnd, string? initialDirectory, string? title)
    {
        nint dialogPointer = 0;
        nint initialFolderPointer = 0;
        nint resultPointer = 0;
        nint pathPointer = 0;
        object? dialogObject = null;
        object? shellItemObject = null;

        try
        {
            Guid classId = NativeMethods.ClsidFileOpenDialog;
            Guid dialogInterfaceId = NativeMethods.IidFileOpenDialog;
            int hresult = NativeMethods.CoCreateInstance(
                in classId,
                0,
                NativeMethods.ClsctxInprocServer,
                in dialogInterfaceId,
                out dialogPointer);
            Marshal.ThrowExceptionForHR(hresult);

            dialogObject = Marshal.GetObjectForIUnknown(dialogPointer);
            var dialog = (IFileOpenDialog)dialogObject;

            hresult = dialog.GetOptions(out uint options);
            Marshal.ThrowExceptionForHR(hresult);

            hresult = dialog.SetOptions(
                options
                | NativeMethods.FosPickFolders
                | NativeMethods.FosForceFilesystem
                | NativeMethods.FosPathMustExist);
            Marshal.ThrowExceptionForHR(hresult);

            if (!string.IsNullOrWhiteSpace(title))
            {
                _ = dialog.SetTitle(title);
            }

            if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            {
                Guid shellItemInterfaceId = NativeMethods.IidShellItem;
                hresult = NativeMethods.SHCreateItemFromParsingName(
                    initialDirectory,
                    0,
                    in shellItemInterfaceId,
                    out initialFolderPointer);

                // La carpeta inicial es una mejora de experiencia, no un
                // requisito: si falla, el diálogo abre en su ubicación por defecto.
                if (hresult >= 0 && initialFolderPointer != 0)
                {
                    _ = dialog.SetFolder(initialFolderPointer);
                }
            }

            hresult = dialog.Show(ownerHwnd);
            if (hresult == NativeMethods.HResultCancelled)
            {
                // Cancelación del usuario: no es un error ni se registra.
                return null;
            }

            Marshal.ThrowExceptionForHR(hresult);

            hresult = dialog.GetResult(out resultPointer);
            Marshal.ThrowExceptionForHR(hresult);

            shellItemObject = Marshal.GetObjectForIUnknown(resultPointer);
            var shellItem = (IShellItem)shellItemObject;
            hresult = shellItem.GetDisplayName(NativeMethods.SigdnFilesysPath, out pathPointer);
            Marshal.ThrowExceptionForHR(hresult);

            return Marshal.PtrToStringUni(pathPointer);
        }
        finally
        {
            // Las cadenas y los punteros COM se liberan siempre, también si el
            // diálogo falla a mitad de la ruta.
            if (pathPointer != 0)
            {
                NativeMethods.CoTaskMemFree(pathPointer);
            }

            ReleaseComObject(shellItemObject);
            ReleaseComObject(dialogObject);

            if (resultPointer != 0)
            {
                Marshal.Release(resultPointer);
            }

            if (initialFolderPointer != 0)
            {
                Marshal.Release(initialFolderPointer);
            }

            if (dialogPointer != 0)
            {
                Marshal.Release(dialogPointer);
            }
        }
    }

    /// <summary>
    /// Libera un contenedor COM sin permitir que un fallo de liberación enmascare
    /// el resultado del diálogo.
    /// </summary>
    private static void ReleaseComObject(object? instance)
    {
        if (instance is null || !Marshal.IsComObject(instance))
        {
            return;
        }

        try
        {
            _ = Marshal.FinalReleaseComObject(instance);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // El objeto ya estaba desconectado; no hay nada que liberar.
        }
    }
}
