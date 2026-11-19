using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FluentASM_IDE.Models;

/// <summary>
/// Diagnóstico actual del documento, traducido a coordenadas base 1 para
/// mostrarlo al usuario y aplicarlo sobre el editor.
/// </summary>
public sealed class DiagnosticItem
{
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromArgb(255, 255, 180, 171));
    private static readonly SolidColorBrush WarningBrush = new(Color.FromArgb(255, 255, 212, 190));
    private static readonly SolidColorBrush InformationBrush = new(Color.FromArgb(255, 189, 200, 207));

    public required string Code { get; init; }

    public required string Severity { get; init; }

    public required string Message { get; init; }

    /// <summary>Línea base 1.</summary>
    public required int Line { get; init; }

    /// <summary>Columna base 1.</summary>
    public required int Column { get; init; }

    /// <summary>Línea final base 1 (inclusiva para mostrar).</summary>
    public required int EndLine { get; init; }

    /// <summary>Columna final base 1 (exclusiva para mostrar).</summary>
    public required int EndColumn { get; init; }

    public required bool IsError { get; init; }

    public required bool IsWarning { get; init; }

    public string Location => Line == EndLine
        ? $"{Line}:{Column}"
        : $"{Line}:{Column}-{EndLine}:{EndColumn}";

    public SolidColorBrush SeverityBrush => IsError
        ? ErrorBrush
        : Severity == "Advertencia"
            ? WarningBrush
            : InformationBrush;
}














