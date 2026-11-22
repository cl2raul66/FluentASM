namespace FluentASM.Core.Execution;

/// <summary>
/// Ubicación de las piezas de una imagen de arranque <c>.img</c> para QEMU.
/// </summary>
/// <param name="BootSectorSize">Tamaño del sector de arranque (512 bytes).</param>
/// <param name="StubSize">Tamaño del stub de 64 bits que salta a la imagen.</param>
/// <param name="KernelAddress">Dirección física donde se copia la imagen <c>.x64</c>.</param>
/// <param name="KernelFileOffset">Offset del primer byte de la imagen dentro de la <c>.img</c>.</param>
/// <param name="KernelLength">Tamaño de la imagen <c>.x64</c> embebida.</param>
/// <param name="ChunkCount">Número de bloques de 32 KiB leídos por la BIOS.</param>
public sealed record BootImageLayout(
    int BootSectorSize,
    int StubSize,
    int KernelAddress,
    int KernelFileOffset,
    int KernelLength,
    int ChunkCount)
{
    /// <summary>Layout vacío usado cuando la imagen no pudo construirse.</summary>
    public static BootImageLayout Empty { get; } = new(0, 0, 0, 0, 0, 0);
}

/// <summary>
/// Resultado de empaquetar una imagen plana <c>.x64</c> en una imagen de
/// arranque <c>.img</c> para QEMU.
/// </summary>
public sealed class BootImageResult
{
    internal BootImageResult(byte[] image, BootImageLayout layout, string? error)
    {
        Image = image;
        Layout = layout;
        Error = error;
    }

    /// <summary>Bytes de la imagen <c>.img</c>, múltiplo de 512 bytes.</summary>
    public byte[] Image { get; }

    /// <summary>Ubicación del sector de arranque, el stub y la imagen embebida.</summary>
    public BootImageLayout Layout { get; }

    /// <summary>Motivo del rechazo, o <see langword="null"/> si el empaquetado fue correcto.</summary>
    public string? Error { get; }

    /// <summary>Indica si la imagen se generó sin errores.</summary>
    public bool Success => Error is null;

    internal static BootImageResult Failed(string error) =>
        new([], BootImageLayout.Empty, error);
}

/// <summary>
/// Empaquetador de imágenes planas <c>.x64</c> en una imagen de arranque
/// <c>.img</c> que QEMU puede iniciar en modo largo (64 bits).
/// </summary>
/// <remarks>
/// <para>
/// La <c>.img</c> contiene un MBR de 512 bytes (firma <c>0x55 0xAA</c>) que
/// normaliza los segmentos y la pila, conserva la unidad de arranque, carga los
/// sectores siguientes con <c>int 0x13 AH=0x42</c> en lecturas de 16 KiB,
/// habilita A20, monta tablas de paginación de identidad PML4/PDPT/PD con
/// páginas de 2 MiB, activa <c>CR4.PAE</c>, <c>IA32_EFER.LME</c> y
/// <c>CR0.PG</c>, entra en modo largo por un salto lejano y llama al offset
/// <c>0</c> de la imagen con la pila alineada a 16 bytes.
/// </para>
/// <para>
/// El cargador se construye byte a byte en C# (sin NASM) y la imagen se
/// rellena hasta un múltiplo de 32 KiB, de modo que cada lectura de la BIOS
/// queda dentro de un mismo bloque y no cruza límites de 64 KiB de DMA.
/// </para>
/// </remarks>
public static class BareMetalBootImageBuilder
{
    /// <summary>Tamaño del sector de arranque del MBR.</summary>
    public const int BootSectorSize = 512;

    /// <summary>Dirección física donde se carga el stub y la imagen <c>.x64</c>.</summary>
    public const int PayloadAddress = 0x1_0000;

    /// <summary>Dirección física de la base de la pila en modo largo.</summary>
    public const int StackTop = 0x9_F000;

    /// <summary>Primera dirección física que no puede ocupar la carga.</summary>
    public const int PayloadLimit = 0x8_0000;

    private const int KernelLoadSegment = 0x1000;

    private const int ChunkSectors = 64;
    private const int ChunkBytes = ChunkSectors * BootSectorSize;

