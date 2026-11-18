using FluentASM.Core.Diagnostics;

namespace FluentASM.Core.Syntax.Ast;

/// <summary>
/// Sección opcional <c>.data(dt -> { dt.define<...>(...) })</c>.
/// </summary>
/// <param name="Content">Lambda declarada dentro de <c>.data(...)</c>.</param>
/// <param name="Definitions">Definiciones de datos extraídas de la lambda.</param>
/// <param name="Span">Intervalo de la sección completa.</param>
public sealed record DataSectionSyntax(
    LambdaExpressionSyntax? Content,
    IReadOnlyList<DataDefinitionSyntax> Definitions,
    TextSpan Span) : SyntaxNode(Span);

/// <summary>
/// Sección obligatoria <c>.execute(ex -> { ex.routines(rt -> { ... }) })</c>.
/// </summary>
/// <param name="Content">Lambda declarada dentro de <c>.execute(...)</c>.</param>
/// <param name="Routines">Rutinas extraídas de <c>rt.define(...)</c>.</param>
/// <param name="Span">Intervalo de la sección completa.</param>
public sealed record ExecuteSectionSyntax(
    LambdaExpressionSyntax? Content,
    IReadOnlyList<RoutineDefinitionSyntax> Routines,
    TextSpan Span) : SyntaxNode(Span);
