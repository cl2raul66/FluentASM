namespace FluentASM.Core.Projects;

/// <summary>
/// Arquitectura destino declarada por un proyecto FluentASM.
/// En Fase 1 solo existe x64 <em>bare metal</em>.
/// </summary>
public enum Architecture
{
    /// <summary>x86-64 (imagen plana PIC, sin sistema operativo previo).</summary>
    X64,
}
