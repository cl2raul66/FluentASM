using CoreDiagnostics = FluentASM.Core.Diagnostics;
using FluentASM.Lsp.Protocol;
using LspDiagnosticSeverity = FluentASM.Lsp.Protocol.DiagnosticSeverity;

namespace FluentASM.Lsp.Server;

/// <summary>
/// Traduce diagnósticos de <c>FluentASM.Core</c> a diagnósticos LSP con rangos
/// exactos, usando el índice de líneas del snapshot y deduplicando incidencias
/// repetidas entre fases.
/// </summary>
internal static class DiagnosticTranslator
{
    /// <summary>Convierte y ordena una secuencia de diagnósticos de Core.</summary>
    public static IReadOnlyList<LspDiagnostic> Translate(
        IEnumerable<CoreDiagnostics.Diagnostic> diagnostics,
        DocumentLines lines,
        int textLength)
    {
        var result = new List<LspDiagnostic>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var diagnostic in diagnostics)
        {
            var range = ResolveRange(diagnostic, lines, textLength);

            // Varias fases pueden informar la misma incidencia sobre el mismo rango.
            string key = $"{diagnostic.Id}|{range.Start.Line}:{range.Start.Character}|{range.End.Line}:{range.End.Character}";
            if (!seen.Add(key))
            {
                continue;
            }

            var severity = diagnostic.Severity switch
            {
                CoreDiagnostics.DiagnosticSeverity.Error => LspDiagnosticSeverity.Error,
                CoreDiagnostics.DiagnosticSeverity.Warning => LspDiagnosticSeverity.Warning,
                _ => LspDiagnosticSeverity.Information,
            };

            result.Add(new LspDiagnostic(range, severity, diagnostic.Message, diagnostic.Id));
        }

        result.Sort(static (left, right) =>
        {
            int byLine = left.Range.Start.Line.CompareTo(right.Range.Start.Line);
            return byLine != 0
                ? byLine
                : left.Range.Start.Character.CompareTo(right.Range.Start.Character);
        });

        return result;
    }

    /// <summary>
    /// Traduce un diagnóstico de Core a un rango LSP usando el offset absoluto
    /// de <see cref="CoreDiagnostics.Diagnostic.Span"/>. Solo cae en
    /// <see cref="CoreDiagnostics.Diagnostic.Location"/> cuando el span no está disponible.
    /// </summary>
    private static LspRange ResolveRange(CoreDiagnostics.Diagnostic diagnostic, DocumentLines lines, int textLength)
    {
        int start;
        int length;

        if (diagnostic.Span is { } span && span.Start >= 0 && span.Start <= textLength)
        {
            start = span.Start;
            length = span.Length;
        }
        else if (diagnostic.Location is { } location)
        {
            start = lines.ToOffset(new Position(
                Math.Max(0, location.Line - 1),
                Math.Max(0, location.Column - 1)));
            length = 1;
        }
        else
        {
            start = 0;
            length = 0;
        }

        int end = Math.Clamp(start + Math.Max(0, length), start, textLength);
        if (end == start && start < textLength)
        {
            end++;
        }

        return new LspRange(lines.ToPosition(start), lines.ToPosition(end));
    }
}
