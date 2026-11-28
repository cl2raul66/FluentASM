using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FluentASM.Lsp;
using FluentASM.Lsp.Diagnostics;
using FluentASM.Lsp.Protocol;
using FluentASM.Lsp.Server;
using FluentASM_IDE.Common.Services;
using FluentASM_IDE.Messages;
using FluentASM_IDE.Models;
using FluentASM_IDE.Services;

namespace FluentASM_IDE.ViewModels;

/// <summary>
/// Estado y comportamiento del componente de edición. Es dueño del documento
/// sincronizado con el LSP, de los diagnósticos, de las sugerencias y de las
/// coordenadas del cursor.
/// </summary>
/// <remarks>
/// <para>
/// El control <c>CodeEditorControl</c> es la vista de este ViewModel: mantiene
/// el <c>RichEditBox</c> y la fachada imperativa <c>ICodeEditor</c>, y notifica
/// aquí los cambios de texto, la selección y el desplazamiento.
/// </para>
/// <para>
/// El texto completo nunca se enlaza por <c>TwoWay</c>: el control lo publica
/// como texto normalizado mediante <see cref="UpdateCurrentText"/>.
/// </para>
/// </remarks>
public partial class EditorViewModel : UiThreadRecipient, IEditorViewModel,
    IRecipient<GoToFirstErrorDiagnosticMessage>,
    IRecipient<GoToFirstWarningDiagnosticMessage>,
    IRecipient<CommandPaletteOpenedMessage>,
    IRecipient<CommandPaletteClosedMessage>,
    IRecipient<ThemeChangedMessage>
{
    /// <summary>
    /// Documento de ejemplo que el editor muestra cuando no hay un proyecto
    /// abierto.
    /// </summary>
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

    private readonly QuickFixService _quickFixService;
    private readonly IncidentRecorder _incidentRecorder;

    private readonly object documentGate = new();
    private int documentVersion;
    private int completionVersion;
    private int completionRequestVersion;
    private Position completionCaret;
    private string documentText = string.Empty;
    private CancellationTokenSource? analysisCts;
    private string? indentationUnit;
    private string savedText = string.Empty;
    private bool commandPaletteOpen;
    private string _documentUri = "file:///" + IdeDefaults.DocumentFileName;

    /// <summary>Desplazamiento vertical del editor, en píxeles.</summary>
    [ObservableProperty]
    public partial double ScrollOffset { get; set; }

    /// <summary>Número de líneas visibles del documento.</summary>
    [ObservableProperty]
    public partial int TotalLines { get; set; } = 32;

    /// <summary>Numeración de líneas del margen.</summary>
    [ObservableProperty]
    public partial string LineNumbersText { get; set; } = string.Empty;

    /// <summary>Visibilidad del overlay de sugerencias.</summary>
    [ObservableProperty]
    public partial bool IsIntelliSenseOpen { get; set; }

    /// <summary>Estado contextual o <em>type-state</em> mostrado por el overlay.</summary>
    [ObservableProperty]
    public partial string CompletionContext { get; set; } = string.Empty;

    /// <summary>Sugerencia seleccionada en el overlay.</summary>
    [ObservableProperty]
    public partial CompletionItem? SelectedCompletion { get; set; }

    /// <summary>Sugerencias vigentes del LSP.</summary>
    [ObservableProperty]
    public partial ObservableCollection<CompletionItem> CompletionItems { get; set; } = [];

    /// <summary>Texto normalizado vigente del documento.</summary>
    [ObservableProperty]
    public partial string CurrentText { get; set; } = string.Empty;

    /// <summary>Línea del cursor (base 1).</summary>
    [ObservableProperty]
    public partial int CurrentLine { get; set; } = 13;

    /// <summary>Columna del cursor (base 1).</summary>
    [ObservableProperty]
    public partial int CurrentCol { get; set; } = 1;

    /// <summary>Contador de errores vigente.</summary>
    [ObservableProperty]
    public partial int DiagnosticErrorCount { get; set; }

    /// <summary>Contador de advertencias vigente.</summary>
    [ObservableProperty]
    public partial int DiagnosticWarningCount { get; set; }

    /// <summary>Indica que el documento tiene cambios sin guardar.</summary>
    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    /// <summary>Servicio de lenguaje compartido por diagnósticos, resaltado, hover y símbolos.</summary>
    public ILanguageService LspServer { get; }

    /// <summary>Diagnósticos vigentes del documento.</summary>
    public ObservableCollection<DiagnosticItem> Diagnostics { get; } = [];

    /// <summary>Acciones rápidas disponibles para el diagnóstico seleccionado.</summary>
    public ObservableCollection<QuickFixAction> QuickFixActions { get; } = [];

    /// <summary>Texto fuente cargado del proyecto abierto (normalizado, <c>\n</c>).</summary>
    public string CurrentSourceText { get; private set; } = string.Empty;

    /// <summary>Ruta del archivo fuente editado; vacía si no hay proyecto abierto.</summary>
    public string SourceFilePath { get; private set; } = string.Empty;

    /// <summary>Indica que se ha cargado un documento real (no el ejemplo en memoria).</summary>
    public bool HasOpenDocument { get; private set; }

    /// <summary>
    /// Se dispara cuando cambian los diagnósticos para que el editor actualice
    /// los subrayados.
    /// </summary>
    public event Action? DiagnosticsChanged;

    /// <summary>Se dispara cuando el usuario pide saltar a un diagnóstico concreto.</summary>
    public event Action<DiagnosticItem>? DiagnosticNavigationRequested;

    /// <summary>
    /// Se dispara cuando otra operación cambió la versión del documento y
    /// conviene reprogramar el análisis.
    /// </summary>
    public event Action? AnalysisRequested;

    /// <summary>
    /// Se dispara con el texto normalizado de un documento que debe mostrarse
    /// en el editor.
    /// </summary>
    public event Action<string>? DocumentOpened;

    /// <summary>
    /// Se dispara cuando el documento se sustituye por completo (por ejemplo,
    /// al aplicar un <em>quick-fix</em>).
    /// </summary>
    /// <param name="sourceText">Texto completo normalizado.</param>
    /// <param name="caretOffset">Posición del caret en el texto normalizado.</param>
    public event Action<string, int>? DocumentTextReplaced;

    /// <summary>Se dispara cuando hay que formatear el documento o la selección.</summary>
    public event Action? FormatRequested;

    /// <summary>Se dispara cuando hay que formatear el documento antes de guardarlo.</summary>
    public event Action? FormatForSaveRequested;

    /// <summary>Se dispara cuando hay que volver a validar el documento.</summary>
    public event Action? RevalidateRequested;

    /// <summary>Se dispara cuando hay que mostrar el outline de símbolos.</summary>
    public event Action? OutlineRequested;

    /// <summary>Se dispara cuando el editor debe recuperar el foco.</summary>
    public event Action? FocusRequested;

    /// <summary>Se dispara cuando hay que aplicar una acción rápida en el caret.</summary>
    public event Action? QuickFixRequested;

    /// <summary>Se dispara cuando cambia el tema efectivo de la aplicación.</summary>
    public event Action<bool>? ThemeChanged;

    /// <summary>
    /// Se dispara cuando el editor pide al <em>shell</em> ejecutar un comando
    /// (por ejemplo <c>/run</c>, <c>/build</c> o <c>/simbolos</c>).
    /// </summary>
    public event Action<string>? ShellCommandRequested;

    /// <summary>
    /// Crea el ViewModel del editor con las dependencias inyectadas por el
    /// contenedor de la aplicación.
    /// </summary>
    public EditorViewModel(
        ILanguageService languageService,
        QuickFixService quickFixService,
        IncidentRecorder incidentRecorder,
        IMessenger messenger)
        : base(messenger)
    {
        ArgumentNullException.ThrowIfNull(languageService);
        ArgumentNullException.ThrowIfNull(quickFixService);
        ArgumentNullException.ThrowIfNull(incidentRecorder);

        LspServer = languageService;
        _quickFixService = quickFixService;
        _incidentRecorder = incidentRecorder;

        InitializeDocument();

        IsActive = true;
    }

    /// <summary>
    /// URI del documento vigente en el LSP (<c>file:///...</c>).
    /// </summary>
    public string DocumentUri
    {
        get => _documentUri;
        private set
        {
            if (string.Equals(_documentUri, value, StringComparison.Ordinal))
            {
                return;
            }

            _documentUri = value;
            Messenger.Send(new DocumentUriChangedMessage(value));
        }
    }

    /// <inheritdoc />
    public IFormattingService? FormattingService => LspServer as IFormattingService;

    /// <summary>
    /// Unidad de indentación efectiva (tabulador o N espacios) resuelta por la
    /// Capa B.
    /// </summary>
    public string IndentationUnit
    {
        get
        {
            if (indentationUnit is not null)
            {
                return indentationUnit;
            }

            try
            {
                indentationUnit = FormattingService?.GetIndentUnit(DocumentUri);
            }
            catch (Exception ex)
            {
                _incidentRecorder.RecordException(ex, "EditorViewModel.IndentationUnit", DocumentUri);
                indentationUnit = null;
            }

            if (string.IsNullOrEmpty(indentationUnit))
            {
                indentationUnit = "    ";
            }

            return indentationUnit;
        }
    }

    /// <summary>
    /// Sincroniza el documento con el snapshot vigente del LSP antes de pedir
    /// un formateo <em>on-type</em> o manual.
    /// </summary>
    public void SyncDocumentForFormatting(string normalizedText)
    {
        ArgumentNullException.ThrowIfNull(normalizedText);
        UpdateDocument(normalizedText);
    }

    /// <summary>Actualiza el número de líneas del margen.</summary>
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

    /// <summary>Actualiza la posición del cursor y la publica en la barra de estado.</summary>
    public void UpdateCursorPosition(int line, int col)
    {
        CurrentLine = Math.Max(1, line);
        CurrentCol = Math.Max(1, col);
        Messenger.Send(new CursorPositionChangedMessage(CurrentLine, CurrentCol));
    }

    /// <summary>
    /// Publica el texto vigente del editor y recalcula el estado de cambios
    /// sin guardar.
    /// </summary>
    public void UpdateCurrentText(string normalizedText)
    {
        ArgumentNullException.ThrowIfNull(normalizedText);

        if (!string.Equals(CurrentText, normalizedText, StringComparison.Ordinal))
        {
            CurrentText = normalizedText;
        }

        RefreshDirtyState();
    }

    /// <summary>
    /// Marca el texto vigente como guardado: es la línea base contra la que se
    /// compara el documento para saber si hay cambios pendientes.
    /// </summary>
    public void MarkSaved(string normalizedText)
    {
        ArgumentNullException.ThrowIfNull(normalizedText);

        savedText = normalizedText;
        RefreshDirtyState();
    }

    /// <summary>
    /// Carga un documento real en el editor: actualiza el LSP, la URI y el
    /// estado de cambios sin guardar.
    /// </summary>
    public void OpenDocument(string normalizedText, string sourceFilePath)
    {
        ArgumentNullException.ThrowIfNull(normalizedText);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);

        string fullPath = Path.GetFullPath(sourceFilePath);
        SourceFilePath = fullPath;
        HasOpenDocument = true;

        DocumentUri = new Uri(fullPath).AbsoluteUri;

        // El motor de formateo resuelve .editorconfig desde la ruta real.
        if (LspServer is FluentLanguageServer languageServer)
        {
            languageServer.DocumentFilePath = fullPath;
        }

        indentationUnit = null;

        lock (documentGate)
        {
            documentText = normalizedText;
            documentVersion++;
            LspServer.OpenDocument(DocumentUri, normalizedText, documentVersion);
        }

        CurrentSourceText = normalizedText;
        savedText = normalizedText;
        CurrentText = normalizedText;
        RefreshDirtyState(force: true);

        DocumentOpened?.Invoke(normalizedText);
    }

    /// <summary>
    /// Muestra el documento de ejemplo en memoria. Solo se usa cuando no hay
    /// ningún proyecto abierto.
    /// </summary>
    public void LoadInitialSample()
    {
        string normalized = DocumentText.Normalize(InitialSampleAsm);
        savedText = normalized;
        CurrentText = normalized;
        RefreshDirtyState(force: true);

        DocumentOpened?.Invoke(normalized);
    }

    /// <summary>Solicita el formateo del documento o de la selección.</summary>
    public void RequestFormat() => FormatRequested?.Invoke();

    /// <summary>Solicita el formateo del documento antes de guardarlo.</summary>
    public void RequestFormatForSave() => FormatForSaveRequested?.Invoke();

    /// <summary>Solicita una nueva validación del documento.</summary>
    public void RequestRevalidate() => RevalidateRequested?.Invoke();

    /// <summary>Solicita el outline de símbolos del documento.</summary>
    public void RequestOutline() => OutlineRequested?.Invoke();

    /// <summary>Solicita que el editor recupere el foco.</summary>
    public void RequestFocus() => FocusRequested?.Invoke();

    /// <summary>Solicita aplicar la acción rápida bajo el caret.</summary>
    public void RequestQuickFixAtCaret() => QuickFixRequested?.Invoke();

    /// <summary>
    /// Pide al <em>shell</em> que ejecute un comando de la paleta (por ejemplo
    /// <c>/run</c>, <c>/build</c> o <c>/simbolos</c>).
    /// </summary>
    public void RequestShellCommand(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ShellCommandRequested?.Invoke(command);
    }

    /// <summary>Navega al primer diagnóstico del documento.</summary>
    [RelayCommand]
    public void GoToFirstDiagnostic()
    {
        DiagnosticItem? diagnostic = FirstErrorDiagnostic ?? FirstDiagnostic;
        if (diagnostic is not null)
        {
            DiagnosticNavigationRequested?.Invoke(diagnostic);
        }
    }

    /// <summary>Navega al primer error del documento.</summary>
    [RelayCommand]
    public void GoToFirstErrorDiagnostic()
    {
        DiagnosticItem? diagnostic = FirstErrorDiagnostic;
        if (diagnostic is not null)
        {
            DiagnosticNavigationRequested?.Invoke(diagnostic);
        }
    }

    /// <summary>Navega a la primera advertencia del documento.</summary>
    [RelayCommand]
    public void GoToFirstWarningDiagnostic()
    {
        DiagnosticItem? diagnostic = FirstWarningDiagnostic;
        if (diagnostic is not null)
        {
            DiagnosticNavigationRequested?.Invoke(diagnostic);
        }
    }

    /// <summary>Cierra el overlay de sugerencias e invalida la sesión.</summary>
    [RelayCommand]
    public void CloseIntelliSense()
    {
        InvalidateCompletions();
    }

    /// <summary>
    /// Invalida por completo la sesión de completado: suggestions, selección,
    /// contexto, versión y caret.
    /// </summary>
    public void InvalidateCompletions()
    {
        Interlocked.Increment(ref completionRequestVersion);
        Volatile.Write(ref completionVersion, 0);
        completionCaret = default;
        CompletionContext = string.Empty;
        SelectedCompletion = null;
        CompletionItems = [];
        IsIntelliSenseOpen = false;
    }

    /// <summary>Selecciona una sugerencia del overlay.</summary>
    [RelayCommand]
    public void SelectCompletion(CompletionItem? item)
    {
        if (item is null)
        {
            return;
        }

        foreach (CompletionItem candidate in CompletionItems)
        {
            candidate.IsSelected = candidate == item;
        }

        SelectedCompletion = item;
    }

    /// <summary>
    /// Aplica el tema a las sugerencias y a los pinceles derivados.
    /// </summary>
    public void UpdateTheme(bool isDark)
    {
        CompletionItem.IsDarkTheme = isDark;

        foreach (CompletionItem item in CompletionItems)
        {
            item.RefreshTheme();
        }

        ThemeChanged?.Invoke(isDark);
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
    /// texto vigente del editor.
    /// </summary>
    public void ApplyAnalysis(DocumentAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ApplyDiagnostics(result.Diagnostics);
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
            SetStatusText("No se pudo aplicar la acción rápida: posición inválida.");
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

        CurrentSourceText = normalized;
        UpdateCurrentText(normalized);
        SetStatusText($"Acción rápida aplicada: {action.Title}");
        QuickFixActions.Clear();

        // Notificar a la vista con el nuevo texto y la posición del caret
        // (el final del texto reemplazado) para que sincronice el editor.
        int caretOffset = Math.Clamp(
            startOffset + action.ReplacementText.Length,
            0,
            normalized.Length);
        DocumentTextReplaced?.Invoke(normalized, caretOffset);

        // Reprogramar el análisis para actualizar diagnósticos y resaltado.
        AnalysisRequested?.Invoke();
    }

    /// <summary>
    /// Solicita suggestions sin ejecutar lexer, parser ni semántica en el hilo
    /// de interfaz.
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

            // La comprobación vive dentro del try: una cancelación posterior al
            // await no debe escapar del método ni llegar al llamante.
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
        }
        catch (OperationCanceledException)
        {
            // Cancelación cooperativa: el usuario siguió escribiendo o movió el
            // caret. Es rutinaria y no se registra como incidencia.
            return;
        }
        catch (Exception ex)
        {
            _incidentRecorder.RecordException(
                ex,
                "EditorViewModel.RequestLspCompletionsAsync",
                DocumentUri);
            return;
        }

        if (Volatile.Read(ref documentVersion) != previousVersion)
        {
            AnalysisRequested?.Invoke();
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

    /// <summary>Diagnóstico más severo disponible, si lo hay.</summary>
    public DiagnosticItem? FirstDiagnostic => Diagnostics.Count > 0 ? Diagnostics[0] : null;

    /// <summary>Primer error del documento, si lo hay.</summary>
    public DiagnosticItem? FirstErrorDiagnostic => Diagnostics.FirstOrDefault(diagnostic => diagnostic.IsError);

    /// <summary>Primera advertencia del documento, si la hay.</summary>
    public DiagnosticItem? FirstWarningDiagnostic => Diagnostics.FirstOrDefault(diagnostic => diagnostic.IsWarning);

    void IRecipient<GoToFirstErrorDiagnosticMessage>.Receive(GoToFirstErrorDiagnosticMessage _)
        => RunOnUiThread(GoToFirstErrorDiagnostic);

    void IRecipient<GoToFirstWarningDiagnosticMessage>.Receive(GoToFirstWarningDiagnosticMessage _)
        => RunOnUiThread(GoToFirstWarningDiagnostic);

    void IRecipient<CommandPaletteOpenedMessage>.Receive(CommandPaletteOpenedMessage _)
        => RunOnUiThread(() =>
        {
            commandPaletteOpen = true;
            InvalidateCompletions();
        });

    void IRecipient<CommandPaletteClosedMessage>.Receive(CommandPaletteClosedMessage _)
        => RunOnUiThread(() => commandPaletteOpen = false);

    void IRecipient<ThemeChangedMessage>.Receive(ThemeChangedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        RunOnUiThread(() => UpdateTheme(message.IsDark));
    }

    /// <summary>
    /// Inicializa el documento en memoria con el ejemplo y lo abre en el LSP.
    /// </summary>
    private void InitializeDocument()
    {
        string normalized = DocumentText.Normalize(InitialSampleAsm);
        lock (documentGate)
        {
            documentText = normalized;
            documentVersion = 1;
            DocumentUri = "file:///" + IdeDefaults.DocumentFileName;
            if (LspServer is FluentLanguageServer languageServer)
            {
                languageServer.DocumentFilePath = null;
            }

            indentationUnit = null;
            LspServer.OpenDocument(DocumentUri, normalized, documentVersion);
        }

        savedText = normalized;
        CurrentText = normalized;
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
        foreach (LspDiagnostic diagnostic in diagnostics)
        {
            var start = diagnostic.Range.Start;
            var end = diagnostic.Range.End;
            bool isError = diagnostic.Severity is DiagnosticSeverity.Error;

            if (isError)
            {
                errors++;
            }
            else if (diagnostic.Severity is DiagnosticSeverity.Warning)
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
                IsWarning = diagnostic.Severity is DiagnosticSeverity.Warning
            });
        }

        DiagnosticErrorCount = errors;
        DiagnosticWarningCount = warnings;
        Messenger.Send(new DiagnosticsSummaryChangedMessage(errors, warnings));
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
        foreach (QuickFixAction action in actions)
        {
            QuickFixActions.Add(action);
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
        if (commandPaletteOpen)
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
        foreach (LspCompletionItem lspItem in completions.Items)
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
    /// Recalcula el estado de cambios sin guardar y lo publica por mensajería.
    /// </summary>
    private void RefreshDirtyState(bool force = false)
    {
        bool dirty = !string.IsNullOrEmpty(SourceFilePath)
            && !string.Equals(CurrentText, savedText, StringComparison.Ordinal);

        if (!force && dirty == IsDirty)
        {
            return;
        }

        IsDirty = dirty;
        Messenger.Send(new DocumentDirtyChangedMessage(dirty));
    }

    /// <summary>Publica un mensaje de estado en la barra de estado.</summary>
    public void SetStatusText(string text)
        => Messenger.Send(new StatusTextChangedMessage(text));

    /// <summary>
    /// Registra una incidencia del componente de editor.
    /// </summary>
    public void RecordException(Exception exception, string source)
        => _incidentRecorder.RecordException(exception, source, DocumentUri);

    /// <summary>
    /// Registra una incidencia informativa del componente de editor.
    /// </summary>
    public void RecordInfo(string code, string message, string source, string? context = null)
        => _incidentRecorder.RecordInfo(code, message, source, context);
}