    /// <summary>Sectores que se piden a la BIOS en cada lectura (16 KiB).</summary>
    private const int ReadSectors = 32;

    /// <summary>Bytes que cubre cada lectura de la BIOS.</summary>
    private const int ReadBytes = ReadSectors * BootSectorSize;

    /// <summary>Párrafos que avanza el segmento de destino tras cada lectura.</summary>
    private const int ReadParagraphs = ReadBytes / 16;

    /// <summary>Lecturas necesarias para cubrir un bloque de 32 KiB del layout.</summary>
    private const int ReadsPerChunk = ChunkSectors / ReadSectors;

    /// <summary>Intentos por lectura antes de declarar un error de disco.</summary>
    private const int ReadAttempts = 3;

    /// <summary>Alineación exigida a cada tabla de paginación.</summary>
    private const int PageTableAlignment = 0x1000;

    /// <summary>Dirección física de la PML4 (tabla de nivel 4).</summary>
    private const int Pml4Address = 0x1000;

    /// <summary>Dirección física del PDPT (tabla de nivel 3).</summary>
    private const int PdptAddress = 0x2000;

    /// <summary>Dirección física del PD (tabla de nivel 2, páginas de 2 MiB).</summary>
    private const int PdAddress = 0x3000;

    /// <summary>Bytes de cada tabla de paginación.</summary>
    private const int PageTableSize = 0x1000;

    /// <summary>Entradas del PD: 512 * 2 MiB = 1 GiB de identidad.</summary>
    private const int PageDirectoryEntries = 512;

    /// <summary>Tamaño de página de 2 MiB del PD (bit PS).</summary>
    private const uint PageSize2MiB = 0x0020_0000;

    /// <summary>Bits de bandera de una entrada de la PD con página de 2 MiB.</summary>
    private const uint PagePresent = 0x01;
    private const uint PageWritable = 0x02;
    private const uint PageSize = 0x80;

    /// <summary>Máximo direccionable por la primera página de identidad.</summary>
    private const int IdentitySpan = 0x0020_0000;

    /// <summary>Valores de control para entrar en modo largo.</summary>
    private const uint Cr0ProtectedModeEnable = 0x0000_0001;
    private const uint Cr0PagingEnable = 0x8000_0000;
    private const uint Cr4PhysicalAddressExtension = 0x0000_0020;
    private const uint EferLongModeEnable = 0x0000_0100;
    private const uint MsrIa32Efer = 0xC000_0080;

    /// <summary>Selectores de la GDT.</summary>
    private const ushort SelectorCode32 = 0x08;
    private const ushort SelectorData = 0x10;
    private const ushort SelectorCode64 = 0x18;

    private const int GdtSelectors = 4;
    private const int GdtSize = GdtSelectors * 8;
    private const int SectorSignatureOffset = 510;

    /// <summary>Descriptores de la GDT (base 0, límite 4 GiB, DPL 0).</summary>
    private const ulong GdtNullDescriptor = 0x0000_0000_0000_0000;
    private const ulong GdtCode32Descriptor = 0x00CF_9A00_0000_FFFF;
    private const ulong GdtDataDescriptor = 0x00CF_9200_0000_FFFF;
    private const ulong GdtCode64Descriptor = 0x00AF_9A00_0000_FFFF;

    static BareMetalBootImageBuilder()
    {
        // Invariantes del arranque en modo largo: si las tablas no están
        // alineadas a 4 KiB, si invaden el payload, si la pila no está alineada
        // a 16 bytes o si payload/pila salen de la primera página de identidad
        // de 2 MiB, el salto a 64 bits acabaría en #PF o #GP.
        if (Pml4Address % PageTableAlignment != 0
            || PdptAddress % PageTableAlignment != 0
            || PdAddress % PageTableAlignment != 0)
        {
            throw new InvalidOperationException(
                "Las tablas de paginación del MBR deben estar alineadas a 4 KiB.");
        }

        if (PdAddress + PageTableSize > PayloadAddress)
        {
            throw new InvalidOperationException(
                "Las tablas de paginación no pueden solaparse con el payload.");
        }

        if (StackTop % 16 != 0)
        {
            throw new InvalidOperationException(
                "La pila de modo largo debe quedar alineada a 16 bytes.");
        }

        if (PayloadLimit > IdentitySpan || StackTop >= IdentitySpan)
        {
            throw new InvalidOperationException(
                "El payload y la pila deben caber en la página de identidad de 2 MiB.");
        }
    }

