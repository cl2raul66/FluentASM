using System;
using System.Collections.Generic;
using System.Linq;
using FluentASM.Lsp.Diagnostics;
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
    /// Registro de Sucesos compartido por la ventana y el resto del IDE. Una
    /// única instancia evita aperturas concurrentes del mismo archivo JSONL.
    /// </summary>
    public static IncidentRecorder Incidents { get; } = new();

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

    private static string CleanLaunchArgument(string? argument) =>
        (argument ?? string.Empty).Trim().Trim('"');

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

        // Recopilar argumentos de línea de comandos. El índice 0 es el propio
        // ejecutable y no debe interpretarse como una ruta de proyecto.
        var argsList = new List<string>();
        try
        {
            string[] commandLineArgs = Environment.GetCommandLineArgs();
            for (int index = 1; index < commandLineArgs.Length; index++)
            {
                string cleaned = CleanLaunchArgument(commandLineArgs[index]);
                if (cleaned.Length > 0 && !argsList.Contains(cleaned, StringComparer.OrdinalIgnoreCase))
                {
                    argsList.Add(cleaned);
                }
            }
        }
        catch { }

        // La activación por archivo de Windows App SDK se expone mediante
        // AppInstance, no mediante LaunchActivatedEventArgs.
        try
        {
            var activatedArgs = Microsoft.Windows.AppLifecycle.AppInstance
                .GetCurrent()
                .GetActivatedEventArgs();

            if (activatedArgs.Kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.File
                && activatedArgs.Data is Windows.ApplicationModel.Activation.IFileActivatedEventArgs fileArgs)
            {
                var file = fileArgs.Files.FirstOrDefault();
                string path = CleanLaunchArgument(file?.Path);
                if (path.Length > 0)
                {
                    ActivatedFilePath = path;
                    if (!argsList.Contains(path, StringComparer.OrdinalIgnoreCase))
                    {
                        argsList.Add(path);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"No se pudo leer la activación enriquecida: {exception}");
        }

        // LaunchActivatedEventArgs.Arguments también puede contener la ruta
        // recibida por el alias de ejecución; normalizar comillas y espacios.
        string launchArgument = CleanLaunchArgument(args.Arguments);
        if (launchArgument.Length > 0 && !argsList.Contains(launchArgument, StringComparer.OrdinalIgnoreCase))
        {
            argsList.Add(launchArgument);
        }

        LaunchArguments = argsList;

        Window.Activate();
    }
}


