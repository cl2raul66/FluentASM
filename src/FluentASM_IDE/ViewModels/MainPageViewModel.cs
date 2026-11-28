using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FluentASM_IDE.Common.Services;
using FluentASM_IDE.Messages;
using FluentASM_IDE.Models;
using FluentASM_IDE.Services;

namespace FluentASM_IDE.ViewModels;

/// <summary>
/// ViewModel del <em>shell</em>: coordina los atajos globales, la gestión de
/// proyecto y los paneles externos (dock de estado y paleta de comandos).
/// <para>
/// El documento, el cursor, los diagnósticos y el IntelliSense pertenecen al
/// componente de edición (<see cref="EditorViewModel"/>), que es independiente
/// del shell. Aquí solo se resuelve qué hacer cuando el editor pide una acción
/// que lo trasciende.
/// </para>
/// </summary>
public partial class MainPageViewModel : UiThreadRecipient,
    IRecipient<PaletteCommandExecutedMessage>,
    IRecipient<DocumentDirtyChangedMessage>
{
    public const string DocumentFileName = IdeDefaults.DocumentFileName;
    public const string WindowTitle = IdeDefaults.WindowTitle;

    private readonly ProjectService _projectService;

    /// <summary>Componente de edición independiente del shell.</summary>
    public EditorViewModel Editor { get; }

    /// <summary>Dock de estado inferior; recibe las variaciones por mensajería.</summary>
    public StatusDockViewModel StatusDock { get; }

    /// <summary>Paleta de comandos independiente del shell.</summary>
    public CommandPaletteViewModel CommandPalette { get; }

    /// <summary>Servicio de apertura y guardado de proyectos (.fproj + .fasm).</summary>
    public ProjectService ProjectService => _projectService;

    public string StatusText
    {
        get => StatusDock.StatusText;
        set => StatusDock.StatusText = value;
    }

    public bool FormatOnSave
    {
        get => StatusDock.FormatOnSave;
        set => StatusDock.FormatOnSave = value;
    }

    public bool IsCommandPaletteOpen
    {
        get => CommandPalette.IsOpen;
        set => CommandPalette.IsOpen = value;
    }

    public string CommandQuery
    {
        get => CommandPalette.CommandQuery;
        set => CommandPalette.CommandQuery = value;
    }

    public ObservableCollection<CommandPaletteItem> FilteredCommands => CommandPalette.FilteredCommands;

    /// <summary>Nombre del proyecto abierto (campo <c>name</c> del <c>.fproj</c>).</summary>
    public string ProjectName { get; private set; } = string.Empty;

    /// <summary>Ruta del archivo fuente <c>.fasm</c> abierto.</summary>
    public string SourceFilePath { get; private set; } = string.Empty;

    /// <summary>Indica si hay un proyecto real abierto (no el ejemplo en memoria).</summary>
    public bool IsProjectOpen { get; private set; }

    /// <summary>Indica si hay cambios sin guardar en el archivo fuente abierto.</summary>
    public bool HasUnsavedChanges => Editor.IsDirty;

    /// <summary>Se dispara cuando el usuario pide abrir un proyecto o archivo.</summary>
    public event Action? OpenProjectRequested;

    /// <summary>Se dispara cuando el usuario pide guardar el archivo fuente.</summary>
    public event Action? SaveRequested;

    /// <summary>Se dispara cuando la paleta de comandos se abre y hay que enfocarla.</summary>
    public event Action? PaletteOpened;

    /// <summary>
    /// Crea el ViewModel del shell con las dependencias inyectadas por el
    /// contenedor de la aplicación.
    /// </summary>
    public MainPageViewModel(
        ProjectService projectService,
        IMessenger messenger,
        EditorViewModel editor,
        StatusDockViewModel statusDock,
        CommandPaletteViewModel commandPalette)
        : base(messenger)
    {
        ArgumentNullException.ThrowIfNull(projectService);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(statusDock);
        ArgumentNullException.ThrowIfNull(commandPalette);

        _projectService = projectService;
        Editor = editor;
        StatusDock = statusDock;
        CommandPalette = commandPalette;

        Editor.ShellCommandRequested += OnShellCommandRequested;

        IsActive = true;
    }

    /// <summary>
    /// Aplica el tema a los componentes independientes mediante mensajería.
    /// </summary>
    public void UpdateTheme(bool isDark)
        => Messenger.Send(new ThemeChangedMessage(isDark));

    /// <summary>Abre o cierra la paleta de comandos.</summary>
    [RelayCommand]
    public void ToggleCommandPalette()
    {
        bool wasOpen = CommandPalette.IsOpen;
        CommandPalette.Toggle();

        if (!wasOpen && CommandPalette.IsOpen)
        {
            PaletteOpened?.Invoke();
        }
    }

    /// <summary>Abre la paleta con un prefijo ya escrito (por ejemplo, «/»).</summary>
    [RelayCommand]
    public void OpenCommandPaletteWithPrefix(string prefix) => CommandPalette.OpenWithPrefix(prefix);

    /// <summary>Cierra la paleta de comandos.</summary>
    [RelayCommand]
    public void CloseCommandPalette() => CommandPalette.Close();

    /// <summary>
    /// Abre un proyecto o archivo fuente, lo carga en el componente de edición
    /// y sincroniza el título y el estado del shell.
    /// </summary>
    public void OpenProject(string sourcePath, string? projectFilePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException($"No se encontró el archivo fuente '{fullSourcePath}'.");
        }

        // Si se proporciona un .fproj, abrir el proyecto completo; si no, se
        // intenta asociar con un .fproj de la misma carpeta o abrir suelto.
        ProjectOpenResult openResult = projectFilePath is not null
            ? _projectService.OpenFproj(projectFilePath)
            : _projectService.OpenSourceFile(fullSourcePath);

        if (!openResult.Success)
        {
            StatusText = projectFilePath is not null
                ? $"Error al abrir proyecto: {openResult.Error}"
                : $"Error al abrir archivo: {openResult.Error}";
            return;
        }

        string sourceText;
        try
        {
            sourceText = File.ReadAllText(_projectService.SourceFilePath!);
        }
        catch (Exception ex)
        {
            StatusText = $"No se pudo leer el archivo fuente: {ex.Message}";
            return;
        }

        IsProjectOpen = _projectService.HasProject;
        ProjectName = _projectService.ProjectName ?? string.Empty;
        SourceFilePath = _projectService.SourceFilePath ?? string.Empty;

        // El editor es el dueño del documento: abre el archivo en el LSP y
        // publica el texto normalizado para que la vista lo muestre.
        Editor.OpenDocument(DocumentText.Normalize(sourceText), SourceFilePath);

        UpdateDocumentTitle();
        StatusText = IsProjectOpen
            ? $"Proyecto abierto: {ProjectName} ({SourceFilePath})"
            : $"Archivo abierto: {SourceFilePath}";
    }

    /// <summary>
    /// Guarda el documento vigente en el archivo fuente abierto. Si está
    /// activo el formateo al guardar, el editor lo aplica antes de escribir.
    /// </summary>
    public bool SaveSource()
    {
        if (FormatOnSave)
        {
            Editor.RequestFormatForSave();
        }

        string text = Editor.CurrentText;
        if (!_projectService.SaveSource(text))
        {
            StatusText = "No se pudo guardar el archivo.";
            return false;
        }

        Editor.MarkSaved(text);
        StatusText = $"Guardado: {SourceFilePath}";
        return true;
    }

    void IRecipient<PaletteCommandExecutedMessage>.Receive(PaletteCommandExecutedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        RunOnUiThread(() => ExecutePaletteCommand(message.Item));
    }

    void IRecipient<DocumentDirtyChangedMessage>.Receive(DocumentDirtyChangedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        RunOnUiThread(UpdateDocumentTitle);
    }

    /// <summary>
    /// Resuelve los comandos globales que llegan desde el editor (atajos de
    /// teclado) o desde la propia paleta.
    /// </summary>
    private void OnShellCommandRequested(string command)
    {
        switch (command)
        {
            case ShellCommands.TogglePalette:
                ToggleCommandPalette();
                break;

            case ShellCommands.Open:
                OpenProjectRequested?.Invoke();
                break;

            case ShellCommands.Save:
                SaveRequested?.Invoke();
                break;

            default:
                // El resto de comandos (por ejemplo /run, /build o /simbolos)
                // se resuelven con el catálogo de la paleta de comandos.
                CommandPalette.ExecuteCommand(command);
                break;
        }
    }

    private void ExecutePaletteCommand(CommandPaletteItem item)
    {
        switch (item.Command)
        {
            case "/problema":
                Editor.GoToFirstDiagnostic();
                break;

            case "/reanalizar":
                Editor.RequestRevalidate();
                break;

            case ShellCommands.Open:
                OpenProjectRequested?.Invoke();
                break;

            case ShellCommands.Save:
                SaveRequested?.Invoke();
                break;

            case "/format":
                Editor.RequestFormat();
                break;

            case "/format al guardar":
                FormatOnSave = !FormatOnSave;
                break;

            case "/esquema":
            case ShellCommands.Symbols:
                Editor.RequestOutline();
                break;

            case "/sucesos":
                if (!CommandPalette.ShowIncidentsLog())
                {
                    StatusText = "Registro de sucesos vacío.";
                }
                break;

            case ShellCommands.Build:
                StatusText = "La compilación real estará disponible en Fase 2.";
                break;

            case ShellCommands.Run:
                StatusText = "La ejecución real estará disponible en Fase 2.";
                break;

            case "/disasm":
                StatusText = "El desensamblado real estará disponible en Fase 2.";
                break;

            default:
                StatusText = $"Referencia: {item.Command}";
                break;
        }
    }

    /// <summary>
    /// Publica el título visible del documento, incluyendo el indicador de
    /// cambios pendientes.
    /// </summary>
    private void UpdateDocumentTitle()
    {
        var suffix = Editor.IsDirty ? " *" : string.Empty;
        string title;
        if (IsProjectOpen && !string.IsNullOrEmpty(ProjectName))
        {
            title = $"{ProjectName}.fasm{suffix}";
        }
        else if (!string.IsNullOrEmpty(SourceFilePath))
        {
            title = Path.GetFileName(SourceFilePath) + suffix;
        }
        else
        {
            title = DocumentFileName + suffix;
        }

        Messenger.Send(new DocumentTitleChangedMessage(title));
    }
}
