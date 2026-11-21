using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentASM.Lsp;
using FluentASM.Lsp.Protocol;
using FluentASM.Lsp.Server;
using FluentASM_IDE.Models;
using FluentASM_IDE.Common.Services;
using FluentASM_IDE.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FluentASM_IDE.ViewModels;

public partial class MainPageViewModel : ObservableObject, IMainPageViewModel
{
    private static readonly SolidColorBrush ErrorCountBrushDark = new(Color.FromArgb(255, 255, 180, 171));
    private static readonly SolidColorBrush ErrorCountBrushLight = new(Color.FromArgb(255, 186, 26, 26));
    private static readonly SolidColorBrush WarningCountBrushDark = new(Color.FromArgb(255, 255, 212, 190));
    private static readonly SolidColorBrush WarningCountBrushLight = new(Color.FromArgb(255, 168, 83, 0));

    private bool _isDarkTheme = true;
    private bool _hasUnsavedChanges;

    public const string DocumentFileName = "miCalculadoraCli.fasm";
    public const string WindowTitle = "FluentASM IDE - " + DocumentFileName;

    private readonly ProjectService _projectService;
    private readonly QuickFixService _quickFixService;

    [ObservableProperty]
    public partial string DocumentTitle { get; set; } = DocumentFileName;

    [ObservableProperty]
    public partial string Architecture { get; set; } = "x64 Fluent";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Listo";

    [ObservableProperty]
    public partial string Encoding { get; set; } = "UTF-8";

