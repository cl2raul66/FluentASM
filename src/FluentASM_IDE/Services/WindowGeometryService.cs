using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using FluentASM.Lsp.Diagnostics;
using FluentASM_IDE.Helpers;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Windows.Storage;

namespace FluentASM_IDE.Services;

/// <summary>
/// Coloca la ventana principal centrada sobre <c>WorkArea</c> con un hueco
/// visual uniforme en los 4 lados, compensando el marco invisible de DWM,
/// escalado DPI y cambios multi-monitor.
/// </summary>
public sealed class WindowGeometryService : IDisposable
{
    private const string LogSource = "WindowGeometryService";
    private const string StateFileName = "window-state.json";
    private const int CurrentStateVersion = 2;

    // Hueco pequeño proporcional al lado menor en DIPs. El mínimo garantiza
    // aire visual suficiente para absorber el marco invisible DWM sin invadir
    // monitores adyacentes ni la barra de tareas.
    private const double EdgeGapRatio = 0.008;
    private const double MinEdgeGapDip = 10;
    private const double MaxEdgeGapDip = 18;

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
    private RectInt32? _startupRect;
    private RectInt32? _lastAutoRect;
    private FrameInsets? _measuredLiveInsets;
    private uint _currentDpi;
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
        _currentDpi = ReadWindowDpiFallback();
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

            FrameInsets insets = GetFrameInsets(dpi);
            RectInt32 placement = ComputeAutoPlacement(area, dpi, insets, out int gapPx, out bool clamped);
            ApplyRect(placement, autoPlaced: true);