    /// <summary>
    /// Empaqueta la imagen plana en una <c>.img</c> arrancable por QEMU.
    /// </summary>
    /// <param name="kernel">Bytes exactos de la imagen <c>.x64</c>.</param>
    /// <returns>La imagen de arranque con su layout, o el motivo del rechazo.</returns>
    public static BootImageResult Build(ReadOnlySpan<byte> kernel)
    {
        if (kernel.IsEmpty)
        {
            return BootImageResult.Failed(
                "La imagen .x64 está vacía: no hay código que cargar en el núcleo de arranque.");
        }

        var stubSize = Align(BuildLongModeStub(0, 0).Length, 16);
        var kernelAddress = PayloadAddress + stubSize;
        var payloadSize = stubSize + kernel.Length;
        if (payloadSize > PayloadLimit - PayloadAddress)
        {
            return BootImageResult.Failed(
                $"La imagen .x64 ocupa {kernel.Length} byte(s) y no cabe entre 0x{PayloadAddress:X} y "
                + $"0x{PayloadLimit:X} (máximo {PayloadLimit - PayloadAddress} byte(s) con el stub y la pila).");
        }

        var stub = BuildLongModeStub(kernelAddress, StackTop);
        var chunkCount = Align(payloadSize, ChunkBytes) / ChunkBytes;
        var bootSector = BuildBootSector(chunkCount);
        var image = new byte[BootSectorSize + (chunkCount * ChunkBytes)];

        bootSector.CopyTo(image, 0);
        stub.CopyTo(image, BootSectorSize);
        kernel.CopyTo(image.AsSpan(BootSectorSize + stubSize));

        var layout = new BootImageLayout(
            BootSectorSize,
            stubSize,
            kernelAddress,
            BootSectorSize + stubSize,
            kernel.Length,
            chunkCount);

        return new BootImageResult(image, layout, null);
    }

    /// <summary>
    /// Stub de 64 bits: ajusta la pila, llama al offset 0 de la imagen y
    /// detiene la CPU si la rutina retorna con <c>RET</c>.
    /// </summary>
    private static byte[] BuildLongModeStub(int kernelAddress, int stackTop)
    {
        var code = new List<byte>(32);

        // CLI: el cargador entrega el control con interrupciones deshabilitadas.
        code.Add(0xFA);

        // MOV RSP, imm64: pila propia alineada a 16 bytes.
        code.Add(0x48);
        code.Add(0xBC);
        AddQword(code, (ulong)stackTop);

        // XOR EBP, EBP: marco base limpio (no se lee ningún valor previo).
        code.Add(0x31);
        code.Add(0xED);

        // MOV RAX, imm64: dirección del offset 0 de la imagen .x64 embebida.
        code.Add(0x48);
        code.Add(0xB8);
        AddQword(code, (ulong)kernelAddress);

        // CALL RAX: entra como una llamada normal, con la dirección de retorno
        // en la pila; si la rutina termina con RET, cae en el HLT de abajo.
        code.Add(0xFF);
        code.Add(0xD0);

        // CLI; HLT; JMP $-2: parada limpia del núcleo bare metal.
        code.Add(0xFA);
        code.Add(0xF4);
        code.Add(0xEB);
        code.Add(0xFD);

        while (code.Count % 16 != 0)
        {
            code.Add(0x90); // NOP de relleno hasta el siguiente múltiplo de 16.
        }

        return [.. code];
    }

