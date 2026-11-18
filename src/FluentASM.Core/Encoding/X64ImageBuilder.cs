using FluentASM.Core.Diagnostics;
using FluentASM.Core.Semantics;
using FluentASM.Core.Syntax.Ast;

namespace FluentASM.Core.Encoding;

/// <summary>
/// Resultado de construir una imagen plana <c>.x64</c>.
/// </summary>
public sealed class ImageBuildResult
{
    internal ImageBuildResult(byte[] image, ImageMap map, DiagnosticBag diagnostics)
    {
        Image = image;
        Map = map;
        Diagnostics = diagnostics;
    }

    /// <summary>Bytes exactos de la imagen plana.</summary>
    public byte[] Image { get; }

    /// <summary>Mapa de secciones, símbolos e instrucciones.</summary>
    public ImageMap Map { get; }

    /// <summary>Diagnósticos acumulados durante la construcción.</summary>
    public DiagnosticBag Diagnostics { get; }

    /// <summary>Indica si la imagen se construyó sin errores.</summary>
    public bool Success => !Diagnostics.HasErrors;

    internal static ImageBuildResult Failed(DiagnosticBag diagnostics) =>
        new([], ImageMap.Empty, diagnostics);
}

/// <summary>
/// Emisor de la imagen plana <c>.x64</c> de Fase 1: ordena <c>.execute</c> y
/// <c>.data</c>, relaja saltos, resuelve referencias RIP-relativas y produce
/// el mapa legible de la compilación.
/// </summary>
public static class X64ImageBuilder
{
    private const int DataAlignment = 16;
    private const int PrologueSize = 5;

    /// <summary>Construye la imagen plana a partir del AST y del análisis semántico.</summary>
    /// <param name="root">Raíz sintáctica del programa.</param>
    /// <param name="semantics">Resultado de M2 (tabla de símbolos y diagnósticos).</param>
    /// <param name="entryPoint">Rutina de entrada; por convención <c>main</c>.</param>
    /// <param name="diagnostics">Bolsa compartida opcional.</param>
    public static ImageBuildResult Build(
        CompilationUnitSyntax root,
        SemanticResult semantics,
        string entryPoint,
        DiagnosticBag? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(semantics);

        var bag = diagnostics ?? new DiagnosticBag();
        if (!semantics.Success)
        {
            return ImageBuildResult.Failed(bag);
        }

        var entry = string.IsNullOrWhiteSpace(entryPoint) ? "main" : entryPoint;
        var routines = (root.Execute?.Routines ?? [])
            .Select(EncodingRoutine.Create)
            .ToList();

        if (routines.Count == 0)
        {
            bag.Error(
                DiagnosticIds.UnknownSymbol,
                "El programa no declara ninguna rutina en .execute(...).");
            return ImageBuildResult.Failed(bag);
        }

        var entryIndex = routines.FindIndex(routine => routine.Name == entry);
        if (entryIndex < 0)
        {
            bag.Error(
                DiagnosticIds.UnknownSymbol,
                $"La rutina de entrada '{entry}' no existe en .execute(...).");
            return ImageBuildResult.Failed(bag);
        }

        var dataDefinitions = CollectData(root, bag);
        if (bag.HasErrors)
        {
            return ImageBuildResult.Failed(bag);
        }

        var layout = Relax(semantics.Symbols, routines, entryIndex, bag);
        if (bag.HasErrors)
        {
            return ImageBuildResult.Failed(bag);
        }

        var data = BuildData(dataDefinitions);
        var dataOffset = data.Entries.Count == 0
            ? layout.ExecuteSize
            : Align(layout.ExecuteSize, DataAlignment);
        var image = Compose(layout, data, dataOffset, entryIndex, bag);
        if (bag.HasErrors)
        {
            return ImageBuildResult.Failed(bag);
        }

        var map = CreateMap(layout, data, dataOffset, entryIndex, entry, image);
        return new ImageBuildResult(image, map, bag);
    }

