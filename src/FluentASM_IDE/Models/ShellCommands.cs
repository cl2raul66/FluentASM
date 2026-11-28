namespace FluentASM_IDE.Models;

/// <summary>
/// Catálogo de comandos globales que el componente de editor puede solicitar al
/// <em>shell</em>. Los comandos que coinciden con el catálogo de la paleta de
/// comandos se resuelven a través de ella; el resto se resuelve directamente en
/// el shell.
/// </summary>
public static class ShellCommands
{
    /// <summary>Abre o cierra la paleta de comandos (Ctrl+P).</summary>
    public const string TogglePalette = "/palette";

    /// <summary>Abre una carpeta de proyecto o un archivo (Ctrl+O).</summary>
    public const string Open = "/abrir";

    /// <summary>Guarda el archivo fuente actual (Ctrl+S).</summary>
    public const string Save = "/guardar";

    /// <summary>Ejecuta el documento (F5).</summary>
    public const string Run = "/run";

    /// <summary>Compila el documento (Ctrl+B).</summary>
    public const string Build = "/build";

    /// <summary>Muestra el outline de símbolos (Ctrl+Shift+O).</summary>
    public const string Symbols = "/simbolos";
}
