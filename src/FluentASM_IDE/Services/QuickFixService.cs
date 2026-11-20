using System;
using System.Collections.Generic;
using System.Linq;
using FluentASM.Core.Diagnostics;
using FluentASM.Core.Semantics;
using FluentASM_IDE.Models;

namespace FluentASM_IDE.Services;

/// <summary>
/// Proveedor de acciones rápidas (code actions) para diagnósticos del LSP.
/// Actualmente implementa el quick-fix para <c>FASM3012</c> (nombre legacy
/// prohibido), que ofrece el nombre canónico sugerido por
/// <see cref="RegisterModel.TryGetLegacySuggestion"/>.
/// </summary>
public sealed class QuickFixService
{
    /// <summary>
    /// Calcula las acciones rápidas disponibles para un diagnóstico dado.
    /// </summary>
    /// <param name="diagnostic">Diagnóstico del documento.</param>
    /// <param name="sourceText">Texto completo del documento (para extraer el nombre legacy).</param>
    /// <returns>Lista de acciones rápidas aplicables, vacía si no hay ninguna.</returns>
    public IReadOnlyList<QuickFixAction> GetActionsFor(DiagnosticItem diagnostic, string sourceText)
    {
        if (diagnostic.Code != "FASM3012")
        {
            return Array.Empty<QuickFixAction>();
        }

        // Extraer el nombre legacy del mensaje o del texto en la posición.
        string? legacyName = ExtractLegacyName(diagnostic, sourceText);
        if (legacyName is null)
        {
            return Array.Empty<QuickFixAction>();
        }

        if (!RegisterModel.TryGetLegacySuggestion(legacyName, out string? suggestion))
        {
            return Array.Empty<QuickFixAction>();
        }

        return new[]
        {
            new QuickFixAction(
                Title: $"Reemplazar '{legacyName}' por '{suggestion}'",
                DiagnosticId: diagnostic.Code,
                LegacyName: legacyName,
                CanonicalName: suggestion,
                StartLine: diagnostic.Line,
                StartColumn: diagnostic.Column,
                EndLine: diagnostic.EndLine,
                EndColumn: diagnostic.EndColumn)
        };
    }

    /// <summary>
    /// Extrae el nombre legacy del diagnóstico. Intenta obtenerlo del mensaje
    /// del diagnóstico y, como fallback, del texto en la posición indicada.
    /// </summary>
    private static string? ExtractLegacyName(DiagnosticItem diagnostic, string sourceText)
    {
        // El mensaje de FASM3012 tiene la forma: "'Rax' es un nombre legacy prohibido..."
        var message = diagnostic.Message;
        var startQuote = message.IndexOf('\'');
        if (startQuote >= 0)
        {
            var endQuote = message.IndexOf('\'', startQuote + 1);
            if (endQuote > startQuote)
            {
                return message[(startQuote + 1)..endQuote];
            }
        }

        // Fallback: leer el identificador en la posición del diagnóstico.
        try
        {
            int lineIndex = Math.Clamp(diagnostic.Line - 1, 0, int.MaxValue);
            var lines = sourceText.Split('\n');
            if (lineIndex < lines.Length)
            {
                var line = lines[lineIndex];
                int colIndex = Math.Clamp(diagnostic.Column - 1, 0, line.Length - 1);
                if (colIndex >= 0 && colIndex < line.Length && IsIdentifierStart(line[colIndex]))
                {
                    int end = colIndex;
                    while (end < line.Length && IsIdentifierPart(line[end]))
                    {
                        end++;
                    }

                    return line[colIndex..end];
                }
            }
        }
        catch
        {
            // Ignorar errores de extracción.
        }

        return null;
    }

    private static bool IsIdentifierStart(char c) =>
        char.IsAsciiLetterLower(c) || char.IsAsciiLetterUpper(c) || c == '_';

    private static bool IsIdentifierPart(char c) =>
        char.IsAsciiLetterOrDigit(c) || c == '_';
}

/// <summary>
/// Acción rápida (code action) para corregir un diagnóstico.
/// </summary>
/// <param name="Title">Texto descriptivo mostrado al usuario.</param>
/// <param name="DiagnosticId">Código del diagnóstico (ej. FASM3012).</param>
/// <param name="LegacyName">Nombre legacy a reemplazar.</param>
/// <param name="CanonicalName">Nombre canónico sugerido.</param>
/// <param name="StartLine">Línea base 1 del inicio del reemplazo.</param>
/// <param name="StartColumn">Columna base 1 del inicio del reemplazo.</param>
/// <param name="EndLine">Línea base 1 del final del reemplazo.</param>
/// <param name="EndColumn">Columna base 1 (exclusiva) del final del reemplazo.</param>
public sealed record QuickFixAction(
    string Title,
    string DiagnosticId,
    string LegacyName,
    string CanonicalName,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn);
