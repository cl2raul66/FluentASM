using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentASM_IDE.Models;
using Microsoft.UI.Xaml;

namespace FluentASM_IDE.ViewModels;

public partial class MainPageViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string DocumentTitle { get; set; } = "CalculoPuro.fasm";

    [ObservableProperty]
    public partial string WindowTitle { get; set; } = "FluentASM IDE - CalculoPuro.fasm";

    [ObservableProperty]
    public partial string Architecture { get; set; } = "x64 Fluent";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Ready";

    [ObservableProperty]
    public partial string IndentInfo { get; set; } = "Spaces: 4";

    [ObservableProperty]
    public partial string Encoding { get; set; } = "UTF-8";

    [ObservableProperty]
    public partial string LineEnding { get; set; } = "CRLF";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveLineMargin))]
    [NotifyPropertyChangedFor(nameof(CursorPositionFormatted))]
    public partial int CurrentLine { get; set; } = 8;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CursorPositionFormatted))]
    public partial int CurrentCol { get; set; } = 14;

    [ObservableProperty]
    public partial int TotalLines { get; set; } = 21;

    [ObservableProperty]
    public partial string LineNumbersText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsCommandPaletteOpen { get; set; } = false;

    [ObservableProperty]
    public partial bool IsIntelliSenseOpen { get; set; } = false;

    [ObservableProperty]
    public partial string CommandQuery { get; set; } = "/";

    public Thickness ActiveLineMargin => new Thickness(0, (CurrentLine - 1) * 22, 0, 0);

    public string CursorPositionFormatted => $"Ln {CurrentLine}, Col {CurrentCol}";

    [ObservableProperty]
    public partial CompletionItem? SelectedCompletion { get; set; }

    public ObservableCollection<CommandPaletteItem> AllCommands { get; } = new();
    public ObservableCollection<CommandPaletteItem> FilteredCommands { get; } = new();
    public ObservableCollection<CompletionItem> CompletionItems { get; } = new();

    public const string InitialSampleAsm =
@"program CalculoPuro
    section .execute
    entry main

    routine Factorial
        parameter n              // n ≡ Gpr1
        Gpr0.copy(Bit 1)        // acumulador = 1
        @loop:
        Gpr0.imult<2,0>(Gpr1)   // Gpr2:Gpr0 *= n (trunca)
        Gpr1.sub(Bit 1)         // n--
        Gpr1.cmp(Bit 0)         // comparar n con 0
        .flowJump(@loop if not rFlags.zero)
        .flowStackJump()        // retorna Gpr0
    end

    routine main
        Gpr1.copy(Bit 5)
        .flowCall(Factorial)    // Gpr0 = 120
        .flowStackJump()
    end