            _startupRect = placement;
            _startupApplied = true;

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
                $"{Describe(area, dpi)}; Gap={gapPx}px({gapPx / scale:0.#} DIP); Insets={insets}; Rect={placement.X},{placement.Y} {placement.Width}x{placement.Height}; Applied={appliedPosition.X},{appliedPosition.Y} {appliedSize.Width}x{appliedSize.Height}; Clamped={clamped}; RestoredMaximized={state?.IsMaximized is true}");
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

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_disposed || _firstActivationHandled || args.WindowActivationState == WindowActivationState.Deactivated)
        {
            return;
        }

        _firstActivationHandled = true;

        _window.DispatcherQueue.TryEnqueue(RefinePlacementAfterComposition);
    }

    private void RefinePlacementAfterComposition()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            // Una vez mostrada la ventana, DWM ya ha compuesto el marco extendido
            // real; lo medimos y refinamos si difiere de la estimación previa.
            if (TryMeasureLiveDwmInsets(out FrameInsets liveInsets))
            {
                _measuredLiveInsets = liveInsets;
            }

            if (!IsNormalState() || _startupRect is not { } expected || _lastAutoRect is null)
            {
                return;
            }

            var area = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Nearest);
            if (area is null)
            {
                return;
            }

            uint dpi = GetDpiForArea(area, _hwnd);
            _currentDpi = dpi;
            FrameInsets activeInsets = _measuredLiveInsets ?? GetFrameInsets(dpi);
            RectInt32 refined = ComputeAutoPlacement(area, dpi, activeInsets, out _, out _);

            if (!IsCurrentRect(refined) || !IsCurrentRect(expected))
            {
                ApplyRect(refined, autoPlaced: true);
                _startupRect = refined;
            }
        }
        catch (Exception ex)
        {
            _incidents.RecordException(ex, LogSource, "Refinando geometría tras composición DWM");
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
            if (!args.DidPositionChange && !args.DidSizeChange)
            {
                return;
            }

            bool normal = IsNormalState();
            if (normal && _lastAutoRect is { } autoRect && !IsCurrentRect(autoRect))
            {
                // El usuario movió, redimensionó o acopló con Snap la ventana.
                _lastAutoRect = null;
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
                if (TryMeasureLiveDwmInsets(out FrameInsets liveInsets))
                {
                    _measuredLiveInsets = liveInsets;
                }

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

            FrameInsets insets = GetFrameInsets(_currentDpi);
            RectInt32 work = area.WorkArea;
            RectInt32 visible = GetCurrentVisibleRect(insets);

            bool overflowsSize = visible.Width > work.Width || visible.Height > work.Height;
            if (!IsTitleBarReachable(visible, work, _currentDpi) || overflowsSize)
            {
                ClampVisibleToWorkArea(area, _currentDpi, insets, "window-moved");
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

            DisplayArea? nearest = GetNearestAvailableDisplayArea();
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
            _measuredLiveInsets = null;

            if (IsNormalState())
            {
                FrameInsets insets = GetFrameInsets(_currentDpi);
                if (IsAutoPlaced())
                {
                    RefitAuto(nearest, _currentDpi, insets, "display-removed");
                }
                else
                {
                    RectInt32 visible = GetCurrentVisibleRect(insets);
                    if (!FitsVisible(visible, nearest.WorkArea))
                    {
                        ClampVisibleToWorkArea(nearest, _currentDpi, insets, "display-removed");
                    }
                }
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
            if (TryMeasureLiveDwmInsets(out FrameInsets liveInsets))
            {
                _measuredLiveInsets = liveInsets;
            }

            if (!IsNormalState())
            {
                return;
            }

            FrameInsets insets = GetFrameInsets(_currentDpi);
            if (IsAutoPlaced())
            {
                // Mientras conserve colocación automática, recalcula el hueco
                // simétrico para adaptarse a nueva resolución, escala o barra.
                if (RefitAuto(updated, _currentDpi, insets, "display-updated"))
                {
                    _incidents.RecordInfo(
                        "FASM1C06",
                        "Configuración de pantalla cambiada; ventana reajustada",
                        LogSource,
                        Describe(updated, _currentDpi));
                }

                return;
            }

            // Si el usuario movió, redimensionó o usó Windows Snap y sigue
            // cabiendo dentro del WorkArea visible, no se toca ni se recentra.
            RectInt32 visible = GetCurrentVisibleRect(insets);
            if (!FitsVisible(visible, updated.WorkArea))
            {
                ClampVisibleToWorkArea(updated, _currentDpi, insets, "display-updated");
            }
        }
        catch (Exception ex)
        {
            _incidents.RecordException(ex, LogSource, "Procesando cambio de configuración de pantalla");
        }
    }

    private bool RefitAuto(DisplayArea area, uint dpi, FrameInsets insets, string reason)
    {
        RectInt32 placement = ComputeAutoPlacement(area, dpi, insets, out int gapPx, out bool clamped);
        if (IsCurrentRect(placement))
        {
            return false;
        }

        ApplyRect(placement, autoPlaced: true);

        string context = string.Create(
            CultureInfo.InvariantCulture,
            $"Motivo={reason}; {Describe(area, dpi)}; Gap={gapPx}px; Insets={insets}; Rect={placement.X},{placement.Y} {placement.Width}x{placement.Height}; Clamped={clamped}");
        _incidents.RecordInfo("FASM1C07", "Ventana reajustada automáticamente", LogSource, context);
        return true;
    }

    /// <summary>
    /// Desplaza o recorta lo mínimo indispensable el rectángulo visible para
    /// que vuelva a entrar en <see cref="DisplayArea.WorkArea"/> sin recentrar
    /// a la fuerza la ventana elegida por el usuario.
    /// </summary>
    private void ClampVisibleToWorkArea(DisplayArea area, uint dpi, FrameInsets insets, string reason)
    {
        RectInt32 work = area.WorkArea;
        RectInt32 visible = GetCurrentVisibleRect(insets);

        (int minWidth, int minHeight) = GetMinimumVisibleSizePx(dpi, insets, work.Width, work.Height);

        int width = Math.Clamp(visible.Width, Math.Min(minWidth, work.Width), work.Width);
        int height = Math.Clamp(visible.Height, Math.Min(minHeight, work.Height), work.Height);

        int x = Math.Clamp(visible.X, work.X, work.X + work.Width - width);
        int y = Math.Clamp(visible.Y, work.Y, work.Y + work.Height - height);

        RectInt32 outer = ToOuterRect(new RectInt32(x, y, width, height), insets);
        if (IsCurrentRect(outer))
        {
            return;
        }

        ApplyRect(outer, autoPlaced: false);

        string context = string.Create(
            CultureInfo.InvariantCulture,
            $"Motivo={reason}; {Describe(area, dpi)}; Visible={x},{y} {width}x{height}; Outer={outer.X},{outer.Y} {outer.Width}x{outer.Height}");
        _incidents.RecordInfo("FASM1C08", "Ventana re-encajada en WorkArea sin recentrar", LogSource, context);
    }

    private RectInt32 ComputeAutoPlacement(
        DisplayArea area,
        uint dpi,
        FrameInsets insets,
        out int gapPx,
        out bool clamped)
    {
        RectInt32 work = area.WorkArea;
        double scale = dpi / 96.0;

        gapPx = ComputeEdgeGapPx(work, scale, insets);
        int availableVisibleWidth = Math.Max(1, work.Width - (gapPx * 2));
        int availableVisibleHeight = Math.Max(1, work.Height - (gapPx * 2));

        (int minVisibleWidth, int minVisibleHeight) = GetMinimumVisibleSizePx(
            dpi,
            insets,
            availableVisibleWidth,
            availableVisibleHeight);

        int visibleWidth = Math.Clamp(availableVisibleWidth, minVisibleWidth, availableVisibleWidth);
        int visibleHeight = Math.Clamp(availableVisibleHeight, minVisibleHeight, availableVisibleHeight);
        clamped = visibleWidth != availableVisibleWidth || visibleHeight != availableVisibleHeight;

        int visibleX = work.X + ((work.Width - visibleWidth) / 2);
        int visibleY = work.Y + ((work.Height - visibleHeight) / 2);

        return ToOuterRect(new RectInt32(visibleX, visibleY, visibleWidth, visibleHeight), insets);
    }

    private static int ComputeEdgeGapPx(RectInt32 work, double scale, FrameInsets insets)
    {
        double shortestSideDip = Math.Min(work.Width, work.Height) / scale;
        double gapDip = Math.Clamp(shortestSideDip * EdgeGapRatio, MinEdgeGapDip, MaxEdgeGapDip);
        int gapPx = (int)Math.Round(gapDip * scale);

        int maxInset = Math.Max(Math.Max(insets.Left, insets.Right), Math.Max(insets.Top, insets.Bottom));
        int safetyCap = Math.Max(1, (int)Math.Round(Math.Min(work.Width, work.Height) / 8.0));

        // Si el área útil lo permite, aseguramos que el hueco cubra al menos el
        // marco invisible DWM para que no invada monitores vecinos ni la barra.
        int minAllowed = Math.Min(maxInset, safetyCap);
        return Math.Clamp(gapPx, Math.Max(1, minAllowed), safetyCap);
    }

    private (int MinWidth, int MinHeight) GetMinimumVisibleSizePx(
        uint dpi,
        FrameInsets insets,
        int maxWidth,
        int maxHeight)
    {
        double scale = dpi / 96.0;

        int minOuterWidth = 0;
        int minOuterHeight = 0;

        if (TryGetPresenter() is { } presenter)
        {
            if (presenter.PreferredMinimumWidth is { } preferredMinWidth and > 0)
            {
                minOuterWidth = preferredMinWidth;
            }

            if (presenter.PreferredMinimumHeight is { } preferredMinHeight and > 0)
            {
                minOuterHeight = preferredMinHeight;
            }
        }

        if (_window.Content is FrameworkElement root)
        {
            if (minOuterWidth == 0 && IsFinitePositive(root.MinWidth))
            {
                minOuterWidth = (int)Math.Ceiling(root.MinWidth * scale) + insets.Horizontal;
            }

            if (minOuterHeight == 0 && IsFinitePositive(root.MinHeight))
            {
                minOuterHeight = (int)Math.Ceiling(root.MinHeight * scale) + insets.Vertical;
            }
        }

        int osMinTrackWidth = Math.Max(1, NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCxMinTrack, dpi));
        int osMinTrackHeight = Math.Max(1, NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCyMinTrack, dpi));

        minOuterWidth = Math.Max(minOuterWidth, osMinTrackWidth);
        minOuterHeight = Math.Max(minOuterHeight, osMinTrackHeight);

        int minVisibleWidth = Math.Max(1, minOuterWidth - insets.Horizontal);
        int minVisibleHeight = Math.Max(1, minOuterHeight - insets.Vertical);

        return (Math.Min(minVisibleWidth, maxWidth), Math.Min(minVisibleHeight, maxHeight));
    }

    private static bool IsFinitePositive(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;

    private static RectInt32 ToOuterRect(RectInt32 visible, FrameInsets insets)
    {
        return new RectInt32(
            visible.X - insets.Left,
            visible.Y - insets.Top,
            visible.Width + insets.Horizontal,
            visible.Height + insets.Vertical);
    }

    private RectInt32 GetCurrentVisibleRect(FrameInsets insets)
    {
        if (TryGetDwmExtendedFrameRect(out RectInt32 extended))
        {
            return extended;
        }

        PointInt32 position = _appWindow.Position;
        SizeInt32 size = _appWindow.Size;
        return new RectInt32(
            position.X + insets.Left,
            position.Y + insets.Top,
            Math.Max(1, size.Width - insets.Horizontal),
            Math.Max(1, size.Height - insets.Vertical));
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

    private bool IsAutoPlaced() => _lastAutoRect is not null;

    private bool IsCurrentRect(RectInt32 rect)
    {
        PointInt32 position = _appWindow.Position;
        SizeInt32 size = _appWindow.Size;
        return position.X == rect.X
            && position.Y == rect.Y
            && size.Width == rect.Width
            && size.Height == rect.Height;
    }

    private static bool FitsVisible(RectInt32 visible, RectInt32 work)
    {
        return visible.X >= work.X
            && visible.Y >= work.Y
            && (long)visible.X + visible.Width <= (long)work.X + work.Width
            && (long)visible.Y + visible.Height <= (long)work.Y + work.Height;
    }

    private bool IsTitleBarReachable(RectInt32 visible, RectInt32 work, uint dpi)
    {
        if (visible.Width <= 0 || visible.Height <= 0)
        {
            return true;
        }

        int titleBarHeight = GetTitleBarHeightPx(dpi);
        int minVisibleWidth = GetMinReachableTitleBarWidthPx(dpi);

        int titleBottom = visible.Y + Math.Min(titleBarHeight, visible.Height);
        bool verticallyReachable = titleBottom > work.Y && visible.Y < work.Y + work.Height;

        int overlapLeft = Math.Max(visible.X, work.X);
        int overlapRight = Math.Min(visible.X + visible.Width, work.X + work.Width);
        bool horizontallyReachable = overlapRight - overlapLeft >= Math.Min(minVisibleWidth, visible.Width);

        return verticallyReachable && horizontallyReachable;
    }

    private int GetTitleBarHeightPx(uint dpi)
    {
        try
        {
            int appBarHeight = _appWindow.TitleBar.Height;
            if (appBarHeight > 0)
            {
                return appBarHeight;
            }
        }
        catch
        {
            // Se usa la métrica del sistema para el DPI activo.
        }

        int caption = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCyCaption, dpi);
        int frame = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCySizeFrame, dpi);
        int padded = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCxPaddedBorder, dpi);
        return Math.Max(1, caption + frame + padded);
    }

    private static int GetMinReachableTitleBarWidthPx(uint dpi)
    {
        int captionButton = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCxSize, dpi);
        int minWindow = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCxMin, dpi);
        return Math.Max(1, Math.Max(captionButton * 3, minWindow));
    }

    private FrameInsets GetFrameInsets(uint dpi)
    {
        if (_measuredLiveInsets is { } measured && _currentDpi == dpi)
        {
            return measured;
        }

        if (TryMeasureLiveDwmInsets(out FrameInsets live))
        {
            _measuredLiveInsets = live;
            return live;
        }

        return EstimateFrameInsetsFromSystemMetrics(dpi);
    }

    private static FrameInsets EstimateFrameInsetsFromSystemMetrics(uint dpi)
    {
        int sizeFrameX = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCxSizeFrame, dpi);
        int sizeFrameY = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCySizeFrame, dpi);
        int padded = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCxPaddedBorder, dpi);
        int visibleBorderX = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCxBorder, dpi);
        int visibleBorderY = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCyBorder, dpi);

        int horizontal = Math.Max(0, sizeFrameX + padded - visibleBorderX);
        int bottom = Math.Max(0, sizeFrameY + padded - visibleBorderY);

        // En Windows 10/11 las ventanas overlapped estándar no tienen marco
        // invisible externo arriba (el borde superior visible coincide con Y).
        return new FrameInsets(horizontal, Top: 0, horizontal, bottom);
    }

    private bool TryMeasureLiveDwmInsets(out FrameInsets insets)
    {
        insets = default;
        if (_hwnd == 0)
        {
            return false;
        }

        if (!NativeMethods.GetWindowRect(_hwnd, out NativeMethods.NativeRect outer))
        {
            return false;
        }

        if (!TryGetDwmExtendedFrameRect(out RectInt32 extended))
        {
            return false;
        }

        int outerWidth = outer.Right - outer.Left;
        int outerHeight = outer.Bottom - outer.Top;
        if (outerWidth <= 0 || outerHeight <= 0)
        {
            return false;
        }

        int left = Math.Max(0, extended.X - outer.Left);
        int top = Math.Max(0, extended.Y - outer.Top);
        int right = Math.Max(0, outer.Right - (extended.X + extended.Width));
        int bottom = Math.Max(0, outer.Bottom - (extended.Y + extended.Height));

        if (left + top + right + bottom == 0)
        {
            // Antes del primer frame DWM suele devolver el mismo rectángulo.
            return false;
        }

        insets = new FrameInsets(left, top, right, bottom);
        return true;
    }

    private bool TryGetDwmExtendedFrameRect(out RectInt32 rect)
    {
        rect = default;
        if (_hwnd == 0)
        {
            return false;
        }

        int size = Marshal.SizeOf<NativeMethods.NativeRect>();
        int hr = NativeMethods.DwmGetWindowAttribute(
            _hwnd,
            NativeMethods.DwmwaExtendedFrameBounds,
            out NativeMethods.NativeRect extended,
            size);
        if (hr != 0)
        {
            return false;
        }

        int width = extended.Right - extended.Left;
        int height = extended.Bottom - extended.Top;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        rect = new RectInt32(extended.Left, extended.Top, width, height);
        return true;
    }

    private bool IsNormalState()
    {
        return TryGetPresenter() is not { } presenter
            || presenter.State is OverlappedPresenterState.Restored;
    }

    private OverlappedPresenter? TryGetPresenter() => _appWindow.Presenter as OverlappedPresenter;

    private uint ReadWindowDpiFallback()
    {
        uint dpi = _hwnd != 0 ? NativeMethods.GetDpiForWindow(_hwnd) : 0;
        return IsValidDpi(dpi) ? dpi : 96;
    }

    private DisplayArea? GetStartupDisplayArea()
    {
        try
        {
            if (NativeMethods.GetCursorPos(out NativeMethods.NativePoint point))
            {
                var cursor = new PointInt32(point.X, point.Y);
                DisplayArea? area = DisplayArea.GetFromPoint(cursor, DisplayAreaFallback.Nearest);
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

    private DisplayArea? GetNearestAvailableDisplayArea()
    {
        try
        {
            DisplayArea? fromWindow = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Nearest);
            if (fromWindow is not null)
            {
                return fromWindow;
            }
        }
        catch
        {
            // Fallback al centro actual o monitor principal.
        }

        try
        {
            PointInt32 position = _appWindow.Position;
            SizeInt32 size = _appWindow.Size;
            var center = new PointInt32(position.X + (size.Width / 2), position.Y + (size.Height / 2));
            return DisplayArea.GetFromPoint(center, DisplayAreaFallback.Nearest)
                ?? DisplayArea.Primary;
        }
        catch
        {
            return DisplayArea.Primary;
        }
    }

    private static uint GetDpiForArea(DisplayArea area, nint hwnd)
    {
        try
        {
            nint monitor = Win32Interop.GetMonitorFromDisplayId(area.DisplayId);
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

        uint windowDpi = hwnd != 0 ? NativeMethods.GetDpiForWindow(hwnd) : 0;
        return IsValidDpi(windowDpi) ? windowDpi : 96;
    }

    private static bool IsValidDpi(uint dpi) => dpi is >= 48 and <= 768;

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

    private readonly record struct FrameInsets(int Left, int Top, int Right, int Bottom)
    {
        public int Horizontal => Left + Right;

        public int Vertical => Top + Bottom;

        public override string ToString() => $"L={Left},T={Top},R={Right},B={Bottom}";
    }

    private sealed class WindowStateFile
    {
        public int Version { get; set; }

        public bool IsMaximized { get; set; }
    }
}
