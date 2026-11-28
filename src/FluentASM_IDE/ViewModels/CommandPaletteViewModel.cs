using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using FluentASM.Lsp;
using FluentASM.Lsp.Diagnostics;
using FluentASM.Lsp.Protocol;
using FluentASM_IDE.Messages;
using FluentASM_IDE.Models;

namespace FluentASM_IDE.ViewModels;

/// <summary>
/// Estado y catálogo de la paleta de comandos. Vive desacoplado del shell:
/// filtra los comandos y, cuando el usuario elige uno, publica
/// <see cref="PaletteCommandExecutedMessage"/> para que el shell decida la
/// acción concreta.
/// </summary>
public partial class CommandPaletteViewModel : UiThreadRecipient,
    IRecipient<DocumentUriChangedMessage>,
    IRecipient<FormatOnSaveChangedMessage>,
    IRecipient<ThemeChangedMessage>
{
    private readonly ILanguageService _languageService;
    private readonly IncidentRecorder _incidentRecorder;
    private string _documentUri = "file:///" + IdeDefaults.DocumentFileName;

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial string CommandQuery { get; set; } = "/";

    public ObservableCollection<CommandPaletteItem> AllCommands { get; } = [];

    public ObservableCollection<CommandPaletteItem> FilteredCommands { get; } = [];

    public CommandPaletteViewModel(
        ILanguageService languageService,
        IncidentRecorder incidentRecorder,
        IMessenger messenger)
        : base(messenger)
    {
        ArgumentNullException.ThrowIfNull(languageService);
        ArgumentNullException.ThrowIfNull(incidentRecorder);

        _languageService = languageService;
        _incidentRecorder = incidentRecorder;

        InitializeCommands();
        IsActive = true;
    }

    /// <summary>Abre o cierra la paleta conservando el comportamiento de Ctrl+P.</summary>
    public void Toggle()
    {
        IsOpen = !IsOpen;
        if (IsOpen)
        {
            CommandQuery = "/";
            FilterCommands(CommandQuery);
            Messenger.Send(new CommandPaletteOpenedMessage());
        }
    }

    /// <summary>Abre la paleta con un prefijo ya escrito (por ejemplo, «/»).</summary>
    public void OpenWithPrefix(string prefix)
    {
        IsOpen = true;
        CommandQuery = prefix;
        FilterCommands(CommandQuery);
        Messenger.Send(new CommandPaletteOpenedMessage());
    }

    /// <summary>Cierra la paleta sin ejecutar ningún comando.</summary>
    public void Close() => IsOpen = false;

    /// <summary>
    /// Cierra la paleta y notifica al shell qué elemento se ha elegido.
    /// </summary>
    public void Execute(CommandPaletteItem? item)
    {
        if (item is null)
        {
            return;
        }

        IsOpen = false;
        Messenger.Send(new PaletteCommandExecutedMessage(item));
    }

    /// <summary>Ejecuta un comando por su texto (usado por atajos del editor).</summary>
    public void ExecuteCommand(string command)
    {
        CommandPaletteItem? item = AllCommands.FirstOrDefault(candidate =>
            string.Equals(candidate.Command, command, StringComparison.OrdinalIgnoreCase));

        if (item is not null)
        {
            Execute(item);
        }
    }

    /// <summary>
    /// Sustituye la lista visible por los últimos sucesos registrados.
    /// Devuelve <c>false</c> si el registro está vacío.
    /// </summary>
    public bool ShowIncidentsLog()
    {
        var incidents = _incidentRecorder.ReadCurrentLog().ToList();
        if (incidents.Count == 0)
        {
            return false;
        }

        FilteredCommands.Clear();
        foreach (var incident in incidents.TakeLast(20).Reverse())
        {
            string time = incident.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff");
            string levelIcon = incident.Level switch
            {
                IncidentLevel.Error => "❌ ",
                IncidentLevel.Warning => "⚠️ ",
                _ => "ℹ️ "
            };

            FilteredCommands.Add(new CommandPaletteItem
            {
                Command = $"{levelIcon}[{time}] {incident.Code}",
                Description = incident.Message,
                Shortcut = incident.Source,
                Category = "Sucesos",
                IconGlyph = incident.Level is IncidentLevel.Error
                    ? "\uE783"
                    : (incident.Level is IncidentLevel.Warning ? "\uE7BA" : "\uE8A6"),
                IconColorHex = incident.Level is IncidentLevel.Error
                    ? "#FFB4AB"
                    : (incident.Level is IncidentLevel.Warning ? "#FFD4BE" : "#60CDFF"),
                IsActive = false,
                IsCommand = false
            });
        }

        if (FilteredCommands.Count > 0)
        {
            FilteredCommands[0].IsActive = true;
        }

        IsOpen = true;
        CommandQuery = "/sucesos";
        return true;
    }

    partial void OnCommandQueryChanged(string value) => FilterCommands(value);

    partial void OnIsOpenChanged(bool value)
    {
        if (!value)
        {
            // El editor solo puede reabrir el overlay de sugerencias cuando la
            // paleta ya está cerrada.
            Messenger.Send(new CommandPaletteClosedMessage());
        }
    }

    void IRecipient<DocumentUriChangedMessage>.Receive(DocumentUriChangedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        RunOnUiThread(() => _documentUri = message.DocumentUri);
    }

    void IRecipient<FormatOnSaveChangedMessage>.Receive(FormatOnSaveChangedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        RunOnUiThread(() => UpdateFormatOnSaveCommand(message.IsFormatOnSave));
    }

    void IRecipient<ThemeChangedMessage>.Receive(ThemeChangedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        RunOnUiThread(() =>
        {
            CommandPaletteItem.IsDarkTheme = message.IsDark;
            foreach (var command in AllCommands)
            {
                command.RefreshTheme();
            }
        });
    }

    private void UpdateFormatOnSaveCommand(bool formatOnSave)
    {
        CommandPaletteItem? item = AllCommands.FirstOrDefault(command =>
            command.Command == "/format al guardar");

        if (item is null)
        {
            return;
        }

        item.Description = formatOnSave
            ? "Formatear al guardar activado: pulsa para desactivarlo"
            : "Alternar el formateo automático al guardar (Ctrl+S)";
    }

    private void FilterCommands(string query)
    {
        FilteredCommands.Clear();
        var trimmed = (query ?? string.Empty).Trim();

        // Prefijo @: mostrar símbolos del documento en lugar de comandos.
        if (trimmed.StartsWith('@'))
        {
            var symbolQuery = trimmed.Length > 1 ? trimmed[1..] : string.Empty;
            FilterSymbols(symbolQuery);
        }
        else
        {
            foreach (var command in AllCommands)
            {
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed == "/" ||
                    command.Command.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                    command.Description.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                    command.Category.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    FilteredCommands.Add(command);
                }
            }
        }

        if (FilteredCommands.Count > 0)
        {
            foreach (var command in FilteredCommands)
            {
                command.IsActive = false;
            }

            FilteredCommands[0].IsActive = true;
        }
    }

    private void FilterSymbols(string query)
    {
        var symbols = _languageService.GetDocumentSymbols(_documentUri);
        var trimmed = (query ?? string.Empty).Trim();

        foreach (var symbol in symbols)
        {
            if (MatchesSymbol(symbol, trimmed))
            {
                AddSymbolToPalette(symbol, 0);
            }
        }
    }

    private static bool MatchesSymbol(DocumentSymbol symbol, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return symbol.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               (symbol.Detail?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void AddSymbolToPalette(DocumentSymbol symbol, int indentLevel)
    {
        var indent = new string(' ', indentLevel * 2);
        var kindLabel = symbol.Kind switch
        {
            SymbolKind.Function => "ƒ ",
            SymbolKind.Field => "• ",
            SymbolKind.Method => "▶ ",
            _ => ""
        };

        FilteredCommands.Add(new CommandPaletteItem
        {
            Command = indent + kindLabel + symbol.Name,
            Description = symbol.Detail ?? string.Empty,
            Shortcut = "",
            Category = "Símbolos (@)",
            IconGlyph = symbol.Kind is SymbolKind.Function ? "\uE749" : "\uE8A6",
            IconColorHex = symbol.Kind is SymbolKind.Function ? "#81CFFF" : "#B4E4FF",
            IsActive = false,
            IsCommand = false
        });

        if (symbol.Children is { Count: > 0 } children)
        {
            foreach (var child in children)
            {
                AddSymbolToPalette(child, indentLevel + 1);
            }
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
            Command = "/format",
            Description = "Formatear el documento o la selección con el Pacto v1",
            Shortcut = "Ctrl+K, Ctrl+D",
            Category = "Comandos (/)",
            IconGlyph = "\uE8A1",
            IconColorHex = "#60CDFF",
            IsActive = false,
            IsCommand = true
        });

        AllCommands.Add(new CommandPaletteItem
        {
            Command = "/format al guardar",
            Description = "Alternar el formateo automático al guardar (Ctrl+S)",
            Shortcut = "",
            Category = "Comandos (/)",
            IconGlyph = "\uE74E",
            IconColorHex = "#B4E4FF",
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
            Command = "/simbolos",
            Description = "Navegar a símbolo (datos, rutinas, etiquetas) — Ctrl+Shift+O",
            Shortcut = "Ctrl+Shift+O",
            Category = "Comandos (/)",
            IconGlyph = "\uE749",
            IconColorHex = "#60CDFF",
            IsActive = false,
            IsCommand = true
        });

        AllCommands.Add(new CommandPaletteItem
        {
            Command = "/sucesos",
            Description = "Ver registro de sucesos (incidentes internos, errores de mapeo, FASM9xxx)",
            Shortcut = "",
            Category = "Comandos (/)",
            IconGlyph = "\uE8A6",
            IconColorHex = "#FFD4BE",
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
}
