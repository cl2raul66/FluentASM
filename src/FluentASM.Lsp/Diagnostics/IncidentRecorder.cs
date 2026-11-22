using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FluentASM.Lsp.Diagnostics;

/// <summary>
/// Registra incidentes internos del LSP/IDE para diagnóstico post-mortem.
/// Escribe en formato JSONL (una línea por incidente) en %LocalAppData%\FluentASM_IDE\logs\.
/// Libre de dependencias WinUI; usable desde el LSP en proceso.
/// </summary>
public sealed class IncidentRecorder : IDisposable
{
    private readonly string _logDirectory;
    private readonly string _logFilePath;
    private readonly ConcurrentQueue<IncidentEntry> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _writerTask;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private bool _disposed;

    /// <summary>
    /// Crea el registrador. Si <paramref name="logDirectory"/> es null, usa
    /// %LocalAppData%\FluentASM_IDE\logs\.
    /// </summary>
    public IncidentRecorder(string? logDirectory = null)
    {
        _logDirectory = logDirectory ?? GetDefaultLogDirectory();
        Directory.CreateDirectory(_logDirectory);

        string dateStamp = DateTime.UtcNow.ToString("yyyyMMdd");
        _logFilePath = Path.Combine(_logDirectory, $"incidents_{dateStamp}.jsonl");

        _writerTask = Task.Run(WriterLoopAsync, _cts.Token);
    }

    private static string GetDefaultLogDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "FluentASM_IDE", "logs");
    }

    /// <summary>
    /// Registra una excepción no controlada o error interno.
    /// </summary>
    public void RecordException(Exception ex, string source, string? context = null)
    {
        var entry = new IncidentEntry
        {
            TimestampUtc = DateTime.UtcNow,
            Level = IncidentLevel.Error,
            Code = "FASM9999",
            Source = source,
            Message = ex.Message,
            StackTrace = ex.StackTrace,
            Context = context,
            ExceptionType = ex.GetType().FullName
        };
        Enqueue(entry);
    }

    /// <summary>
    /// Registra un diagnóstico de tipo FASM9xxx (errores internos del pipeline).
    /// </summary>
    public void RecordInternalError(string code, string message, string source, string? context = null, string? reproducibleFragment = null)
    {
        var entry = new IncidentEntry
        {
            TimestampUtc = DateTime.UtcNow,
            Level = IncidentLevel.Error,
            Code = code,
            Source = source,
            Message = message,
            Context = context,
            ReproducibleFragment = reproducibleFragment
        };
        Enqueue(entry);
    }

    /// <summary>
    /// Registra un error de mapeo/rango (offsets, spans, coordenadas).
    /// </summary>
    public void RecordMappingError(string code, string message, string source, string? context = null, int? offset = null, int? length = null)
    {
        var entry = new IncidentEntry
        {
            TimestampUtc = DateTime.UtcNow,
            Level = IncidentLevel.Warning,
            Code = code,
            Source = source,
            Message = message,
            Context = context,
            Offset = offset,
            Length = length
        };
        Enqueue(entry);
    }

    /// <summary>
    /// Registra un evento informativo (inicio, análisis completado, etc.).
    /// </summary>
    public void RecordInfo(string code, string message, string source, string? context = null)
    {
        var entry = new IncidentEntry
        {
            TimestampUtc = DateTime.UtcNow,
            Level = IncidentLevel.Information,
            Code = code,
            Source = source,
            Message = message,
            Context = context
        };
        Enqueue(entry);
    }

    private void Enqueue(IncidentEntry entry)
    {
        if (_disposed)
        {
            return;
        }
        _queue.Enqueue(entry);
        _signal.Release();
    }

    private async Task WriterLoopAsync()
    {
        try
        {
            await using var writer = new StreamWriter(_logFilePath, append: true);
            while (!_cts.Token.IsCancellationRequested)
            {
                await _signal.WaitAsync(_cts.Token);
                while (_queue.TryDequeue(out var entry))
                {
                    string json = JsonSerializer.Serialize(entry, _jsonOptions);
                    await writer.WriteLineAsync(json);
                }
                await writer.FlushAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Normal al disponer
        }
        catch (Exception ex)
        {
            // Fallback: escribir en Debug para no perder el incidente
            System.Diagnostics.Debug.WriteLine($"[IncidentRecorder] Error escribiendo log: {ex}");
        }
    }

    /// <summary>
    /// Obtiene la ruta del archivo de log actual.
    /// </summary>
    /// <summary>
    /// Obtiene todos los incidentes del archivo de log actual (para el comando /logs).
    /// </summary>
    public IEnumerable<IncidentEntry> ReadCurrentLog()
    {
        if (!File.Exists(_logFilePath))
        {
            yield break;
        }

        foreach (string line in File.ReadLines(_logFilePath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var entry = TryParseEntry(line);
            if (entry != null)
            {
                yield return entry;
            }
        }
    }

    private IncidentEntry? TryParseEntry(string line)
    {
        try
        {
            var entry = JsonSerializer.Deserialize<IncidentEntry>(line, _jsonOptions);
            return entry;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _cts.Cancel();
        _signal.Release();
        try { _writerTask.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts.Dispose();
        _signal.Dispose();
    }
}

/// <summary>
/// Nivel de severidad del incidente.
/// </summary>
public enum IncidentLevel
{
    Information = 0,
    Warning = 1,
    Error = 2
}

/// <summary>
/// Entrada individual de incidente en formato JSONL.
/// </summary>
public sealed record IncidentEntry
{
    public DateTime TimestampUtc { get; init; }
    public IncidentLevel Level { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? Context { get; init; }
    public string? StackTrace { get; init; }
    public string? ExceptionType { get; init; }
    public string? ReproducibleFragment { get; init; }
    public int? Offset { get; init; }
    public int? Length { get; init; }
}