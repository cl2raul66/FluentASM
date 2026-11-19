using System;
using System.Collections.ObjectModel;
using System.Linq;
using FluentASM_IDE.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FluentASM_IDE.Controls;

public sealed partial class IntelliSenseOverlay : UserControl
{
    private static readonly SolidColorBrush ActiveCategoryBgDark = new(Color.FromArgb(80, 96, 205, 255));
    private static readonly SolidColorBrush ActiveCategoryBgLight = new(Color.FromArgb(80, 0, 103, 192));
    private static readonly SolidColorBrush InactiveCategoryBg = new(Color.FromArgb(0, 0, 0, 0));

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(ObservableCollection<CompletionItem>), typeof(IntelliSenseOverlay),
            new PropertyMetadata(null, OnItemsSourceChangedStatic));

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem), typeof(CompletionItem), typeof(IntelliSenseOverlay),
            new PropertyMetadata(null));

    public ObservableCollection<CompletionItem>? ItemsSource
    {
        get => (ObservableCollection<CompletionItem>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public CompletionItem? SelectedItem
    {
        get => (CompletionItem?)GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public ObservableCollection<CompletionItem> DisplayedItems { get; } = new();

    public string CurrentCategory { get; private set; } = "All";

    public event EventHandler<CompletionItem>? ItemCommitted;

    public IntelliSenseOverlay()
    {
        InitializeComponent();
        ActualThemeChanged += (s, e) =>
        {
            // Actualiza visualmente los fondos de las categorías.
            RefreshFilter();
        };
    }

    private static void OnItemsSourceChangedStatic(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is IntelliSenseOverlay control)
        {
            control.RefreshFilter();
        }
    }

    public void SetCategory(string category)
    {
        CurrentCategory = category;
        RefreshFilter();
    }

    public SolidColorBrush GetCategoryBg(string category)
    {
        bool isActive = string.Equals(CurrentCategory, category, StringComparison.OrdinalIgnoreCase) ||
                        ((CurrentCategory == "Verbs" || CurrentCategory == "Methods") && (category == "Verbs" || category == "Methods"));
        bool isDark = ActualTheme != ElementTheme.Light;
        return isActive ? (isDark ? ActiveCategoryBgDark : ActiveCategoryBgLight) : InactiveCategoryBg;
    }

    public void RefreshFilter()
    {
        DisplayedItems.Clear();
        if (ItemsSource == null) return;

        foreach (var item in ItemsSource)
        {
            bool match = false;
            if (CurrentCategory == "All")
            {
                match = true;
            }
            else if ((CurrentCategory == "Verbs" || CurrentCategory == "Methods") &&
                     (item.Category == "Verbs" || item.Category == "Methods"))
            {
                match = true;
            }
            else if (string.Equals(item.Category, CurrentCategory, StringComparison.OrdinalIgnoreCase))
            {
                match = true;
            }

            if (match)
            {
                DisplayedItems.Add(item);
            }
        }

        if (DisplayedItems.Count > 0)
        {
            Select(DisplayedItems[0]);
        }
        else
        {
            SelectedItem = null;
        }
    }

    public void SelectNext()
    {
        if (DisplayedItems.Count == 0) return;
        int index = SelectedItem != null ? DisplayedItems.IndexOf(SelectedItem) : -1;
        index = (index + 1) % DisplayedItems.Count;
        Select(DisplayedItems[index]);
    }

    public void SelectPrevious()
    {
        if (DisplayedItems.Count == 0) return;
        int index = SelectedItem != null ? DisplayedItems.IndexOf(SelectedItem) : 0;
        index = (index - 1 + DisplayedItems.Count) % DisplayedItems.Count;
        Select(DisplayedItems[index]);
    }

    public void CommitSelected()
    {
        if (SelectedItem != null)
        {
            ItemCommitted?.Invoke(this, SelectedItem);
        }
    }

    private void Select(CompletionItem item)
    {
        foreach (var it in DisplayedItems)
        {
            it.IsSelected = ReferenceEquals(it, item);
        }
        SelectedItem = item;
    }

    private void OnItemPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is CompletionItem item)
        {
            Select(item);
        }
    }

    private void OnItemPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is CompletionItem item)
        {
            Select(item);
            CommitSelected();
        }
    }

    private void OnFilterCategoryClicked(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string category)
        {
            SetCategory(category);
        }
    }
}













