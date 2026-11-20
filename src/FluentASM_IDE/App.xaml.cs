using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FluentASM_IDE;
/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// The main application window. Use <c>App.Window</c> from any class that needs
    /// the window reference (for dialogs, pickers, interop, etc.).
    /// </summary>
    public static Window Window { get; private set; } = null!;

    /// <summary>
    /// The UI thread dispatcher. Use <c>App.DispatcherQueue</c> to marshal calls
    /// to the UI thread. Fully qualified to avoid CS0104 ambiguity with
    /// <see cref="Windows.System.DispatcherQueue"/>.
    /// </summary>
    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

    /// <summary>
    /// The native window handle (HWND). Use for file pickers,
    /// <c>DataTransferManager</c>, and any WinRT interop that requires
    /// <c>InitializeWithWindow</c>.
    /// </summary>
    public static nint WindowHandle =>
        WinRT.Interop.WindowNative.GetWindowHandle(Window);

    /// <summary>
    /// Argumentos de línea de comandos pasados al lanzamiento (por ejemplo,
    /// ruta de carpeta, archivo <c>.fproj</c> o archivo <c>.fasm</c>).
    /// </summary>
    public static IReadOnlyList<string> LaunchArguments { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// Ruta activada por asociación de archivo (clic derecho → Abrir con FluentASM),
    /// o <c>null</c> si no hay activación por archivo.
    /// </summary>
    public static string? ActivatedFilePath { get; private set; }

    /// <summary>
    /// Initializes the singleton application object.
    /// </summary>
    public App()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        // Recopilar argumentos de línea de comandos.
        var argsList = new List<string>();
        try
        {
            argsList.AddRange(Environment.GetCommandLineArgs());
        }
        catch { }

        // Si hay activación por archivo (clic derecho → Abrir con FluentASM),
        // extraer la ruta del primer archivo desde IFileActivatedEventArgs.
        if (args is object activatedArgs && activatedArgs is Windows.ApplicationModel.Activation.IFileActivatedEventArgs fileArgs)
        {
            var file = fileArgs.Files.FirstOrDefault();
            if (file is not null)
            {
                var path = file.Path;
                argsList.Add(path);
                ActivatedFilePath = path;
            }
        }
        else if (!string.IsNullOrEmpty(args.Arguments))
        {
            argsList.Add(args.Arguments);
            ActivatedFilePath = args.Arguments;
        }

        LaunchArguments = argsList;

        Window.Activate();
    }
}


