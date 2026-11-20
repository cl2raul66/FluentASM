using System;
using System.Collections.Generic;
using FluentASM.Lsp.Protocol;

namespace FluentASM_IDE.Services;

/// <summary>
/// Adaptador de acciones rápidas entre el contrato LSP y el modelo de la IDE.
/// La detección de correcciones y el conocimiento del lenguaje permanecen en
/// <see cref="FluentASM.Lsp.ILanguageService"/>.
/// </summary>
public sealed class QuickFixService
{
    /// <summary>
    /// Convierte las acciones del servicio de lenguaje en acciones aplicables
    /// por el editor. Solo se admiten reemplazos de un único rango, que es el
    /// formato usado por las correcciones rápidas de la Fase 1B.
    /// </summary>
    public IReadOnlyList<QuickFixAction> GetActions(IReadOnlyList<LspCodeAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);

        var result = new List<QuickFixAction>();
        foreach (var action in actions)
        {
            if (action.Edits.Count != 1)
            {
                continue;
            }

            LspTextEdit edit = action.Edits[0];
            result.Add(new QuickFixAction(
                Title: action.Title,
                DiagnosticId: action.DiagnosticCode ?? "LSP",
                StartLine: edit.Range.Start.Line + 1,
                StartColumn: edit.Range.Start.Character + 1,
                EndLine: edit.Range.End.Line + 1,
                EndColumn: edit.Range.End.Character + 1,
                ReplacementText: edit.NewText));
        }

        return result;
    }
}

/// <summary>
/// Acción rápida (code action) adaptada para el editor de la IDE.
/// </summary>
/// <param name="Title">Texto descriptivo mostrado al usuario.</param>
/// <param name="DiagnosticId">Código del diagnóstico relacionado.</param>
/// <param name="StartLine">Línea base 1 del inicio del reemplazo.</param>
/// <param name="StartColumn">Columna base 1 del inicio del reemplazo.</param>
/// <param name="EndLine">Línea base 1 del final del reemplazo.</param>
/// <param name="EndColumn">Columna base 1 (exclusiva) del final del reemplazo.</param>
/// <param name="ReplacementText">Texto que sustituye al rango.</param>
public sealed record QuickFixAction(
    string Title,
    string DiagnosticId,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    string ReplacementText);
