using System.Collections.ObjectModel;
using System.Collections.Specialized;
using FluentASM_IDE.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;

namespace FluentASM_IDE.Controls;

/// <summary>
/// Superficie modal de la paleta de comandos.
/// <para>
/// El control no conoce el ViewModel: expone estado mediante propiedades de
/// dependencia (<see cref="IsOpen"/>, <see cref="ItemsSource"/>,
/// <see cref="CommandQuery"/> y <see cref="SelectedItem"/>) y notifica los
/// dos hechos que la página necesita observar: la ejecución de un comando
/// (<see cref="CommandExecuted"/>) y el cierre (<see cref="Closed"/>).
/// </para>
/// </summary>
public sealed partial class CommandPaletteOverlay : UserControl
{
    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(CommandPaletteOverlay),
            new PropertyMetadata(false, OnIsOpenChangedStatic));

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(ObservableCollection<CommandPaletteItem>), typeof(CommandPaletteOverlay),
            new PropertyMetadata(null, OnItemsSourceChangedStatic));

    public static readonly DependencyProperty CommandQueryProperty =
        DependencyProperty.Register(nameof(CommandQuery), typeof(string), typeof(CommandPaletteOverlay),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem), typeof(CommandPaletteItem), typeof(CommandPaletteOverlay),
            new PropertyMetadata(null, OnSelectedItemChangedStatic));

    /// <summary>Abre o cierra la paleta. Enlazable en modo TwoWay.</summary>
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>Comandos visibles en la paleta.</summary>
    public ObservableCollection<CommandPaletteItem>? ItemsSource
    {
        get => (ObservableCollection<CommandPaletteItem>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>Texto de filtrado. Enlazable en modo TwoWay.</summary>
    public string CommandQuery
    {
        get => (string)GetValue(CommandQueryProperty);
        set => SetValue(CommandQueryProperty, value);
    }

    /// <summary>Comando activo (resaltado) dentro de la lista.</summary>
    public CommandPaletteItem? SelectedItem
    {
        get => (CommandPaletteItem?)GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>Se produce cuando el usuario elige un comando de la lista.</summary>
    public event EventHandler<CommandPaletteItem>? CommandExecuted;

    /// <summary>Se produce cuando la paleta se cierra.</summary>
    public event EventHandler? Closed;

    /// <summary>
    /// Evita encolar varios intentos de foco para la misma apertura.
    /// </summary>
    private bool _focusPending;

    /// <summary>
    /// Número de intentos diferidos de foco ya realizados en esta apertura.
    /// </summary>
    private int _focusAttempts;

    /// <summary>
    /// Tope de intentos diferidos de foco por apertura.
    /// </summary>
    private const int MaxDeferredFocusAttempts = 2;

    public CommandPaletteOverlay()
    {
        InitializeComponent();
        UpdateVisibility();
    }

    /// <summary>
    /// Lleva el foco a la caja de búsqueda.
    /// <para>
    /// El foco no puede fijarse de forma síncrona cuando la paleta se abre
    /// con una tecla: el <c>RichEditBox</c> todavía está procesando el evento
    /// y recupera el foco al terminarlo. Por eso el intento se hace también
    /// de forma diferida, en el dispatcher, cuando el control ya es visible,
    /// tiene layout y el evento de teclado ha concluded.
    /// </para>
    /// </summary>
    public void FocusInput()
    {
        UpdateVisibility();
        PaletteRoot.UpdateLayout();

        CommandInputBox.Focus(FocusState.Programmatic);
        ScheduleDeferredFocus(DispatcherQueuePriority.Normal);
    }

    /// <summary>
    /// Encola un intento de foco para después del evento de teclado actual.
    /// Si el foco síncrono funcionó, este intento solo lo confirma.
    /// </summary>
    private void ScheduleDeferredFocus(DispatcherQueuePriority priority)
    {
        if (_focusPending || _focusAttempts >= MaxDeferredFocusAttempts)
        {
            return;
        }

        DispatcherQueue? queue = DispatcherQueue;
        if (queue is null)
        {
            return;
        }

        _focusPending = true;
        queue.TryEnqueue(priority, () =>
        {
            _focusPending = false;

            if (!IsOpen)
            {
                return;
            }

            if (!CommandInputBox.Focus(FocusState.Programmatic))
            {
                PaletteRoot.UpdateLayout();
                CommandInputBox.Focus(FocusState.Programmatic);
            }

            PlaceCaretAtEnd();
            _focusAttempts++;

            // Si el editor recuperó el foco al terminar su propio evento, se
            // reintenta una última vez con la prioridad más baja.
            if (!HasInputFocus())
            {
                ScheduleDeferredFocus(DispatcherQueuePriority.Low);
            }
        });
    }

    /// <summary>
    /// Indica si la caja de búsqueda es el elemento con el foco activo.
    /// </summary>
    private bool HasInputFocus()
    {
        XamlRoot? root = CommandInputBox.XamlRoot;
        if (root is null)
        {
            return false;
        }

        return ReferenceEquals(FocusManager.GetFocusedElement(root), CommandInputBox);
    }

    /// <summary>
    /// Coloca el cursor al final del texto para que el usuario escriba a
    /// continuación del prefijo ya presente (por ejemplo, «/»).
    /// </summary>
    private void PlaceCaretAtEnd()
    {
        int length = CommandInputBox.Text?.Length ?? 0;
        if (length > 0)
        {
            CommandInputBox.Select(length, 0);
        }
    }

    private static void OnIsOpenChangedStatic(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not CommandPaletteOverlay control)
        {
            return;
        }

        control.UpdateVisibility();

        if (control.IsOpen)
        {
            control._focusAttempts = 0;
            control.FocusInput();
        }
        else
        {
            control.Closed?.Invoke(control, EventArgs.Empty);
        }
    }

    private static void OnItemsSourceChangedStatic(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not CommandPaletteOverlay control)
        {
            return;
        }

        if (e.OldValue is ObservableCollection<CommandPaletteItem> oldCollection)
        {
            oldCollection.CollectionChanged -= control.OnItemsCollectionChanged;
        }

        if (e.NewValue is ObservableCollection<CommandPaletteItem> newCollection)
        {
            newCollection.CollectionChanged += control.OnItemsCollectionChanged;
        }

        control.SyncSelection();
    }

    private static void OnSelectedItemChangedStatic(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CommandPaletteOverlay control)
        {
            control.ApplyActiveState(e.NewValue as CommandPaletteItem);
        }
    }

    private void UpdateVisibility()
    {
        PaletteRoot.Visibility = IsOpen ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncSelection();
    }

    /// <summary>
    /// Mantiene la selección coherente con la lista filtrada: si el comando
    /// activo ya no está en la colección, se selecciona el primero, que es el
    /// que el filtrado deja resaltado.
    /// </summary>
    private void SyncSelection()
    {
        if (ItemsSource is null)
        {
            return;
        }

        if (SelectedItem is not null && IndexOf(SelectedItem) >= 0)
        {
            return;
        }

        SelectedItem = ItemsSource.Count > 0 ? ItemsSource[0] : null;
    }

    private void ApplyActiveState(CommandPaletteItem? selected)
    {
        if (selected is null || ItemsSource is null)
        {
            return;
        }

        foreach (CommandPaletteItem item in ItemsSource)
        {
            item.IsActive = ReferenceEquals(item, selected);
        }
    }

    private int IndexOf(CommandPaletteItem item)
    {
        if (ItemsSource is null)
        {
            return -1;
        }

        for (int index = 0; index < ItemsSource.Count; index++)
        {
            if (ReferenceEquals(ItemsSource[index], item))
            {
                return index;
            }
        }

        return -1;
    }

    private void SelectNext()
    {
        if (ItemsSource is null || ItemsSource.Count == 0)
        {
            return;
        }

        int index = SelectedItem is null ? -1 : IndexOf(SelectedItem);
        int next = (index + 1) % ItemsSource.Count;
        SelectedItem = ItemsSource[next];
    }

    private void SelectPrevious()
    {
        if (ItemsSource is null || ItemsSource.Count == 0)
        {
            return;
        }

        int index = SelectedItem is null ? 0 : IndexOf(SelectedItem);
        if (index < 0)
        {
            index = 0;
        }

        int previous = (index - 1 + ItemsSource.Count) % ItemsSource.Count;
        SelectedItem = ItemsSource[previous];
    }

    private void CommitSelected()
    {
        CommandPaletteItem? item = FindActiveItem() ?? ItemsSource?.FirstOrDefault();
        if (item is not null)
        {
            CommandExecuted?.Invoke(this, item);
        }
    }

    private CommandPaletteItem? FindActiveItem()
    {
        if (ItemsSource is null)
        {
            return null;
        }

        foreach (CommandPaletteItem item in ItemsSource)
        {
            if (item.IsActive)
            {
                return item;
            }
        }

        return null;
    }

    private void Close()
    {
        if (IsOpen)
        {
            IsOpen = false;
        }
    }

    private void OnBackdropPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Close();
    }

    private void OnPaletteContainerClicked(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnEscBadgeClicked(object sender, PointerRoutedEventArgs e)
    {
        Close();
        e.Handled = true;
    }

    private void OnPaletteItemHover(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is CommandPaletteItem item)
        {
            SelectedItem = item;
        }
    }

    private void OnPaletteItemUnhover(object sender, PointerRoutedEventArgs e)
    {
        // Paridad 1:1 con la paleta original: al abandonar el puntero se
        // conserva el comando resaltado.
    }

    private void OnPaletteItemClicked(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is CommandPaletteItem item)
        {
            CommandExecuted?.Invoke(this, item);
        }
    }

    private void OnCommandTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox && !string.Equals(textBox.Text, CommandQuery, StringComparison.Ordinal))
        {
            CommandQuery = textBox.Text;
        }
    }

    private void OnCommandInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape:
                Close();
                e.Handled = true;
                break;

            case VirtualKey.Down:
                SelectNext();
                e.Handled = true;
                break;

            case VirtualKey.Up:
                SelectPrevious();
                e.Handled = true;
                break;

            case VirtualKey.Enter:
                CommitSelected();
                e.Handled = true;
                break;
        }
    }
}
