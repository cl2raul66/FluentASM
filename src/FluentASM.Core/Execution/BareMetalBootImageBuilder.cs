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
/// carga los sectores siguientes con <c>int 0x13 AH=0x42</c>, habilita A20,
/// monta tablas de paginación de identidad PML4/PDPT/PD con páginas de 2 MiB,
/// activa <c>CR4.PAE</c>, <c>IA32_EFER.LME</c> y <c>CR0.PG</c>, entra en modo
/// largo por un salto lejano y llama al offset <c>0</c> de la imagen con la
/// pila alineada a 16 bytes.
/// </para>
/// <para>
/// El cargador se construye byte a byte en C# (sin NASM) y la imagen se
/// rellena hasta un múltiplo de 32 KiB para que la BIOS pueda leerla en bloques
/// de 64 sectores sin cruzar límites de 64 KiB.
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
    private const int GdtSelectors = 4;
    private const int GdtSize = GdtSelectors * 8;
    private const int SectorSignatureOffset = 510;

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
        writer.Emit(0xBE);                                      // MOV SI, dap
        writer.EmitAddress16("dap");
        writer.Emit(0xC6, 0x04, 0x10);                          // MOV BYTE [SI], 0x10   (tamaño del DAP)
        writer.Emit(0xC6, 0x44, 0x01, 0x00);                    // MOV BYTE [SI+1], 0
        writer.Emit(0xC7, 0x44, 0x02, ChunkSectors, 0x00);      // MOV WORD [SI+2], 64   (sectores por lectura)
        writer.Emit(0xC7, 0x44, 0x04, 0x00, 0x00);              // MOV WORD [SI+4], 0    (offset del buffer)
        writer.Emit(0xC7, 0x44, 0x08, 0x01, 0x00);              // MOV WORD [SI+8], 1    (LBA bajo = 1)
        writer.Emit(0xC7, 0x44, 0x0A, 0x00, 0x00);              // MOV WORD [SI+10], 0
        writer.Emit(0xC7, 0x44, 0x0C, 0x00, 0x00);              // MOV WORD [SI+12], 0   (LBA alto)
        writer.Emit(0xC7, 0x44, 0x0E, 0x00, 0x00);              // MOV WORD [SI+14], 0

        // Bucle de carga: cada iteración lee 64 sectores (32 KiB) en
        // ES:0x0000, avanza el segmento 0x800 párrafos y el LBA 64 sectores.
        writer.Mark("loadLoop");
        writer.Emit(0xA1);                                      // MOV AX, [loadSegment]
        writer.EmitAddress16("loadSegment");
        writer.Emit(0x8E, 0xC0);                                // MOV ES, AX
        writer.Emit(0xBE);                                      // MOV SI, dap
        writer.EmitAddress16("dap");
        writer.Emit(0x89, 0x44, 0x06);                          // MOV [SI+6], AX  (segmento del buffer)
        writer.Emit(0x8A, 0x16);                                // MOV DL, [bootDrive]
        writer.EmitAddress16("bootDrive");
        writer.Emit(0xB4, 0x42);                                // MOV AH, 0x42   (lectura extendida)
        writer.Emit(0xCD, 0x13);                                // INT 0x13
        writer.Emit(0x72);                                      // JC diskError
        writer.EmitRelative8("diskError");
        writer.Emit(0x81, 0x06);                                // ADD WORD [loadSegment], 0x0800
        writer.EmitAddress16("loadSegment");
        writer.Emit(0x00, 0x08);
        writer.Emit(0x83, 0x06);                                // ADD WORD [dap+8], 64
        writer.EmitAddress16("dap", 8);
        writer.Emit(0x40);
        writer.Emit(0xFF, 0x0E);                                // DEC WORD [remainingChunks]
        writer.EmitAddress16("remainingChunks");
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
        // Se ponen a cero PML4 (0x1000), PDPT (0x2000) y PD (0x3000) con REP STOSD.
        writer.Emit(0x31, 0xC0);                                // XOR AX, AX
        writer.Emit(0x8E, 0xD8);                                // MOV DS, AX
        writer.Emit(0x8E, 0xC0);                                // MOV ES, AX
        writer.Emit(0x8E, 0xD0);                                // MOV SS, AX
        writer.Emit(0xB8, 0x00, 0x01);                          // MOV AX, 0x0100  (ES = 0x0100 -> lineal 0x1000)
        writer.Emit(0x8E, 0xC0);                                // MOV ES, AX
        writer.Emit(0x31, 0xFF);                                // XOR DI, DI
        writer.Emit(0x66, 0x31, 0xC0);                          // XOR EAX, EAX
        writer.Emit(0x66, 0xB9, 0x00, 0x0C, 0x00, 0x00);        // MOV ECX, 0xC00  (3072 dwords)
        writer.Emit(0x66, 0xF3, 0xAB);                          // REP STOSD

        // PML4[0] -> PDPT (presente, escribible).
        writer.Emit(0x31, 0xFF);                                // XOR DI, DI
        writer.Emit(0x66, 0xB8, 0x03, 0x20, 0x00, 0x00);        // MOV EAX, 0x2003
        writer.Emit(0x66, 0xAB);                                // STOSD

        // PDPT[0] -> PD (presente, escribible).
        writer.Emit(0xB8, 0x00, 0x02);                          // MOV AX, 0x0200  (ES = 0x0200 -> lineal 0x2000)
        writer.Emit(0x8E, 0xC0);                                // MOV ES, AX
        writer.Emit(0x31, 0xFF);                                // XOR DI, DI
        writer.Emit(0x66, 0xB8, 0x03, 0x30, 0x00, 0x00);        // MOV EAX, 0x3003
        writer.Emit(0x66, 0xAB);                                // STOSD

        // PD: 512 entradas de 2 MiB -> 1 GiB de identidad (presente, escribible, PS=1).
        // Cada entrada ocupa 8 bytes (dword bajo = dirección + flags, dword alto = 0),
        // por eso se emiten dos STOSD por iteración intercambiando EAX con EDX.
        writer.Emit(0xB8, 0x00, 0x03);                          // MOV AX, 0x0300  (ES = 0x0300 -> lineal 0x3000)
        writer.Emit(0x8E, 0xC0);                                // MOV ES, AX
        writer.Emit(0x31, 0xFF);                                // XOR DI, DI
        writer.Emit(0x66, 0x31, 0xD2);                          // XOR EDX, EDX  (dword alto de la entrada)
        writer.Emit(0x66, 0xB8, 0x83, 0x00, 0x00, 0x00);        // MOV EAX, 0x00000083
        writer.Emit(0x66, 0xBB, 0x00, 0x00, 0x20, 0x00);        // MOV EBX, 0x00200000
        writer.Emit(0x66, 0xB9, 0x00, 0x02, 0x00, 0x00);        // MOV ECX, 512
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

        // GDT con descriptores nulo, código 32, datos y código 64.
        writer.Emit(0x0F, 0x01, 0x16);                          // LGDT [gdtr]
        writer.EmitAddress16("gdtr");

        // CR0.PE = 1 y salto lejano al trampolín de 32 bits (selector 0x08).
        writer.Emit(0x0F, 0x20, 0xC0);                          // MOV EAX, CR0
        writer.Emit(0x66, 0x83, 0xC8, 0x01);                    // OR EAX, 1
        writer.Emit(0x0F, 0x22, 0xC0);                          // MOV CR0, EAX
        writer.Emit(0x66, 0xEA);                                // JMP FAR 0x08:pm32
        writer.EmitAddress32("pm32");
        writer.Emit(0x08, 0x00);

        // ─────────────────── Modo protegido de 32 bits ───────────────────
        writer.Mark("pm32");
        writer.Emit(0x66, 0xB8, 0x10, 0x00);                    // MOV AX, 0x10 (operando 16 en modo 32)
        writer.Emit(0x8E, 0xD8);                                // MOV DS, AX
        writer.Emit(0x8E, 0xC0);                                // MOV ES, AX
        writer.Emit(0x8E, 0xD0);                                // MOV SS, AX
        writer.Emit(0x8E, 0xE0);                                // MOV FS, AX
        writer.Emit(0x8E, 0xE8);                                // MOV GS, AX
        writer.Emit(0xBC, 0x00, 0x7C, 0x00, 0x00);              // MOV ESP, 0x00007C00

        writer.Emit(0x0F, 0x20, 0xE0);                          // MOV EAX, CR4
        writer.Emit(0x0D, 0x20, 0x00, 0x00, 0x00);              // OR EAX, 0x20   (PAE = 1)
        writer.Emit(0x0F, 0x22, 0xE0);                          // MOV CR4, EAX
        writer.Emit(0xB8, 0x00, 0x10, 0x00, 0x00);              // MOV EAX, 0x1000 (PML4)
        writer.Emit(0x0F, 0x22, 0xD8);                          // MOV CR3, EAX

        writer.Emit(0xB9, 0x80, 0x00, 0x00, 0xC0);              // MOV ECX, 0xC0000080 (IA32_EFER)
        writer.Emit(0x0F, 0x32);                                // RDMSR
        writer.Emit(0x0D, 0x00, 0x01, 0x00, 0x00);              // OR EAX, 0x100  (LME = 1)
        writer.Emit(0x0F, 0x30);                                // WRMSR

        writer.Emit(0x0F, 0x20, 0xC0);                          // MOV EAX, CR0
        writer.Emit(0x0D, 0x00, 0x00, 0x00, 0x80);              // OR EAX, 0x80000000 (PG = 1)
        writer.Emit(0x0F, 0x22, 0xC0);                          // MOV CR0, EAX
        writer.Emit(0xEA);                                      // JMP FAR 0x18:0x00010000
        writer.Emit(0x00, 0x00, 0x01, 0x00);
        writer.Emit(0x18, 0x00);

        // ─────────────────────────── Datos del sector ───────────────────────────
        writer.Mark("bootDrive");
        writer.Emit(0x00);                                      // Unidad de arranque (DL).

        writer.Mark("loadSegment");
        writer.EmitWord(KernelLoadSegment);                     // Segmento del siguiente bloque.

        writer.Mark("remainingChunks");
        writer.EmitWord(chunkCount);                            // Bloques de 32 KiB pendientes.

        writer.Mark("gdtr");
        writer.EmitWord(GdtSize - 1);                           // Límite de la GDT.
        writer.EmitAddress32("gdt");                            // Base lineal de la GDT.

        writer.Mark("gdt");
        AddGdtEntry(writer, 0x0000_0000_0000_0000);             // 0x00: nulo
        AddGdtEntry(writer, 0x00CF_9A00_0000_FFFF);             // 0x08: código 32 bits (D=1)
        AddGdtEntry(writer, 0x00CF_9200_0000_FFFF);             // 0x10: datos 32/64 bits
        AddGdtEntry(writer, 0x00AF_9A00_0000_FFFF);             // 0x18: código 64 bits (L=1)

        writer.Mark("dap");
        writer.Emit(new byte[16]);                              // Disk Address Packet.

        return writer.Finish();
    }

    private static void AddGdtEntry(BootSectorWriter writer, ulong descriptor) =>
        writer.EmitQword(descriptor);

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
