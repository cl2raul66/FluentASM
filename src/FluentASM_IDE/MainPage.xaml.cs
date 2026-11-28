using System;
using FluentASM.Lsp.Diagnostics;
using FluentASM_IDE.Models;
using FluentASM_IDE.Services;
using FluentASM_IDE.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace FluentASM_IDE;

/// <summary>
/// Página del <em>shell</em>. Coordina los atajos globales, la gestión de
/// proyecto y los paneles externos (dock de estado y paleta de comandos).
/// <para>
/// El documento, el cursor, el resaltado, los adornos y el IntelliSense viven
/// en el componente de edición <see cref="Controls.CodeEditorControl"/>, que
/// es independiente del shell.
/// </para>
/// </summary>
public sealed partial class MainPage : Page
{
    /// <summary>ViewModel del shell.</summary>
    public MainPageViewModel ViewModel { get; } =
        App.Services.GetRequiredService<MainPageViewModel>();

    private readonly ProjectService _projectService;
    private readonly IncidentRecorder _incidentRecorder;
    private readonly ISettingsService _settingsService;
    private readonly IFolderPickerService _folderPickerService;
    private readonly IDialogService _dialogService;

    public MainPage()
    {
        InitializeComponent();

        _projectService = ViewModel.ProjectService;
        _incidentRecorder = App.Services.GetRequiredService<IncidentRecorder>();
        _settingsService = App.Services.GetRequiredService<ISettingsService>();
        _folderPickerService = App.Services.GetRequiredService<IFolderPickerService>();
        _dialogService = App.Services.GetRequiredService<IDialogService>();

        ViewModel.OpenProjectRequested += OnOpenProjectRequested;
        ViewModel.SaveRequested += OnSaveRequested;
        ViewModel.PaletteOpened += OnPaletteOpened;

        ActualThemeChanged += OnActualThemeChanged;
        Loaded += MainPage_Loaded;
    }

    #region Startup

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        bool isDark = ActualTheme is not ElementTheme.Light;
        ViewModel.UpdateTheme(isDark);

        // Procesar argumentos de lanzamiento y activación por archivo.
        ProcessLaunchArguments();

        // Si no se ha abierto ningún proyecto, el editor muestra el ejemplo
        // en memoria.
        if (!ViewModel.Editor.HasOpenDocument)
        {
            ViewModel.Editor.LoadInitialSample();
        }

