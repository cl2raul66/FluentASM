using System.ComponentModel;
using System.Runtime.InteropServices;
using FluentASM.Core.Encoding;

namespace FluentASM.Core.Execution;

/// <summary>
/// Resultado de ejecutar una imagen plana <c>.x64</c> dentro del proceso actual.
/// </summary>
/// <remarks>
/// El ejecutor solo admite rutinas de usuario que retornan con
/// <c>Ipr.flowStackJump()</c> (<c>RET</c>) y dejan su resultado en
/// <c>Gpr0</c> (<c>RAX</c>). Las imágenes con instrucciones privilegiadas se
/// rechazan antes de reservar memoria ejecutable.
/// </remarks>
public sealed class InMemoryRunResult
{
    private InMemoryRunResult(bool executed, long returnValue, string? error)
    {
        Executed = executed;
        ReturnValue = returnValue;
        Error = error;
    }

    /// <summary>Indica si la imagen llegó a ejecutarse.</summary>
    public bool Executed { get; }

    /// <summary>Valor devuelto en <c>Gpr0</c> (<c>RAX</c>) por la rutina ejecutada.</summary>
    public long ReturnValue { get; }

    /// <summary>Motivo del rechazo, o <see langword="null"/> si la ejecución fue correcta.</summary>
    public string? Error { get; }

    /// <summary>Indica si la imagen se ejecutó y retornó sin rechazo previo.</summary>
    public bool Success => Executed && Error is null;

    internal static InMemoryRunResult Completed(long returnValue) => new(true, returnValue, null);

    internal static InMemoryRunResult Rejected(string error) => new(false, 0, error);
}

/// <summary>
/// Lanzador de imágenes planas <c>.x64</c> PIC: reserva memoria ejecutable,
/// copia los bytes de la imagen, invoca su offset <c>0</c> y recupera el valor
/// de 64 bits que la rutina deja en <c>Gpr0</c> (<c>RAX</c>).
/// </summary>
/// <remarks>
/// <para>
/// En Windows usa <c>VirtualAlloc</c> con <c>PAGE_EXECUTE_READWRITE</c>,
/// <c>FlushInstructionCache</c> y <c>VirtualFree</c>; en Linux y macOS usa
/// <c>mmap</c>/<c>munmap</c> anónimos. El tamaño reservado es exactamente el de
/// la imagen, redondeado por el sistema operativo al tamaño de página.
/// </para>
/// <para>
/// Las imágenes que contienen instrucciones privilegiadas (<c>CLI</c>,
/// <c>STI</c>, <c>HLT</c>, <c>IN</c> u <c>OUT</c>) se rechazan: pertenecen al
/// modo <em>bare metal</em> y deben ejecutarse con <c>fluentasm boot-image</c>
/// dentro de QEMU.
/// </para>
/// </remarks>
public static partial class InMemoryRunner
{
    private const uint MemCommit = 0x0000_1000;
    private const uint MemReserve = 0x0000_2000;
    private const uint MemRelease = 0x0000_8000;
    private const uint PageExecuteReadWrite = 0x0000_0040;

    private const int ProtRead = 0x1;
    private const int ProtWrite = 0x2;
    private const int ProtExecute = 0x4;
    private const int MapPrivate = 0x02;
    private const int LinuxMapAnonymous = 0x20;
    private const int MacMapAnonymous = 0x1000;

    private static readonly string[] PrivilegedMnemonics = ["cli", "sti", "hlt", "in", "out"];

    /// <summary>
    /// Ejecuta la imagen y devuelve el valor retornado en <c>Gpr0</c>.
    /// </summary>
    /// <param name="image">Bytes exactos de la imagen plana <c>.x64</c>.</param>
    /// <param name="map">
    /// Mapa de la imagen para detectar instrucciones privilegiadas por
    /// instrucción; si se omite, se analiza el byte de cada posición (modo
    /// conservador).
    /// </param>
    /// <returns>Resultado de la ejecución o el motivo del rechazo.</returns>
    public static InMemoryRunResult Run(ReadOnlySpan<byte> image, ImageMap? map = null)
    {
        if (image.IsEmpty)
        {
            return InMemoryRunResult.Rejected(
                "La imagen está vacía: no hay código que ejecutar en el offset 0.");
        }

        if (FindPrivilegedInstruction(image, map) is { } reason)
        {
            return InMemoryRunResult.Rejected(reason);
        }

        if (!RuntimeInformation.ProcessArchitecture.Equals(Architecture.X64))
        {
            return InMemoryRunResult.Rejected(
                $"El ejecutor en memoria solo admite procesos x64; el actual es {RuntimeInformation.ProcessArchitecture}.");
        }

        return OperatingSystem.IsWindows()
            ? RunWindows(image)
            : RunUnix(image);
    }

