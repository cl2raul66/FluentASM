using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace FluentASM_IDE.Services;

/// <summary>
/// Traduce una posición del puntero a un offset del documento del editor.
/// </summary>
/// <remarks>
/// <c>RichEditTextDocument.GetRangeFromPoint</c> documenta coordenadas de
/// pantalla/cliente, pero el <c>RichEditBox</c> de WinUI 3 resuelve el punto
/// contra el documento sin aplicar el desplazamiento vertical del scroll
/// interno. Por eso el punto se traslada al espacio del documento sumando
/// <c>ScrollOffset</c>.
/// </remarks>
internal static class EditorHitTesting
{
    /// <summary>
    /// Devuelve el offset del documento para un punto del editor, aplicando el
    /// desplazamiento vertical del scroll. Devuelve <c>-1</c> si no se puede
    /// resolver.
    /// </summary>
    public static int GetOffset(RichEditBox editor, Point position, double scrollOffset)
    {
        var documentPoint = scrollOffset > 0.5
            ? new Point(position.X, position.Y + scrollOffset)
            : position;

        try
        {
            return editor.Document
                .GetRangeFromPoint(documentPoint, PointOptions.ClientCoordinates)
                .StartPosition;
        }
        catch
        {
            return -1;
        }
    }
}