    private static LayoutSnapshot Relax(
        SymbolTable symbols,
        IReadOnlyList<EncodingRoutine> routines,
        int entryIndex,
        DiagnosticBag diagnostics)
    {
        var near = new HashSet<(int Routine, int Action)>();
        var maximumIterations = Math.Max(4, routines.Count * 8);
        LayoutSnapshot snapshot = default!;

        for (var iteration = 0; ; iteration++)
        {
            var iterationDiagnostics = new DiagnosticBag();
            snapshot = EncodeLayout(symbols, routines, entryIndex, near, iterationDiagnostics);
            var promoted = false;

            foreach (var instruction in snapshot.Instructions)
            {
                if (instruction.Instruction.Branch != BranchForm.Short
                    || !TryResolveLocalDisplacement(snapshot, instruction, out var displacement))
                {
                    continue;
                }

                if (displacement is < sbyte.MinValue or > sbyte.MaxValue)
                {
                    near.Add((instruction.RoutineIndex, instruction.ActionIndex));
                    promoted = true;
                }
            }

            if (!promoted)
            {
                CopyDiagnostics(iterationDiagnostics, diagnostics);
                break;
            }

            if (iteration >= maximumIterations)
            {
                CopyDiagnostics(iterationDiagnostics, diagnostics);
                diagnostics.Error(
                    DiagnosticIds.InvalidVerb,
                    "La relajación de saltos no convergió tras el número máximo de iteraciones.");
                return snapshot;
            }
        }

        foreach (var instruction in snapshot.Instructions)
        {
            if (instruction.Instruction.Branch != BranchForm.Loop
                || !TryResolveLocalDisplacement(snapshot, instruction, out var displacement))
            {
                continue;
            }

            if (displacement is < sbyte.MinValue or > sbyte.MaxValue)
            {
                diagnostics.Error(
                    DiagnosticIds.RelativeOutOfRange,
                    $"Ipr.flowLoop({instruction.Instruction.Fixups[0].Target.Name}) requiere un desplazamiento rel8 "
                    + $"y el layout produce {displacement} byte(s) (rango -128..127).",
                    instruction.Action.Verb.Location,
                    instruction.Action.Span);
            }
        }

        return snapshot;
    }

    private static LayoutSnapshot EncodeLayout(
        SymbolTable symbols,
        IReadOnlyList<EncodingRoutine> routines,
        int entryIndex,
        IReadOnlySet<(int Routine, int Action)> near,
        DiagnosticBag diagnostics)
    {
        var encoder = new X64InstructionEncoder(symbols, diagnostics);
        var cursor = entryIndex == 0 ? 0 : PrologueSize;
        var routineSnapshots = new List<RoutineSnapshot>(routines.Count);
        var instructions = new List<InstructionSnapshot>();

        for (var routineIndex = 0; routineIndex < routines.Count; routineIndex++)
        {
            var routine = routines[routineIndex];
            var routineSnapshot = new RoutineSnapshot(routine, routineIndex, cursor);
            var actionIndex = 0;
            string? currentLabel = null;

            foreach (var statement in routine.Definition.Body)
            {
                switch (statement)
                {
                    case LabelStatementSyntax label:
                        routineSnapshot.AddLabel(label.Label.Text, cursor);
                        currentLabel = label.Label.Text;
                        break;

                    case ActionStatementSyntax action:
                        var encoded = encoder.Encode(
                            action,
                            routine,
                            near.Contains((routineIndex, actionIndex)));

                        foreach (var instruction in encoded)
                        {
                            instructions.Add(new InstructionSnapshot(
                                cursor,
                                routineIndex,
                                routine,
                                currentLabel,
                                action,
                                actionIndex,
                                instruction));
                            cursor += instruction.Bytes.Length;
                        }

                        actionIndex++;
                        break;

                    default:
                        break;
                }
            }

            routineSnapshot.Complete(cursor);
            routineSnapshots.Add(routineSnapshot);
        }

        return new LayoutSnapshot(cursor, routineSnapshots, instructions);
    }

