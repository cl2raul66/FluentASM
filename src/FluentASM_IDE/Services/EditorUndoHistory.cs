using System;
using System.Collections.Generic;

namespace FluentASM_IDE.Services;

/// <summary>Estado del editor en un instante: texto normalizado y selección.</summary>
/// <param name="Text">Texto con saltos <c>\n</c>.</param>
/// <param name="SelectionStart">Inicio de la selección en offsets del editor.</param>
/// <param name="SelectionEnd">Fin de la selección en offsets del editor.</param>
internal sealed record EditorUndoState(string Text, int SelectionStart, int SelectionEnd);

/// <summary>
/// Historial de deshacer/rehacer solo para texto. El historial propio del
/// <c>RichEditBox</c> se desactiva porque también registra los cambios de
/// formato del resaltado, y entonces <c>Ctrl+Z</c> deshace colores en lugar de
/// escritura.
/// </summary>
/// <remarks>
/// La escritura se agrupa como espera el usuario: un paso nuevo tras una pausa,
/// al cambiar de línea o al pegar texto. Los cambios de formato nunca entran en
/// el historial porque no modifican el texto.
/// </remarks>
internal sealed class EditorUndoHistory
{
    /// <summary>Pausa que separa dos pasos de escritura.</summary>
    public static readonly TimeSpan Pause = TimeSpan.FromSeconds(1);

    /// <summary>Máximo de estados conservados.</summary>
    public const int Limit = 200;

    private readonly LinkedList<EditorUndoState> undo = new();
    private readonly Stack<EditorUndoState> redo = new();
    private DateTime lastEdit = DateTime.MinValue;

    public bool CanUndo => undo.Count > 0;

    public bool CanRedo => redo.Count > 0;

    /// <summary>
    /// Registra una edición del usuario con el estado inmediatamente anterior y
    /// el texto insertado (vacío en un borrado).
    /// </summary>
    public void Record(EditorUndoState before, string inserted, DateTime now)
    {
        bool newStep = undo.Count == 0
            || now - lastEdit >= Pause
            || inserted.Contains('\n', StringComparison.Ordinal)
            || inserted.Length > 1;

        if (newStep)
        {
            undo.AddLast(before);
            if (undo.Count > Limit)
            {
                undo.RemoveFirst();
            }
        }

        redo.Clear();
        lastEdit = now;
    }

    /// <summary>Estado al que volver, o <c>null</c> si no hay nada que deshacer.</summary>
    public EditorUndoState? Undo(EditorUndoState current)
    {
        if (undo.Last is not { } last)
        {
            return null;
        }

        undo.RemoveLast();
        redo.Push(current);
        lastEdit = DateTime.MinValue;
        return last.Value;
    }

    /// <summary>Estado al que volver, o <c>null</c> si no hay nada que rehacer.</summary>
    public EditorUndoState? Redo(EditorUndoState current)
    {
        if (redo.Count == 0)
        {
            return null;
        }

        undo.AddLast(current);
        if (undo.Count > Limit)
        {
            undo.RemoveFirst();
        }

        lastEdit = DateTime.MinValue;
        return redo.Pop();
    }

    /// <summary>Vacía el historial (al cargar un documento nuevo).</summary>
    public void Clear()
    {
        undo.Clear();
        redo.Clear();
        lastEdit = DateTime.MinValue;
    }
}
