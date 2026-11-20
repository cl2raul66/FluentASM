using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentASM.Lsp;
using FluentASM.Lsp.Protocol;
using FluentASM.Lsp.Server;
using FluentASM_IDE.Models;
using FluentASM_IDE.Common.Services;
using FluentASM_IDE.Services;
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

    public const string DocumentFileName = "miCalculadoraCli.fasm";
    public const string WindowTitle = "FluentASM IDE - " + DocumentFileName;

    [ObservableProperty]
    public partial string DocumentTitle { get; set; } = DocumentFileName;

    [ObservableProperty]
    public partial string Architecture { get; set; } = "x64 Fluent";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Listo";

    [ObservableProperty]
    public partial string Encoding { get; set; } = "UTF-8";

    [ObservableProperty]
    public partial string LineEnding { get; set; } = "CRLF";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveLineMargin))]
    [NotifyPropertyChangedFor(nameof(IntelliSenseMargin))]
    [NotifyPropertyChangedFor(nameof(CursorPositionFormatted))]
    public partial int CurrentLine { get; set; } = 13;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CursorPositionFormatted))]
    public partial int CurrentCol { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveLineMargin))]
    [NotifyPropertyChangedFor(nameof(IntelliSenseMargin))]
    public partial double ScrollOffset { get; set; } = 0;

    [ObservableProperty]
    public partial int TotalLines { get; set; } = 32;

    [ObservableProperty]
    public partial string LineNumbersText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsCommandPaletteOpen { get; set; } = false;

    [ObservableProperty]
    public partial bool IsIntelliSenseOpen { get; set; } = false;

    [ObservableProperty]
    public partial string CommandQuery { get; set; } = "/";

    public ILanguageService LspServer { get; } = new FluentLanguageServer();

    private int documentVersion = 1;

    public const string DocumentUri = "file:///" + DocumentFileName;

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

    public Thickness IntelliSenseMargin => new Thickness(200, 8 + CurrentLine * 22.0 - ScrollOffset + 4, 0, 0);

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
    public ObservableCollection<CompletionItem> CompletionItems { get; } = new();

    /// <summary>
    /// Se dispara cuando la paleta pide volver a validar el documento actual.
    /// </summary>
    public event Action? RevalidationRequested;

    /// <summary>
    /// Se dispara cuando cambian los diagnósticos para que la vista actualice
    /// los subrayados del editor.
    /// </summary>
    public event Action? DiagnosticsChanged;

    /// <summary>
    /// Se dispara cuando el usuario pide saltar a un diagnóstico concreto.
    /// </summary>
    public event Action<DiagnosticItem>? DiagnosticNavigationRequested;

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

    public MainPageViewModel()
    {
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
            IsIntelliSenseOpen = false;
        }
    }

    [RelayCommand]
    public void OpenCommandPaletteWithPrefix(string prefix)
    {
        IsCommandPaletteOpen = true;
        CommandQuery = prefix;
        FilterCommands(CommandQuery);
        IsIntelliSenseOpen = false;
    }

    [RelayCommand]
    public void CloseCommandPalette()
    {
        IsCommandPaletteOpen = false;
    }

    [RelayCommand]
    public void ToggleIntelliSense()
    {
        IsIntelliSenseOpen = !IsIntelliSenseOpen;
        if (IsIntelliSenseOpen)
        {
            IsCommandPaletteOpen = false;
        }
    }

    [RelayCommand]
    public void CloseIntelliSense()
    {
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
        LspServer.OpenDocument(DocumentUri, DocumentText.Normalize(InitialSampleAsm), 1);
        RequestLspCompletions(InitialSampleAsm, openPopup: false);
    }

    public void SyncDocumentWithLsp(string sourceCode)
    {
        string normalized = DocumentText.Normalize(sourceCode);
        documentVersion++;
        LspServer.UpdateDocument(DocumentUri, normalized, documentVersion);
        ApplyDiagnostics(LspServer.ValidateDocument(DocumentUri));
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
    }

    public void RequestLspCompletions(string sourceCode, bool openPopup = true)
    {
        documentVersion++;
        LspServer.UpdateDocument(DocumentUri, DocumentText.Normalize(sourceCode), documentVersion);
        var lspCompletions = LspServer.GetCompletions(DocumentUri, new Position(CurrentLine - 1, CurrentCol - 1));

        CompletionItems.Clear();
        foreach (var lspItem in lspCompletions.Items)
        {
            CompletionItems.Add(CompletionItem.FromLsp(lspItem));
        }

        if (CompletionItems.Count > 0)
        {
            SelectCompletion(CompletionItems[0]);
            if (openPopup)
            {
                IsIntelliSenseOpen = true;
            }
        }
    }
}


