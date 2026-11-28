using FluentASM.Lsp;
using FluentASM.Lsp.Protocol;
using FluentASM_IDE.Common.Services;
using FluentASM_IDE.Models;

namespace FluentASM_IDE.Editing;

/// <summary>Ediciones calculadas por la Capa B para un texto concreto.</summary>
/// <param name="Text">Texto normalizado sobre el que se calcularon.</param>
/// <param name="Edits">Ediciones ordenadas, mínimas y sin solapamientos.</param>
internal sealed record FormatResult(string Text, IReadOnlyList<TextEdit> Edits);

/// <summary>
/// Puente entre la Capa A y el motor de formateo. Sincroniza el documento con
/// el snapshot vigente del LSP y solicita las ediciones sin conocer las reglas
/// del Pacto v1.
/// </summary>
internal sealed class FormatCommandHandler
{
    private readonly IEditorViewModel viewModel;

    public FormatCommandHandler(IEditorViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        this.viewModel = viewModel;
    }

    /// <summary>Formatea el documento completo.</summary>
    public FormatResult? FormatDocument(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        IFormattingService? service = viewModel.FormattingService;
        if (service is null)
        {
            return null;
        }

        viewModel.SyncDocumentForFormatting(text);
        return new FormatResult(text, service.FormatDocument(viewModel.DocumentUri));
    }

    /// <summary>Formatea solo las líneas tocadas por la selección.</summary>
    public FormatResult? FormatSelection(string text, int selectionStart, int selectionLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        IFormattingService? service = viewModel.FormattingService;
        if (service is null)
        {
            return null;
        }

        int start = Math.Clamp(selectionStart, 0, text.Length);
        int end = Math.Clamp(selectionStart + selectionLength, start, text.Length);
        (int startLine, int startCharacter) = DocumentText.ToLineCharacter(text, start);
        (int endLine, int endCharacter) = DocumentText.ToLineCharacter(text, end);
        var range = new LspRange(
            new Position(startLine, startCharacter),
            new Position(endLine, endCharacter));

        viewModel.SyncDocumentForFormatting(text);
        return new FormatResult(text, service.FormatRange(viewModel.DocumentUri, range));
    }

    /// <summary>
    /// Formateo <em>on-type</em> acotado a la línea del disparador. Solo se
    /// invoca para <c>}</c> y para el <c>&gt;</c> de <c>-&gt;</c>.
    /// </summary>
    public FormatResult? FormatOnType(string text, int caret, char trigger)
    {
        ArgumentNullException.ThrowIfNull(text);
        IFormattingService? service = viewModel.FormattingService;
        if (service is null)
        {
            return null;
        }

        int offset = Math.Clamp(caret, 0, text.Length);
        (int line, int character) = DocumentText.ToLineCharacter(text, offset);

        viewModel.SyncDocumentForFormatting(text);
        return new FormatResult(
            text,
            service.FormatOnType(viewModel.DocumentUri, new Position(line, character), trigger));
    }
}