    private static byte[] BuildBootSector(int chunkCount)
    {
        var writer = new BootSectorWriter();

        // ───────────────────────── Modo real (16 bits) ─────────────────────────
        // La BIOS puede ceder el control como 0x0000:0x7C00 o como
        // 0x07C0:0x0000. Se normaliza CS con un salto lejano a 0x0000:entry
        // para que todas las direcciones absolutas del cargador (calculadas con
        // origen 0x7C00) sean correctas, y se guarda DL antes de cualquier INT
        // porque el BIOS lo usa para la unidad de arranque.
        writer.Emit(0xFA);                                      // CLI
        writer.Emit(0x31, 0xC0);                                // XOR AX, AX
        writer.Emit(0x8E, 0xD8);                                // MOV DS, AX  (DS = 0)
        writer.Emit(0x8E, 0xC0);                                // MOV ES, AX
        writer.Emit(0x8E, 0xD0);                                // MOV SS, AX
        writer.Emit(0xBC, 0x00, 0x7C);                          // MOV SP, 0x7C00
        writer.Emit(0xFC);                                      // CLD (cadena ascendente)
        writer.Emit(0x88, 0x16);                                // MOV [bootDrive], DL
        writer.EmitAddress16("bootDrive");
        writer.Emit(0xEA);                                      // JMP FAR 0x0000:entry
        writer.EmitAddress16("entry");
        writer.Emit(0x00, 0x00);

        writer.Mark("entry");
        writer.Emit(0xFB);                                      // STI (la BIOS necesita interrupciones)

        // A20: primero por BIOS (int 15h AX=0x2401), luego el puerto 0x92 como
        // respaldo en máquinas donde la llamada BIOS no está implementada.
        writer.Emit(0xB8, 0x01, 0x24);                          // MOV AX, 0x2401
        writer.Emit(0xCD, 0x15);                                // INT 0x15
        writer.Emit(0xE4, 0x92);                                // IN AL, 0x92
        writer.Emit(0x0C, 0x02);                                // OR AL, 0x02   (A20 = 1)
        writer.Emit(0x24, 0xFE);                                // AND AL, 0xFE  (no reiniciar)
        writer.Emit(0xE6, 0x92);                                // OUT 0x92, AL

        // Disk Address Packet en DS:SI (16 bytes) para int 13h AH=0x42.
        // Se leen 32 sectores (16 KiB) por llamada en lugar de 64: así ninguna
        // transferencia cruza el límite de 63 sectores por pista que algunos
        // BIOS imponen a AH=0x02 y tampoco se cruza un límite de 64 KiB de DMA.
        writer.Emit(0xBE);                                      // MOV SI, dap
        writer.EmitAddress16("dap");
        writer.Emit(0xC6, 0x04, 0x10);                          // MOV BYTE [SI], 0x10   (tamaño del DAP)
        writer.Emit(0xC6, 0x44, 0x01, 0x00);                    // MOV BYTE [SI+1], 0    (reservado)
        writer.Emit(0xC7, 0x44, 0x02, ReadSectors, 0x00);       // MOV WORD [SI+2], 32   (sectores por lectura)
        writer.Emit(0xC7, 0x44, 0x04, 0x00, 0x00);              // MOV WORD [SI+4], 0    (offset del buffer)
        writer.Emit(0xC7, 0x44, 0x08, 0x01, 0x00);              // MOV WORD [SI+8], 1    (LBA bajo = 1)
        writer.Emit(0xC7, 0x44, 0x0A, 0x00, 0x00);              // MOV WORD [SI+10], 0
        writer.Emit(0xC7, 0x44, 0x0C, 0x00, 0x00);              // MOV WORD [SI+12], 0   (LBA alto)
        writer.Emit(0xC7, 0x44, 0x0E, 0x00, 0x00);              // MOV WORD [SI+14], 0

        // Bucle de carga: cada iteración lee 16 KiB en ES:0x0000, avanza el
        // segmento 0x400 párrafos y el LBA 32 sectores. Un fallo del BIOS se
        // reintenta hasta tres veces tras reiniciar el disco (AH=0x00).
        writer.Mark("loadLoop");
        writer.Emit(0xA1);                                      // MOV AX, [loadSegment]
        writer.EmitAddress16("loadSegment");
        writer.Emit(0x8E, 0xC0);                                // MOV ES, AX
        writer.Emit(0xBE);                                      // MOV SI, dap
        writer.EmitAddress16("dap");
        writer.Emit(0x89, 0x44, 0x06);                          // MOV [SI+6], AX  (segmento del buffer)
        writer.Emit(0xC6, 0x06);                                // MOV BYTE [attempts], 3
        writer.EmitAddress16("attempts");
        writer.Emit(ReadAttempts);

        writer.Mark("readRetry");
        writer.Emit(0x8A, 0x16);                                // MOV DL, [bootDrive]
        writer.EmitAddress16("bootDrive");
        writer.Emit(0xB4, 0x42);                                // MOV AH, 0x42   (lectura extendida LBA)
        writer.Emit(0xCD, 0x13);                                // INT 0x13
        writer.Emit(0x73);                                      // JNC readOk
        writer.EmitRelative8("readOk");
        writer.Emit(0x30, 0xE4);                                // XOR AH, AH     (reinicio del disco)
        writer.Emit(0xCD, 0x13);                                // INT 0x13
        writer.Emit(0xFE, 0x0E);                                // DEC BYTE [attempts]
        writer.EmitAddress16("attempts");
        writer.Emit(0x75);                                      // JNZ readRetry
        writer.EmitRelative8("readRetry");
        writer.Emit(0xEB);                                      // JMP diskError
        writer.EmitRelative8("diskError");

        writer.Mark("readOk");
        writer.Emit(0x81, 0x06);                                // ADD WORD [loadSegment], 0x0400
        writer.EmitAddress16("loadSegment");
        writer.Emit((byte)(ReadParagraphs & 0xFF), (byte)(ReadParagraphs >> 8));
        writer.Emit(0x81, 0x06);                                // ADD WORD [dap+8], 32
        writer.EmitAddress16("dap", 8);
        writer.Emit(ReadSectors, 0x00);
        writer.Emit(0x83, 0x16);                                // ADC WORD [dap+10], 0
        writer.EmitAddress16("dap", 10);
        writer.Emit(0x00);
        writer.Emit(0xFF, 0x0E);                                // DEC WORD [remainingReads]
        writer.EmitAddress16("remainingReads");
        writer.Emit(0x75);                                      // JNZ loadLoop
        writer.EmitRelative8("loadLoop");
        writer.Emit(0xEB);                                      // JMP loadDone (no caer en el error)
        writer.EmitRelative8("loadDone");

        // Error de disco: muestra 'E' por la consola de la BIOS y detiene la CPU.
        writer.Mark("diskError");
        writer.Emit(0xB0, 0x45);                                // MOV AL, 'E'
        writer.Emit(0xB4, 0x0E);                                // MOV AH, 0x0E  (teletype)
        writer.Emit(0xCD, 0x10);                                // INT 0x10
        writer.Mark("halt");
        writer.Emit(0xFA);                                      // CLI
        writer.Emit(0xF4);                                      // HLT
        writer.Emit(0xEB, 0xFD);                                // JMP halt

        writer.Mark("loadDone");

        // ─────────────────── Tablas de paginación de identidad ───────────────────
        // PML4 (0x1000), PDPT (0x2000) y PD (0x3000) están alineadas a 4 KiB y se
        // limpian con REP STOSD (3 tablas * 4096 bytes / 4 = 0xC00 dwords) antes
        // de escribir ninguna entrada, para no dejar bits reservados activos.
        writer.Emit(0x31, 0xC0);                                // XOR AX, AX
        writer.Emit(0x8E, 0xD8);                                // MOV DS, AX
        writer.Emit(0x8E, 0xC0);                                // MOV ES, AX
        writer.Emit(0x8E, 0xD0);                                // MOV SS, AX
        writer.Emit(0xB8);                                      // MOV AX, segmento de PML4
        EmitWordValue(writer, (ushort)(Pml4Address >> 4));
        writer.Emit(0x8E, 0xC0);                                // MOV ES, AX
        writer.Emit(0x31, 0xFF);                                // XOR DI, DI
        writer.Emit(0x66, 0x31, 0xC0);                          // XOR EAX, EAX
        writer.Emit(0x66, 0xB9);                                // MOV ECX, 0xC00 (3072 dwords)
        EmitDword(writer, (uint)(3 * PageTableSize / 4));
        writer.Emit(0x66, 0xF3, 0xAB);                          // REP STOSD

        // PML4[0] -> PDPT | 0x03 (presente, escribible).
        writer.Emit(0x31, 0xFF);                                // XOR DI, DI
        writer.Emit(0x66, 0xB8);                                // MOV EAX, PdptAddress | 0x03
        EmitDword(writer, PdptAddress | 0x03u);
        writer.Emit(0x66, 0xAB);                                // STOSD

        // PDPT[0] -> PD | 0x03 (presente, escribible).
        writer.Emit(0xB8);                                      // MOV AX, segmento de PDPT
        EmitWordValue(writer, (ushort)(PdptAddress >> 4));
        writer.Emit(0x8E, 0xC0);                                // MOV ES, AX
        writer.Emit(0x31, 0xFF);                                // XOR DI, DI
        writer.Emit(0x66, 0xB8);                                // MOV EAX, PdAddress | 0x03
        EmitDword(writer, PdAddress | 0x03u);
        writer.Emit(0x66, 0xAB);                                // STOSD

        // PD: 512 entradas de 2 MiB -> 1 GiB de identidad (presente, escribible, PS=1).
        // Cada entrada ocupa 8 bytes (dword bajo = dirección + 0x83, dword alto = 0),
        // por eso se emiten dos STOSD por iteración intercambiando EAX con EDX.
        writer.Emit(0xB8);                                      // MOV AX, segmento de PD
        EmitWordValue(writer, (ushort)(PdAddress >> 4));
        writer.Emit(0x8E, 0xC0);                                // MOV ES, AX
        writer.Emit(0x31, 0xFF);                                // XOR DI, DI
        writer.Emit(0x66, 0x31, 0xD2);                          // XOR EDX, EDX  (dword alto)
        writer.Emit(0x66, 0xB8);                                // MOV EAX, 0x83
        EmitDword(writer, PagePresent | PageWritable | PageSize);
        writer.Emit(0x66, 0xBB);                                // MOV EBX, 2 MiB
        EmitDword(writer, PageSize2MiB);
        writer.Emit(0x66, 0xB9);                                // MOV ECX, 512
        EmitDword(writer, PageDirectoryEntries);
        writer.Mark("pdLoop");
        writer.Emit(0x66, 0xAB);                                // STOSD: dword bajo (dirección + flags)
        writer.Emit(0x66, 0x92);                                // XCHG EAX, EDX
        writer.Emit(0x66, 0xAB);                                // STOSD: dword alto = 0
        writer.Emit(0x66, 0x92);                                // XCHG EAX, EDX
        writer.Emit(0x66, 0x01, 0xD8);                          // ADD EAX, EBX
        writer.Emit(0xE2);                                      // LOOP pdLoop
        writer.EmitRelative8("pdLoop");

        // Vuelta a los segmentos planos de 16 bits antes de cargar la GDT.
        writer.Emit(0x31, 0xC0);                                // XOR AX, AX
        writer.Emit(0x8E, 0xD8);                                // MOV DS, AX
        writer.Emit(0x8E, 0xC0);                                // MOV ES, AX
        writer.Emit(0x8E, 0xD0);                                // MOV SS, AX

        // GDT en 0x7C00 + gdt (base lineal en gdtr) con límite 31: nulo, código
        // de 32 bits (0x08), datos planos (0x10) y código de 64 bits (0x18).
        writer.Emit(0x0F, 0x01, 0x16);                          // LGDT [gdtr]
        writer.EmitAddress16("gdtr");

        // CR0.PE = 1 y salto lejano al trampolín de 32 bits (selector 0x08).
        writer.Emit(0x0F, 0x20, 0xC0);                          // MOV EAX, CR0
        writer.Emit(0x66, 0x0D);                                // OR EAX, 0x01 (PE)
        EmitDword(writer, Cr0ProtectedModeEnable);
        writer.Emit(0x0F, 0x22, 0xC0);                          // MOV CR0, EAX
        writer.Emit(0x66, 0xEA);                                // JMP FAR 0x08:pm32
        writer.EmitAddress32("pm32");
        EmitWordValue(writer, SelectorCode32);

        // ─────────────────── Modo protegido de 32 bits ───────────────────
        writer.Mark("pm32");
        writer.Emit(0x66, 0xB8);                                // MOV AX, 0x10 (operando 16 en modo 32)
        EmitWordValue(writer, SelectorData);
        writer.Emit(0x8E, 0xD8);                                // MOV DS, AX
        writer.Emit(0x8E, 0xC0);                                // MOV ES, AX
        writer.Emit(0x8E, 0xD0);                                // MOV SS, AX
        writer.Emit(0x8E, 0xE0);                                // MOV FS, AX
        writer.Emit(0x8E, 0xE8);                                // MOV GS, AX
        writer.Emit(0xBC);                                      // MOV ESP, 0x00007C00
        EmitDword(writer, 0x0000_7C00);

        // Orden exigido por Intel: CR4.PAE = 1, CR3 = PML4, EFER.LME = 1 y,
        // por último, CR0.PG = 1 con un salto lejano inmediato a 64 bits.
        writer.Emit(0x0F, 0x20, 0xE0);                          // MOV EAX, CR4
        writer.Emit(0x0D);                                      // OR EAX, 0x20 (PAE = 1)
        EmitDword(writer, Cr4PhysicalAddressExtension);
        writer.Emit(0x0F, 0x22, 0xE0);                          // MOV CR4, EAX
        writer.Emit(0xB8);                                      // MOV EAX, Pml4Address
        EmitDword(writer, Pml4Address);
        writer.Emit(0x0F, 0x22, 0xD8);                          // MOV CR3, EAX

        writer.Emit(0xB9);                                      // MOV ECX, IA32_EFER
        EmitDword(writer, MsrIa32Efer);
        writer.Emit(0x0F, 0x32);                                // RDMSR
        writer.Emit(0x0D);                                      // OR EAX, 0x100 (LME = 1)
        EmitDword(writer, EferLongModeEnable);
        writer.Emit(0x0F, 0x30);                                // WRMSR

        writer.Emit(0x0F, 0x20, 0xC0);                          // MOV EAX, CR0
        writer.Emit(0x0D);                                      // OR EAX, 0x80000000 (PG = 1)
        EmitDword(writer, Cr0PagingEnable);
        writer.Emit(0x0F, 0x22, 0xC0);                          // MOV CR0, EAX
        writer.Emit(0xEA);                                      // JMP FAR 0x18:PayloadAddress
        EmitDword(writer, PayloadAddress);
        EmitWordValue(writer, SelectorCode64);

        // ─────────────────────────── Datos del sector ───────────────────────────
        writer.Mark("bootDrive");
        writer.Emit(0x00);                                      // Unidad de arranque (DL).

        writer.Mark("loadSegment");
        writer.EmitWord(KernelLoadSegment);                     // Segmento del siguiente bloque.

        writer.Mark("remainingReads");
        writer.EmitWord(chunkCount * ReadsPerChunk);            // Lecturas de 16 KiB pendientes.

        writer.Mark("attempts");
        writer.Emit(ReadAttempts);                              // Intentos por lectura.

        writer.Mark("gdtr");
        writer.EmitWord(GdtSize - 1);                           // Límite de la GDT.
        writer.EmitAddress32("gdt");                            // Base lineal de la GDT.

        writer.Mark("gdt");
        AddGdtEntry(writer, GdtNullDescriptor);                 // 0x00: nulo
        AddGdtEntry(writer, GdtCode32Descriptor);               // 0x08: código 32 bits (D=1)
        AddGdtEntry(writer, GdtDataDescriptor);                 // 0x10: datos 32/64 bits
        AddGdtEntry(writer, GdtCode64Descriptor);               // 0x18: código 64 bits (L=1)

        writer.Mark("dap");
        writer.Emit(new byte[16]);                              // Disk Address Packet.

        return writer.Finish();
    }

