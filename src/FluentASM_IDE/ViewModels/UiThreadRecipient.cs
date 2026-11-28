using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Dispatching;

namespace FluentASM_IDE.ViewModels;

/// <summary>
/// Base para ViewModels que reciben mensajes y actualizan estado enlazado a
/// la interfaz. Garantiza que la actualización ocurra en el hilo de UI aunque
/// el emisor del mensaje sea una tarea en segundo plano.
/// </summary>
public abstract class UiThreadRecipient : ObservableRecipient
{
    protected UiThreadRecipient(IMessenger messenger)
        : base(messenger)
    {
    }

    /// <summary>
    /// Ejecuta <paramref name="action"/> en el hilo de UI, de forma inmediata
    /// si ya estamos en él o mediante <see cref="DispatcherQueue"/> si no.
    /// </summary>
    protected static void RunOnUiThread(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        DispatcherQueue? queue = App.DispatcherQueue;
        if (queue is null || queue.HasThreadAccess)
        {
            action();
            return;
        }

        queue.TryEnqueue(() => action());
    }
}