    private static bool TryResolveLocalDisplacement(
        LayoutSnapshot snapshot,
        InstructionSnapshot instruction,
        out int displacement)
    {
        displacement = 0;
        var fixup = instruction.Instruction.Fixups.FirstOrDefault(
            candidate => candidate.Kind == InstructionFixupKind.Relative
                && candidate.Target.Kind == InstructionFixupTargetKind.LocalLabel);
        if (fixup is null)
        {
            return false;
        }

        var labels = snapshot.Routines[instruction.RoutineIndex].Labels;
        if (!labels.TryGetValue(fixup.Target.Name, out var target))
        {
            return false;
        }

        var end = instruction.Offset + instruction.Instruction.Bytes.Length;
        var value = (long)target + fixup.Addend - end;
        if (value is < int.MinValue or > int.MaxValue)
        {
            return false;
        }

        displacement = (int)value;
        return true;
    }

    private static byte[] Compose(
        LayoutSnapshot layout,
        DataLayout data,
        int dataOffset,
        int entryIndex,
        DiagnosticBag diagnostics)
    {
        var image = new byte[dataOffset + data.Bytes.Length];
        Array.Fill(image, (byte)0x90, 0, dataOffset);
        if (data.Bytes.Length > 0)
        {
            Array.Copy(data.Bytes, 0, image, dataOffset, data.Bytes.Length);
        }

        foreach (var instruction in layout.Instructions)
        {
            Array.Copy(
                instruction.Instruction.Bytes,
                0,
                image,
                instruction.Offset,
                instruction.Instruction.Bytes.Length);
        }

        if (entryIndex != 0)
        {
            var entryOffset = layout.Routines[entryIndex].Offset;
            image[0] = 0xE9;
            WriteInt32(image, 1, entryOffset - PrologueSize);
        }

        var routineOffsets = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var routine in layout.Routines)
        {
            if (routine.Routine.Name is { } name)
            {
                routineOffsets[name] = routine.Offset;
            }
        }