end";

    public MainPageViewModel()
    {
        UpdateLineCount(21);
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
    public async Task ExecuteActivePaletteItem()
    {
        var active = FilteredCommands.FirstOrDefault(c => c.IsActive) ?? FilteredCommands.FirstOrDefault();
        if (active != null)
        {
            await ExecutePaletteItem(active);
        }
    }

    [RelayCommand]
    public async Task ExecutePaletteItem(CommandPaletteItem? item)
    {
        if (item == null) return;
        IsCommandPaletteOpen = false;

        switch (item.Command.ToLowerInvariant())
        {
            case "/build":
                StatusText = "Compilando CalculoPuro.fasm (x64 nativo)...";
                await Task.Delay(850);
                StatusText = "Compilación exitosa: CalculoPuro.bin (0 errores, 0 warnings)";
                break;

            case "/run":
                StatusText = "Ejecutando Factorial(5) en entorno aislado...";
                await Task.Delay(800);
                StatusText = "Resultado: Gpr0 = 120 (código de salida: 0)";
                break;

            case "/disasm":
                StatusText = "Generando desensamblado nativo x64 de CalculoPuro...";
                await Task.Delay(600);
                StatusText = $"Desensamblado listo: {TotalLines} líneas analizadas";
                break;

            case "/format":
                StatusText = "Sintaxis FluentASM formateada";
                break;

            default:
                StatusText = $"Referencia: {item.Command}";
                break;
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

        // Section: Comandos (/)
        AllCommands.Add(new CommandPaletteItem
        {
            Command = "/build",
            Description = "Compilar binario x64 con compilador FluentASM",
            Shortcut = "Ctrl+B",
            Category = "Comandos (/)",
            IconGlyph = "\uE756",
            IconColorHex = "#60CDFF",
            IsActive = true,
            IsCommand = true
        });

        AllCommands.Add(new CommandPaletteItem
        {
            Command = "/run",
            Description = "Ejecutar routine main en entorno aislado",
            Shortcut = "F5",
            Category = "Comandos (/)",
            IconGlyph = "\uE768",
            IconColorHex = "#81CFFF",
            IsActive = false,
            IsCommand = true
        });

        AllCommands.Add(new CommandPaletteItem
        {
            Command = "/disasm",
            Description = "Ver código máquina x64 generado y offsets",
            Shortcut = "Ctrl+D",
            Category = "Comandos (/)",
            IconGlyph = "\uE8A5",
            IconColorHex = "#81CFFF",
            IsActive = false,
            IsCommand = true
        });

        AllCommands.Add(new CommandPaletteItem
        {
            Command = "/format",
            Description = "Formatear indentación y bloques de código fluido",
            Shortcut = "Shift+Alt+F",
            Category = "Comandos (/)",
            IconGlyph = "\uE8EC",
            IconColorHex = "#81CFFF",
            IsActive = false,
            IsCommand = true
        });

        // Section: Ayuda y Referencia
        AllCommands.Add(new CommandPaletteItem
        {
            Command = "Gpr0..Gpr15",
            Description = "Register File unificado x64 (GPR) y vistas (.low32, .low8)",
            Shortcut = "",
            Category = "Ayuda y Referencia",
            IconGlyph = "\uE713",
            IconColorHex = "#81CFFF",
            IsActive = false,
            IsCommand = false
        });

        AllCommands.Add(new CommandPaletteItem
        {
            Command = ".flowJump & .flowCall",
            Description = "Control de flujo estructurado y llamadas ABI",
            Shortcut = "",
            Category = "Ayuda y Referencia",
            IconGlyph = "\uE8EE",
            IconColorHex = "#FFD4BE",
            IsActive = false,
            IsCommand = false
        });

        AllCommands.Add(new CommandPaletteItem
        {
            Command = "rFlags (zero, carry...)",
            Description = "Banderas de condición para saltos fluidos",
            Shortcut = "",
            Category = "Ayuda y Referencia",
            IconGlyph = "\uE82D",
            IconColorHex = "#FFD4BE",
            IsActive = false,
            IsCommand = false
        });

        FilterCommands("/");
    }

    private void InitializeIntelliSense()
    {
        CompletionItems.Clear();

        var item1 = new CompletionItem
        {
            Label = "imult<2,0>(Gpr1)",
            Kind = "method",
            Detail = "multiplicación con tupla de registros Gpr2:Gpr0 = Gpr0 * Gpr1",
            Signature = "Gpr0.imult<2,0>(multiplicando: Gpr1)",
            Inputs = "Gpr0 (acumulador), Gpr1 (multiplicando)",
            Modifies = "Gpr0 (low 64b), Gpr2 (high 64b), rFlags.overflow",
            Returns = "Gpr2:Gpr0 (producto de 128 bits)",
            IconGlyph = "\uE756",
            IconColorHex = "#60CDFF",
            Category = "Methods",
            HasDocumentation = true,
            IsSelected = true
        };

        var item2 = new CompletionItem
        {
            Label = "copy(Bit 1)",
            Kind = "method",
            Detail = "carga valor inmediato con tipo Bit en registro",
            Signature = "Gpr0.copy(valor: Bit 64)",
            Inputs = "Bit 1 (inmediato de 64 bits)",
            Modifies = "Gpr0 (sobrescribe celda de 64 bits)",
            Returns = "Gpr0",
            IconGlyph = "\uE756",
            IconColorHex = "#60CDFF",
            Category = "Methods",
            HasDocumentation = true,
            IsSelected = false
        };

        var item3 = new CompletionItem
        {
            Label = "sub(Bit 1)",
            Kind = "method",
            Detail = "decrementa operando y actualiza rFlags",
            Signature = "Gpr1.sub(decremento: Bit 64)",
            Inputs = "Gpr1, Bit 1",
            Modifies = "Gpr1, rFlags (zero, sign, carry)",
            Returns = "Gpr1",
            IconGlyph = "\uE756",
            IconColorHex = "#60CDFF",
            Category = "Methods",
            HasDocumentation = true,
            IsSelected = false
        };

        var item4 = new CompletionItem
        {
            Label = "cmp(Bit 0)",
            Kind = "method",
            Detail = "compara operando con cero sin modificar Gpr1",
            Signature = "Gpr1.cmp(referencia: Bit 64)",
            Inputs = "Gpr1, Bit 0",
            Modifies = "rFlags (zero, sign, carry, overflow)",
            Returns = "void",
            IconGlyph = "\uE756",
            IconColorHex = "#60CDFF",
            Category = "Methods",
            HasDocumentation = true,
            IsSelected = false
        };

        var item5 = new CompletionItem
        {
            Label = ".flowJump(@loop if not rFlags.zero)",
            Kind = "flow",
            Detail = "salto condicional evaluando rFlags.zero",
            Signature = ".flowJump(destino: Label if not rFlags.zero)",
            Inputs = "destino (@loop), condición (not rFlags.zero)",
            Modifies = "rip (instruction pointer)",
            Returns = "void",
            IconGlyph = "\uE8EE",
            IconColorHex = "#FFD4BE",
            Category = "Flow",
            HasDocumentation = true,
            IsSelected = false
        };

        var item6 = new CompletionItem
        {
            Label = ".flowCall(Factorial)",
            Kind = "flow",
            Detail = "invoca subrutina siguiendo ABI de x64",
            Signature = ".flowCall(rutina: Routine)",
            Inputs = "Factorial (parámetro n en Gpr1)",
            Modifies = "rip, rsp, Gpr0 (retorno)",
            Returns = "Gpr0 (valor 120)",
            IconGlyph = "\uE8EE",
            IconColorHex = "#FFD4BE",
            Category = "Flow",
            HasDocumentation = true,
            IsSelected = false
        };

        var item7 = new CompletionItem
        {
            Label = "@loop",
            Kind = "label",
            Detail = "etiqueta local de iteración en routine Factorial",
            Signature = "label @loop: bloque de iteración",
            Inputs = "none",
            Modifies = "none",
            Returns = "offset de instrucción",
            IconGlyph = "\uE8EC",
            IconColorHex = "#B4E4FF",
            Category = "Labels",
            HasDocumentation = true,
            IsSelected = false
        };

        var item8 = new CompletionItem
        {
            Label = "Gpr1",
            Kind = "register",
            Detail = "registro GPR 1 (Rcx en ABI Win64 / n)",
            Signature = "register Gpr1: celda física de 64 bits",
            Inputs = "64-bit general purpose register",
            Modifies = "n/a",
            Returns = "n/a",
            IconGlyph = "\uE713",
            IconColorHex = "#81CFFF",
            Category = "Registers",
            HasDocumentation = true,
            IsSelected = false
        };

        CompletionItems.Add(item1);
        CompletionItems.Add(item2);
        CompletionItems.Add(item3);
        CompletionItems.Add(item4);
        CompletionItems.Add(item5);
        CompletionItems.Add(item6);
        CompletionItems.Add(item7);
        CompletionItems.Add(item8);

        SelectedCompletion = item1;
    }
}
