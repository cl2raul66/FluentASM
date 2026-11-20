using System.Collections.Generic;
using FluentASM.Lsp.Protocol;

namespace FluentASM_IDE.Models;

/// <summary>
/// Resultado de analizar una versión concreta del documento. La vista solo
/// aplica diagnósticos y tokens si la versión sigue siendo la vigente.
/// </summary>
/// <param name="Version">Versión incremental del documento analizado.</param>
/// <param name="Text">Texto normalizado que se analizó.</param>
/// <param name="Diagnostics">Diagnósticos LSP de esa versión.</param>
/// <param name="HighlightTokens">Tokens clasificados para el resaltado.</param>
public sealed record DocumentAnalysisResult(
    int Version,
    string Text,
    IReadOnlyList<LspDiagnostic> Diagnostics,
    IReadOnlyList<HighlightToken> HighlightTokens);
