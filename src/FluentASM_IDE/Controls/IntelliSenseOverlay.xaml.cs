using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using FluentASM_IDE.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace FluentASM_IDE.Controls;

public sealed partial class IntelliSenseOverlay : UserControl
{
    private const string AllCategoryKey = "\u0000all";

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(ObservableCollection<CompletionItem>), typeof(IntelliSenseOverlay),
            new PropertyMetadata(null, OnItemsSourceChangedStatic));

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem), typeof(CompletionItem), typeof(IntelliSenseOverlay),
            new PropertyMetadata(null, OnSelectedItemChangedStatic));

    public static readonly DependencyProperty ContextTextProperty =
        DependencyProperty.Register(nameof(ContextText), typeof(string), typeof(IntelliSenseOverlay),
            new PropertyMetadata(string.Empty, OnContextTextChangedStatic));

    public static readonly DependencyProperty ContextVisibilityProperty =
        DependencyProperty.Register(nameof(ContextVisibility), typeof(Visibility), typeof(IntelliSenseOverlay),
            new PropertyMetadata(Visibility.Collapsed));

    public static readonly DependencyProperty DocumentationVisibilityProperty =
        DependencyProperty.Register(nameof(DocumentationVisibility), typeof(Visibility), typeof(IntelliSenseOverlay),
            new PropertyMetadata(Visibility.Collapsed));

    public static readonly DependencyProperty SelectedSignatureProperty =
        DependencyProperty.Register(nameof(SelectedSignature), typeof(string), typeof(IntelliSenseOverlay),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SelectedDetailProperty =
        DependencyProperty.Register(nameof(SelectedDetail), typeof(string), typeof(IntelliSenseOverlay),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SelectedDocumentationProperty =
        DependencyProperty.Register(nameof(SelectedDocumentation), typeof(string), typeof(IntelliSenseOverlay),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SelectedInputsProperty =
        DependencyProperty.Register(nameof(SelectedInputs), typeof(string), typeof(IntelliSenseOverlay),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SelectedModifiesProperty =
        DependencyProperty.Register(nameof(SelectedModifies), typeof(string), typeof(IntelliSenseOverlay),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SelectedReturnsProperty =
        DependencyProperty.Register(nameof(SelectedReturns), typeof(string), typeof(IntelliSenseOverlay),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SelectedFlagsProperty =
        DependencyProperty.Register(nameof(SelectedFlags), typeof(string), typeof(IntelliSenseOverlay),
            new PropertyMetadata(string.Empty));

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

    public string ContextText
    {
        get => (string)GetValue(ContextTextProperty);
        set => SetValue(ContextTextProperty, value);
    }

    public Visibility ContextVisibility
    {
        get => (Visibility)GetValue(ContextVisibilityProperty);
        private set => SetValue(ContextVisibilityProperty, value);
    }

    public Visibility DocumentationVisibility
    {
        get => (Visibility)GetValue(DocumentationVisibilityProperty);
        private set => SetValue(DocumentationVisibilityProperty, value);
    }

    public string SelectedSignature
    {
        get => (string)GetValue(SelectedSignatureProperty);
        private set => SetValue(SelectedSignatureProperty, value);
    }

    public string SelectedDetail
    {
        get => (string)GetValue(SelectedDetailProperty);
        private set => SetValue(SelectedDetailProperty, value);
    }

    public string SelectedDocumentation
    {
        get => (string)GetValue(SelectedDocumentationProperty);
        private set => SetValue(SelectedDocumentationProperty, value);
    }

    public string SelectedInputs
    {
        get => (string)GetValue(SelectedInputsProperty);
        private set => SetValue(SelectedInputsProperty, value);
    }

    public string SelectedModifies
    {
        get => (string)GetValue(SelectedModifiesProperty);
        private set => SetValue(SelectedModifiesProperty, value);
    }

    public string SelectedReturns
    {
        get => (string)GetValue(SelectedReturnsProperty);
        private set => SetValue(SelectedReturnsProperty, value);
    }

    public string SelectedFlags
    {
        get => (string)GetValue(SelectedFlagsProperty);
        private set => SetValue(SelectedFlagsProperty, value);
    }

    public ObservableCollection<CompletionItem> DisplayedItems { get; } = new();

    public ObservableCollection<CompletionCategory> Categories { get; } = new();

    public string CurrentCategory { get; private set; } = AllCategoryKey;

    public event EventHandler<CompletionItem>? ItemCommitted;

    public IntelliSenseOverlay()
    {
        InitializeComponent();
        ActualThemeChanged += (_, _) => RefreshTheme();
    }

    private static void OnItemsSourceChangedStatic(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not IntelliSenseOverlay control)
        {
            return;
        }

        if (e.OldValue is ObservableCollection<CompletionItem> oldCollection)
        {
            oldCollection.CollectionChanged -= control.OnItemsCollectionChanged;
        }

        if (e.NewValue is ObservableCollection<CompletionItem> newCollection)
        {
            newCollection.CollectionChanged += control.OnItemsCollectionChanged;
        }

        control.RefreshFilter();
    }

    private static void OnSelectedItemChangedStatic(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is IntelliSenseOverlay control)
        {
            control.OnSelectedItemChanged();
        }
    }

    private static void OnContextTextChangedStatic(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is IntelliSenseOverlay control)
        {
            control.ContextVisibility = string.IsNullOrWhiteSpace(control.ContextText)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshFilter();
    }

    public void SetCategory(string category)
    {
        CurrentCategory = category;
        RefreshFilter();
    }

    public void RefreshFilter()
    {
        IReadOnlyList<CompletionItem> source =
            (IReadOnlyList<CompletionItem>?)ItemsSource?.ToList() ?? Array.Empty<CompletionItem>();

        string[] categoryNames = source
            .Select(item => item.Category)
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(category => category, StringComparer.Ordinal)
            .ToArray();

        if (!categoryNames.Contains(CurrentCategory, StringComparer.Ordinal))
        {
            CurrentCategory = AllCategoryKey;
        }

        Categories.Clear();
        Categories.Add(new CompletionCategory(AllCategoryKey, "Todos")
        {
            IsActive = CurrentCategory == AllCategoryKey,
        });
        foreach (string category in categoryNames)
        {
            Categories.Add(new CompletionCategory(category, category)
            {
                IsActive = CurrentCategory == category,
            });
        }

        DisplayedItems.Clear();
        foreach (CompletionItem item in source)
        {
            if (CurrentCategory == AllCategoryKey ||
                string.Equals(item.Category, CurrentCategory, StringComparison.Ordinal))
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
            UpdateDocumentation();
        }
    }

    public void SelectNext()
    {
        if (DisplayedItems.Count == 0)
        {
            return;
        }

        int index = SelectedItem is null ? -1 : DisplayedItems.IndexOf(SelectedItem);
        index = (index + 1) % DisplayedItems.Count;
        Select(DisplayedItems[index]);
    }

    public void SelectPrevious()
    {
        if (DisplayedItems.Count == 0)
        {
            return;
        }

        int index = SelectedItem is null ? 0 : DisplayedItems.IndexOf(SelectedItem);
        if (index < 0)
        {
            index = 0;
        }

        index = (index - 1 + DisplayedItems.Count) % DisplayedItems.Count;
        Select(DisplayedItems[index]);
    }

    public void CommitSelected()
    {
        if (SelectedItem is not null)
        {
            ItemCommitted?.Invoke(this, SelectedItem);
        }
    }

    private void Select(CompletionItem item)
    {
        foreach (CompletionItem candidate in DisplayedItems)
        {
            candidate.IsSelected = ReferenceEquals(candidate, item);
        }

        SelectedItem = item;
        UpdateDocumentation();
        ScrollIntoView(item);
    }

    private void OnSelectedItemChanged()
    {
        if (ItemsSource is not null)
        {
            foreach (CompletionItem item in ItemsSource)
            {
                item.IsSelected = ReferenceEquals(item, SelectedItem);
            }
        }

        UpdateDocumentation();
        if (SelectedItem is not null)
        {
            ScrollIntoView(SelectedItem);
        }
    }

    private void UpdateDocumentation()
    {
        CompletionItem? item = SelectedItem;
        DocumentationVisibility = item is not null && item.HasDocumentation
            ? Visibility.Visible
            : Visibility.Collapsed;
        SelectedSignature = item?.Signature ?? string.Empty;
        SelectedDetail = item?.Detail ?? string.Empty;
        SelectedDocumentation = item?.Documentation ?? string.Empty;
        SelectedInputs = item?.Inputs ?? string.Empty;
        SelectedModifies = item?.Modifies ?? string.Empty;
        SelectedReturns = item?.Returns ?? string.Empty;
        SelectedFlags = item?.FlagsAffected ?? string.Empty;
    }

    private void ScrollIntoView(CompletionItem item)
    {
        try
        {
            CompletionListView.UpdateLayout();
            CompletionListView.ScrollIntoView(item);
        }
        catch
        {
            // La lista todavía puede no estar cargada; se desplazará en la
            // siguiente navegación o cuando el overlay vuelva a mostrarse.
        }
    }

    private void OnOverlayEffectiveViewportChanged(
        FrameworkElement sender,
        EffectiveViewportChangedEventArgs e)
    {
        if (Visibility != Visibility.Visible || SelectedItem is null)
        {
            return;
        }

        // EffectiveViewportChanged se produce después del arrange y cuando
        // el control vuelve a tener viewport. Es el punto adecuado para
        // reintentar el scroll de una selección publicada mientras estaba
        // colapsado.
        UpdateLayout();
        ScrollIntoView(SelectedItem);
    }

    private void RefreshTheme()
    {
        foreach (CompletionCategory category in Categories)
        {
            category.RefreshTheme();
        }
    }

    private void OnItemPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is CompletionItem item)
        {
            Select(item);
        }
    }

    private void OnItemPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is CompletionItem item)
        {
            Select(item);
            CommitSelected();
        }
    }

    private void OnFilterCategoryClicked(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is CompletionCategory category)
        {
            SetCategory(category.Key);
        }
    }
}
