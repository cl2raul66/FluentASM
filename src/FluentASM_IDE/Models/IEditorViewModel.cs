using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using FluentASM.Lsp;
using FluentASM_IDE.Services;

namespace FluentASM_IDE.Models;

/// <summary>
/// Contrato del componente de edición que consumen los servicios de la vista
/// (resaltado, adornos de diagnóstico, hover, outline, teclado y formateo).
/// <para>
/// El editor es un componente independiente del <em>shell</em>: habla con su
/// propio <see cref="EditorViewModel"/> y nunca con
/// <see cref="MainPageViewModel"/>. Los comandos que pertenecen al shell
/// (abrir, guardar, ejecutar) se solicitan mediante
/// <see cref="RequestShellCommand"/>.
/// </para>
/// </summary>
public interface IEditorViewModel
{
    // Diagnostics
    ObservableCollection<DiagnosticItem> Diagnostics { get; }
    event Action? DiagnosticsChanged;
    event Action<DiagnosticItem>? DiagnosticNavigationRequested;

    // IntelliSense
    bool IsIntelliSenseOpen { get; set; }
    void CloseIntelliSense();

    // Acciones rápidas
    ObservableCollection<QuickFixAction> QuickFixActions { get; }
    void ApplyQuickFix(QuickFixAction action);

    // Documento y servicio de lenguaje
    ILanguageService LspServer { get; }
    string DocumentUri { get; }

    /// <summary>Texto normalizado vigente del documento.</summary>
    string CurrentText { get; }

    /// <summary>Indica que el documento tiene cambios sin guardar.</summary>
    bool IsDirty { get; }

    // Formateo (Capa B)
    IFormattingService? FormattingService { get; }
    string IndentationUnit { get; }
    void SyncDocumentForFormatting(string normalizedText);

    // Coordenadas del editor
    double ScrollOffset { get; }
    int CurrentLine { get; }
    int CurrentCol { get; }
    void UpdateCursorPosition(int line, int col);
    void UpdateLineCount(int count);

    /// <summary>
    /// Sincroniza el documento con una versión incremental y devuelve el
    /// análisis vigente, o <c>null</c> si una versión más reciente lo descartó.
    /// </summary>
    Task<DocumentAnalysisResult?> AnalyzeDocumentAsync(
        string sourceCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pide al <em>shell</em> que ejecute un comando global (por ejemplo
    /// <c>/abrir</c>, <c>/guardar</c>, <c>/run</c> o <c>/simbolos</c>).
    /// </summary>
    void RequestShellCommand(string command);
}
