using System.Runtime.InteropServices;
using FluentASM.Lsp.Diagnostics;
using Microsoft.Windows.Storage;
using Windows.ApplicationModel;

namespace FluentASM_IDE.Services;

/// <summary>
/// Implementación de <see cref="ISettingsService"/> sobre
/// <see cref="ApplicationData"/> del Windows App SDK.
/// </summary>
/// <remarks>
/// <para>
/// Con identidad de paquete (MSIX) se usa <see cref="ApplicationData.GetDefault"/>.
/// Sin identidad (<c>WindowsPackageType=None</c>) se usa
/// <see cref="ApplicationData.GetForUnpackaged"/> con el publicador y el
/// producto verificados en <c>Package.appxmanifest</c>.
/// </para>
/// <para>
/// Solo se usa <c>LocalSettings</c>, que es un almacén propio gestionado por
/// el sistema. No se escribe ningún archivo, por lo que no colisiona con
/// <c>window-state.json</c> (geometría de ventana) ni con el directorio del
/// <see cref="IncidentRecorder"/>.
/// </para>
/// </remarks>
public sealed class SettingsService : ISettingsService, IDisposable
{
    private const string LastOpenedFolderKey = "LastOpenedFolder";

    // Valores verificados en Package.appxmanifest: Properties/PublisherDisplayName
    // ("cl2ra") y Properties/DisplayName ("FluentASM_IDE"). No se usa
    // Identity/@Publisher ("CN=cl2ra") porque ApplicationData.GetForUnpackaged
    // solo admite ASCII alfanumérico, espacio, guion, punto y guion bajo; el
    // certificado incluye '=' y la API lo rechaza con E_INVALIDARG.
    private const string UnpackagedPublisher = "cl2ra";
    private const string UnpackagedProduct = "FluentASM_IDE";

    private readonly IncidentRecorder _incidents;
    private ApplicationData? _applicationData;
    private bool _applicationDataInitialized;
    private bool _disposed;

    public SettingsService(IncidentRecorder incidents)
    {
        ArgumentNullException.ThrowIfNull(incidents);
        _incidents = incidents;
    }

    /// <inheritdoc />
    public string? LastOpenedFolder => ReadLastOpenedFolder();

    /// <inheritdoc />
    public string GetInitialFolder()
    {
        string? lastOpenedFolder = ReadLastOpenedFolder();
        if (!string.IsNullOrWhiteSpace(lastOpenedFolder) && Directory.Exists(lastOpenedFolder))
        {
            return lastOpenedFolder;
        }

        return GetDocumentsFolder();
    }

    /// <inheritdoc />
    public void SaveLastOpenedFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        try
        {
            ApplicationData? applicationData = GetApplicationData();
            if (applicationData is null)
            {
                return;
            }

            applicationData.LocalSettings.Values[LastOpenedFolderKey] = folderPath;
        }
        catch (Exception ex)
        {
            _incidents.RecordException(
                ex,
                "SettingsService.SaveLastOpenedFolder",
                folderPath);
        }
    }

    private string? ReadLastOpenedFolder()
    {
        try
        {
            ApplicationData? applicationData = GetApplicationData();
            if (applicationData is null)
            {
                return null;
            }

            if (applicationData.LocalSettings.Values.TryGetValue(
                    LastOpenedFolderKey,
                    out object? value)
                && value is string folderPath
                && !string.IsNullOrWhiteSpace(folderPath))
            {
                return folderPath;
            }
        }
        catch (Exception ex)
        {
            _incidents.RecordException(ex, "SettingsService.ReadLastOpenedFolder");
        }

        return null;
    }

    private ApplicationData? GetApplicationData()
    {
        if (_applicationDataInitialized)
        {
            return _applicationData;
        }

        _applicationDataInitialized = true;
        try
        {
            _applicationData = HasPackageIdentity()
                ? ApplicationData.GetDefault()
                : ApplicationData.GetForUnpackaged(UnpackagedPublisher, UnpackagedProduct);
        }
        catch (Exception ex)
        {
            _incidents.RecordException(
                ex,
                "SettingsService.GetApplicationData",
                $"ApplicationData (publisher={UnpackagedPublisher}, product={UnpackagedProduct})");
        }

        return _applicationData;
    }

    /// <summary>
    /// Indica si el proceso actual tiene identidad de paquete. La consulta a
    /// <see cref="Package.Current"/> es el mecanismo canónico de WinRT; en
    /// procesos <em>unpackaged</em> lanza una excepción esperada.
    /// </summary>
    private static bool HasPackageIdentity()
    {
        try
        {
            _ = Package.Current.Id;
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            return false;
        }
    }

    private static string GetDocumentsFolder()
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return string.IsNullOrWhiteSpace(documents) ? string.Empty : documents;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _applicationData?.Dispose();
        _applicationData = null;
    }
}