        // Asegurar foco para atajos de teclado de página (Ctrl+O, Ctrl+P, etc.).
        this.Focus(FocusState.Programmatic);
        this.GotFocus += (_, _) =>
        {
            // GotFocus se emite de forma asíncrona y se propaga desde
            // cualquier descendiente que reciba el foco. Con la paleta
            // abierta, el foco pertenece a su caja de búsqueda: sin esta
            // guarda la página devolvía el foco al editor en cuanto el
            // evento llegaba hasta ella y la paleta perdía el foco.
            if (ViewModel.IsCommandPaletteOpen)
            {
                return;
            }

            ViewModel.Editor.RequestFocus();
        };
    }

    private void OnActualThemeChanged(FrameworkElement sender, object _)
        => ViewModel.UpdateTheme(sender.ActualTheme != ElementTheme.Light);

    private void ProcessLaunchArguments()
    {
        // Activación por archivo (clic derecho → Abrir con FluentASM).
        if (App.ActivatedFilePath is { } activatedPath)
        {
            if (TryGetExistingPath(activatedPath, out string activatedFullPath))
            {
                OpenProjectOrFolder(activatedFullPath);
            }
            else
            {
                ViewModel.StatusText = $"No se encontró la ruta activada: {activatedPath}";
            }

            return;
        }

        // Argumentos de línea de comandos. Los flags del sistema y los argumentos
        // que no correspondan a una ruta existente se ignoran sin interrumpir el arranque.
        foreach (var arg in App.LaunchArguments)
        {
            if (!TryGetExistingPath(arg, out string fullPath))
            {
                continue;
            }

            if (Directory.Exists(fullPath))
            {
                OpenProjectOrFolder(fullPath);
                return;
            }

            var extension = Path.GetExtension(fullPath).ToLowerInvariant();
            if (extension == ".fproj" || extension == ".fasm")
            {
                OpenProjectOrFolder(fullPath);
                return;
            }
        }
    }

    #endregion

    #region Project management

    private void OnOpenProjectRequested() => OpenFolderPickerAsync();

    private void OnSaveRequested() => SaveCurrentFile();

    private async void OpenProjectOrFolder(string path)
    {
        try
        {
            await OpenProjectOrFolderCoreAsync(path);
        }
        catch (Exception ex)
        {
            _incidentRecorder.RecordException(ex, "MainPage.OpenProjectOrFolder", path);
            ViewModel.StatusText = $"No se pudo abrir: {path}";
        }
    }

    private async Task OpenProjectOrFolderCoreAsync(string path)
    {
        // Si hay cambios no guardados, pedir confirmación antes de abrir otro proyecto.
        if (ViewModel.HasUnsavedChanges)
        {
            bool discardChanges = await _dialogService.ConfirmAsync(
                "Cambios sin guardar",
                "Hay cambios sin guardar en el archivo fuente. ¿Descartarlos y abrir otro proyecto?",
                "Descartar",
                "Cancelar");

            if (!discardChanges)
            {
                return;
            }
        }

        if (!TryGetFullPath(path, out string fullPath))
        {
            ViewModel.StatusText = $"Ruta no válida: {path}";
            return;
        }

        if (Directory.Exists(fullPath))
        {
            var result = _projectService.OpenFolder(fullPath);
            ApplyProjectOpenResult(result, fullPath);
        }
        else if (File.Exists(fullPath))
        {
            var extension = Path.GetExtension(fullPath).ToLowerInvariant();
            if (extension == ".fproj")
            {
                var result = _projectService.OpenFproj(fullPath);
                ApplyProjectOpenResult(result, Path.GetDirectoryName(fullPath));
            }
            else if (extension == ".fasm")
            {
                var result = _projectService.OpenSourceFile(fullPath);
                ApplyProjectOpenResult(result, Path.GetDirectoryName(fullPath));
            }
            else
            {
                ViewModel.StatusText = $"Tipo de archivo no compatible: {extension}";
            }
        }
        else
        {
            ViewModel.StatusText = $"No se encontró la ruta: {fullPath}";
        }
    }

    private void ApplyProjectOpenResult(ProjectOpenResult result, string? openedFolder)
    {
        if (!result.Success)
        {
            ViewModel.StatusText = $"Error al abrir: {result.Error}";
            return;
        }

        if (!string.IsNullOrWhiteSpace(openedFolder))
        {
            _settingsService.SaveLastOpenedFolder(openedFolder);
        }

        ViewModel.OpenProject(result.SourcePath!, result.Project?.ProjectFilePath);
    }

    private async void OpenFolderPickerAsync()
    {
        try
        {
            if (ViewModel.HasUnsavedChanges)
            {
                bool discardChanges = await _dialogService.ConfirmAsync(
                    "Cambios sin guardar",
                    "Hay cambios sin guardar en el archivo fuente. ¿Descartarlos y abrir otro proyecto?",
                    "Descartar",
                    "Cancelar");

                if (!discardChanges)
                {
                    return;
                }
            }

            // Selector nativo Win32 a través del servicio: funciona con
            // identidad de paquete (MSIX) y en despliegues unpackaged o
            // elevados, donde Windows.Storage.Pickers.FolderPicker falla.
            string? folderPath = _folderPickerService.PickFolder(
                _settingsService.GetInitialFolder(),
                "Abrir carpeta de proyecto");

            if (!string.IsNullOrEmpty(folderPath))
            {
                OpenProjectOrFolder(folderPath);
            }
        }
        catch (Exception ex)
        {
            _incidentRecorder.RecordException(ex, "MainPage.OpenFolderPickerAsync");
            ViewModel.StatusText = "No se pudo abrir el selector de carpetas.";
        }
    }

    private void SaveCurrentFile() => ViewModel.SaveSource();

    #endregion

    #region Shell panels and global shortcuts

    /// <summary>
    /// El dock de estado solicita formatear: la acción necesita el editor y,
    /// al terminar, el foco vuelve a él.
    /// </summary>
    private void OnStatusDockFormatRequested(object sender, EventArgs e)
    {
        ViewModel.Editor.RequestFormat();
        ViewModel.Editor.RequestFocus();
    }

    /// <summary>
    /// Al cerrarse la paleta el foco vuelve al editor, con independencia de
    /// cómo se haya cerrado (Escape, clic en el backdrop, badge o comando).
    /// </summary>
    private void OnPaletteClosed(object sender, EventArgs e)
    {
        ViewModel.CloseCommandPalette();
        ViewModel.Editor.RequestFocus();
    }

    private void OnPaletteOpened() => CommandPaletteControl.FocusInput();

    private void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool isCtrl = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) &
                       CoreVirtualKeyStates.Down) != 0;

        if (e.Key == VirtualKey.Escape)
        {
            if (ViewModel.IsCommandPaletteOpen)
            {
                ViewModel.CloseCommandPalette();
                e.Handled = true;
            }
        }
        else if (isCtrl && e.Key == VirtualKey.P)
        {
            ViewModel.ToggleCommandPalette();
            e.Handled = true;
        }
        else if (isCtrl && (int)e.Key == 190) // 190 = OemPeriod (tecla punto normal)
        {
            // Ctrl+. (punto normal) - Quick fix. El editor es el dueño de la
            // acción; el shell solo reenvía la petición.
            ViewModel.Editor.RequestQuickFixAtCaret();
            e.Handled = true;
        }
        else if (isCtrl && e.Key == VirtualKey.O)
        {
            OpenFolderPickerAsync();
            e.Handled = true;
        }
        else if (isCtrl && e.Key == VirtualKey.S)
        {
            SaveCurrentFile();
            e.Handled = true;
        }
    }

    #endregion

    #region Helpers

    private static bool TryGetExistingPath(string? argument, out string fullPath)
    {
        if (!TryGetFullPath(argument, out fullPath))
        {
            return false;
        }

        return Directory.Exists(fullPath) || File.Exists(fullPath);
    }

    private static bool TryGetFullPath(string? argument, out string fullPath)
    {
        fullPath = string.Empty;
        string candidate = (argument ?? string.Empty).Trim().Trim('"');
        if (candidate.Length == 0)
        {
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(candidate);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or NotSupportedException
            or IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            fullPath = string.Empty;
            return false;
        }
    }

    #endregion
}
