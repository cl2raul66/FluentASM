namespace FluentASM_IDE.Editing;

/// <summary>
/// Fachada mínima sobre el editor para la Capa A (asistencia instantánea).
/// Trabaja siempre con texto normalizado (<c>\n</c>) y offsets en ese mismo
/// espacio, de modo que las reglas no conocen <c>RichEditBox</c> ni sus
/// párrafos <c>\r</c>.
/// </summary>
/// <remarks>
/// La implementación de referencia es <see cref="RichEditBoxCodeEditor"/>, pero
/// la interfaz permite probar las reglas con un editor en memoria. La Capa A no
/// lee <c>.editorconfig</c> ni calcula <c>TextEdit[]</c>: la indentación se
/// recibe ya resuelta en <see cref="IndentUnit"/>.
/// </remarks>
internal interface ICodeEditor
{
    /// <summary>Texto vigente normalizado (<c>\n</c>).</summary>
    string Text { get; }

    /// <summary>Posición del caret en offsets del texto normalizado.</summary>
    int CaretOffset { get; }

    /// <summary>Inicio de la selección en offsets normalizados.</summary>
    int SelectionStart { get; }

    /// <summary>Longitud de la selección en offsets normalizados.</summary>
    int SelectionLength { get; }

    /// <summary>Unidad de indentación efectiva (un tabulador o N espacios).</summary>
    string IndentUnit { get; set; }

    /// <summary>
    /// Indica si el editor está aplicando un cambio propio y los eventos de
    /// texto deben considerarse reentrantes.
    /// </summary>
    bool IsApplying { get; }

    /// <summary>Reemplaza <paramref name="length"/> caracteres por <paramref name="text"/>.</summary>
    void Replace(int start, int length, string text);

    /// <summary>Mueve el caret a <paramref name="offset"/> (texto normalizado).</summary>
    void SetCaret(int offset);

    /// <summary>Marca el inicio de un cambio síncrono propio.</summary>
    void BeginApply();

    /// <summary>Marca el fin de un cambio síncrono propio.</summary>
    void EndApply();

    /// <summary>Indica si la posición está dentro de una cadena o comentario.</summary>
    bool IsInsideStringOrComment(int offset);

    /// <summary>Sección <em>type-state</em> de la posición.</summary>
    FasmSectionKind GetSection(int offset);
}
