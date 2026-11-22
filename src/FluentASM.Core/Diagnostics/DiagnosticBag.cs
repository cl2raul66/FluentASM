using System.Collections;

namespace FluentASM.Core.Diagnostics;

/// <summary>
/// Colección ordenada de diagnósticos compartida por las etapas del compilador.
/// </summary>
public sealed class DiagnosticBag : IReadOnlyCollection<Diagnostic>
{
    private readonly List<Diagnostic> items = [];

    /// <inheritdoc />
    public int Count => items.Count;

    /// <summary>Indica si se registró al menos un diagnóstico de severidad <see cref="DiagnosticSeverity.Error"/>.</summary>
    public bool HasErrors => items.Exists(item => item.Severity == DiagnosticSeverity.Error);

    /// <summary>Diagnósticos en orden de emisión.</summary>
    public IReadOnlyList<Diagnostic> Items => items;

    /// <summary>Agrega un diagnóstico de severidad <see cref="DiagnosticSeverity.Error"/>.</summary>
    public void Error(
        string id,
        string message,
        SourceLocation? location = null,
        TextSpan? span = null) =>
        items.Add(new Diagnostic(id, DiagnosticSeverity.Error, message, location, span));

    /// <summary>Agrega un diagnóstico de severidad <see cref="DiagnosticSeverity.Warning"/>.</summary>
    public void Warning(
        string id,
        string message,
        SourceLocation? location = null,
        TextSpan? span = null) =>
        items.Add(new Diagnostic(id, DiagnosticSeverity.Warning, message, location, span));

    /// <summary>Agrega un diagnóstico ya construido.</summary>
    public void Add(Diagnostic diagnostic) => items.Add(diagnostic);

    /// <inheritdoc />
    public IEnumerator<Diagnostic> GetEnumerator() => items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}