        var dataOffsets = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in data.Entries)
        {
            dataOffsets[entry.Name] = dataOffset + entry.Offset;
        }

        foreach (var instruction in layout.Instructions)
        {
            var end = instruction.Offset + instruction.Instruction.Bytes.Length;
            foreach (var fixup in instruction.Instruction.Fixups)
            {
                int target;
                switch (fixup.Target.Kind)
                {
                    case InstructionFixupTargetKind.LocalLabel:
                        if (!layout.Routines[instruction.RoutineIndex].Labels.TryGetValue(
                                fixup.Target.Name,
                                out target))
                        {
                            diagnostics.Error(
                                DiagnosticIds.UnknownSymbol,
                                $"La etiqueta '{fixup.Target.Name}' no existe en la rutina '{Describe(instruction.Routine)}'.");
                            continue;
                        }

                        break;

                    case InstructionFixupTargetKind.Routine:
                        if (!routineOffsets.TryGetValue(fixup.Target.Name, out target))
                        {
                            diagnostics.Error(
                                DiagnosticIds.UnknownSymbol,
                                $"La rutina '{fixup.Target.Name}' no existe en la imagen.");
                            continue;
                        }

                        break;

                    case InstructionFixupTargetKind.DataSymbol:
                        if (!dataOffsets.TryGetValue(fixup.Target.Name, out target))
                        {
                            diagnostics.Error(
                                DiagnosticIds.UnknownSymbol,
                                $"El símbolo de datos '{fixup.Target.Name}' no existe en la imagen.");
                            continue;
                        }

                        break;

                    default:
                        continue;
                }

                var value = (long)target + fixup.Addend - end;
                if (!TryWriteValue(image, instruction.Offset + fixup.ByteOffset, fixup.Size, value, diagnostics))
                {
                    continue;
                }
            }
        }

        return image;
    }

    private static bool TryWriteValue(byte[] image, int offset, int size, long value, DiagnosticBag diagnostics)
    {
        if (size == 1)
        {
            if (value is < sbyte.MinValue or > sbyte.MaxValue)
            {
                diagnostics.Error(
                    DiagnosticIds.RelativeOutOfRange,
                    $"La referencia rel8 no cabe en -128..127 (valor {value}).");
                return false;
            }

            image[offset] = (byte)(sbyte)value;
            return true;
        }

        if (value is < int.MinValue or > int.MaxValue)
        {
            diagnostics.Error(
                DiagnosticIds.RelativeOutOfRange,
                $"La referencia rel32 no cabe en 32 bits (valor {value}).");
            return false;
        }

        WriteInt32(image, offset, (int)value);
        return true;
    }

    private static void WriteInt32(byte[] image, int offset, int value)
    {
        var unsigned = unchecked((uint)value);
        image[offset] = (byte)unsigned;
        image[offset + 1] = (byte)(unsigned >> 8);
        image[offset + 2] = (byte)(unsigned >> 16);
        image[offset + 3] = (byte)(unsigned >> 24);
    }

    private static List<EncodingDataEntry> CollectData(CompilationUnitSyntax root, DiagnosticBag diagnostics)
    {
        var entries = new List<EncodingDataEntry>();
        long total = 0;
        foreach (var definition in root.Data?.Definitions ?? [])
        {
            if (definition.Name is not { } name)
            {
                continue;
            }

            if (!MagnitudeInfo.TryCreate(definition.Capacity, out var capacity) || !capacity.IsByte)
            {
                continue;
            }

            total += capacity.Bytes;
            if (total > int.MaxValue)
            {
                diagnostics.Error(
                    DiagnosticIds.DataPayloadOverflow,
                    $"La sección .data excede el tamaño máximo de la imagen plana al añadir '{name.Text}'.");
                continue;
            }

            entries.Add(new EncodingDataEntry(name.Text, capacity, definition));
        }

        return entries;
    }

    private static DataLayout BuildData(IReadOnlyList<EncodingDataEntry> definitions)
    {
        var entries = new List<DataEntry>(definitions.Count);
        var total = (int)definitions.Sum(definition => definition.Capacity.Bytes);
        var bytes = new byte[total];
        var cursor = 0;

        foreach (var definition in definitions)
        {
            var length = (int)definition.Capacity.Bytes;
            var payload = EncodeData(definition.Definition, definition.Capacity);
            Array.Copy(payload, 0, bytes, cursor, payload.Length);
            entries.Add(new DataEntry(definition.Name, cursor, length));
            cursor += length;
        }

        return new DataLayout(bytes, entries);
    }

    private static byte[] EncodeData(DataDefinitionSyntax definition, MagnitudeInfo capacity)
    {
        var size = (int)capacity.Bytes;
        var buffer = new byte[size];
        var values = definition.Values;

        if (values.Count == 0)
        {
            return buffer;
        }

        if (values.Count == 1)
        {
            switch (values[0])
            {
                case DefaultValueSyntax:
                    return buffer;

                case IntegerLiteralSyntax integer:
                    WriteLittleEndian(buffer, 0, size, integer.Value);
                    return buffer;

                case StringLiteralSyntax text:
                    var utf8 = System.Text.Encoding.UTF8.GetBytes(text.Value);
                    Array.Copy(utf8, 0, buffer, 0, Math.Min(utf8.Length, size));
                    return buffer;

                default:
                    return buffer;
            }
        }

        var elementSize = size / values.Count;
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] is IntegerLiteralSyntax integer)
            {
                WriteLittleEndian(buffer, index * elementSize, elementSize, integer.Value);
            }
        }

        return buffer;
    }

    private static void WriteLittleEndian(byte[] buffer, int offset, int size, long value)
    {
        var unsigned = unchecked((ulong)value);
        for (var index = 0; index < size && offset + index < buffer.Length; index++)
        {
            buffer[offset + index] = (byte)(unsigned >> (index * 8));
        }
    }

    private static ImageMap CreateMap(
        LayoutSnapshot layout,
        DataLayout data,
        int dataOffset,
        int entryIndex,
        string entryPoint,
        byte[] image)
    {
        var instructions = new List<ImageInstruction>();
        if (entryIndex != 0)
        {
            instructions.Add(new ImageInstruction(
                0,
                image[..PrologueSize],
                $"jmp near {entryPoint}",
                "<entrada>",
                null));
        }

        foreach (var instruction in layout.Instructions)
        {
            instructions.Add(new ImageInstruction(
                instruction.Offset,
                image.AsSpan(instruction.Offset, instruction.Instruction.Bytes.Length).ToArray(),
                instruction.Instruction.Text,
                Describe(instruction.Routine),
                instruction.Label));
        }

        var sections = new List<ImageSection>
        {
            new(".execute", 0, layout.ExecuteSize, 0x90),
        };
        if (data.Bytes.Length > 0)
        {
            sections.Add(new ImageSection(".data", dataOffset, data.Bytes.Length, 0x00));
        }

        var symbols = new List<ImageSymbol>();
        foreach (var routine in layout.Routines)
        {
            symbols.Add(new ImageSymbol(
                ImageSymbolKind.Routine,
                Describe(routine.Routine),
                ".execute",
                routine.Offset,
                routine.Size));

            foreach (var label in routine.LabelOrder)
            {
                symbols.Add(new ImageSymbol(
                    ImageSymbolKind.Label,
                    label,
                    ".execute",
                    routine.Labels[label],
                    0));
            }
        }

        foreach (var entry in data.Entries)
        {
            symbols.Add(new ImageSymbol(
                ImageSymbolKind.Data,
                entry.Name,
                ".data",
                dataOffset + entry.Offset,
                entry.Size));
        }

        return new ImageMap(
            entryPoint,
            layout.Routines[entryIndex].Offset,
            sections,
            symbols,
            instructions);
    }

    private static string Describe(EncodingRoutine routine) =>
        routine.Name ?? "<implícita>";

    private static int Align(int value, int alignment) =>
        (value + alignment - 1) & ~(alignment - 1);

    private static void CopyDiagnostics(DiagnosticBag source, DiagnosticBag destination)
    {
        foreach (var diagnostic in source)
        {
            destination.Add(diagnostic);
        }
    }

    private sealed class RoutineSnapshot
    {
        public RoutineSnapshot(EncodingRoutine routine, int index, int offset)
        {
            Routine = routine;
            Index = index;
            Offset = offset;
        }

        public EncodingRoutine Routine { get; }

        public int Index { get; }

        public int Offset { get; }

        public int Size { get; private set; }

        public Dictionary<string, int> Labels { get; } = new(StringComparer.Ordinal);

        public List<string> LabelOrder { get; } = [];

        public void AddLabel(string name, int offset)
        {
            if (Labels.TryAdd(name, offset))
            {
                LabelOrder.Add(name);
            }
        }

        public void Complete(int end) => Size = end - Offset;
    }

    private sealed record InstructionSnapshot(
        int Offset,
        int RoutineIndex,
        EncodingRoutine Routine,
        string? Label,
        ActionStatementSyntax Action,
        int ActionIndex,
        EncodedInstruction Instruction);

    private sealed class LayoutSnapshot
    {
        public LayoutSnapshot(
            int executeSize,
            IReadOnlyList<RoutineSnapshot> routines,
            IReadOnlyList<InstructionSnapshot> instructions)
        {
            ExecuteSize = executeSize;
            Routines = routines;
            Instructions = instructions;
        }

        public int ExecuteSize { get; }

        public IReadOnlyList<RoutineSnapshot> Routines { get; }

        public IReadOnlyList<InstructionSnapshot> Instructions { get; }
    }

    private sealed record EncodingDataEntry(string Name, MagnitudeInfo Capacity, DataDefinitionSyntax Definition);

    private sealed record DataEntry(string Name, int Offset, int Size);

    private sealed class DataLayout
    {
        public DataLayout(byte[] bytes, IReadOnlyList<DataEntry> entries)
        {
            Bytes = bytes;
            Entries = entries;
        }

        public byte[] Bytes { get; }

        public IReadOnlyList<DataEntry> Entries { get; }
    }
}
