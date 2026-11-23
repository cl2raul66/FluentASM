using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using FluentASM.Lsp.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Windows.Storage;

namespace FluentASM_IDE.Services;

/// <summary>
/// Coloca la ventana principal respetando <c>WorkArea</c>, DPI y configuraciones
/// multi-monitor. Mantiene <see cref="MainWindow"/> como simple orquestador.
/// </summary>
public sealed class WindowGeometryService : IDisposable
{
    private const string LogSource = "WindowGeometryService";
    private const string StateFileName = "window-state.json";
    private const int CurrentStateVersion = 2;

    // Hueco uniforme entre la ventana y el WorkArea. Se define en DIP y se
    // autorregula con el lado menor para conservar el mismo aire visual en
    // cualquier resolución o escala (8-24 DIP ≈ 8-25 px a 100 %).
    private const double EdgeGapRatio = 0.008;
    private const double MinEdgeGapDip = 4;
    private const double MaxEdgeGapDip = 17;

    private const double MinWidthDip = 720;
    private const double MinHeightDip = 480;
    private const int TitleBarDip = 32;
    private const int MinTitleBarVisibleDip = 96;

    private readonly Window _window;
    private readonly AppWindow _appWindow;
    private readonly nint _hwnd;
    private readonly IncidentRecorder _incidents;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private DisplayAreaWatcher? _watcher;
    private ulong? _currentDisplayValue;
    private (double Width, double Height)? _lastNormalSizeDip;
    private RectInt32? _startupRect;
    private RectInt32? _lastAutoRect;
    private uint _currentDpi = 96;
    private bool _isApplying;
    private bool _startupApplied;
    private bool _firstActivationHandled;
    private bool _eventsHooked;
    private bool _stateSaved;
    private bool _disposed;

    public WindowGeometryService(Window window, IncidentRecorder incidents)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(incidents);