    [ObservableProperty]
    public partial string LineEnding { get; set; } = "LF";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveLineMargin))]
    [NotifyPropertyChangedFor(nameof(CursorPositionFormatted))]
    public partial int CurrentLine { get; set; } = 13;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CursorPositionFormatted))]
    public partial int CurrentCol { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveLineMargin))]
    public partial double ScrollOffset { get; set; } = 0;

    [ObservableProperty]
    public partial int TotalLines { get; set; } = 32;

    [ObservableProperty]
    public partial string LineNumbersText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsCommandPaletteOpen { get; set; } = false;

    [ObservableProperty]
    public partial bool IsIntelliSenseOpen { get; set; } = false;

    /// <summary>Estado contextual o type-state shown by the completion overlay.</summary>
    [ObservableProperty]
    public partial string CompletionContext { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CommandQuery { get; set; } = "/";

    private static readonly ILoggerFactory LspLoggerFactory = LoggerFactory.Create(builder =>
        builder.SetMinimumLevel(LogLevel.Debug).AddDebug());

    /// <summary>Servicio de lenguaje compartido por diagnósticos, resaltado, hover y símbolos.</summary>
    public ILanguageService LspServer { get; }

    /// <summary>Servicio de apertura y guardado de proyectos (.fproj + .fasm).</summary>
    public ProjectService ProjectService => _projectService;

    /// <summary>Servicio de acciones rápidas (code actions) para diagnósticos.</summary>
    public QuickFixService QuickFixService => _quickFixService;

    /// <summary>Acciones rápidas disponibles para el diagnóstico actualmente seleccionado.</summary>
    public ObservableCollection<QuickFixAction> QuickFixActions { get; } = new();

    private readonly object documentGate = new();
    private int documentVersion;
    private int completionVersion;
    private int completionRequestVersion;
    private Position completionCaret;
    private string documentText = string.Empty;
    private CancellationTokenSource? analysisCts;

    /// <summary>URI del documento vigente en el LSP (<c>file:///...</c>).</summary>
    public string DocumentUri { get; private set; } = "file:///" + DocumentFileName;

    /// <summary>Nombre del proyecto abierto (campo <c>name</c> del <c>.fproj</c>).</summary>
    public string ProjectName { get; private set; } = string.Empty;

    /// <summary>Ruta del archivo fuente <c>.fasm</c> abierto.</summary>
    public string SourceFilePath { get; private set; } = string.Empty;

    /// <summary>Indica si hay un proyecto real abierto (no el ejemplo en memoria).</summary>
    public bool IsProjectOpen { get; private set; }

    /// <summary>Indica si hay cambios sin guardar en el archivo fuente abierto.</summary>
    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set
        {
            if (_hasUnsavedChanges != value)
            {
                _hasUnsavedChanges = value;
                OnPropertyChanged();
                // Actualizar el título para mostrar el indicador de cambios pendientes (*).
                UpdateDocumentTitle();
                UnsavedChangesChanged?.Invoke();
            }
        }
    }

    /// <summary>Texto fuente cargado del proyecto abierto (normalizado, <c>\n</c>).</summary>
    public string CurrentSourceText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiagnosticErrorSummary))]
    public partial int DiagnosticErrorCount { get; set; } = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiagnosticWarningSummary))]
    public partial int DiagnosticWarningCount { get; set; } = 0;

    public ObservableCollection<DiagnosticItem> Diagnostics { get; } = new();

    public DiagnosticItem? FirstDiagnostic => Diagnostics.Count > 0 ? Diagnostics[0] : null;

    public DiagnosticItem? FirstErrorDiagnostic => Diagnostics.FirstOrDefault(diagnostic => diagnostic.IsError);

    public DiagnosticItem? FirstWarningDiagnostic => Diagnostics.FirstOrDefault(diagnostic => diagnostic.IsWarning);

    public Thickness ActiveLineMargin => new Thickness(0, 8 + (CurrentLine - 1) * 22.0 - ScrollOffset, 0, 0);

    public string CursorPositionFormatted => $"Ln {CurrentLine}, Col {CurrentCol}";

    public string DiagnosticErrorSummary => DiagnosticErrorCount == 1
        ? "1 error"
        : $"{DiagnosticErrorCount} errores";

    public string DiagnosticWarningSummary => DiagnosticWarningCount == 1
        ? "1 advertencia"
        : $"{DiagnosticWarningCount} advertencias";

    public SolidColorBrush DiagnosticErrorBrush => _isDarkTheme ? ErrorCountBrushDark : ErrorCountBrushLight;

    public SolidColorBrush DiagnosticWarningBrush => _isDarkTheme ? WarningCountBrushDark : WarningCountBrushLight;

    public void UpdateTheme(bool isDark)
    {
        _isDarkTheme = isDark;
        CommandPaletteItem.IsDarkTheme = isDark;
        CompletionItem.IsDarkTheme = isDark;

        foreach (var cmd in AllCommands)
        {
            cmd.RefreshTheme();
        }
        foreach (var item in CompletionItems)
        {
            item.RefreshTheme();
        }

        OnPropertyChanged(nameof(DiagnosticErrorBrush));
        OnPropertyChanged(nameof(DiagnosticWarningBrush));
    }

    [ObservableProperty]
    public partial CompletionItem? SelectedCompletion { get; set; }

    public ObservableCollection<CommandPaletteItem> AllCommands { get; } = new();
    public ObservableCollection<CommandPaletteItem> FilteredCommands { get; } = new();

    [ObservableProperty]
    public partial ObservableCollection<CompletionItem> CompletionItems { get; set; } = new();

    /// <summary>
    /// Se dispara cuando la paleta pide volver a validar el documento actual.
    /// </summary>
    public event Action? RevalidationRequested;

    /// <summary>
    /// Se dispara cuando otra operación (por ejemplo, IntelliSense) cambió la
    /// versión del documento y conviene reprogramar el análisis.
    /// </summary>
    public event Action? AnalysisRequested;

    /// <summary>
    /// Se dispara cuando cambian los diagnósticos para que la vista actualice
    /// los subrayados del editor.
    /// </summary>
    public event Action? DiagnosticsChanged;

    /// <summary>
    /// Se dispara cuando el usuario pide saltar a un diagnóstico concreto.
    /// </summary>
    public event Action<DiagnosticItem>? DiagnosticNavigationRequested;

    /// <summary>
    /// Se dispara cuando se abre un proyecto o archivo fuente nuevo, con el
    /// texto fuente ya cargado para que la vista lo muestre en el editor.
    /// </summary>
    /// <param name="sourceText">Texto fuente normalizado.</param>
    public event Action<string>? ProjectOpened;

    /// <summary>
    /// Se dispara cuando el ViewModel reemplaza el texto completo del documento
    /// (por ejemplo, al aplicar un quick-fix). La vista debe sincronizar el editor.
    /// </summary>
    /// <param name="sourceText">Texto completo normalizado.</param>
    /// <param name="caretOffset">Posición del caret en el texto normalizado.</param>
    public event Action<string, int>? DocumentTextReplaced;

    /// <summary>
    /// Se dispara cuando cambia el estado de cambios sin guardar.
    /// </summary>
    public event Action? UnsavedChangesChanged;

    /// <summary>
    /// Se dispara cuando el usuario pide abrir un proyecto (desde la paleta o teclado).
    /// </summary>
    public event Action? OpenProjectRequested;

    /// <summary>
    /// Se dispara cuando el usuario pide guardar el archivo fuente (desde la paleta o teclado).
    /// </summary>
    public event Action? SaveRequested;

    /// <summary>
    /// Se dispara cuando el usuario pide mostrar el outline de símbolos (desde la paleta).
    /// </summary>
    public event Action? OutlineRequested;

    /// <summary>
    /// Se dispara cuando hay acciones rápidas disponibles para un diagnóstico.
    /// </summary>
    public event Action? QuickFixAvailable;

    public const string InitialSampleAsm =
        """
        miCalculadoraCli
            .data(dt -> {
                dt.define<Byte:8>(d -> d.id(numA).value(10))
                dt.define<Byte:8>(d -> d.id(numB).value(20))
            })
            .execute(ex -> {
                ex.routines(rt -> {

                    rt.define(d -> {
                        d.id(main)
                        d.returns(Gpr0)
                        d.run(() -> {
                            Gpr1.copy([numA])
                            Gpr2.copy([numB])
                            Ipr.flowCall(c -> c.target(sumar).inputs(Gpr1, Gpr2).output(Gpr0))
                            Ipr.flowStackJump()
                        })
                    })

                    rt.define(d -> {
                        d.id(sumar)
                        d.returns(Gpr0)
                        d.run((arg1<Byte:8>, arg2<Byte:8>) -> {
                            Gpr0.copy(arg1)
                            Gpr0.add(arg2)
                            Ipr.flowStackJump()
                        })
                    })

                })
            })
            .initialize()
        """;

    /// <summary>Crea el ViewModel con el servidor de lenguaje por defecto.</summary>
    public MainPageViewModel()
        : this(new FluentLanguageServer(LspLoggerFactory), new ProjectService(), new QuickFixService())
    {
    }

    /// <summary>
    /// Crea el ViewModel con un <see cref="ILanguageService"/> concreto, lo que
    /// permite sustituirlo en pruebas sin acoplarse a la implementación LSP.
    /// </summary>
    public MainPageViewModel(ILanguageService languageService)
        : this(languageService, new ProjectService(), new QuickFixService())
    {
    }

    /// <summary>
    /// Crea el ViewModel con un <see cref="ILanguageService"/> y un
    /// <see cref="ProjectService"/> concretos.
    /// </summary>
    public MainPageViewModel(ILanguageService languageService, ProjectService projectService)
        : this(languageService, projectService, new QuickFixService())
    {
    }

    /// <summary>
    /// Crea el ViewModel con dependencias inyectadas.
    /// </summary>
    public MainPageViewModel(ILanguageService languageService, ProjectService projectService, QuickFixService quickFixService)
    {
        ArgumentNullException.ThrowIfNull(languageService);
        ArgumentNullException.ThrowIfNull(projectService);
        ArgumentNullException.ThrowIfNull(quickFixService);
        LspServer = languageService;
        _projectService = projectService;
        _quickFixService = quickFixService;

        UpdateLineCount(DocumentText.CountLines(DocumentText.Normalize(InitialSampleAsm)));
        InitializeCommands();
        InitializeIntelliSense();
    }

    public void UpdateLineCount(int count)
    {
        TotalLines = Math.Max(1, count);
        var sb = new StringBuilder();
        for (int i = 1; i <= TotalLines; i++)
        {
            sb.AppendLine(i.ToString());
        }
        LineNumbersText = sb.ToString().TrimEnd();
    }

    public void UpdateCursorPosition(int line, int col)
    {
        CurrentLine = Math.Max(1, line);
        CurrentCol = Math.Max(1, col);
    }

    [RelayCommand]
    public void ToggleCommandPalette()
    {
        IsCommandPaletteOpen = !IsCommandPaletteOpen;
        if (IsCommandPaletteOpen)
        {
            CommandQuery = "/";
            FilterCommands(CommandQuery);
            InvalidateCompletions();
        }
    }

    [RelayCommand]
    public void OpenCommandPaletteWithPrefix(string prefix)
    {
        IsCommandPaletteOpen = true;
        CommandQuery = prefix;
        FilterCommands(CommandQuery);
        InvalidateCompletions();
    }

    [RelayCommand]
    public void CloseCommandPalette()
    {
        IsCommandPaletteOpen = false;
    }

    [RelayCommand]
    public void ToggleIntelliSense()
    {
        if (IsIntelliSenseOpen)
        {
            InvalidateCompletions();
            return;
        }

        IsCommandPaletteOpen = false;
        IsIntelliSenseOpen = true;
    }

    [RelayCommand]
    public void CloseIntelliSense()
    {
        InvalidateCompletions();
    }

    /// <summary>
    /// Invalida por completo la sesión de completado: suggestions, selección,
    /// contexto, versión y caret. Se usa ante cualquier mutación del texto,
    /// incluida una deshacer/rehacer o un quick-fix programático.
    /// </summary>
    public void InvalidateCompletions()
    {
        Interlocked.Increment(ref completionRequestVersion);
        Volatile.Write(ref completionVersion, 0);
        completionCaret = default;
        CompletionContext = string.Empty;
        SelectedCompletion = null;
        CompletionItems = new ObservableCollection<CompletionItem>();
        IsIntelliSenseOpen = false;
    }

    [RelayCommand]
    public void GoToFirstDiagnostic()
    {
        DiagnosticItem? diagnostic = FirstErrorDiagnostic ?? FirstDiagnostic;
        if (diagnostic != null)
        {
            DiagnosticNavigationRequested?.Invoke(diagnostic);
        }
    }

    [RelayCommand]
    public void GoToFirstErrorDiagnostic()
    {
        DiagnosticItem? diagnostic = FirstErrorDiagnostic;
        if (diagnostic != null)
        {
            DiagnosticNavigationRequested?.Invoke(diagnostic);
        }
    }

    [RelayCommand]
    public void GoToFirstWarningDiagnostic()
    {
        DiagnosticItem? diagnostic = FirstWarningDiagnostic;
        if (diagnostic != null)
        {
            DiagnosticNavigationRequested?.Invoke(diagnostic);
        }
    }

    [RelayCommand]
    public void Revalidate()
    {
        RevalidationRequested?.Invoke();
    }

    [RelayCommand]
    public void SelectCompletion(CompletionItem? item)
    {
        if (item == null) return;

        foreach (var c in CompletionItems)
        {
            c.IsSelected = (c == item);
        }
        SelectedCompletion = item;
    }

    [RelayCommand]
    public void SelectNextPaletteItem()
    {
        if (FilteredCommands.Count == 0) return;
        int index = -1;
        for (int i = 0; i < FilteredCommands.Count; i++)
        {
            if (FilteredCommands[i].IsActive) { index = i; break; }
        }

        int next = (index + 1) % FilteredCommands.Count;
        for (int i = 0; i < FilteredCommands.Count; i++)
        {
            FilteredCommands[i].IsActive = (i == next);
        }
    }

    [RelayCommand]
    public void SelectPrevPaletteItem()
    {
        if (FilteredCommands.Count == 0) return;
        int index = 0;
        for (int i = 0; i < FilteredCommands.Count; i++)
        {
            if (FilteredCommands[i].IsActive) { index = i; break; }
        }

        int prev = (index - 1 + FilteredCommands.Count) % FilteredCommands.Count;
        for (int i = 0; i < FilteredCommands.Count; i++)
        {
            FilteredCommands[i].IsActive = (i == prev);
        }
    }

    [RelayCommand]
    public void ExecuteActivePaletteItem()
    {
        var active = FilteredCommands.FirstOrDefault(c => c.IsActive) ?? FilteredCommands.FirstOrDefault();
        if (active != null)
        {
            ExecutePaletteItem(active);
        }
    }

    [RelayCommand]
    public void ExecutePaletteItem(CommandPaletteItem? item)
    {
        if (item == null) return;
        IsCommandPaletteOpen = false;

        switch (item.Command)
        {
            case "/problema":
                GoToFirstDiagnostic();
                break;

            case "/reanalizar":
                RevalidationRequested?.Invoke();
                break;

            case "/abrir":
                OpenProjectRequested?.Invoke();
                break;

            case "/guardar":
                SaveRequested?.Invoke();
                break;

            case "/esquema":
                OutlineRequested?.Invoke();
                break;

            case "/build":
                StatusText = "La compilación real estará disponible en Fase 2.";
                break;

            case "/run":
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

    public void ExecuteCommand(string command)
    {
        CommandPaletteItem? item = AllCommands.FirstOrDefault(candidate =>
            string.Equals(candidate.Command, command, StringComparison.OrdinalIgnoreCase));

        if (item != null)
        {
            ExecutePaletteItem(item);
        }
    }

    partial void OnCommandQueryChanged(string value)
    {
        FilterCommands(value);
    }

    private void FilterCommands(string query)
    {
        FilteredCommands.Clear();
        var trimmed = (query ?? string.Empty).Trim();

        foreach (var cmd in AllCommands)
        {
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed == "/" ||
                cmd.Command.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                cmd.Description.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                cmd.Category.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                FilteredCommands.Add(cmd);
            }
        }

        if (FilteredCommands.Count > 0)
        {
            foreach (var c in FilteredCommands) c.IsActive = false;
            FilteredCommands[0].IsActive = true;
        }
    }

    private void InitializeCommands()
    {
        AllCommands.Clear();

        // Comandos reales disponibles en Fase 0.
        AllCommands.Add(new CommandPaletteItem
        {
            Command = "/problema",
            Description = "Ir al primer diagnóstico del documento",
            Shortcut = "",
            Category = "Comandos (/)",
            IconGlyph = "\uE783",
            IconColorHex = "#FFB4AB",
            IsActive = true,
            IsCommand = true
        });

        AllCommands.Add(new CommandPaletteItem
        {
            Command = "/reanalizar",
            Description = "Volver a validar el documento con el LSP en memoria",
            Shortcut = "",
            Category = "Comandos (/)",
            IconGlyph = "\uE72C",
            IconColorHex = "#60CDFF",
            IsActive = false,
            IsCommand = true
        });

        AllCommands.Add(new CommandPaletteItem
        {
            Command = "/abrir",
            Description = "Abrir carpeta de proyecto o archivo .fproj / .fasm",
            Shortcut = "Ctrl+O",
            Category = "Comandos (/)",
            IconGlyph = "\uE823",
            IconColorHex = "#60CDFF",
            IsActive = false,
            IsCommand = true
        });

        AllCommands.Add(new CommandPaletteItem
        {
            Command = "/guardar",
            Description = "Guardar el archivo fuente actual (.fasm)",
            Shortcut = "Ctrl+S",
            Category = "Comandos (/)",
            IconGlyph = "\uE74E",
            IconColorHex = "#60CDFF",
            IsActive = false,
            IsCommand = true
        });

        AllCommands.Add(new CommandPaletteItem
        {
            Command = "/esquema",
            Description = "Mostrar outline de símbolos del documento",
            Shortcut = "",
            Category = "Comandos (/)",
            IconGlyph = "\uE8A6",
            IconColorHex = "#60CDFF",
            IsActive = false,
            IsCommand = true
        });

        AllCommands.Add(new CommandPaletteItem
        {
            Command = "/build",
            Description = "Compilación real (disponible en Fase 2)",
            Shortcut = "Ctrl+B",
            Category = "Próximamente (Fase 2)",
            IconGlyph = "\uE756",
            IconColorHex = "#60CDFF",
            IsActive = false,
            IsCommand = true
        });

        AllCommands.Add(new CommandPaletteItem
        {
            Command = "/run",
            Description = "Ejecución real (disponible en Fase 2)",
            Shortcut = "F5",
            Category = "Próximamente (Fase 2)",
            IconGlyph = "\uE768",
            IconColorHex = "#81CFFF",
            IsActive = false,
            IsCommand = true
        });

        AllCommands.Add(new CommandPaletteItem
        {
            Command = "/disasm",
            Description = "Desensamblado real (disponible en Fase 2)",
            Shortcut = "",
            Category = "Próximamente (Fase 2)",
            IconGlyph = "\uE8A5",
            IconColorHex = "#81CFFF",
            IsActive = false,
            IsCommand = true
        });

        // Referencia rápida de la sintaxis canónica.
        AllCommands.Add(new CommandPaletteItem
        {
            Command = "Gpr0..Gpr15",
            Description = "Registro unificado x64 y vistas (.low32, .low16, .low8, .high8; sin .high8 en Gpr4–Gpr15)",
            Shortcut = "",
            Category = "Ayuda y Referencia",
            IconGlyph = "\uE713",
            IconColorHex = "#81CFFF",
            IsActive = false,
            IsCommand = false
        });

        AllCommands.Add(new CommandPaletteItem
        {
            Command = "Ipr.flowJump / Ipr.flowCall / Ipr.flowStackJump",
            Description = "Control de flujo estructurado sobre el puntero de instrucción",
            Shortcut = "",
            Category = "Ayuda y Referencia",
            IconGlyph = "\uE8EE",
            IconColorHex = "#FFD4BE",
            IsActive = false,
            IsCommand = false
        });

        AllCommands.Add(new CommandPaletteItem
        {
            Command = "Frf.zero / Frf.carry / Frf.sign / Frf.overflow...",
            Description = "Banderas de condición para saltos fluidos condicionales",
            Shortcut = "",
            Category = "Ayuda y Referencia",
            IconGlyph = "\uE82D",
            IconColorHex = "#FFD4BE",
            IsActive = false,
            IsCommand = false
        });

        AllCommands.Add(new CommandPaletteItem
        {
            Command = ".data(dt -> { ... })",
            Description = "Declaración de datos con dt.define y capacidad explícita Byte:N",
            Shortcut = "",
            Category = "Ayuda y Referencia",
            IconGlyph = "\uE8F1",
            IconColorHex = "#B4E4FF",
            IsActive = false,
            IsCommand = false
        });

        FilterCommands("/");
    }

    private void InitializeIntelliSense()
    {
        string normalized = DocumentText.Normalize(InitialSampleAsm);
        lock (documentGate)
        {
            documentText = normalized;
            documentVersion = 1;
            DocumentUri = "file:///" + DocumentFileName;
            LspServer.OpenDocument(DocumentUri, normalized, documentVersion);
        }

    }

    /// <summary>
    /// Sincroniza el documento con una versión incremental y espera el análisis
    /// vigente. Si llega una versión más reciente, cancela esta y devuelve
    /// <c>null</c> para que la vista no aplique resultados obsoletos.
    /// </summary>
    public async Task<DocumentAnalysisResult?> AnalyzeDocumentAsync(
        string sourceCode,
        CancellationToken cancellationToken = default)
    {
        string normalized = DocumentText.Normalize(sourceCode);
        int version = UpdateDocument(normalized);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationTokenSource? previous;
        lock (documentGate)
        {
            previous = analysisCts;
            analysisCts = cts;
        }

        // La versión anterior puede haberse completado y liberado su CTS; la
        // cancelación es best-effort y nunca debe romper el análisis vigente.
        try
        {
            previous?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        CancellationToken token = cts.Token;

        try
        {
            var snapshot = await Task.Run(
                () => LspServer.GetSnapshot(DocumentUri, token),
                token).ConfigureAwait(true);

            if (snapshot is null
                || token.IsCancellationRequested
                || version != Volatile.Read(ref documentVersion))
            {
                return null;
            }

            return new DocumentAnalysisResult(version, normalized, snapshot.Diagnostics, snapshot.HighlightTokens);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            lock (documentGate)
            {
                if (ReferenceEquals(analysisCts, cts))
                {
                    analysisCts = null;
                }
            }

            cts.Dispose();
        }
    }

    /// <summary>
    /// Aplica los diagnósticos de un análisis que la vista ya validó contra el
    /// texto vigente del editor. Así nunca se muestran subrayados de una
    /// versión anterior.
    /// </summary>
    public void ApplyAnalysis(DocumentAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ApplyDiagnostics(result.Diagnostics);
    }

    /// <summary>
    /// Actualiza el documento solo si el texto cambió y devuelve la versión
    /// vigente. Evita reanálisis por sincronizaciones redundantes.
    /// </summary>
    private int UpdateDocument(string normalized)
    {
        lock (documentGate)
        {
            if (string.Equals(normalized, documentText, StringComparison.Ordinal))
            {
                return documentVersion;
            }

            documentText = normalized;
            documentVersion++;
            LspServer.UpdateDocument(DocumentUri, normalized, documentVersion);
            return documentVersion;
        }
    }

    private void ApplyDiagnostics(IReadOnlyList<LspDiagnostic> diagnostics)
    {
        int errors = 0;
        int warnings = 0;

        Diagnostics.Clear();
        foreach (var diagnostic in diagnostics)
        {
            var start = diagnostic.Range.Start;
            var end = diagnostic.Range.End;
            bool isError = diagnostic.Severity == DiagnosticSeverity.Error;

            if (isError)
            {
                errors++;
            }
            else if (diagnostic.Severity == DiagnosticSeverity.Warning)
            {
                warnings++;
            }

            Diagnostics.Add(new DiagnosticItem
            {
                Code = string.IsNullOrWhiteSpace(diagnostic.Code) ? "LSP" : diagnostic.Code,
                Severity = diagnostic.Severity switch
                {
                    DiagnosticSeverity.Error => "Error",
                    DiagnosticSeverity.Warning => "Advertencia",
                    DiagnosticSeverity.Information => "Información",
                    _ => "Sugerencia"
                },
                Message = diagnostic.Message,
                Line = start.Line + 1,
                Column = start.Character + 1,
                EndLine = end.Line + 1,
                EndColumn = end.Character + 1,
                IsError = isError,
                IsWarning = diagnostic.Severity == DiagnosticSeverity.Warning
            });
        }

        DiagnosticErrorCount = errors;
        DiagnosticWarningCount = warnings;
        DiagnosticsChanged?.Invoke();

        // Calcular acciones rápidas para el primer diagnóstico que las tenga.
        UpdateQuickFixActions();
    }

    private void UpdateQuickFixActions()
    {
        QuickFixActions.Clear();

        // Las correcciones se calculan en el servicio de lenguaje. La IDE solo
        // adapta sus rangos y textos al modelo que consume el editor.
        IReadOnlyList<LspCodeAction> languageActions = LspServer.GetCodeActions(DocumentUri);
        IReadOnlyList<QuickFixAction> actions = _quickFixService.GetActions(languageActions);
        foreach (var action in actions)
        {
            QuickFixActions.Add(action);
        }

        if (QuickFixActions.Count > 0)
        {
            QuickFixAvailable?.Invoke();
        }
    }

    /// <summary>
    /// Aplica una acción rápida (code action) sobre el documento usando el
    /// rango y el texto de reemplazo propuestos por el servicio de lenguaje.
    /// </summary>
    public void ApplyQuickFix(QuickFixAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        // El documento del servicio está normalizado con saltos \n; sus
        // posiciones se usan directamente sobre el texto canónico.
        string text;
        lock (documentGate)
        {
            text = documentText;
        }

        // Calcular el offset absoluto base 0 a partir de línea/columna base 1.
        int startOffset = GetOffsetFromLineColumn(text, action.StartLine, action.StartColumn);
        int endOffset = GetOffsetFromLineColumn(text, action.EndLine, action.EndColumn);

        if (startOffset < 0 || endOffset <= startOffset || endOffset > text.Length)
        {
            StatusText = "No se pudo aplicar la acción rápida: posición inválida.";
            return;
        }

        string before = text[..startOffset];
        string after = text[endOffset..];
        string newText = before + action.ReplacementText + after;

        // Actualizar el documento en el LSP y marcar cambios no guardados.
        // Una mutación programática invalida cualquier suggestion que siguiera
        // abierta, aunque el editor no dispare SelectionChanged.
        InvalidateCompletions();
        string normalized = DocumentText.Normalize(newText);
        lock (documentGate)
        {
            documentText = normalized;
            documentVersion++;
            LspServer.UpdateDocument(DocumentUri, normalized, documentVersion);
        }

        HasUnsavedChanges = true;
        StatusText = $"Acción rápida aplicada: {action.Title}";
        QuickFixActions.Clear();

        // Notificar a la vista con el nuevo texto y la posición del caret
        // (el final del texto reemplazado) para que sincronice el editor.
        CurrentSourceText = normalized;
        int caretOffset = Math.Clamp(
            startOffset + action.ReplacementText.Length,
            0,
            normalized.Length);
        DocumentTextReplaced?.Invoke(normalized, caretOffset);

        // Reprogramar el análisis para actualizar diagnósticos y resaltado.
        AnalysisRequested?.Invoke();
    }

    private static int GetOffsetFromLineColumn(string text, int line1Based, int column1Based)
    {
        int line = Math.Clamp(line1Based - 1, 0, int.MaxValue);
        int column = Math.Clamp(column1Based - 1, 0, int.MaxValue);

        int offset = 0;
        int currentLine = 0;

        while (offset < text.Length && currentLine < line)
        {
            if (text[offset] == '\n')
            {
                currentLine++;
            }
            offset++;
        }

        // Avanzar 'column' caracteres dentro de la línea.
        int charsInLine = 0;
        while (offset < text.Length && charsInLine < column && text[offset] != '\n')
        {
            offset++;
            charsInLine++;
        }

        return Math.Clamp(offset, 0, text.Length);
    }

    /// <summary>
    /// Abre un proyecto o archivo fuente y lo sincroniza con el LSP.
    /// </summary>
    public void OpenProject(string sourcePath, string? projectFilePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException($"No se encontró el archivo fuente '{fullSourcePath}'.");
        }

        // Si se proporciona un .fproj, abrir el proyecto completo.
        if (projectFilePath is not null)
        {
            var openResult = _projectService.OpenFproj(projectFilePath);
            if (!openResult.Success)
            {
                StatusText = $"Error al abrir proyecto: {openResult.Error}";
                return;
            }
        }
        else
        {
            // Intentar asociar con un .fproj de la misma carpeta, o abrir suelto.
            var openResult = _projectService.OpenSourceFile(fullSourcePath);
            if (!openResult.Success)
            {
                StatusText = $"Error al abrir archivo: {openResult.Error}";
                return;
            }
        }

        // Leer el contenido del archivo fuente.
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

        // Configurar el URI del documento en el LSP.
        InvalidateCompletions();
        DocumentUri = new Uri(fullSourcePath).AbsoluteUri;
        IsProjectOpen = _projectService.HasProject;
        ProjectName = _projectService.ProjectName ?? string.Empty;
        SourceFilePath = _projectService.SourceFilePath ?? string.Empty;

        // Actualizar título y estado.
        UpdateDocumentTitle();
        StatusText = IsProjectOpen
            ? $"Proyecto abierto: {ProjectName} ({SourceFilePath})"
            : $"Archivo abierto: {SourceFilePath}";

        // Abrir el documento en el LSP con el texto del archivo.
        string normalized = DocumentText.Normalize(sourceText);
        lock (documentGate)
        {
            documentText = normalized;
            documentVersion++;
            LspServer.OpenDocument(DocumentUri, normalized, documentVersion);
        }

        HasUnsavedChanges = false;

        // Notificar a la vista con el texto cargado para que lo muestre en el editor.
        CurrentSourceText = normalized;
        ProjectOpened?.Invoke(normalized);
    }

    /// <summary>
    /// Guarda el texto actual en el archivo fuente abierto (<c>.fasm</c>).
    /// </summary>
    public bool SaveSource(string editorText)
    {
        if (!_projectService.SaveSource(editorText))
        {
            StatusText = "No se pudo guardar el archivo.";
            return false;
        }

        HasUnsavedChanges = false;
        StatusText = $"Guardado: {SourceFilePath}";
        return true;
    }

    /// <summary>
    /// Marca o desmarca el estado de cambios sin guardar.
    /// </summary>
    public void MarkUnsavedChanges(bool hasChanges)
    {
        HasUnsavedChanges = hasChanges;
    }

    private void UpdateDocumentTitle()
    {
        var suffix = HasUnsavedChanges ? " *" : string.Empty;
        if (IsProjectOpen && !string.IsNullOrEmpty(ProjectName))
        {
            DocumentTitle = $"{ProjectName}.fasm{suffix}";
        }
        else if (!string.IsNullOrEmpty(SourceFilePath))
        {
            DocumentTitle = Path.GetFileName(SourceFilePath) + suffix;
        }
        else
        {
            DocumentTitle = DocumentFileName + suffix;
        }
    }

    public void RequestLspCompletions(string sourceCode, bool openPopup = true)
    {
        var requestedCaret = new Position(CurrentLine - 1, CurrentCol - 1);
        RequestLspCompletions(sourceCode, requestedCaret, openPopup);
    }

    /// <summary>
    /// Solicita suggestions para un caret explícito y conserva la versión del
    /// documento junto con la posición exacta a la que pertenecen.
    /// </summary>
    public void RequestLspCompletions(
        string sourceCode,
        Position requestedCaret,
        bool openPopup = true)
    {
        int previousVersion = Volatile.Read(ref documentVersion);
        UpdateDocument(DocumentText.Normalize(sourceCode));
        int currentVersion = Volatile.Read(ref documentVersion);
        int requestVersion = BeginCompletionRequest();
        var lspCompletions = LspServer.GetCompletions(DocumentUri, requestedCaret);

        if (!CanPublishCompletions(lspCompletions, currentVersion, requestVersion))
        {
            if (currentVersion != previousVersion)
            {
                AnalysisRequested?.Invoke();
            }

            return;
        }

        PublishCompletions(lspCompletions, currentVersion, requestedCaret, openPopup);

        // Si IntelliSense adelantó una versión nueva, la vista reprograma el
        // análisis para no quedarse con un resaltado obsoleto.
        if (Volatile.Read(ref documentVersion) != previousVersion)
        {
            AnalysisRequested?.Invoke();
        }
    }

    /// <summary>
    /// Solicita suggestions sin ejecutar lexer, parser ni semántica en el hilo
    /// de interfaz. La solicitud queda invalidada si cambia el documento, el
    /// caret o se inicia una consulta más reciente.
    /// </summary>
    public async Task RequestLspCompletionsAsync(
        string sourceCode,
        Position requestedCaret,
        bool openPopup = true,
        CancellationToken cancellationToken = default)
    {
        int previousVersion = Volatile.Read(ref documentVersion);
        UpdateDocument(DocumentText.Normalize(sourceCode));
        int currentVersion = Volatile.Read(ref documentVersion);
        int requestVersion = BeginCompletionRequest();

        CompletionList lspCompletions;
        try
        {
            lspCompletions = await Task.Run(
                () => LspServer.GetCompletions(DocumentUri, requestedCaret, cancellationToken),
                cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!CanPublishCompletions(lspCompletions, currentVersion, requestVersion))
        {
            if (currentVersion != previousVersion)
            {
                AnalysisRequested?.Invoke();
            }

            return;
        }

        PublishCompletions(lspCompletions, currentVersion, requestedCaret, openPopup);
        if (Volatile.Read(ref documentVersion) != previousVersion)
        {
            AnalysisRequested?.Invoke();
        }
    }

    private int BeginCompletionRequest()
    {
        InvalidateCompletions();
        return Volatile.Read(ref completionRequestVersion);
    }

    private bool CanPublishCompletions(
        CompletionList completions,
        int currentVersion,
        int requestVersion) =>
        requestVersion == Volatile.Read(ref completionRequestVersion) &&
        currentVersion == Volatile.Read(ref documentVersion) &&
        completions.Version is int snapshotVersion &&
        snapshotVersion == currentVersion;

    private void PublishCompletions(
        CompletionList completions,
        int currentVersion,
        Position requestedCaret,
        bool openPopup)
    {
        if (IsCommandPaletteOpen)
        {
            // La paleta de comandos y el overlay son superficies excluyentes.
            // Una solicitud que quedó pendiente no puede reabrir IntelliSense
            // detrás de la paleta.
            return;
        }

        Volatile.Write(ref completionVersion, completions.Version ?? currentVersion);
        completionCaret = completions.Caret ?? requestedCaret;
        CompletionContext = completions.Context ?? string.Empty;

        var items = new ObservableCollection<CompletionItem>();
        foreach (var lspItem in completions.Items)
        {
            items.Add(CompletionItem.FromLsp(lspItem, completions.ReplaceRange));
        }

        CompletionItems = items;
        if (CompletionItems.Count > 0)
        {
            SelectCompletion(CompletionItems[0]);
            if (openPopup)
            {
                IsIntelliSenseOpen = true;
            }
        }
    }

    /// <summary>
    /// Indica si la sesión de completado sigue perteneciendo al documento y al
    /// caret actuales. Impide confirmar un resultado obsoleto.
    /// </summary>
    public bool IsCompletionCurrent(Position caret) =>
        IsIntelliSenseOpen &&
        Volatile.Read(ref completionVersion) == Volatile.Read(ref documentVersion) &&
        completionCaret == caret;
}