    private static void AddGdtEntry(BootSectorWriter writer, ulong descriptor) =>
        writer.EmitQword(descriptor);

    private static void EmitWordValue(BootSectorWriter writer, ushort value) =>
        writer.Emit((byte)value, (byte)(value >> 8));

    private static void EmitDword(BootSectorWriter writer, uint value) =>
        writer.Emit(
            (byte)value,
            (byte)(value >> 8),
            (byte)(value >> 16),
            (byte)(value >> 24));

    private static void AddQword(List<byte> bytes, ulong value)
    {
        for (var index = 0; index < 8; index++)
        {
            bytes.Add((byte)(value >> (index * 8)));
        }
    }

    private static int Align(int value, int alignment) =>
        (value + alignment - 1) & ~(alignment - 1);

    /// <summary>
    /// Emisor del sector de arranque: mantiene un cursor, resuelve etiquetas
    /// internas (direcciones absolutas del MBR y saltos relativos) y valida
    /// que el código y los datos caben antes de la firma <c>0x55 0xAA</c>.
    /// </summary>
    private sealed class BootSectorWriter
    {
        private const int Origin = 0x7C00;

        private readonly byte[] buffer = new byte[BootSectorSize];
        private readonly Dictionary<string, int> labels = new(StringComparer.Ordinal);
        private readonly List<Patch> patches = [];
        private int cursor;