        _window = window;
        _appWindow = window.AppWindow;
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _incidents = incidents;
    }

    /// <summary>
    /// Calcula y aplica tamaño, posición y estado antes de que la ventana se
    /// muestre, evitando el parpadeo inicial.
    /// </summary>
    public void ApplyStartupPlacement()
    {
        if (_disposed || _startupApplied)
        {
            return;
        }

        try
        {
            WindowStateFile? state = LoadState();
            DisplayArea? area = GetStartupDisplayArea();
            if (area is null)
            {
                _incidents.RecordInternalError(
                    "FASM1C01",
                    "No se pudo determinar el monitor de arranque; se conserva el tamaño del sistema.",
                    LogSource);
                return;
            }

            uint dpi = GetDpiForArea(area, _hwnd);
            _currentDpi = dpi;
            _currentDisplayValue = area.DisplayId.Value;

            // El tamaño inicial se deriva siempre del WorkArea menos el hueco:
            // no se restaura el tamaño de sesiones anteriores.
            RectInt32 placement = ComputePlacement(area, dpi, widthDip: null, heightDip: null, out int gapPx, out bool clamped);
            ApplyRect(placement, autoPlaced: true);
            _startupRect = placement;
            _lastNormalSizeDip = ToDip(placement.Width, placement.Height, dpi);
            _startupApplied = true;

            // Se captura el tamaño normal aplicado antes de maximizar para que
            // el log distinga el cálculo del estado final.
            SizeInt32 appliedSize = _appWindow.Size;
            PointInt32 appliedPosition = _appWindow.Position;

            if (state?.IsMaximized is true)
            {
                try
                {
                    TryGetPresenter()?.Maximize();
                }
                catch (Exception ex)
                {
                    _incidents.RecordException(ex, LogSource, "Restaurando el estado maximizado");
                }
            }

            double scale = dpi / 96.0;
            string context = string.Create(
                CultureInfo.InvariantCulture,
                $"{Describe(area, dpi)}; Gap={gapPx}px({gapPx / scale:0.#} DIP); Rect={placement.X},{placement.Y} {placement.Width}x{placement.Height}; RectDip={placement.Width / scale:0}x{placement.Height / scale:0}; Applied={appliedPosition.X},{appliedPosition.Y} {appliedSize.Width}x{appliedSize.Height}; Clamped={clamped}; RestoredMaximized={state?.IsMaximized is true}");
            _incidents.RecordInfo("FASM1C02", "Ventana colocada al iniciar", LogSource, context);
        }
        catch (Exception ex)
        {
            _incidents.RecordException(ex, LogSource, "Aplicando la geometría inicial");
        }
        finally
        {
            HookEvents();
        }
    }

    private void HookEvents()
    {
        if (_eventsHooked || _disposed)
        {
            return;
        }

        _eventsHooked = true;
        _appWindow.Changed += OnAppWindowChanged;
        _appWindow.Closing += OnAppWindowClosing;
        _window.Activated += OnWindowActivated;

        try
        {
            _watcher = DisplayArea.CreateWatcher();
            _watcher.Removed += OnDisplayRemoved;
            _watcher.Updated += OnDisplayUpdated;
            _watcher.Start();
        }
        catch (Exception ex)
        {
            _incidents.RecordException(ex, LogSource, "Suscribiendo DisplayAreaWatcher");
        }
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (_disposed || _isApplying)
        {
            return;
        }

        try
        {
            bool normal = IsNormalState();

            if (normal && args.DidSizeChange)
            {
                SizeInt32 size = _appWindow.Size;
                if (size.Width > 0 && size.Height > 0)
                {
                    _lastNormalSizeDip = ToDip(size.Width, size.Height, GetCurrentDpi());
                }
            }

            if (!args.DidPositionChange && !args.DidSizeChange)
            {
                return;
            }

            var area = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Nearest);
            if (area is null)
            {
                return;
            }

            if (_currentDisplayValue is null || area.DisplayId.Value != _currentDisplayValue.Value)
            {
                _currentDisplayValue = area.DisplayId.Value;
                _currentDpi = GetDpiForArea(area, _hwnd);
                _incidents.RecordInfo(
                    "FASM1C03",
                    "Ventana movida a otro monitor",
                    LogSource,
                    Describe(area, _currentDpi));
            }

            if (!normal)
            {
                return;
            }

            RectInt32 work = area.WorkArea;
            bool tooLarge = _appWindow.Size.Width > work.Width || _appWindow.Size.Height > work.Height;
            if (!IsTitleBarReachable(work) || tooLarge)
            {
                Refit(area, "window-moved");
            }
        }
        catch (Exception ex)
        {
            _incidents.RecordException(ex, LogSource, "Procesando AppWindow.Changed");
        }
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_disposed)
        {
            SaveState();
        }
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_disposed || _firstActivationHandled || args.WindowActivationState == WindowActivationState.Deactivated)
        {
            return;
        }

        _firstActivationHandled = true;

        if (_startupRect is not { } expected)
        {
            return;
        }

        // WinUI puede aplicar su tamaño por defecto al mostrar la ventana. Se
        // comprueba en cuanto el HWND se estabiliza y se corrige si hizo falta.
        _window.DispatcherQueue.TryEnqueue(() => VerifyStartupPlacement(expected));
    }

    private void VerifyStartupPlacement(RectInt32 expected)
    {
        if (_disposed || !IsNormalState())
        {
            return;
        }

        try
        {
            PointInt32 position = _appWindow.Position;
            SizeInt32 size = _appWindow.Size;
            const int tolerance = 4;
            bool matches = Math.Abs(size.Width - expected.Width) <= tolerance
                && Math.Abs(size.Height - expected.Height) <= tolerance
                && Math.Abs(position.X - expected.X) <= tolerance
                && Math.Abs(position.Y - expected.Y) <= tolerance;

            string context = string.Create(
                CultureInfo.InvariantCulture,
                $"Esperado={expected.X},{expected.Y} {expected.Width}x{expected.Height}; Actual={position.X},{position.Y} {size.Width}x{size.Height}; DPI={GetCurrentDpi()}");
            _incidents.RecordInfo("FASM1C10", "Verificación de geometría tras activar", LogSource, context);

            if (!matches)
            {
                _incidents.RecordMappingError(
                    "FASM1C11",
                    "El tamaño inicial no se mantuvo tras activar; se reaplica.",
                    LogSource,
                    context);

                ApplyRect(expected, autoPlaced: true);
                _lastNormalSizeDip = ToDip(expected.Width, expected.Height, GetCurrentDpi());
            }
        }
        catch (Exception ex)
        {
            _incidents.RecordException(ex, LogSource, "Verificando la geometría tras activar");
        }
    }

    private void OnDisplayRemoved(DisplayAreaWatcher sender, DisplayArea removed)
    {
        if (_disposed || _isApplying || _currentDisplayValue is null)
        {
            return;
        }

        try
        {
            if (removed.DisplayId.Value != _currentDisplayValue.Value)
            {
                return;
            }

            PointInt32 position = _appWindow.Position;
            PointInt32 probe = new(position.X + 40, position.Y + 16);
            DisplayArea? nearest = DisplayArea.GetFromPoint(probe, DisplayAreaFallback.Nearest)
                ?? DisplayArea.Primary;

            if (nearest is null)
            {
                _incidents.RecordInternalError(
                    "FASM1C04",
                    "El monitor actual desapareció y no hay un área de trabajo disponible.",
                    LogSource,
                    $"DisplayId={removed.DisplayId.Value}");
                return;
            }

            _currentDisplayValue = nearest.DisplayId.Value;
            _currentDpi = GetDpiForArea(nearest, _hwnd);

            if (IsNormalState())
            {
                Refit(nearest, "display-removed", refill: IsAutoPlaced());
            }

            _incidents.RecordInfo(
                "FASM1C05",
                "Monitor desconectado; seguimiento actualizado",
                LogSource,
                Describe(nearest, _currentDpi));
        }
        catch (Exception ex)
        {
            _incidents.RecordException(ex, LogSource, "Procesando desconexión de monitor");
        }
    }

    private void OnDisplayUpdated(DisplayAreaWatcher sender, DisplayArea updated)
    {
        if (_disposed || _isApplying || _currentDisplayValue is null)
        {
            return;
        }

        try
        {
            if (updated.DisplayId.Value != _currentDisplayValue.Value)
            {
                return;
            }

            _currentDpi = GetDpiForArea(updated, _hwnd);

            if (!IsNormalState())
            {
                return;
            }

            bool autoPlaced = IsAutoPlaced();

            // Si la ventana conserva el tamaño automático, un cambio de escala
            // o resolución recalcula el hueco; si el usuario la redimensionó,
            // solo se reajusta cuando desborda.
            if (Refit(updated, "display-updated", refill: autoPlaced))
            {
                _incidents.RecordInfo(
                    "FASM1C06",
                    "Configuración de pantalla cambiada; ventana reajustada",
                    LogSource,
                    Describe(updated, _currentDpi));
            }
        }
        catch (Exception ex)
        {
            _incidents.RecordException(ex, LogSource, "Procesando cambio de configuración de pantalla");
        }
    }

    private bool Refit(DisplayArea area, string reason, bool refill = false)
    {
        if (!IsNormalState())
        {
            return false;
        }

        uint dpi = GetDpiForArea(area, _hwnd);
        _currentDpi = dpi;
        _currentDisplayValue = area.DisplayId.Value;

        bool autoPlaced = refill || IsAutoPlaced();

        double? widthDip = null;
        double? heightDip = null;
        if (!refill)
        {
            widthDip = _lastNormalSizeDip?.Width ?? _appWindow.Size.Width * 96.0 / dpi;
            heightDip = _lastNormalSizeDip?.Height ?? _appWindow.Size.Height * 96.0 / dpi;
        }

        RectInt32 placement = ComputePlacement(area, dpi, widthDip, heightDip, out int gapPx, out bool clamped);
        if (SameRect(placement))
        {
            return false;
        }

        ApplyRect(placement, autoPlaced);
        _lastNormalSizeDip = ToDip(placement.Width, placement.Height, dpi);

        string context = string.Create(
            CultureInfo.InvariantCulture,
            $"Motivo={reason}; {Describe(area, dpi)}; Gap={gapPx}px; Rect={placement.X},{placement.Y} {placement.Width}x{placement.Height}; Clamped={clamped}");
        _incidents.RecordInfo("FASM1C07", "Ventana reajustada automáticamente", LogSource, context);
        return true;
    }

    private static RectInt32 ComputePlacement(
        DisplayArea area,
        uint dpi,
        double? widthDip,
        double? heightDip,
        out int gapPx,
        out bool clamped)
    {
        RectInt32 work = area.WorkArea;
        double scale = dpi / 96.0;

        gapPx = ComputeEdgeGapPx(work, scale);
        int availableWidth = Math.Max(1, work.Width - (gapPx * 2));
        int availableHeight = Math.Max(1, work.Height - (gapPx * 2));

        int minWidth = Math.Min((int)Math.Round(MinWidthDip * scale), availableWidth);
        int minHeight = Math.Min((int)Math.Round(MinHeightDip * scale), availableHeight);

        // Sin tamaño previo se ocupa todo el área útil menos el hueco; con un
        // tamaño previo solo se recorta si no cabe en el nuevo WorkArea.
        int desiredWidth = widthDip is { } savedWidth
            ? (int)Math.Round(savedWidth * scale)
            : availableWidth;
        int desiredHeight = heightDip is { } savedHeight
            ? (int)Math.Round(savedHeight * scale)
            : availableHeight;

        int width = Math.Clamp(desiredWidth, minWidth, availableWidth);
        int height = Math.Clamp(desiredHeight, minHeight, availableHeight);
        clamped = width != desiredWidth || height != desiredHeight;

        int x = work.X + ((work.Width - width) / 2);
        int y = work.Y + ((work.Height - height) / 2);
        return new RectInt32(x, y, width, height);
    }

    private static int ComputeEdgeGapPx(RectInt32 work, double scale)
    {
        double shortestSideDip = Math.Min(work.Width, work.Height) / scale;
        double gapDip = Math.Clamp(shortestSideDip * EdgeGapRatio, MinEdgeGapDip, MaxEdgeGapDip);
        int gapPx = (int)Math.Round(gapDip * scale);

        // Nunca consumir más de un octavo del lado menor (pantallas pequeñas).
        int safetyCap = Math.Max(1, (int)Math.Round(Math.Min(work.Width, work.Height) / 8.0));
        return Math.Clamp(gapPx, 1, safetyCap);
    }

    private void ApplyRect(RectInt32 rect, bool autoPlaced)
    {
        _isApplying = true;
        try
        {
            _appWindow.MoveAndResize(rect);
        }
        finally
        {
            _isApplying = false;
        }

        _lastAutoRect = autoPlaced ? rect : null;
    }

    /// <summary>
    /// Indica si el tamaño actual sigue siendo el colocado automáticamente
    /// (relleno menos hueco), es decir, si el usuario no lo ha redimensionado.
    /// </summary>
    private bool IsAutoPlaced()
    {
        if (_lastAutoRect is not { } rect)
        {
            return false;
        }

        SizeInt32 size = _appWindow.Size;
        const int tolerance = 4;
        return Math.Abs(size.Width - rect.Width) <= tolerance
            && Math.Abs(size.Height - rect.Height) <= tolerance;
    }

    private bool SameRect(RectInt32 rect)
    {
        PointInt32 position = _appWindow.Position;
        SizeInt32 size = _appWindow.Size;
        const int tolerance = 4;
        return Math.Abs(position.X - rect.X) <= tolerance
            && Math.Abs(position.Y - rect.Y) <= tolerance
            && Math.Abs(size.Width - rect.Width) <= tolerance
            && Math.Abs(size.Height - rect.Height) <= tolerance;
    }

    private bool Fits(RectInt32 work)
    {
        PointInt32 position = _appWindow.Position;
        SizeInt32 size = _appWindow.Size;
        return position.X >= work.X
            && position.Y >= work.Y
            && (long)position.X + size.Width <= (long)work.X + work.Width
            && (long)position.Y + size.Height <= (long)work.Y + work.Height;
    }

    private bool IsTitleBarReachable(RectInt32 work)
    {
        PointInt32 position = _appWindow.Position;
        SizeInt32 size = _appWindow.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return true;
        }

        double scale = GetCurrentDpi() / 96.0;
        int strip = (int)Math.Round(TitleBarDip * scale);
        int minVisible = (int)Math.Round(MinTitleBarVisibleDip * scale);

        int titleBottom = position.Y + Math.Min(strip, size.Height);
        bool verticallyReachable = titleBottom > work.Y && position.Y < work.Y + work.Height;

        int visibleLeft = Math.Max(position.X, work.X);
        int visibleRight = Math.Min(position.X + size.Width, work.X + work.Width);
        bool horizontallyReachable = visibleRight - visibleLeft >= Math.Min(minVisible, size.Width);

        return verticallyReachable && horizontallyReachable;
    }

    private bool IsNormalState()
    {
        return TryGetPresenter() is not { } presenter
            || presenter.State is OverlappedPresenterState.Restored;
    }

    private OverlappedPresenter? TryGetPresenter() => _appWindow.Presenter as OverlappedPresenter;

    private uint GetCurrentDpi()
    {
        var area = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Nearest);
        if (area is not null)
        {
            return GetDpiForArea(area, _hwnd);
        }

        uint dpi = NativeMethods.GetDpiForWindow(_hwnd);
        return IsValidDpi(dpi) ? dpi : _currentDpi;
    }

    private DisplayArea? GetStartupDisplayArea()
    {
        try
        {
            if (NativeMethods.GetCursorPos(out NativeMethods.NativePoint point))
            {
                PointInt32 cursor = new(point.X, point.Y);
                var area = DisplayArea.GetFromPoint(cursor, DisplayAreaFallback.Nearest);
                if (area is not null)
                {
                    return area;
                }
            }
        }
        catch
        {
            // Fallback a la pantalla principal.
        }

        try
        {
            return DisplayArea.Primary
                ?? DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        }
        catch
        {
            return null;
        }
    }

    private static uint GetDpiForArea(DisplayArea area, nint hwnd)
    {
        try
        {
            RectInt32 work = area.WorkArea;
            NativeMethods.NativePoint center = new()
            {
                X = work.X + (work.Width / 2),
                Y = work.Y + (work.Height / 2)
            };

            nint monitor = NativeMethods.MonitorFromPoint(center, NativeMethods.MonitorDefaultToNearest);
            if (monitor != 0)
            {
                int result = NativeMethods.GetDpiForMonitor(
                    monitor,
                    NativeMethods.MdtEffectiveDpi,
                    out uint dpiX,
                    out _);
                if (result == 0 && IsValidDpi(dpiX))
                {
                    return dpiX;
                }
            }
        }
        catch
        {
            // Se intenta GetDpiForWindow como siguiente opción.
        }

        uint windowDpi = NativeMethods.GetDpiForWindow(hwnd);
        return IsValidDpi(windowDpi) ? windowDpi : 96;
    }

    private static bool IsValidDpi(uint dpi) => dpi is >= 48 and <= 768;

    private static (double Width, double Height) ToDip(int width, int height, uint dpi)
    {
        double scale = dpi / 96.0;
        return (width / scale, height / scale);
    }

    private static string Describe(DisplayArea area, uint dpi)
    {
        RectInt32 work = area.WorkArea;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"DisplayId={area.DisplayId.Value}; DPI={dpi}; Scale={dpi / 96.0:0.00}; WorkArea={work.X},{work.Y} {work.Width}x{work.Height}");
    }

    private WindowStateFile? LoadState()
    {
        string path = GetStateFilePath();
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            string json = File.ReadAllText(path);
            WindowStateFile? state = JsonSerializer.Deserialize<WindowStateFile>(json, _jsonOptions);
            if (state is null)
            {
                return null;
            }

            if (state.Version < CurrentStateVersion)
            {
                _incidents.RecordInfo(
                    "FASM1C09",
                    "window-state.json de una versión anterior; se conserva solo el estado maximizado.",
                    LogSource,
                    $"Version={state.Version}");
                state.Version = CurrentStateVersion;
            }

            return state;
        }
        catch (Exception ex)
        {
            _incidents.RecordException(ex, LogSource, $"Leyendo {path}");
            return null;
        }
    }

    private void SaveState()
    {
        if (!_startupApplied)
        {
            return;
        }

        try
        {
            var state = new WindowStateFile
            {
                Version = CurrentStateVersion,
                IsMaximized = TryGetPresenter()?.State == OverlappedPresenterState.Maximized
            };

            string path = GetStateFilePath();
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(state, _jsonOptions));
            _stateSaved = true;
        }
        catch (Exception ex)
        {
            _incidents.RecordException(ex, LogSource, "Guardando window-state.json");
        }
    }

    private static string GetStateFilePath()
    {
        string directory;
        try
        {
            directory = ApplicationData.Current.LocalFolder.Path;
        }
        catch
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            directory = Path.Combine(localAppData, "FluentASM_IDE");
        }

        return Path.Combine(directory, StateFileName);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (!_stateSaved)
            {
                SaveState();
            }
        }
        catch
        {
            // SaveState ya registra sus propios fallos.
        }

        _appWindow.Changed -= OnAppWindowChanged;
        _appWindow.Closing -= OnAppWindowClosing;
        _window.Activated -= OnWindowActivated;

        if (_watcher is not null)
        {
            try
            {
                _watcher.Removed -= OnDisplayRemoved;
                _watcher.Updated -= OnDisplayUpdated;
                _watcher.Stop();
            }
            catch (Exception ex)
            {
                _incidents.RecordException(ex, LogSource, "Deteniendo DisplayAreaWatcher");
            }
        }
    }

    private sealed class WindowStateFile
    {
        public int Version { get; set; }

        public bool IsMaximized { get; set; }
    }

    private static class NativeMethods
    {
        internal const uint MonitorDefaultToNearest = 2;
        internal const int MdtEffectiveDpi = 0;

        [StructLayout(LayoutKind.Sequential)]
        internal struct NativePoint
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetCursorPos(out NativePoint point);

        [DllImport("user32.dll")]
        internal static extern nint MonitorFromPoint(NativePoint point, uint flags);

        [DllImport("user32.dll")]
        internal static extern uint GetDpiForWindow(nint hwnd);

        [DllImport("shcore.dll")]
        internal static extern int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);
    }
}
