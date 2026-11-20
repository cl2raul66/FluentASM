using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentASM.Lsp.Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FluentASM_IDE.Models;

/// <summary>
/// Interfaz mínima que los services (Common) necesitan del ViewModel principal.
/// Evita dependencia circular: Common no referencia al proyecto principal.
/// </summary>
public interface IMainPageViewModel
{
    // Diagnostics
    ObservableCollection<DiagnosticItem> Diagnostics { get; }
    int DiagnosticErrorCount { get; }
    int DiagnosticWarningCount { get; }
    event Action? DiagnosticsChanged;
    event Action<DiagnosticItem>? DiagnosticNavigationRequested;

    // IntelliSense
    ObservableCollection<CompletionItem> CompletionItems { get; }
    CompletionItem? SelectedCompletion { get; set; }
    bool IsIntelliSenseOpen { get; set; }
    void CloseIntelliSense();
    void RequestLspCompletions(string sourceCode, bool openPopup = true);

    // Command Palette
    bool IsCommandPaletteOpen { get; set; }
    string CommandQuery { get; set; }
    ObservableCollection<CommandPaletteItem> FilteredCommands { get; }
    void CloseCommandPalette();
    void ToggleCommandPalette();
    void ExecutePaletteItem(CommandPaletteItem? item);
    void SelectNextPaletteItem();
    void SelectPrevPaletteItem();
    void ExecuteActivePaletteItem();
    void ExecuteCommand(string command);

    // Status/Theme
    string StatusText { get; set; }
    void UpdateTheme(bool isDark);

    // Editor coordinates
    void UpdateCursorPosition(int line, int col);
    void UpdateLineCount(int count);

    // LSP Sync
    event Action? RevalidationRequested;
    event Action? AnalysisRequested;

    /// <summary>
    /// Sincroniza el documento con una versión incremental y devuelve el
    /// análisis vigente, o <c>null</c> si una versión más reciente lo descartó.
    /// </summary>
    Task<DocumentAnalysisResult?> AnalyzeDocumentAsync(string sourceCode, CancellationToken cancellationToken = default);
}