        public void Emit(params byte[] bytes)
        {
            foreach (var value in bytes)
            {
                if (cursor >= SectorSignatureOffset)
                {
                    throw new InvalidOperationException(
                        "El cargador de arranque excede los 510 bytes del MBR.");
                }

                buffer[cursor++] = value;
            }
        }

        public void EmitWord(int value)
        {
            Emit((byte)value, (byte)(value >> 8));
        }

        public void EmitQword(ulong value)
        {
            for (var index = 0; index < 8; index++)
            {
                Emit((byte)(value >> (index * 8)));
            }
        }

        public void Mark(string label) => labels[label] = cursor;

        public void EmitAddress16(string label, int addend = 0)
        {
            patches.Add(new Patch(cursor, label, addend, PatchKind.Absolute16));
            Emit(0x00, 0x00);
        }

        public void EmitAddress32(string label)
        {
            patches.Add(new Patch(cursor, label, 0, PatchKind.Absolute32));
            Emit(0x00, 0x00, 0x00, 0x00);
        }

        public void EmitRelative8(string label)
        {
            patches.Add(new Patch(cursor, label, 0, PatchKind.Relative8));
            Emit(0x00);
        }

        public byte[] Finish()
        {
            foreach (var patch in patches)
            {
                if (!labels.TryGetValue(patch.Label, out var target))
                {
                    throw new InvalidOperationException(
                        $"La etiqueta '{patch.Label}' del cargador no existe.");
                }

                switch (patch.Kind)
                {
                    case PatchKind.Absolute16:
                        var absolute = (ushort)(Origin + target + patch.Addend);
                        buffer[patch.Offset] = (byte)absolute;
                        buffer[patch.Offset + 1] = (byte)(absolute >> 8);
                        break;

                    case PatchKind.Absolute32:
                        var linear = Origin + target;
                        buffer[patch.Offset] = (byte)linear;
                        buffer[patch.Offset + 1] = (byte)(linear >> 8);
                        buffer[patch.Offset + 2] = (byte)(linear >> 16);
                        buffer[patch.Offset + 3] = (byte)(linear >> 24);
                        break;

                    case PatchKind.Relative8:
                        var displacement = Origin + target - (Origin + patch.Offset + 1);
                        if (displacement is < sbyte.MinValue or > sbyte.MaxValue)
                        {
                            throw new InvalidOperationException(
                                $"El salto rel8 a '{patch.Label}' no cabe en un byte.");
                        }

                        buffer[patch.Offset] = (byte)(sbyte)displacement;
                        break;

                    default:
                        throw new InvalidOperationException("Tipo de parche desconocido.");
                }
            }

            buffer[SectorSignatureOffset] = 0x55;
            buffer[SectorSignatureOffset + 1] = 0xAA;
            return buffer;
        }
    }

    private enum PatchKind
    {
        Absolute16,
        Absolute32,
        Relative8,
    }

    private sealed record Patch(int Offset, string Label, int Addend, PatchKind Kind);
}


