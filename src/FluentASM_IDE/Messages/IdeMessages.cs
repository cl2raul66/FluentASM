using FluentASM_IDE.Models;

namespace FluentASM_IDE.Messages;

/// <summary>
/// Cambio de coordenadas del cursor en el documento activo.
/// </summary>
public sealed record CursorPositionChangedMessage(int Line, int Column);

/// <summary>
/// Resumen de diagnósticos vigente del documento activo.
/// </summary>
public sealed record DiagnosticsSummaryChangedMessage(int ErrorCount, int WarningCount);

/// <summary>
/// Título visible del documento activo (incluye el indicador de cambios).
/// </summary>
public sealed record DocumentTitleChangedMessage(string Title);

/// <summary>
/// URI del documento vigente en el servicio de lenguaje.
/// </summary>
public sealed record DocumentUriChangedMessage(string DocumentUri);

/// <summary>
/// Estado de la opción «Formatear al guardar».
/// </summary>
public sealed record FormatOnSaveChangedMessage(bool IsFormatOnSave);

/// <summary>
/// Tema efectivo de la aplicación, para que los componentes independientes
/// refresquen sus pinceles y recursos derivados.
/// </summary>
public sealed record ThemeChangedMessage(bool IsDark);

/// <summary>
/// El dock de estado solicita navegar al primer error del documento.
/// </summary>
public sealed record GoToFirstErrorDiagnosticMessage;

/// <summary>
/// El dock de estado solicita navegar a la primera advertencia del documento.
/// </summary>
public sealed record GoToFirstWarningDiagnosticMessage;

/// <summary>
/// La paleta de comandos confirma la ejecución de un elemento. El shell es
/// quien decide qué acción concreta corresponde al comando.
/// </summary>
public sealed record PaletteCommandExecutedMessage(CommandPaletteItem Item);

/// <summary>
/// La paleta de comandos se ha abierto y el editor debe invalidar la sesión
/// de autocompletado vigente.
/// </summary>
public sealed record CommandPaletteOpenedMessage;

/// <summary>
/// La paleta de comandos se ha cerrado: el editor puede volver a publicar
/// sugerencias.
/// </summary>
public sealed record CommandPaletteClosedMessage;

/// <summary>
/// Mensaje de estado dirigido a la barra de estado inferior.
/// </summary>
public sealed record StatusTextChangedMessage(string Text);

/// <summary>
/// El documento editado ha pasado a estar sucio (o limpio) respecto al último
/// guardado.
/// </summary>
public sealed record DocumentDirtyChangedMessage(bool IsDirty);
