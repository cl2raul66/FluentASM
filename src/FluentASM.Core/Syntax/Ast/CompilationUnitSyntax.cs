using FluentASM.Core.Diagnostics;

namespace FluentASM.Core.Syntax.Ast;

/// <summary>
/// Raíz del archivo <c>sourcesCode.fasm</c>: nombre del proyecto y flujo
/// <c>[.data(...)] .execute(...) .initialize()</c>.
/// </summary>
/// <param name="ProjectName">Identificador raíz del proyecto; <c>null</c> si el archivo está vacío o mal formado.</param>
/// <param name="Data">Sección <c>.data(...)</c> opcional.</param>
/// <param name="Execute">Sección <c>.execute(...)</c> obligatoria.</param>
/// <param name="Initialize">Token de <c>.initialize()</c> que cierra el contrato.</param>
/// <param name="Span">Intervalo completo del archivo.</param>
public sealed record CompilationUnitSyntax(
    Token? ProjectName,
    DataSectionSyntax? Data,
    ExecuteSectionSyntax? Execute,
    Token? Initialize,
    TextSpan Span) : SyntaxNode(Span);