    private static unsafe InMemoryRunResult RunWindows(ReadOnlySpan<byte> image)
    {
        var size = (nuint)image.Length;
        var address = VirtualAlloc(IntPtr.Zero, size, MemCommit | MemReserve, PageExecuteReadWrite);
        if (address == IntPtr.Zero)
        {
            var code = Marshal.GetLastPInvokeError();
            return InMemoryRunResult.Rejected(
                $"VirtualAlloc no pudo reservar {image.Length} byte(s) ejecutables ({DescribeError(code)}).");
        }

        try
        {
            fixed (byte* source = image)
            {
                Buffer.MemoryCopy(source, (void*)address, image.Length, image.Length);
            }

            _ = FlushInstructionCache(GetCurrentProcess(), address, size);
            return InMemoryRunResult.Completed(Invoke(address));
        }
        finally
        {
            _ = VirtualFree(address, 0, MemRelease);
        }
    }

    private static unsafe InMemoryRunResult RunUnix(ReadOnlySpan<byte> image)
    {
        var anonymous = OperatingSystem.IsMacOS() ? MacMapAnonymous : LinuxMapAnonymous;
        var address = mmap(
            IntPtr.Zero,
            (nuint)image.Length,
            ProtRead | ProtWrite | ProtExecute,
            MapPrivate | anonymous,
            -1,
            0);
        if (address == new IntPtr(-1))
        {
            var code = Marshal.GetLastPInvokeError();
            return InMemoryRunResult.Rejected(
                $"mmap no pudo reservar {image.Length} byte(s) ejecutables ({DescribeError(code)}).");
        }

        try
        {
            fixed (byte* source = image)
            {
                Buffer.MemoryCopy(source, (void*)address, image.Length, image.Length);
            }

            return InMemoryRunResult.Completed(Invoke(address));
        }
        finally
        {
            _ = munmap(address, (nuint)image.Length);
        }
    }

    private static unsafe long Invoke(IntPtr address) =>
        ((delegate* unmanaged<long>)(void*)address)();

    private static string DescribeError(int code) =>
        $"error {code}: {new Win32Exception(code).Message}";

    private static string? FindPrivilegedInstruction(ReadOnlySpan<byte> image, ImageMap? map)
    {
        if (map is not null)
        {
            foreach (var instruction in map.Instructions)
            {
                var space = instruction.Text.IndexOf(' ', StringComparison.Ordinal);
                var mnemonic = space < 0 ? instruction.Text : instruction.Text[..space];
                if (Array.IndexOf(PrivilegedMnemonics, mnemonic) >= 0)
                {
                    return DescribePrivileged(mnemonic, instruction.Offset);
                }
            }

            return null;
        }

        for (var offset = 0; offset < image.Length; offset++)
        {
            if (DescribeOpcode(image[offset]) is { } mnemonic)
            {
                return DescribePrivileged(mnemonic, offset);
            }
        }

        return null;
    }

    private static string? DescribeOpcode(byte opcode) => opcode switch
    {
        0xFA => "cli",
        0xFB => "sti",
        0xF4 => "hlt",
        0xE4 or 0xE5 or 0xEC or 0xED => "in",
        0xE6 or 0xE7 or 0xEE or 0xEF => "out",
        _ => null,
    };

    private static string DescribePrivileged(string mnemonic, int offset) =>
        $"La imagen contiene la instrucción privilegiada '{mnemonic}' en el offset 0x{offset:X4}; "
        + "el ejecutor en memoria solo admite rutinas de usuario. "
        + "Para bare metal usa 'fluentasm boot-image' y arranca la .img en QEMU.";

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr VirtualAlloc(IntPtr lpAddress, nuint dwSize, uint flAllocationType, uint flProtect);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int VirtualFree(IntPtr lpAddress, nuint dwSize, uint dwFreeType);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int FlushInstructionCache(IntPtr hProcess, IntPtr lpBaseAddress, nuint dwSize);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetCurrentProcess();

    [LibraryImport("libc", SetLastError = true)]
    private static partial IntPtr mmap(IntPtr addr, nuint length, int prot, int flags, int fd, nint offset);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int munmap(IntPtr addr, nuint length);
}


