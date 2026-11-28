using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FluentASM_IDE.Messages;
using FluentASM_IDE.Models;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FluentASM_IDE.ViewModels;

/// <summary>
/// Estado de presentación del dock de estado inferior. Recibe las variaciones
/// del documento por mensajería y expone los comandos que el shell resuelve
/// (navegación a diagnósticos y cambio de «Formatear al guardar»).
/// </summary>
public partial class StatusDockViewModel : UiThreadRecipient,
    IRecipient<CursorPositionChangedMessage>,
    IRecipient<DiagnosticsSummaryChangedMessage>,
    IRecipient<DocumentTitleChangedMessage>,
    IRecipient<StatusTextChangedMessage>,
    IRecipient<ThemeChangedMessage>
{
    private static readonly SolidColorBrush ErrorCountBrushDark = new(Color.FromArgb(255, 255, 180, 171));
    private static readonly SolidColorBrush ErrorCountBrushLight = new(Color.FromArgb(255, 186, 26, 26));
    private static readonly SolidColorBrush WarningCountBrushDark = new(Color.FromArgb(255, 255, 212, 190));
    private static readonly SolidColorBrush WarningCountBrushLight = new(Color.FromArgb(255, 168, 83, 0));

    private bool _isDarkTheme = true;

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Listo";

    [ObservableProperty]
    public partial string DocumentTitle { get; set; } = IdeDefaults.DocumentFileName;

    [ObservableProperty]
    public partial string Encoding { get; set; } = "UTF-8";

    [ObservableProperty]
    public partial string LineEnding { get; set; } = "LF";

    [ObservableProperty]
    public partial string Architecture { get; set; } = "x64 Fluent";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiagnosticErrorSummary))]
    public partial int DiagnosticErrorCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiagnosticWarningSummary))]
    public partial int DiagnosticWarningCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CursorPositionFormatted))]
    public partial int CurrentLine { get; set; } = 13;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CursorPositionFormatted))]
    public partial int CurrentCol { get; set; } = 1;

    [ObservableProperty]
    public partial bool FormatOnSave { get; set; }

    public string CursorPositionFormatted => $"Ln {CurrentLine}, Col {CurrentCol}";

    public string DiagnosticErrorSummary => DiagnosticErrorCount == 1
        ? "1 error"
        : $"{DiagnosticErrorCount} errores";

    public string DiagnosticWarningSummary => DiagnosticWarningCount == 1
        ? "1 advertencia"
        : $"{DiagnosticWarningCount} advertencias";

    public SolidColorBrush DiagnosticErrorBrush => _isDarkTheme ? ErrorCountBrushDark : ErrorCountBrushLight;

    public SolidColorBrush DiagnosticWarningBrush => _isDarkTheme ? WarningCountBrushDark : WarningCountBrushLight;

    public StatusDockViewModel(IMessenger messenger)
        : base(messenger)
    {
        IsActive = true;
    }

    /// <summary>
    /// Al alternar «Formatear al guardar» se actualiza el mensaje de estado y
    /// se avisa a la paleta para que refresque la descripción del comando.
    /// </summary>
    partial void OnFormatOnSaveChanged(bool value)
    {
        StatusText = value
            ? "Formatear al guardar: activado."
            : "Formatear al guardar: desactivado.";

        Messenger.Send(new FormatOnSaveChangedMessage(value));
    }

    [RelayCommand]
    private void GoToFirstError()
        => Messenger.Send(new GoToFirstErrorDiagnosticMessage());

    [RelayCommand]
    private void GoToFirstWarning()
        => Messenger.Send(new GoToFirstWarningDiagnosticMessage());

    void IRecipient<CursorPositionChangedMessage>.Receive(CursorPositionChangedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        RunOnUiThread(() =>
        {
            CurrentLine = Math.Max(1, message.Line);
            CurrentCol = Math.Max(1, message.Column);
        });
    }

    void IRecipient<DiagnosticsSummaryChangedMessage>.Receive(DiagnosticsSummaryChangedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        RunOnUiThread(() =>
        {
            DiagnosticErrorCount = Math.Max(0, message.ErrorCount);
            DiagnosticWarningCount = Math.Max(0, message.WarningCount);
        });
    }

    void IRecipient<DocumentTitleChangedMessage>.Receive(DocumentTitleChangedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        RunOnUiThread(() => DocumentTitle = message.Title);
    }

    void IRecipient<StatusTextChangedMessage>.Receive(StatusTextChangedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        RunOnUiThread(() => StatusText = message.Text);
    }

    void IRecipient<ThemeChangedMessage>.Receive(ThemeChangedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        RunOnUiThread(() =>
        {
            _isDarkTheme = message.IsDark;
            OnPropertyChanged(nameof(DiagnosticErrorBrush));
            OnPropertyChanged(nameof(DiagnosticWarningBrush));
        });
    }
}
