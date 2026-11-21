using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FluentASM.Core.Catalog;
using FluentASM.Core.Semantics;
using FluentASM.Core.Syntax;
using FluentASM.Core.Syntax.Ast;
using FluentASM.Lsp.Catalog;
using FluentASM.Lsp.Protocol;
using FluentASM.Lsp.Server;
using CoreTextSpan = FluentASM.Core.Diagnostics.TextSpan;

namespace FluentASM.Lsp.Completion;

/// <summary>
/// Proveedor contextual de autocompletado. Decide el estado del lenguaje a
/// partir de los tokens, el AST y la tabla de símbolos; los miembros y formas
/// insertables provienen exclusivamente del catálogo canónico de Core.
/// </summary>
internal static class ContextualCompletionProvider
{
    public static CompletionList GetCompletions(
        DocumentSnapshot snapshot,
        Position position,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();

        int offset = snapshot.Lines.ToOffset(position);
        var analysis = Analyze(snapshot, offset);
        cancellationToken.ThrowIfCancellationRequested();

        if (analysis.Kind == CompletionKind.None)
        {
            return new CompletionList(
                IsIncomplete: false,
                Items: Array.Empty<LspCompletionItem>(),
                ReplaceRange: null,
                Context: analysis.ContextLabel,
                Caret: position,
                Version: snapshot.Version);
        }

        IReadOnlyList<Candidate> candidates = CreateCandidates(snapshot, analysis);
        cancellationToken.ThrowIfCancellationRequested();

        var matches = candidates
            .Select(candidate => (Candidate: candidate, Score: MatchScore(candidate, analysis.PrefixText)))
            .Where(entry => entry.Score >= 0)
            .OrderBy(entry => entry.Score)
            .ThenBy(entry => entry.Candidate.SortText, StringComparer.Ordinal)
            .ThenBy(entry => entry.Candidate.Label, StringComparer.Ordinal)
            .Select(entry => entry.Candidate)
            .ToList();

        var range = new LspRange(
            snapshot.Lines.ToPosition(analysis.ReplaceStart),
            snapshot.Lines.ToPosition(analysis.ReplaceEnd));

        var items = new List<LspCompletionItem>(matches.Count);
        foreach (Candidate candidate in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string insertion = candidate.InsertText;
            int marker = insertion.IndexOf("$0", StringComparison.Ordinal);
            int caretOffset = marker >= 0 ? marker : insertion.Length;

            items.Add(new LspCompletionItem(
                Label: candidate.Label,
                Kind: candidate.Kind,
                Detail: candidate.Detail,
                Documentation: candidate.Documentation,
                InsertText: insertion,
                FilterText: candidate.FilterText,
                SortText: candidate.SortText,
                Metadata: candidate.Metadata,
                TextEdit: new LspTextEdit(range, insertion),
                Category: candidate.Category,
                CaretOffset: caretOffset,
                InsertTextFormat: marker >= 0 ? InsertTextFormat.Snippet : InsertTextFormat.PlainText));
        }

        return new CompletionList(
            IsIncomplete: false,
            Items: items,
            ReplaceRange: range,
            Context: analysis.ContextLabel,
            Caret: position,
            Version: snapshot.Version);
    }

    private static CompletionAnalysis Analyze(DocumentSnapshot snapshot, int offset)
    {
        if (IsInsideNonCodeLiteral(snapshot, offset))
        {
            return CompletionAnalysis.None;
        }

        PrefixInfo prefix = GetPrefix(snapshot.Text, snapshot.LexResult.Tokens, offset);
        var tokens = snapshot.LexResult.Tokens;
        var delimiters = BuildDelimiterStack(tokens, offset);
        SectionInfo section = GetSectionInfo(tokens, offset);
        int braceDepth = GetBraceDepth(tokens, offset);

        RoutineScope scope = FindRoutineScope(snapshot, offset);
        int? memberDot = FindMemberDot(
            snapshot.Text,
            tokens,
            snapshot.LexResult.Comments,
            prefix,
            offset);

        if (prefix.Text.StartsWith("@", StringComparison.Ordinal))
        {
            bool isDeclaration = IsLabelDeclarationPosition(snapshot.Text, prefix);
            return CompletionAnalysis.Label(
                prefix,
                scope,
                isDeclaration ? "Etiqueta local · declaración" : "Etiqueta local",
                isDeclaration);
        }

        if (memberDot.HasValue)
        {
            if (IsTopLevelDot(tokens, memberDot.Value))
            {
                return CompletionAnalysis.Root(prefix, scope, section, hasLeadingDot: true, "Raíz");
            }

            ReceiverPath? path = GetReceiverPath(tokens, memberDot.Value);
            if (path is null)
            {
                return CompletionAnalysis.None;
            }

            // Un punto abre un contexto de miembro. Si el receptor no es
            // válido en el type-state actual, no se degrada a root ni se
            // mezclan miembros de otra sección.
            return AnalyzeReceiver(
                snapshot,
                path,
                prefix,
                scope,
                section,
                tokens,
                delimiters,
                braceDepth,
                memberDot.Value) ?? CompletionAnalysis.None;
        }

        if (delimiters.Count > 0)
        {
            DelimiterFrame frame = delimiters[^1];
            if (frame.Kind == TokenKind.OpenBracket)
            {
                if (IsPortBracket(tokens, frame.TokenIndex))
                {
                    return CompletionAnalysis.PortAddress(prefix, scope, "Puerto · dirección");
                }

                bool hasExplicitCapacity = TryGetMemoryCapacity(
                    tokens,
                    frame.TokenIndex,
                    out _);
                return CompletionAnalysis.Memory(
                    prefix,
                    scope,
                    "Memoria",
                    hasExplicitCapacity);
            }

            if (frame.Kind == TokenKind.OpenAngle)
            {
                // Una vez escrito `Bit:` o `Byte:` el cursor está en el tamaño;
                // volver a ofrecer unidades produciría `Byte:Byte:N`.
                if (HasColonBetween(tokens, frame.TokenIndex, offset))
                {
                    return CompletionAnalysis.None;
                }

                MagnitudeUnitKind magnitudeKind = GetMagnitudeUnitKind(tokens, frame.TokenIndex);
                return CompletionAnalysis.Magnitude(prefix, scope, "Magnitud", magnitudeKind);
            }

            if (frame.Kind == TokenKind.OpenParen)
            {
                string? method = GetMethodName(tokens, frame.TokenIndex);
                if (!string.IsNullOrEmpty(method))
                {
                    CompletionAnalysis? argument = AnalyzeMethod(
                        snapshot,
                        tokens,
                        delimiters,
                        frame.TokenIndex,
                        method,
                        prefix,
                        scope,
                        section);
                    if (argument is not null)
                    {
                        return argument;
                    }
                }
            }
        }

        if (scope.Definition is not null)
        {
            if (scope.IsInBody)
            {
                return CompletionAnalysis.RoutineBody(prefix, scope, "Rutina · cuerpo");
            }

            return CompletionAnalysis.RoutineDescriptor(prefix, scope, "Rutina · contrato");
        }

        // En el nivel superior se completan constructores encadenables, no
        // miembros de la última sección que ya terminó.
        if (delimiters.Count == 0)
        {
            return CompletionAnalysis.Root(prefix, scope, section, hasLeadingDot: false, "Raíz");
        }

        if (section.Kind == SectionKind.Data)
        {
            return CompletionAnalysis.Data(prefix, scope, ".data");
        }

        if (section.Kind == SectionKind.Execute)
        {
            return braceDepth >= 2
                ? CompletionAnalysis.RoutinesBlock(prefix, scope, ".execute · rutinas")
                : CompletionAnalysis.Execute(prefix, scope, ".execute");
        }

        return CompletionAnalysis.Root(prefix, scope, section, hasLeadingDot: false, "Raíz");
    }

    private static CompletionAnalysis? AnalyzeReceiver(
        DocumentSnapshot snapshot,
        ReceiverPath path,
        PrefixInfo prefix,
        RoutineScope scope,
        SectionInfo section,
        IReadOnlyList<Token> tokens,
        IReadOnlyList<DelimiterFrame> delimiters,
        int braceDepth,
        int memberDotIndex)
    {
        string receiver = path.Base;
        bool dataDescriptor = IsDataDescriptorContext(
            section,
            scope,
            braceDepth,
            tokens,
            delimiters);
        bool routineDescriptor = IsRoutineDescriptorContext(section, scope);
        bool flowCall = IsFlowCallContext(scope, tokens, delimiters);

        if (path.Shape == ReceiverShape.Memory)
        {
            if (!scope.IsInBody)
            {
                return null;
            }

            ReceiverPath? memory = ResolveMemoryReceiver(snapshot, path, scope);
            return memory is null
                ? null
                : CompletionAnalysis.Member(
                    prefix,
                    scope,
                    string.Empty,
                    "Memory",
                    "Memoria",
                    section,
                    memory);
        }

        if (path.Shape == ReceiverShape.Port)
        {
            return scope.IsInBody
                ? CompletionAnalysis.Member(
                    prefix,
                    scope,
                    "Port",
                    "Port",
                    "Puerto",
                    section,
                    path)
                : null;
        }

        if (path.Shape == ReceiverShape.Pair)
        {
            return scope.IsInBody && IsCanonicalPair(path)
                ? CompletionAnalysis.Member(
                    prefix,
                    scope,
                    path.Base,
                    "Pair",
                    "Par GPR",
                    section,
                    path)
                : null;
        }

        if (LanguageCatalog.Gprs.Any(gpr => string.Equals(gpr.Name, receiver, StringComparison.Ordinal)))
        {
            if (!scope.IsInBody)
            {
                return null;
            }

            bool isView = path.Members.Count == 1 &&
                IsValidGprView(receiver, path.Members[0]);
            if (path.Members.Count > 1 || (path.Members.Count == 1 && !isView))
            {
                return null;
            }

            return CompletionAnalysis.Member(
                prefix,
                scope,
                receiver,
                receiverKind: isView ? "GprView" : "Gpr",
                contextLabel: receiver,
                section,
                path);
        }

        if (path.Members.Count == 0)
        {
            switch (receiver)
            {
                case "Frf":
                    return scope.IsInBody
                        ? CompletionAnalysis.Member(prefix, scope, receiver, "Frf", "Frf", section, path)
                        : null;
                case "Ipr":
                    return scope.IsInBody
                        ? CompletionAnalysis.Member(prefix, scope, receiver, "Ipr", "Ipr", section, path)
                        : null;
                case "Stack":
                    return scope.IsInBody
                        ? CompletionAnalysis.Member(prefix, scope, receiver, "Stack", "Stack", section, path)
                        : null;
                case "Cpu":
                    return scope.IsInBody
                        ? CompletionAnalysis.Member(prefix, scope, receiver, "Cpu", "Cpu", section, path)
                        : null;
                case "dt" when section.Kind == SectionKind.Data &&
                    scope.Definition is null &&
                    braceDepth == 1:
                    return CompletionAnalysis.Member(prefix, scope, receiver, "Directive", ".data · dt", section, path);
                case "ex" when section.Kind == SectionKind.Execute &&
                    scope.Definition is null &&
                    braceDepth == 1:
                    return CompletionAnalysis.Member(prefix, scope, receiver, "Directive", ".execute · ex", section, path);
                case "rt" when section.Kind == SectionKind.Execute &&
                    scope.Definition is null &&
                    braceDepth == 2:
                    return CompletionAnalysis.Member(prefix, scope, receiver, "Directive", ".execute · rt", section, path);
                case "d" when dataDescriptor || routineDescriptor:
                    return CompletionAnalysis.Member(prefix, scope, receiver, "Directive", "Descriptor · d", section, path);
                case "c" when flowCall:
                    return CompletionAnalysis.Member(
                        prefix,
                        scope,
                        receiver,
                        "Directive",
                        "flowCall · c",
                        section,
                        path);
            }

            if (scope.IsInBody &&
                scope.Parameters.Any(parameter => string.Equals(parameter.Name, receiver, StringComparison.Ordinal)))
            {
                return CompletionAnalysis.Member(prefix, scope, receiver, "Register", receiver, section, path);
            }

            return null;
        }

        // Las cadenas fluidas conservan el orden canónico de sus miembros. El
        // receptor base sigue siendo `d` o `c`; los miembros indican qué
        // llamada se está escribiendo ahora y cuáles ya se consumieron.
        if (receiver == "d" && dataDescriptor)
        {
            return path.Members.SequenceEqual(new[] { "id" })
                ? CompletionAnalysis.Member(prefix, scope, "d.id", "Directive", "Descriptor · d", section, path)
                : null;
        }

        if (receiver == "d" && routineDescriptor)
        {
            if (path.Members.Count == 0)
            {
                return CompletionAnalysis.Member(
                    prefix,
                    scope,
                    receiver,
                    "Directive",
                    "Descriptor · d",
                    section,
                    path);
            }

            if (path.Members.SequenceEqual(new[] { "id" }) ||
                path.Members.SequenceEqual(new[] { "id", "returns" }))
            {
                return CompletionAnalysis.Member(
                    prefix,
                    scope,
                    "d." + string.Join(".", path.Members),
                    "Directive",
                    "Descriptor · d",
                    section,
                    path);
            }

            return null;
        }

        if (receiver == "c" && flowCall)
        {
            string? target = FindFlowTargetName(tokens, memberDotIndex);
            return CompletionAnalysis.Member(
                prefix,
                scope,
                "c." + string.Join(".", path.Members),
                "Directive",
                "flowCall · c",
                section,
                path,
                target);
        }

        return null;
    }

    private static ReceiverPath? ResolveMemoryReceiver(
        DocumentSnapshot snapshot,
        ReceiverPath path,
        RoutineScope scope)
    {
        string? addressBase = path.AddressBase;
        if (string.IsNullOrEmpty(addressBase))
        {
            return null;
        }

        bool isRegisterBase = IsRegisterBaseName(addressBase);
        bool isParameter = scope.Parameters.Any(parameter =>
            string.Equals(parameter.Name, addressBase, StringComparison.Ordinal));
        DataSymbol? data = CollectDataSymbols(snapshot).FirstOrDefault(symbol =>
            string.Equals(symbol.Name, addressBase, StringComparison.Ordinal));

        if (path.HasExplicitCapacity)
        {
            if (!isRegisterBase && !isParameter && data is null)
            {
                return null;
            }

            return path;
        }

        if (data is not null)
        {
            return path with { Bits = checked((int)data.BitSize) };
        }

        if (isParameter)
        {
            RoutineParameter parameter = scope.Parameters.First(candidate =>
                string.Equals(candidate.Name, addressBase, StringComparison.Ordinal));
            return path with { Bits = checked((int)parameter.Magnitude.Bits) };
        }

        // Un acceso por registro sin Byte:N es rechazado por Core.
        return null;
    }

    private static bool IsRegisterBaseName(string name)
    {
        if (RegisterModel.TryGetGprIndex(name, out int index))
        {
            return index >= 0 && index < LanguageCatalog.Gprs.Count;
        }

        int separator = name.IndexOf('.', StringComparison.Ordinal);
        if (separator <= 0 || separator == name.Length - 1)
        {
            return false;
        }

        string register = name[..separator];
        string view = name[(separator + 1)..];
        return RegisterModel.TryGetGprIndex(register, out index) &&
            index >= 0 &&
            index < LanguageCatalog.Gprs.Count &&
            IsValidGprView(register, view);
    }

    private static bool IsDataDescriptorContext(
        SectionInfo section,
        RoutineScope scope,
        int braceDepth,
        IReadOnlyList<Token> tokens,
        IReadOnlyList<DelimiterFrame> delimiters) =>
        section.Kind == SectionKind.Data &&
        scope.Definition is null &&
        HasMethodCall(tokens, delimiters, "define", "dt");

    private static bool IsRoutineDescriptorContext(
        SectionInfo section,
        RoutineScope scope) =>
        section.Kind == SectionKind.Execute &&
        scope.Definition is not null &&
        !scope.IsInBody;

    private static bool IsFlowCallContext(
        RoutineScope scope,
        IReadOnlyList<Token> tokens,
        IReadOnlyList<DelimiterFrame> delimiters) =>
        scope.Definition is not null &&
        scope.IsInBody &&
        HasMethodCall(tokens, delimiters, "flowCall", "Ipr");

    private static bool HasMethodCall(
        IReadOnlyList<Token> tokens,
        IReadOnlyList<DelimiterFrame> delimiters,
        string method,
        string receiver)
    {
        foreach (DelimiterFrame frame in delimiters)
        {
            ReceiverPath? methodReceiver = GetMethodReceiver(tokens, frame.TokenIndex);
            if (frame.Kind != TokenKind.OpenParen ||
                !string.Equals(GetMethodName(tokens, frame.TokenIndex), method, StringComparison.Ordinal) ||
                !string.Equals(methodReceiver?.Base, receiver, StringComparison.Ordinal))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool IsValidGprView(string receiver, string member)
    {
        GprDescriptor? descriptor = LanguageCatalog.Gprs.FirstOrDefault(
            gpr => string.Equals(gpr.Name, receiver, StringComparison.Ordinal));
        return descriptor is not null && descriptor.ValidViews.Any(
            view => string.Equals(view.Suffix.TrimStart('.'), member, StringComparison.Ordinal));
    }

    private static bool IsFlagStateVerb(string method) =>
        FlagCatalog.TryGetFlags(method, out _) &&
        VerbCatalog.TryGet(method, out VerbDefinition definition) &&
        definition.Receivers.HasFlag(ReceiverShape.Frf) &&
        definition.MinArguments == 1 &&
        definition.MaxArguments == 1;

    private static CompletionAnalysis? AnalyzeMethod(
        DocumentSnapshot snapshot,
        IReadOnlyList<Token> tokens,
        IReadOnlyList<DelimiterFrame> delimiters,
        int openerIndex,
        string method,
        PrefixInfo prefix,
        RoutineScope scope,
        SectionInfo section)
    {
        ReceiverPath receiverPath = GetMethodReceiver(tokens, openerIndex) ?? ReceiverPath.Empty;
        string receiver = receiverPath.Base;
        bool flowCall = IsFlowCallContext(scope, tokens, delimiters);
        bool dataDescriptor = IsDataDescriptorContext(
            section,
            scope,
            GetBraceDepth(tokens, prefix.EndOffset),
            tokens,
            delimiters);
        bool routineDescriptor = IsRoutineDescriptorContext(section, scope);

        switch (method)
        {
            case "target" when receiver == "c" && flowCall:
                return CompletionAnalysis.FlowTarget(prefix, scope, "flowCall · target");
            case "flowCall" when receiver == "Ipr":
                return HasFlowContractStart(tokens, openerIndex, prefix.EndOffset)
                    ? CompletionAnalysis.FlowContract(prefix, scope, "Ipr · flowCall · contrato")
                    : CompletionAnalysis.FlowCall(prefix, scope, "Ipr · flowCall");
            case "flowJump" when receiver == "Ipr":
                return CompletionAnalysis.FlowTargetLabel(prefix, scope, "Ipr · flowJump");
            case "flowLoop" when receiver == "Ipr":
                return CompletionAnalysis.Argument(
                    prefix,
                    scope,
                    receiver,
                    method,
                    "Ipr · flowLoop");
            case "inputs" when receiver == "c" && flowCall:
                return CompletionAnalysis.FlowInputs(
                    prefix,
                    scope,
                    "flowCall · inputs",
                    FindFlowTargetName(tokens, openerIndex));
            case "output" when receiver == "c" && flowCall:
                return CompletionAnalysis.FlowOutput(
                    prefix,
                    scope,
                    "flowCall · output",
                    FindFlowTargetName(tokens, openerIndex));
            case "returns" when receiver == "d" && routineDescriptor:
                return CompletionAnalysis.Argument(
                    prefix,
                    scope,
                    receiver,
                    method,
                    "Contrato · returns");
            case "id" when receiver == "d" && (dataDescriptor || routineDescriptor):
                return CompletionAnalysis.Argument(
                    prefix,
                    scope,
                    receiver,
                    method,
                    routineDescriptor ? "Rutina · id" : "Dato · id");
            case "value" when receiver == "d" && dataDescriptor:
                return CompletionAnalysis.Argument(
                    prefix,
                    scope,
                    receiver,
                    method,
                    ".data · value");
            case "define" when receiver is "dt" or "rt":
                return CompletionAnalysis.Argument(
                    prefix,
                    scope,
                    receiver,
                    method,
                    "Directiva · define");
            case "routines" when receiver == "ex":
                return CompletionAnalysis.Argument(
                    prefix,
                    scope,
                    receiver,
                    method,
                    "Directiva · routines");
            case "run" when receiver == "d" && routineDescriptor:
                return CompletionAnalysis.Argument(
                    prefix,
                    scope,
                    receiver,
                    method,
                    "Rutina · cuerpo");
        }

        if (VerbCatalog.TryGet(method, out VerbDefinition definition) &&
            TryGetReceiverShape(receiverPath, scope, out ReceiverShape receiverShape) &&
            definition.Receivers.HasFlag(receiverShape) &&
            (!definition.Receivers.HasFlag(ReceiverShape.Pair) ||
                IsPairVerbAllowed(receiverPath, method)))
        {
            string context = method switch
            {
                "copy" => "Operando · copy",
                "loadAddress" => "Dirección · loadAddress",
                _ when receiver == "Stack" => "Pila · " + method,
                _ when receiver == "Frf" => "Bandera · " + method,
                _ when receiver == "Ipr" => "Ipr · " + method,
                _ when receiver == "Cpu" => "Cpu · " + method,
                _ => "Argumento · " + method,
            };
            return CompletionAnalysis.Argument(
                prefix,
                scope,
                receiver,
                method,
                context,
                receiverPath);
        }

        return CompletionAnalysis.None;
    }

    private static IReadOnlyList<Candidate> CreateCandidates(
        DocumentSnapshot snapshot,
        CompletionAnalysis analysis)
    {
        return analysis.Kind switch
        {
            CompletionKind.Root => CreateRootCandidates(analysis),
            CompletionKind.Data => CreateDataCandidates(analysis),
            CompletionKind.Execute => CreateExecuteCandidates(analysis),
            CompletionKind.RoutinesBlock => CreateRoutinesBlockCandidates(analysis),
            CompletionKind.RoutineDescriptor => CreateRoutineDescriptorCandidates(analysis),
            CompletionKind.RoutineBody => CreateRoutineBodyCandidates(snapshot, analysis),
            CompletionKind.Member => CreateMemberCandidates(snapshot, analysis),
            CompletionKind.Label => CreateLabelCandidates(analysis),
            CompletionKind.Memory => CreateMemoryCandidates(snapshot, analysis),
            CompletionKind.PortAddress => CreatePortAddressCandidates(),
            CompletionKind.Magnitude => CreateMagnitudeCandidates(analysis.MagnitudeKind),
            CompletionKind.FlowCall => CreateFlowCallCandidates(snapshot, analysis),
            CompletionKind.FlowContract => CreateFlowContractCandidates(snapshot, analysis),
            CompletionKind.FlowTarget => CreateFlowTargetCandidates(snapshot, analysis),
            CompletionKind.FlowTargetLabel => CreateFlowAddressCandidates(analysis),
            CompletionKind.FlowInputs => CreateFlowInputCandidates(snapshot, analysis),
            CompletionKind.FlowOutput => CreateFlowOutputCandidates(snapshot, analysis),
            CompletionKind.Argument => CreateArgumentCandidates(snapshot, analysis),
            _ => Array.Empty<Candidate>(),
        };
    }

    private static IReadOnlyList<Candidate> CreateRootCandidates(CompletionAnalysis analysis)
    {
        var candidates = new List<Candidate>();
        foreach (FlowConstructorDescriptor constructor in LanguageCatalog.FlowConstructors)
        {
            bool allowed = analysis.Section.Kind switch
            {
                SectionKind.None => constructor.Name is ".data" or ".execute",
                SectionKind.Data => constructor.Name == ".execute",
                SectionKind.Execute => constructor.Name == ".initialize",
                _ => false,
            };

            if (!allowed || !LspCatalog.TryGetItem(constructor.Name, out LspCompletionItem source))
            {
                continue;
            }

            string insertion = analysis.HasLeadingDot
                ? constructor.Name.TrimStart('.')
                : constructor.Name;
            string filter = constructor.Name.TrimStart('.');
            candidates.Add(FromCatalog(
                source,
                constructor.Name,
                insertion,
                filter,
                "Flujo",
                "00_" + filter,
                source.Metadata?.Category ?? "Flujo"));
        }

        return candidates;
    }

    private static IReadOnlyList<Candidate> CreateDataCandidates(CompletionAnalysis analysis)
    {
        var candidates = new List<Candidate>();
        AddDirectiveStatement(candidates, "dt.define", "define", "dt", "01_define");
        return candidates;
    }

    private static IReadOnlyList<Candidate> CreateExecuteCandidates(CompletionAnalysis analysis)
    {
        var candidates = new List<Candidate>();
        AddDirectiveStatement(candidates, "ex.routines", "routines", "ex", "01_routines");
        return candidates;
    }

    private static IReadOnlyList<Candidate> CreateRoutinesBlockCandidates(CompletionAnalysis analysis)
    {
        var candidates = new List<Candidate>();
        AddDirectiveStatement(candidates, "rt.define", "define", "rt", "01_define");
        return candidates;
    }

    private static IReadOnlyList<Candidate> CreateRoutineDescriptorCandidates(CompletionAnalysis analysis)
    {
        var candidates = new List<Candidate>();
        AddDirectiveStatement(candidates, "d.id", "id", "d", "01_id");
        AddDirectiveStatement(candidates, "d.returns", "returns", "d", "02_returns");
        AddDirectiveStatement(candidates, "d.run", "run", "d", "03_run");
        return candidates;
    }

    private static IReadOnlyList<Candidate> CreateRoutineBodyCandidates(
        DocumentSnapshot snapshot,
        CompletionAnalysis analysis)
    {
        var candidates = new List<Candidate>();
        foreach (GprDescriptor gpr in LanguageCatalog.Gprs)
        {
            if (LspCatalog.TryGetItem(gpr.Name, out LspCompletionItem source))
            {
                candidates.Add(FromCatalog(
                    source,
                    gpr.Name,
                    gpr.Name,
                    gpr.Name,
                    "Registros",
                    "01_" + gpr.Name,
                    source.Metadata?.Category ?? "Registros"));
            }
        }

        foreach (UnitDescriptor unit in LanguageCatalog.Units)
        {
            if (unit.Unit is RegisterUnit.None)
            {
                // Port es un operando de hardware, no un receptor identificable
                // por nombre dentro del cuerpo de una rutina.
                continue;
            }

            if (LspCatalog.TryGetItem(unit.Name, out LspCompletionItem source))
            {
                candidates.Add(FromCatalog(
                    source,
                    unit.Name,
                    unit.Name,
                    unit.Name,
                    "Registros",
                    "02_" + unit.Name,
                    source.Metadata?.Category ?? "Registros"));
            }
        }

        foreach (RoutineParameter parameter in analysis.Scope.Parameters)
        {
            candidates.Add(CreateParameterCandidate(parameter));
        }

        return candidates
            .GroupBy(candidate => candidate.Label, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    private static IReadOnlyList<Candidate> CreateMemberCandidates(
        DocumentSnapshot snapshot,
        CompletionAnalysis analysis)
    {
        return analysis.ReceiverKind switch
        {
            "Gpr" => CreateGprCandidates(analysis, includeViews: true),
            "GprView" => CreateGprCandidates(analysis, includeViews: false),
            "Register" => CreateVerbCandidates(ReceiverShape.Register),
            "Memory" => CreateMemoryMemberCandidates(analysis),
            "Port" => CreateVerbCandidates(ReceiverShape.Port),
            "Pair" => CreatePairCandidates(analysis),
            "Frf" => CreateFrfCandidates(),
            "Ipr" => CreateIprCandidates(),
            "Stack" => CreateStackCandidates(),
            "Cpu" => CreateCpuCandidates(),
            "Directive" => CreateDirectiveMemberCandidates(snapshot, analysis),
            _ => Array.Empty<Candidate>(),
        };
    }

    private static IReadOnlyList<Candidate> CreateGprCandidates(
        CompletionAnalysis analysis,
        bool includeViews)
    {
        var candidates = new List<Candidate>();
        GprDescriptor? descriptor = LanguageCatalog.Gprs.FirstOrDefault(
            gpr => string.Equals(gpr.Name, analysis.Receiver, StringComparison.Ordinal));
        if (descriptor is null)
        {
            return candidates;
        }

        if (includeViews)
        {
            for (int viewIndex = 0; viewIndex < descriptor.ValidViews.Length; viewIndex++)
            {
                RegisterViewDescriptor view = descriptor.ValidViews[viewIndex];
                if (view.Suffix.Length == 0)
                {
                    continue;
                }

                string label = descriptor.Name + view.Suffix;
                if (!LspCatalog.TryGetItem(label, out LspCompletionItem source))
                {
                    continue;
                }

                string member = view.Suffix.TrimStart('.');
                candidates.Add(FromCatalog(
                    source,
                    label,
                    member,
                    member,
                    "Registros",
                    viewIndex.ToString("D2") + "_" + view.Suffix,
                    source.Metadata?.Category ?? "Registros"));
            }
        }

        int receiverBits = TryGetReceiverBits(analysis) ?? 64;
        candidates.AddRange(CreateVerbCandidates(
            ReceiverShape.Register,
            definition => receiverBits > 8 ||
                (definition.Name is not ("copyUnsigned" or "copySigned"))));
        return candidates;
    }

    private static IReadOnlyList<Candidate> CreateMemoryMemberCandidates(
        CompletionAnalysis analysis)
    {
        return CreateVerbCandidates(
            ReceiverShape.Memory,
            definition => analysis.ReceiverPath.HasExplicitCapacity ||
                definition.Name is not ("copyBlock" or "fillBlock"));
    }

    private static IReadOnlyList<Candidate> CreatePairCandidates(
        CompletionAnalysis analysis)
    {
        ReceiverPath path = analysis.ReceiverPath;
        if (path.Members.Count != 1 ||
            !IsFullGprName(path.Base) ||
            !IsFullGprName(path.Members[0]))
        {
            return Array.Empty<Candidate>();
        }

        bool multiplyPair =
            path.Base == "Gpr2" && path.Members[0] == "Gpr0";
        bool dividePair =
            path.Base == "Gpr0" && path.Members[0] == "Gpr2";

        return CreateVerbCandidates(
            ReceiverShape.Pair,
            definition => definition.Name switch
            {
                "multiplyWide" or "signExtend" => multiplyPair,
                "divide" or "divideSigned" => dividePair,
                _ => false,
            });
    }

    private static IReadOnlyList<Candidate> CreateFrfCandidates()
    {
        var candidates = new List<Candidate>();
        var physicalNames = LanguageCatalog.Flags.Select(flag => flag.Name).ToHashSet(StringComparer.Ordinal);

        foreach (FlagDescriptor flag in LanguageCatalog.Flags)
        {
            string label = "Frf." + flag.Name;
            if (!LspCatalog.TryGetItem(label, out LspCompletionItem source))
            {
                continue;
            }

            candidates.Add(FromCatalog(
                source,
                label,
                flag.Name,
                flag.Name,
                "Registros",
                "00_" + flag.Name,
                source.Metadata?.Category ?? "Registros"));
        }

        foreach (FlagAliasDescriptor alias in LanguageCatalog.FlagAliases)
        {
            if (physicalNames.Contains(alias.Alias))
            {
                continue;
            }

            string label = "Frf." + alias.Alias;
            if (!LspCatalog.TryGetItem(label, out LspCompletionItem source))
            {
                continue;
            }

            candidates.Add(FromCatalog(
                source,
                label,
                alias.Alias,
                alias.Alias,
                "Registros",
                "01_" + alias.Alias,
                source.Metadata?.Category ?? "Registros"));
        }

        candidates.AddRange(CreateVerbCandidates(ReceiverShape.Frf));
        return candidates;
    }

    private static IReadOnlyList<Candidate> CreateIprCandidates()
    {
        var candidates = new List<Candidate>();
        if (LspCatalog.TryGetItem("Ipr.current", out LspCompletionItem current))
        {
            candidates.Add(FromCatalog(
                current,
                "Ipr.current",
                "current",
                "current",
                "Registros",
                "00_current",
                current.Metadata?.Category ?? "Registros"));
        }

        candidates.AddRange(CreateVerbCandidates(ReceiverShape.Ipr));
        return candidates;
    }

    private static IReadOnlyList<Candidate> CreateStackCandidates() =>
        CreateVerbCandidates(ReceiverShape.Stack);

    private static IReadOnlyList<Candidate> CreateCpuCandidates() =>
        CreateVerbCandidates(ReceiverShape.Cpu);

    private static IReadOnlyList<Candidate> CreateVerbCandidates(
        ReceiverShape receiverShape,
        Func<VerbDefinition, bool>? predicate = null)
    {
        var candidates = new List<Candidate>();
        foreach (VerbDescriptor verb in LanguageCatalog.Verbs)
        {
            if (!VerbCatalog.TryGet(verb.Name, out VerbDefinition definition) ||
                !definition.Receivers.HasFlag(receiverShape) ||
                (predicate is not null && !predicate(definition)))
            {
                continue;
            }

            string sourceLabel = verb.Name + "($0)";
            if (!LspCatalog.TryGetItem(sourceLabel, out LspCompletionItem source))
            {
                continue;
            }

            bool noArguments = verb.MinArguments == 0 && verb.MaxArguments == 0;
            string insertion = noArguments ? verb.Name + "()" : verb.Name + "($0)";
            candidates.Add(FromCatalog(
                source,
                verb.Name,
                insertion,
                verb.Name,
                "Verbos",
                (receiverShape == ReceiverShape.Register ? "10_" : "01_") + verb.Name,
                source.Metadata?.Category ?? "Verbos"));
        }

        return candidates;
    }

    private static IReadOnlyList<Candidate> CreateDirectiveMemberCandidates(
        DocumentSnapshot snapshot,
        CompletionAnalysis analysis)
    {
        var candidates = new List<Candidate>();
        IReadOnlyList<string> members = analysis.ReceiverPath.Members;

        switch (analysis.ReceiverPath.Base)
        {
            case "dt" when members.Count == 0:
                AddDirective(candidates, "dt.define", "define", "define", "01_define");
                break;
            case "ex" when members.Count == 0:
                AddDirective(candidates, "ex.routines", "routines", "routines", "01_routines");
                break;
            case "rt" when members.Count == 0:
                AddDirective(candidates, "rt.define", "define", "define", "01_define");
                break;
            case "d" when analysis.Section.Kind == SectionKind.Data && members.Count == 0:
                AddDirective(candidates, "d.id", "id", "id", "01_id");
                break;
            case "d" when analysis.Section.Kind == SectionKind.Data &&
                members.SequenceEqual(new[] { "id" }):
                AddDirective(candidates, "d.value", "value", "value", "01_value");
                break;
            case "d" when analysis.Section.Kind != SectionKind.Data && members.Count == 0:
                AddDirective(candidates, "d.id", "id", "id", "01_id");
                AddDirective(candidates, "d.returns", "returns", "returns", "02_returns");
                AddDirective(candidates, "d.run", "run", "run", "03_run");
                break;
            case "d" when analysis.Section.Kind != SectionKind.Data &&
                members.SequenceEqual(new[] { "id" }):
                AddDirective(candidates, "d.returns", "returns", "returns", "01_returns");
                AddDirective(candidates, "d.run", "run", "run", "02_run");
                break;
            case "d" when analysis.Section.Kind != SectionKind.Data &&
                members.SequenceEqual(new[] { "id", "returns" }):
                AddDirective(candidates, "d.run", "run", "run", "01_run");
                break;
            case "c" when members.Count == 0:
                AddDirective(candidates, "c.target", "target", "target", "01_target");
                break;
            case "c" when members.SequenceEqual(new[] { "target" }):
                AddFlowContractMembers(candidates, snapshot, analysis.TargetName);
                break;
            case "c" when members.SequenceEqual(new[] { "target", "inputs" }):
                LocalRoutine? target = FindLocalRoutine(snapshot, analysis.TargetName);
                if (target?.HasReturn == true)
                {
                    AddDirective(candidates, "c.output", "output", "output", "01_output");
                }
                break;
        }

        return candidates;
    }

    private static void AddFlowContractMembers(
        ICollection<Candidate> candidates,
        DocumentSnapshot snapshot,
        string targetName)
    {
        LocalRoutine? target = FindLocalRoutine(snapshot, targetName);
        if (target is null)
        {
            return;
        }

        if (target.ParameterCount > 0)
        {
            // Core exige declarar `.inputs(...)` antes de `.output(...)` cuando
            // la rutina recibe parámetros. No se ofrece una salida prematura
            // que produciría un contrato incompleto.
            AddDirective(candidates, "c.inputs", "inputs", "inputs", "01_inputs");
            return;
        }

        if (target.HasReturn)
        {
            AddDirective(candidates, "c.output", "output", "output", "01_output");
        }
    }

    private static LocalRoutine? FindLocalRoutine(
        DocumentSnapshot snapshot,
        string name) =>
        string.IsNullOrEmpty(name)
            ? null
            : CollectRoutines(snapshot).FirstOrDefault(
                routine => string.Equals(routine.Name, name, StringComparison.Ordinal));

    private static IReadOnlyList<Candidate> CreateLabelCandidates(CompletionAnalysis analysis)
    {
        if (analysis.IsLabelDeclaration)
        {
            // En una declaración no se propone una etiqueta ya existente. Los
            // nombres nuevos se escriben libremente; el catálogo solo contiene
            // referencias a etiquetas ya declaradas.
            return Array.Empty<Candidate>();
        }

        var candidates = new List<Candidate>();
        foreach (LocalLabel label in analysis.Scope.Labels)
        {
            if (label.StartOffset == analysis.Prefix.StartOffset)
            {
                // No se ofrece como destino la misma etiqueta que el usuario
                // está declarando en la posición actual.
                continue;
            }

            candidates.Add(new Candidate(
                label.Name,
                CompletionItemKind.Reference,
                "Etiqueta local: " + label.Name,
                MarkupContent.Markdown("Etiqueta local `" + label.Name + "` definida en la rutina actual."),
                label.Name,
                label.Name,
                "Etiquetas",
                "00_" + label.Name,
                new FluentItemMetadata(
                    Category: "Etiquetas",
                    Signature: label.Name + ":",
                    Description: "Punto de anclaje de flujo local.",
                    Inputs: "Ninguno",
                    Modifies: "Ipr",
                    Returns: "Destino de salto")));
        }

        return candidates;
    }

    private static IReadOnlyList<Candidate> CreateFlowAddressCandidates(
        CompletionAnalysis analysis)
    {
        var candidates = new List<Candidate>(CreateLabelCandidates(analysis));
        foreach (GprDescriptor gpr in LanguageCatalog.Gprs)
        {
            if (!LspCatalog.TryGetItem(gpr.Name, out LspCompletionItem source))
            {
                continue;
            }

            candidates.Add(FromCatalog(
                source,
                gpr.Name,
                gpr.Name,
                gpr.Name,
                "Registros",
                "01_" + gpr.Name,
                source.Metadata?.Category ?? "Registros"));
        }

        return candidates;
    }

    private static IReadOnlyList<Candidate> CreateMemoryCandidates(
        DocumentSnapshot snapshot,
        CompletionAnalysis analysis)
    {
        var candidates = new List<Candidate>();
        foreach (DataSymbol data in CollectDataSymbols(snapshot))
        {
            candidates.Add(CreateDataCandidate(data));
        }

        if (analysis.HasExplicitCapacity)
        {
            foreach (GprDescriptor gpr in LanguageCatalog.Gprs)
            {
                if (LspCatalog.TryGetItem(gpr.Name, out LspCompletionItem source))
                {
                    candidates.Add(FromCatalog(
                        source,
                        gpr.Name,
                        gpr.Name,
                        gpr.Name,
                        "Registros",
                        "01_" + gpr.Name,
                        source.Metadata?.Category ?? "Registros"));
                }
            }
        }

        foreach (RoutineParameter parameter in analysis.Scope.Parameters)
        {
            candidates.Add(CreateParameterCandidate(parameter));
        }

        return candidates;
    }

    private static IReadOnlyList<Candidate> CreatePortAddressCandidates()
    {
        if (!LspCatalog.TryGetItem("Gpr2.low16", out LspCompletionItem source))
        {
            return Array.Empty<Candidate>();
        }

        return new[]
        {
            FromCatalog(
                source,
                "Gpr2.low16",
                "Gpr2.low16",
                "Gpr2.low16",
                "Registros",
                "00_Gpr2.low16",
                source.Metadata?.Category ?? "Registros"),
        };
    }

    private static IReadOnlyList<Candidate> CreateMemoryDataCandidates(DocumentSnapshot snapshot)
    {
        var candidates = new List<Candidate>();
        foreach (DataSymbol data in CollectDataSymbols(snapshot))
        {
            candidates.Add(CreateDataCandidate(data));
        }

        return candidates;
    }

    private static IReadOnlyList<Candidate> CreateMagnitudeCandidates(
        MagnitudeUnitKind magnitudeKind = MagnitudeUnitKind.Any)
    {
        var candidates = new List<Candidate>();
        foreach (MagnitudeDescriptor magnitude in LanguageCatalog.Magnitudes)
        {
            bool allowed = magnitudeKind switch
            {
                MagnitudeUnitKind.Bit => magnitude.Name.Contains("Bit", StringComparison.Ordinal),
                MagnitudeUnitKind.Byte => magnitude.Name.Contains("Byte", StringComparison.Ordinal),
                _ => true,
            };

            if (!allowed ||
                !LspCatalog.TryGetItem(
                    magnitude.Name,
                    "Magnitudes",
                    out LspCompletionItem source))
            {
                continue;
            }

            candidates.Add(FromCatalog(
                source,
                magnitude.Name,
                magnitude.Name + ":$0",
                magnitude.Name,
                "Magnitudes",
                "00_" + magnitude.Name,
                source.Metadata?.Category ?? "Magnitudes"));
        }

        return candidates;
    }

    private static IReadOnlyList<Candidate> CreateFlowCallCandidates(
        DocumentSnapshot snapshot,
        CompletionAnalysis analysis)
    {
        return CreateRoutineCandidates(snapshot, analysis, shortFormOnly: true);
    }

    private static IReadOnlyList<Candidate> CreateFlowContractCandidates(
        DocumentSnapshot snapshot,
        CompletionAnalysis analysis)
    {
        var candidates = new List<Candidate>();
        foreach (LocalRoutine routine in CollectRoutines(snapshot))
        {
            string detail = routine.HasReturn
                ? "Destino de contrato: " + routine.Name + " · " + routine.ReturnDisplay
                : "Destino de contrato: " + routine.Name;
            var metadata = new FluentItemMetadata(
                Category: "Rutinas",
                Signature: "c.target(" + routine.Name + ")",
                Description: "Rutina válida como destino de Ipr.flowCall.",
                Inputs: routine.ParameterCount == 0
                    ? "Ninguno"
                    : routine.ParameterCount.ToString() + " parámetro(s)",
                Modifies: routine.HasReturn ? routine.ReturnDisplay + " (retorno)" : "Stack",
                Returns: routine.HasReturn ? routine.ReturnDisplay ?? "Gpr" : "void");

            candidates.Add(new Candidate(
                routine.Name,
                CompletionItemKind.Function,
                detail,
                MarkupContent.Markdown("Destino canónico `c.target(" + routine.Name + ")`."),
                "c.target(" + routine.Name + ")",
                routine.Name,
                "Rutinas",
                "00_" + routine.Name,
                metadata));
        }

        return candidates;
    }

    private static IReadOnlyList<Candidate> CreateFlowTargetCandidates(
        DocumentSnapshot snapshot,
        CompletionAnalysis analysis)
    {
        return CreateRoutineCandidates(snapshot, analysis, shortFormOnly: false);
    }

    private static IReadOnlyList<Candidate> CreateFlowInputCandidates(
        DocumentSnapshot snapshot,
        CompletionAnalysis analysis)
    {
        if (string.IsNullOrEmpty(analysis.Receiver))
        {
            return Array.Empty<Candidate>();
        }

        LocalRoutine? routine = CollectRoutines(snapshot).FirstOrDefault(
            candidate => string.Equals(candidate.Name, analysis.Receiver, StringComparison.Ordinal));
        if (routine is null)
        {
            return Array.Empty<Candidate>();
        }

        int argumentIndex = FindFlowInputsArgumentIndex(
            snapshot.LexResult.Tokens,
            analysis.Prefix.EndOffset);
        if (argumentIndex < 0 || argumentIndex >= routine.Parameters.Count)
        {
            return Array.Empty<Candidate>();
        }

        LocalRoutineParameter parameter = routine.Parameters[argumentIndex];
        string? display = GetExpectedInputDisplay(parameter);
        if (display is null)
        {
            return Array.Empty<Candidate>();
        }

        var metadata = new FluentItemMetadata(
            Category: "Entradas",
            Signature: "c.inputs(" + display + ")",
            Description: "Entrada " + (argumentIndex + 1) + " esperada por la rutina `" + routine.Name + "`.",
            Inputs: display,
            Modifies: "Ninguno",
            Returns: display);
        return new[]
        {
            new Candidate(
                display,
                CompletionItemKind.Field,
                "Entrada esperada: " + display,
                MarkupContent.Markdown("La rutina `" + routine.Name + "` espera `" + display + "` para esta entrada."),
                display,
                display,
                "Entradas",
                "00_" + argumentIndex.ToString("D2"),
                metadata),
        };
    }

    private static IReadOnlyList<Candidate> CreateFlowOutputCandidates(
        DocumentSnapshot snapshot,
        CompletionAnalysis analysis)
    {
        if (string.IsNullOrEmpty(analysis.Receiver))
        {
            return Array.Empty<Candidate>();
        }

        LocalRoutine? routine = CollectRoutines(snapshot).FirstOrDefault(
            candidate => string.Equals(candidate.Name, analysis.Receiver, StringComparison.Ordinal));
        if (routine?.ReturnDisplay is not { } display)
        {
            return Array.Empty<Candidate>();
        }

        var metadata = new FluentItemMetadata(
            Category: "Salidas",
            Signature: "c.output(" + display + ")",
            Description: "Salida esperada por la rutina `" + routine.Name + "`.",
            Inputs: display,
            Modifies: "Ninguno",
            Returns: display);
        return new[]
        {
            new Candidate(
                display,
                CompletionItemKind.Field,
                "Salida esperada: " + display,
                MarkupContent.Markdown("La rutina `" + routine.Name + "` devuelve `" + display + "`."),
                display,
                display,
                "Salidas",
                "00_output",
                metadata),
        };
    }

    private static int FindFlowInputsArgumentIndex(
        IReadOnlyList<Token> tokens,
        int offset)
    {
        int opener = -1;
        for (int index = 0; index < tokens.Count; index++)
        {
            Token token = tokens[index];
            if (token.Span.Start >= offset)
            {
                break;
            }

            if (token.Kind == TokenKind.OpenParen &&
                index > 0 &&
                IsIdentifierToken(tokens[index - 1].Kind) &&
                string.Equals(tokens[index - 1].Text, "inputs", StringComparison.Ordinal))
            {
                opener = index;
            }
        }

        if (opener < 0)
        {
            return -1;
        }

        int argumentIndex = 0;
        for (int index = opener + 1; index < tokens.Count; index++)
        {
            Token token = tokens[index];
            if (token.Span.Start >= offset)
            {
                break;
            }

            if (token.Kind == TokenKind.Comma)
            {
                argumentIndex++;
            }
        }

        return argumentIndex;
    }

    private static string? GetExpectedInputDisplay(LocalRoutineParameter parameter) => parameter.Bits switch
    {
        64 => "Gpr" + parameter.RegisterIndex,
        32 => "Gpr" + parameter.RegisterIndex + ".low32",
        16 => "Gpr" + parameter.RegisterIndex + ".low16",
        8 => "Gpr" + parameter.RegisterIndex + ".low8",
        _ => null,
    };

    private static IReadOnlyList<Candidate> CreateRoutineCandidates(
        DocumentSnapshot snapshot,
        CompletionAnalysis analysis,
        bool shortFormOnly)
    {
        var candidates = new List<Candidate>();
        foreach (LocalRoutine routine in CollectRoutines(snapshot))
        {
            if (shortFormOnly && (routine.ParameterCount != 0 || routine.HasReturn))
            {
                continue;
            }

            string detail = routine.HasReturn
                ? "Rutina de usuario: " + routine.Name + " · " + routine.ReturnDisplay
                : "Rutina de usuario: " + routine.Name;
            var metadata = new FluentItemMetadata(
                Category: "Rutinas",
                Signature: "routine " + routine.Name,
                Description: "Rutina definida en el código actual.",
                Inputs: routine.ParameterCount switch
                {
                    0 => "Ninguno",
                    1 => "1 parámetro",
                    _ => $"{routine.ParameterCount} parámetros",
                },
                Modifies: routine.HasReturn ? routine.ReturnDisplay + " (retorno)" : "Stack",
                Returns: routine.HasReturn ? routine.ReturnDisplay ?? "Gpr" : "void");

            candidates.Add(new Candidate(
                routine.Name,
                CompletionItemKind.Function,
                detail,
                MarkupContent.Markdown("Invocación a la rutina definida `" + routine.Name + "`."),
                routine.Name,
                routine.Name,
                "Rutinas",
                "00_" + routine.Name,
                metadata));
        }

        return candidates;
    }

    private static IReadOnlyList<Candidate> CreateArgumentCandidates(
        DocumentSnapshot snapshot,
        CompletionAnalysis analysis)
    {
        if (analysis.Receiver == "Frf" && IsFlagStateVerb(analysis.Method))
        {
            return CreateFlagStateArgumentCandidates();
        }

        return analysis.Method switch
        {
            "returns" => CreateReturnRegisterCandidates(),
            "id" => CreateRoutineIdentifierCandidates(snapshot, analysis),
            "loadAddress" => CreateMemoryDataCandidates(snapshot),
            "value" => CreateContextualKeywordCandidates("defaultValue"),
            "copy" => CreateCopyArgumentCandidates(snapshot, analysis),
            "copyUnsigned" or "copySigned" =>
                CreateExtensionArgumentCandidates(snapshot, analysis),
            "copyBlock" => CreateCopyBlockArgumentCandidates(analysis),
            "fillBlock" => CreateFillBlockArgumentCandidates(),
            "conditionalCopy" =>
                CreateConditionalCopyArgumentCandidates(snapshot, analysis),
            "exchange" or
            "add" or "sub" or "addWithCarry" or "subWithBorrow" or
            "compare" or "multiply" or "and" or "or" or "xor" or "test" =>
                CreateValueOperandCandidates(snapshot, analysis),
            "multiplyWide" or "divide" or "divideSigned" =>
                CreatePairOperandCandidates(),
            "shiftLeft" or "shiftRightLogical" or "shiftRightArithmetic" or
            "rotateLeft" or "rotateRight" =>
                CreateShiftArgumentCandidates(snapshot, analysis),
            "testBit" or "setBit" or "clearBit" or "toggleBit" =>
                CreateBitIndexArgumentCandidates(),
            "push" => CreateStackPushArgumentCandidates(snapshot, analysis),
            "pop" => CreateStackPopArgumentCandidates(analysis),
            "reserve" or "release" => CreateMagnitudeCandidates(MagnitudeUnitKind.Byte),
            "flowLoop" => CreateFlowAddressCandidates(analysis),
            _ => Array.Empty<Candidate>(),
        };
    }

    private static IReadOnlyList<Candidate> CreateCopyArgumentCandidates(
        DocumentSnapshot snapshot,
        CompletionAnalysis analysis)
    {
        if (analysis.ReceiverPath.Shape == ReceiverShape.Port)
        {
            return CreatePortCopyArgumentCandidates();
        }

        var candidates = new List<Candidate>(CreateValueOperandCandidates(snapshot, analysis));
        bool isRegisterReceiver = TryGetReceiverShape(
            analysis.ReceiverPath,
            analysis.Scope,
            out ReceiverShape receiverShape) &&
            receiverShape == ReceiverShape.Register;
        if (analysis.ReceiverPath.Shape == ReceiverShape.Memory)
        {
            // Core no admite copia directa memoria → memoria.
            candidates = candidates
                .Where(candidate => !candidate.InsertText.StartsWith("[", StringComparison.Ordinal))
                .ToList();
        }
        else if (isRegisterReceiver &&
            TryGetReceiverBits(analysis) == 64 &&
            LspCatalog.TryGetItem("Ipr.current", out LspCompletionItem current))
        {
            candidates.Add(FromCatalog(
                current,
                "Ipr.current",
                "Ipr.current",
                "Ipr.current",
                "Registros",
                "02_Ipr.current",
                current.Metadata?.Category ?? "Registros"));
        }

        if (isRegisterReceiver)
        {
            candidates.AddRange(CreateContextualKeywordCandidates("defaultValue"));
        }

        return candidates;
    }

    private static IReadOnlyList<Candidate> CreatePortCopyArgumentCandidates()
    {
        if (!LspCatalog.TryGetItem("Gpr0.low8", out LspCompletionItem source))
        {
            return Array.Empty<Candidate>();
        }

        return new[]
        {
            FromCatalog(
                source,
                "Gpr0.low8",
                "Gpr0.low8",
                "Gpr0.low8",
                "Registros",
                "00_Gpr0.low8",
                source.Metadata?.Category ?? "Registros"),
        };
    }

    private static IReadOnlyList<Candidate> CreateCopyBlockArgumentCandidates(
        CompletionAnalysis analysis)
    {
        if (analysis.ReceiverPath.Bits is not { } bits ||
            bits <= 0 ||
            bits % 8 != 0)
        {
            return Array.Empty<Candidate>();
        }

        long bytes = bits / 8;
        string label = $"Byte:{bytes} [Gpr6]";
        return new[]
        {
            new Candidate(
                label,
                CompletionItemKind.Field,
                "Origen de bloque: Gpr6",
                MarkupContent.Markdown("Origen canónico de `copyBlock`: `Byte:N [Gpr6]`."),
                label,
                "Byte:" + bytes,
                "Memoria",
                "00_copyBlock",
                new FluentItemMetadata(
                    Category: "Memoria",
                    Signature: label,
                    Description: "Bloque de origen para copyBlock.",
                    Inputs: label,
                    Modifies: "Gpr1, Gpr6, Gpr7",
                    Returns: "void")),
        };
    }

    private static IReadOnlyList<Candidate> CreateFillBlockArgumentCandidates()
    {
        if (!LspCatalog.TryGetItem("Gpr0.low8", out LspCompletionItem source))
        {
            return Array.Empty<Candidate>();
        }

        return new[]
        {
            FromCatalog(
                source,
                "Gpr0.low8",
                "Gpr0.low8",
                "Gpr0.low8",
                "Registros",
                "00_Gpr0.low8",
                source.Metadata?.Category ?? "Registros"),
        };
    }

    private static IReadOnlyList<Candidate> CreatePairOperandCandidates()
    {
        var candidates = new List<Candidate>();
        foreach (GprDescriptor gpr in LanguageCatalog.Gprs)
        {
            if (!LspCatalog.TryGetItem(gpr.Name, out LspCompletionItem source))
            {
                continue;
            }

            candidates.Add(FromCatalog(
                source,
                gpr.Name,
                gpr.Name,
                gpr.Name,
                "Registros",
                "01_" + gpr.Name,
                source.Metadata?.Category ?? "Registros"));
        }

        return candidates;
    }

    private static IReadOnlyList<Candidate> CreateValueOperandCandidates(
        DocumentSnapshot snapshot,
        CompletionAnalysis analysis)
    {
        var candidates = new List<Candidate>();
        int? receiverBits = TryGetReceiverBits(analysis);

        foreach (GprDescriptor gpr in LanguageCatalog.Gprs)
        {
            foreach (RegisterViewDescriptor view in gpr.ValidViews)
            {
                if (receiverBits.HasValue && view.Bits != receiverBits.Value)
                {
                    continue;
                }

                string label = gpr.Name + view.Suffix;
                if (!LspCatalog.TryGetItem(label, out LspCompletionItem source))
                {
                    continue;
                }

                candidates.Add(FromCatalog(
                    source,
                    label,
                    label,
                    label,
                    "Registros",
                    "01_" + label,
                    source.Metadata?.Category ?? "Registros"));
            }
        }

        foreach (RoutineParameter parameter in analysis.Scope.Parameters)
        {
            if (receiverBits.HasValue && parameter.Magnitude.Bits != receiverBits.Value)
            {
                continue;
            }

            candidates.Add(CreateParameterCandidate(parameter));
        }

        foreach (DataSymbol data in CollectDataSymbols(snapshot))
        {
            if (receiverBits.HasValue && data.BitSize != receiverBits.Value)
            {
                continue;
            }

            candidates.Add(CreateDataCandidate(data) with
            {
                InsertText = "[" + data.Name + "]",
            });
        }

        return candidates
            .Where(candidate => IsHigh8Low8Compatible(analysis.ReceiverPath, candidate))
            .GroupBy(candidate => candidate.Label, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    private static bool IsHigh8Low8Compatible(
        ReceiverPath receiver,
        Candidate candidate)
    {
        bool receiverIsHigh8 =
            receiver.Members.Count == 1 &&
            receiver.Members[0] == "high8" &&
            IsFullGprName(receiver.Base);
        bool receiverIsConflictingLow8 =
            receiver.Members.Count == 1 &&
            receiver.Members[0] == "low8" &&
            TryGetGprNumber(receiver.Base) is >= 4 and <= 7;

        if (!receiverIsHigh8 && !receiverIsConflictingLow8)
        {
            return true;
        }

        string[] parts = candidate.Label.Split('.', 2);
        if (parts.Length != 2 ||
            TryGetGprNumber(parts[0]) is not int candidateRegister)
        {
            return true;
        }

        bool candidateIsHigh8 =
            parts[1] == "high8" && candidateRegister is >= 0 and <= 3;
        bool candidateIsConflictingLow8 =
            parts[1] == "low8" && candidateRegister is >= 4 and <= 7;

        return !((receiverIsHigh8 && candidateIsConflictingLow8) ||
            (receiverIsConflictingLow8 && candidateIsHigh8));
    }

    private static int? TryGetGprNumber(string name) =>
        RegisterModel.TryGetGprIndex(name, out int index) &&
        index >= 0 &&
        index < LanguageCatalog.Gprs.Count
            ? index
            : null;

    private static IReadOnlyList<Candidate> CreateExtensionArgumentCandidates(
        DocumentSnapshot snapshot,
        CompletionAnalysis analysis)
    {
        var candidates = new List<Candidate>();
        int? receiverBits = TryGetReceiverBits(analysis);
        if (!receiverBits.HasValue || receiverBits.Value <= 8)
        {
            return candidates;
        }

        foreach (GprDescriptor gpr in LanguageCatalog.Gprs)
        {
            foreach (RegisterViewDescriptor view in gpr.ValidViews)
            {
                // El codificador de Core admite fuentes de 1, 2 o 4 bytes. La
                // comparación con el ancho destino no es una regla del
                // lenguaje: una extensión a 16/32/64 bits puede partir de
                // cualquiera de esos anchos.
                if (view.Bits is not (8 or 16 or 32))
                {
                    continue;
                }

                string label = gpr.Name + view.Suffix;
                if (!LspCatalog.TryGetItem(label, out LspCompletionItem source))
                {
                    continue;
                }

                candidates.Add(FromCatalog(
                    source,
                    label,
                    label,
                    label,
                    "Registros",
                    "01_" + label,
                    source.Metadata?.Category ?? "Registros"));
            }
        }

        foreach (RoutineParameter parameter in analysis.Scope.Parameters)
        {
            if (parameter.Magnitude.Bits is not (8 or 16 or 32))
            {
                continue;
            }

            candidates.Add(CreateParameterCandidate(parameter));
        }

        foreach (DataSymbol data in CollectDataSymbols(snapshot))
        {
            if (data.BitSize is not (8 or 16 or 32))
            {
                continue;
            }

            candidates.Add(CreateDataCandidate(data) with
            {
                InsertText = "[" + data.Name + "]",
            });
        }

        return candidates
            .Where(candidate => IsHigh8Low8Compatible(analysis.ReceiverPath, candidate))
            .ToArray();
    }

    private static IReadOnlyList<Candidate> CreateConditionalCopyArgumentCandidates(
        DocumentSnapshot snapshot,
        CompletionAnalysis analysis)
    {
        var conditions = LanguageCatalog.Flags
            .Select(flag => "Frf." + flag.Name)
            .Concat(LanguageCatalog.FlagAliases.Select(alias => "Frf." + alias.Alias))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(condition => condition, StringComparer.Ordinal)
            .ToArray();

        var candidates = new List<Candidate>();
        foreach (Candidate source in CreateValueOperandCandidates(snapshot, analysis))
        {
            foreach (string condition in conditions)
            {
                candidates.Add(source with
                {
                    Label = source.Label + " if " + condition,
                    Detail = source.Detail + " · condición " + condition,
                    InsertText = source.InsertText + " if " + condition,
                    FilterText = source.FilterText,
                    SortText = source.SortText + "_" + condition,
                    Metadata = source.Metadata with
                    {
                        Description = source.Metadata.Description + " · condición " + condition,
                    },
                });
            }
        }

        return candidates;
    }

    private static IReadOnlyList<Candidate> CreateShiftArgumentCandidates(
        DocumentSnapshot snapshot,
        CompletionAnalysis analysis)
    {
        var candidates = new List<Candidate>(
            CreateMagnitudeCandidates(MagnitudeUnitKind.Bit));

        // El contador variable de x64 es exactamente CL (`Gpr1.low8`).
        if (LspCatalog.TryGetItem("Gpr1.low8", out LspCompletionItem source))
        {
            candidates.Add(FromCatalog(
                source,
                "Gpr1.low8",
                "Gpr1.low8",
                "Gpr1.low8",
                "Registros",
                "90_Gpr1.low8",
                source.Metadata?.Category ?? "Registros"));
        }

        return candidates;
    }

    private static IReadOnlyList<Candidate> CreateBitIndexArgumentCandidates()
    {
        return CreateMagnitudeCandidates(MagnitudeUnitKind.Bit);
    }

    private static int? TryGetReceiverBits(CompletionAnalysis analysis)
    {
        ReceiverPath path = analysis.ReceiverPath;
        if (path.Shape is ReceiverShape.Memory or ReceiverShape.Port or ReceiverShape.Pair)
        {
            return path.Bits;
        }

        string receiver = path.Base;
        if (LanguageCatalog.Gprs.Any(gpr =>
                string.Equals(gpr.Name, receiver, StringComparison.Ordinal)))
        {
            if (path.Members.Count == 0)
            {
                return 64;
            }

            if (path.Members.Count != 1 ||
                !IsValidGprView(receiver, path.Members[0]) ||
                !RegisterModel.TryParseView(path.Members[0], out RegisterView view))
            {
                return null;
            }

            return view switch
            {
                RegisterView.Low32 => 32,
                RegisterView.Low16 => 16,
                RegisterView.Low8 or RegisterView.High8 => 8,
                _ => 64,
            };
        }

        RoutineParameter? parameter = analysis.Scope.Parameters.FirstOrDefault(
            candidate => string.Equals(candidate.Name, receiver, StringComparison.Ordinal));
        return parameter?.Magnitude.Bits is { } bits ? checked((int)bits) : null;
    }

    private static IReadOnlyList<Candidate> CreateStackPushArgumentCandidates(
        DocumentSnapshot snapshot,
        CompletionAnalysis analysis)
    {
        var candidates = new List<Candidate>();
        foreach (GprDescriptor gpr in LanguageCatalog.Gprs)
        {
            foreach (RegisterViewDescriptor view in gpr.ValidViews)
            {
                string label = gpr.Name + view.Suffix;
                if (!LspCatalog.TryGetItem(label, out LspCompletionItem source))
                {
                    continue;
                }

                candidates.Add(FromCatalog(
                    source,
                    label,
                    label,
                    label,
                    "Registros",
                    "01_" + label,
                    source.Metadata?.Category ?? "Registros"));
            }
        }

        foreach (RoutineParameter parameter in analysis.Scope.Parameters)
        {
            candidates.Add(CreateParameterCandidate(parameter));
        }

        if (LspCatalog.TryGetItem("Frf", out LspCompletionItem frf))
        {
            candidates.Add(FromCatalog(
                frf,
                "Frf",
                "Frf",
                "Frf",
                "Registros",
                "02_Frf",
                frf.Metadata?.Category ?? "Registros"));
        }

        return candidates;
    }

    private static IReadOnlyList<Candidate> CreateStackPopArgumentCandidates(
        CompletionAnalysis analysis)
    {
        if (!LspCatalog.TryGetItem("Stack", out LspCompletionItem stack))
        {
            return Array.Empty<Candidate>();
        }

        return new[]
        {
            FromCatalog(
                stack,
                "Stack",
                "Stack",
                "Stack",
                "Registros",
                "00_Stack",
                stack.Metadata?.Category ?? "Registros"),
        };
    }

    private static IReadOnlyList<Candidate> CreateFlagStateArgumentCandidates()
    {
        var candidates = new List<Candidate>
        {
            new(
                "0",
                CompletionItemKind.Value,
                "Estado de bandera: 0",
                MarkupContent.Markdown("Estado apagado de la bandera."),
                "0",
                "0",
                "Literales",
                "00_0",
                new FluentItemMetadata(
                    Category: "Literales",
                    Signature: "0",
                    Description: "Estado apagado de la bandera.",
                    Inputs: "Ninguno",
                    Modifies: "Frf",
                    Returns: "0")),
            new(
                "1",
                CompletionItemKind.Value,
                "Estado de bandera: 1",
                MarkupContent.Markdown("Estado encendido de la bandera."),
                "1",
                "1",
                "Literales",
                "01_1",
                new FluentItemMetadata(
                    Category: "Literales",
                    Signature: "1",
                    Description: "Estado encendido de la bandera.",
                    Inputs: "Ninguno",
                    Modifies: "Frf",
                    Returns: "1")),
        };

        // Core admite el estado `not` para todas las banderas de estado.
        if (LspCatalog.TryGetItem("not", "Contexto", out LspCompletionItem not))
        {
            candidates.Add(FromCatalog(
                not,
                "not",
                "not",
                "not",
                "Literales",
                "02_not",
                not.Metadata?.Category ?? "Literales"));
        }

        return candidates;
    }

    private static IReadOnlyList<Candidate> CreateRoutineIdentifierCandidates(
        DocumentSnapshot snapshot,
        CompletionAnalysis analysis)
    {
        if (!string.Equals(analysis.ContextLabel, "Rutina · id", StringComparison.Ordinal))
        {
            return Array.Empty<Candidate>();
        }

        if (string.Equals(analysis.ContextLabel, "Dato · id", StringComparison.Ordinal))
        {
            // Los nombres nuevos no tienen una entrada fija en el catálogo. No
            // se propone una rutina existente como si fuera una declaración
            // nueva de datos.
            return Array.Empty<Candidate>();
        }

        HashSet<string> existingRoutines = CollectRoutines(snapshot)
            .Select(routine => routine.Name)
            .ToHashSet(StringComparer.Ordinal);
        string? currentName = analysis.Scope.Definition?.Name?.Text;
        if (currentName is not null)
        {
            existingRoutines.Add(currentName);
        }

        return CreateRoutineCandidates(snapshot, analysis, shortFormOnly: false)
            .Where(candidate => !existingRoutines.Contains(candidate.InsertText))
            .ToArray();
    }

    private static IReadOnlyList<Candidate> CreateContextualKeywordCandidates(string keyword)
    {
        if (!LspCatalog.TryGetItem(keyword, out LspCompletionItem source))
        {
            return Array.Empty<Candidate>();
        }

        return new[]
        {
            FromCatalog(
                source,
                keyword,
                keyword,
                keyword,
                "Contexto",
                "00_" + keyword,
                source.Metadata?.Category ?? "Contexto"),
        };
    }

    private static IReadOnlyList<Candidate> CreateReturnRegisterCandidates()
    {
        var candidates = new List<Candidate>();
        foreach (GprDescriptor gpr in LanguageCatalog.Gprs)
        {
            for (int viewIndex = 0; viewIndex < gpr.ValidViews.Length; viewIndex++)
            {
                RegisterViewDescriptor view = gpr.ValidViews[viewIndex];
                string label = gpr.Name + view.Suffix;
                if (!LspCatalog.TryGetItem(label, out LspCompletionItem source))
                {
                    continue;
                }

                candidates.Add(FromCatalog(
                    source,
                    label,
                    label,
                    label,
                    "Registros",
                    viewIndex.ToString("D2") + "_" + label,
                    source.Metadata?.Category ?? "Registros"));
            }
        }

        return candidates;
    }

    private static void AddDirective(
        ICollection<Candidate> candidates,
        string label,
        string directiveName,
        string filter,
        string sortText)
    {
        if (!LspCatalog.TryGetItem(directiveName, out LspCompletionItem source))
        {
            return;
        }

        candidates.Add(FromCatalog(
            source,
            label,
            directiveName,
            filter,
            "Directivas",
            sortText,
            source.Metadata?.Category ?? "Directivas"));
    }

    private static void AddDirectiveStatement(
        ICollection<Candidate> candidates,
        string label,
        string directiveName,
        string filter,
        string sortText)
    {
        if (!LspCatalog.TryGetItem(directiveName, out LspCompletionItem source))
        {
            return;
        }

        candidates.Add(FromCatalog(
            source,
            label,
            label,
            filter,
            "Directivas",
            sortText,
            source.Metadata?.Category ?? "Directivas"));
    }

    private static Candidate CreateDataCandidate(DataSymbol data)
    {
        var metadata = new FluentItemMetadata(
            Category: "Datos",
            Signature: $"{data.Name}: {data.Capacity.Display}",
            Description: "Símbolo declarado en .data.",
            Inputs: "Ninguno",
            Modifies: "Memoria de datos",
            Returns: $"{data.ByteSize} bytes ({data.BitSize} bits)");

        return new Candidate(
            data.Name,
            CompletionItemKind.Field,
            $"Dato: {data.Name} · {data.ByteSize} bytes",
            MarkupContent.Markdown($"Símbolo de datos `{data.Name}` con capacidad {data.Capacity.Display}."),
            data.Name,
            data.Name,
            "Datos",
            "00_" + data.Name,
            metadata);
    }

    private static Candidate CreateParameterCandidate(RoutineParameter parameter)
    {
        var metadata = new FluentItemMetadata(
            Category: "Parámetros",
            Signature: $"{parameter.Name}: {parameter.Magnitude.Display}",
            Description: "Alias de entrada de la rutina actual.",
            Inputs: "Ninguno",
            Modifies: $"Gpr{parameter.RegisterIndex}",
            Returns: parameter.Magnitude.Display);

        return new Candidate(
            parameter.Name,
            CompletionItemKind.Variable,
            $"Parámetro: {parameter.Name} · {parameter.Magnitude.Display}",
            MarkupContent.Markdown($"Parámetro `{parameter.Name}` de la rutina actual."),
            parameter.Name,
            parameter.Name,
            "Parámetros",
            "00_" + parameter.Name,
            metadata);
    }

    private static Candidate FromCatalog(
        LspCompletionItem source,
        string label,
        string insertion,
        string filter,
        string category,
        string sortText,
        string metadataCategory)
    {
        FluentItemMetadata metadata = source.Metadata ?? new FluentItemMetadata(
            Category: metadataCategory,
            Signature: label,
            Description: source.Detail ?? label,
            Inputs: "Ninguno",
            Modifies: "Ninguno",
            Returns: "void");

        return new Candidate(
            label,
            source.Kind,
            source.Detail,
            source.Documentation,
            insertion,
            filter,
            category,
            sortText,
            metadata);
    }

    private static int MatchScore(Candidate candidate, string prefix)
    {
        if (prefix.Length == 0)
        {
            return 0;
        }

        string filter = candidate.FilterText;
        if (string.Equals(filter, prefix, StringComparison.Ordinal))
        {
            return 0;
        }

        return filter.StartsWith(prefix, StringComparison.Ordinal) ? 1 : -1;
    }

    private static IReadOnlyList<LocalRoutine> CollectRoutines(DocumentSnapshot snapshot)
    {
        var routines = new List<LocalRoutine>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (snapshot.SemanticResult is { } semantic)
        {
            foreach (RoutineSymbol routine in semantic.Symbols.Routines)
            {
                if (routine.Name is not { Length: > 0 } name || seen.Contains(name))
                {
                    continue;
                }

                int declaredParameterCount = routine.Definition.Run?.Parameters.Count ?? 0;
                bool validReturn = routine.ReturnsExpression is null || routine.Returns is not null;
                bool validParameters = declaredParameterCount == routine.Parameters.Count;
                if (!validReturn ||
                    !validParameters ||
                    HasErrorOverlapping(snapshot, routine.Definition.Span))
                {
                    continue;
                }

                seen.Add(name);
                routines.Add(new LocalRoutine(
                    name,
                    routine.Parameters.Count,
                    routine.Returns is not null,
                    routine.Parameters
                        .Select(parameter => new LocalRoutineParameter(
                            parameter.RegisterIndex,
                            checked((int)parameter.Magnitude.Bits)))
                        .ToArray(),
                    routine.Returns?.Display));
            }
        }

        if (snapshot.ParseResult.Root.Execute is { } execute)
        {
            foreach (RoutineDefinitionSyntax definition in execute.Routines)
            {
                if (definition.Name is not { } nameToken || seen.Contains(nameToken.Text))
                {
                    continue;
                }

                if (LanguageCatalog.LegacyNames.ContainsKey(nameToken.Text) ||
                    HasErrorOverlapping(snapshot, definition.Span) ||
                    !TryCreateLocalRoutine(definition, out LocalRoutine? routine))
                {
                    continue;
                }

                seen.Add(nameToken.Text);
                routines.Add(routine!);
            }
        }

        return routines;
    }

    private static bool TryCreateLocalRoutine(
        RoutineDefinitionSyntax definition,
        out LocalRoutine? routine)
    {
        routine = null;
        if (definition.Name is not { } nameToken)
        {
            return false;
        }

        var parameters = new List<LocalRoutineParameter>();
        if (definition.Run is { } run)
        {
            if (run.Parameters.Count > RegisterModel.MaxParameters)
            {
                return false;
            }

            for (int index = 0; index < run.Parameters.Count; index++)
            {
                if (run.Parameters[index].Magnitude is not { } magnitudeSyntax ||
                    !MagnitudeInfo.TryCreate(magnitudeSyntax, out MagnitudeInfo magnitude) ||
                    magnitude.Bits > 64)
                {
                    return false;
                }

                int register = RegisterModel.InputRegister(index);
                if (register < 0)
                {
                    return false;
                }

                parameters.Add(new LocalRoutineParameter(
                    register,
                    checked((int)magnitude.Bits)));
            }
        }

        string? returnDisplay = null;
        if (definition.Returns is { } returns)
        {
            if (!RegisterModel.TryResolve(returns, out RegisterReference reference) ||
                !reference.IsGpr ||
                reference.View == RegisterView.Invalid)
            {
                return false;
            }

            returnDisplay = reference.Display;
        }

        routine = new LocalRoutine(
            nameToken.Text,
            parameters.Count,
            returnDisplay is not null,
            parameters,
            returnDisplay);
        return true;
    }

    private static IReadOnlyList<DataSymbol> CollectDataSymbols(DocumentSnapshot snapshot)
    {
        var symbols = new List<DataSymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var definitions = new Dictionary<string, DataDefinitionSyntax>(StringComparer.Ordinal);
        if (snapshot.ParseResult.Root.Data is { } dataSection)
        {
            foreach (DataDefinitionSyntax definition in dataSection.Definitions)
            {
                if (definition.Name is { } name)
                {
                    definitions.TryAdd(name.Text, definition);
                }
            }
        }

        if (snapshot.SemanticResult is { } semantic)
        {
            foreach (DataSymbol symbol in semantic.Symbols.DataSymbols)
            {
                if (seen.Contains(symbol.Name))
                {
                    continue;
                }

                CoreTextSpan span = definitions.TryGetValue(symbol.Name, out DataDefinitionSyntax? definition)
                    ? definition.Span
                    : symbol.Declaration.Span;
                if (HasErrorOverlapping(snapshot, span))
                {
                    continue;
                }

                seen.Add(symbol.Name);
                symbols.Add(symbol);
            }
        }

        if (snapshot.ParseResult.Root.Data is { } data)
        {
            foreach (DataDefinitionSyntax definition in data.Definitions)
            {
                if (definition.Name is not { } nameToken || seen.Contains(nameToken.Text))
                {
                    continue;
                }

                if (HasErrorOverlapping(snapshot, definition.Span) ||
                    !MagnitudeInfo.TryCreate(definition.Capacity, out MagnitudeInfo info))
                {
                    continue;
                }

                seen.Add(nameToken.Text);
                symbols.Add(new DataSymbol(nameToken.Text, info, nameToken));
            }
        }

        return symbols;
    }

    private static bool HasErrorOverlapping(DocumentSnapshot snapshot, CoreTextSpan span)
    {
        foreach (LspDiagnostic diagnostic in snapshot.Diagnostics)
        {
            if (diagnostic.Severity != DiagnosticSeverity.Error)
            {
                continue;
            }

            int diagnosticStart = snapshot.Lines.ToOffset(diagnostic.Range.Start);
            int diagnosticEnd = snapshot.Lines.ToOffset(diagnostic.Range.End);
            // Los spans son semiabiertos: [start, end). Un diagnóstico que
            // termina exactamente en span.Start no se solapa y otro que
            // empieza en span.End tampoco.
            if (diagnosticStart < span.End && diagnosticEnd > span.Start)
            {
                return true;
            }
        }

        return false;
    }

    private static RoutineScope FindRoutineScope(DocumentSnapshot snapshot, int offset)
    {
        RoutineDefinitionSyntax? definition = null;
        if (snapshot.ParseResult.Root.Execute is { } execute)
        {
            foreach (RoutineDefinitionSyntax candidate in execute.Routines)
            {
                // Los spans del AST son semiabiertos. El caret situado justo
                // después de `rt.define(...)` ya pertenece al bloque de
                // rutinas, no al contrato de la rutina anterior.
                bool contains = candidate.Span.Start < offset && offset < candidate.Span.End;
                bool bodyContains = candidate.Run?.Body is { } body &&
                    body.Span.Start < offset && offset < body.Span.End;
                if (!contains && !bodyContains)
                {
                    continue;
                }

                if (definition is null || candidate.Span.Start > definition.Span.Start)
                {
                    definition = candidate;
                }
            }
        }

        if (definition is null)
        {
            return RoutineScope.Empty;
        }

        RoutineSymbol? semanticRoutine = null;
        if (snapshot.SemanticResult is { } semantic)
        {
            semanticRoutine = semantic.Symbols.Routines.FirstOrDefault(routine =>
                routine.Definition.Span.Start == definition.Span.Start &&
                routine.Definition.Span.End == definition.Span.End);
        }

        IReadOnlyList<RoutineParameter> parameters;
        if (semanticRoutine is not null)
        {
            parameters = semanticRoutine.Parameters;
        }
        else
        {
            var parsedParameters = new List<RoutineParameter>();
            if (definition.Run is { } run)
            {
                for (int index = 0; index < run.Parameters.Count; index++)
                {
                    LambdaParameterSyntax parameter = run.Parameters[index];
                    if (parameter.Magnitude is null ||
                        !MagnitudeInfo.TryCreate(parameter.Magnitude, out MagnitudeInfo info) ||
                        info.Bits <= 0 ||
                        info.Bits > 64)
                    {
                        continue;
                    }

                    int register = RegisterModel.InputRegister(index);
                    if (register < 0)
                    {
                        continue;
                    }

                    parsedParameters.Add(new RoutineParameter(
                        parameter.Name.Text,
                        info,
                        register));
                }
            }

            parameters = parsedParameters;
        }

        var labels = new List<LocalLabel>();
        var seenLabels = new HashSet<string>(StringComparer.Ordinal);
        if (semanticRoutine is not null)
        {
            foreach (LabelSymbol label in snapshot.SemanticResult!.Symbols.LabelsOf(semanticRoutine).Values)
            {
                if (seenLabels.Add(label.Name))
                {
                    labels.Add(new LocalLabel(label.Name, label.Declaration.Span.Start));
                }
            }
        }

        foreach (StatementSyntax statement in definition.Body)
        {
            if (statement is LabelStatementSyntax label && seenLabels.Add(label.Label.Text))
            {
                labels.Add(new LocalLabel(label.Label.Text, label.Label.Span.Start));
            }
        }

        bool isInBody = definition.Run?.Body is { } runBody &&
            runBody.Span.Start <= offset && offset < runBody.Span.End;

        return new RoutineScope(definition, semanticRoutine, parameters, labels, isInBody);
    }

    private static bool IsInsideNonCodeLiteral(DocumentSnapshot snapshot, int offset)
    {
        foreach (Token token in snapshot.LexResult.Tokens)
        {
            if (token.Kind == TokenKind.Integer &&
                offset >= token.Span.Start &&
                offset <= token.Span.End)
            {
                return true;
            }
        }

        foreach (CommentTrivia comment in snapshot.LexResult.Comments)
        {
            bool atLineCommentEnd = !comment.IsBlock && offset == comment.Span.End;
            if (offset >= comment.Span.Start &&
                (offset < comment.Span.End || atLineCommentEnd))
            {
                return true;
            }
        }

        foreach (Token token in snapshot.LexResult.Tokens)
        {
            if (token.Kind != TokenKind.String ||
                offset < token.Span.Start ||
                offset > token.Span.End)
            {
                continue;
            }

            // Una cadena terminada ocupa también sus dos límites: insertar
            // antes o después de ella dentro del mismo argumento produce una
            // expresión concatenada inválida.
            return true;
        }

        return false;
    }

    private static PrefixInfo GetPrefix(string text, IReadOnlyList<Token> tokens, int offset)
    {
        foreach (Token token in tokens)
        {
            if (!IsWordToken(token.Kind))
            {
                continue;
            }

            if (token.Span.Start <= offset && offset < token.Span.End)
            {
                string partial = token.Text[..Math.Clamp(offset - token.Span.Start, 0, token.Text.Length)];
                return new PrefixInfo(partial, token.Span.Start, offset);
            }

            if (token.Span.End == offset)
            {
                return new PrefixInfo(token.Text, token.Span.Start, offset);
            }
        }

        if (offset > 0 && text[offset - 1] == '@')
        {
            return new PrefixInfo("@", offset - 1, offset);
        }

        return new PrefixInfo(string.Empty, offset, offset);
    }

    private static bool IsLabelDeclarationPosition(string text, PrefixInfo prefix)
    {
        int lineStart = prefix.StartOffset;
        while (lineStart > 0 && text[lineStart - 1] is not ('\r' or '\n'))
        {
            lineStart--;
        }

        for (int index = lineStart; index < prefix.StartOffset; index++)
        {
            if (!char.IsWhiteSpace(text[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPortBracket(IReadOnlyList<Token> tokens, int openerIndex) =>
        openerIndex > 0 && tokens[openerIndex - 1].Kind == TokenKind.Port;

    private static bool IsWordToken(TokenKind kind) => kind is
        TokenKind.Identifier
        or TokenKind.Label
        or TokenKind.Magnitude
        or TokenKind.DefaultValue
        or TokenKind.If
        or TokenKind.Not
        or TokenKind.And
        or TokenKind.Or
        or TokenKind.Equal
        or TokenKind.Unequal
        or TokenKind.Port;

    private static int GetBraceDepth(IReadOnlyList<Token> tokens, int offset)
    {
        int depth = 0;
        foreach (Token token in tokens)
        {
            if (token.Span.Start >= offset || token.Kind == TokenKind.EndOfFile)
            {
                break;
            }

            if (token.Kind == TokenKind.OpenBrace)
            {
                depth++;
            }
            else if (token.Kind == TokenKind.CloseBrace)
            {
                depth = Math.Max(0, depth - 1);
            }
        }

        return depth;
    }

    private static bool HasColonBetween(
        IReadOnlyList<Token> tokens,
        int openerIndex,
        int offset)
    {
        for (int index = openerIndex + 1; index < tokens.Count; index++)
        {
            Token token = tokens[index];
            if (token.Span.Start >= offset)
            {
                return false;
            }

            if (token.Kind == TokenKind.Colon)
            {
                return true;
            }

            if (token.Kind == TokenKind.CloseAngle)
            {
                return false;
            }
        }

        return false;
    }

    private static MagnitudeUnitKind GetMagnitudeUnitKind(
        IReadOnlyList<Token> tokens,
        int openerIndex)
    {
        if (openerIndex <= 0 || !IsIdentifierToken(tokens[openerIndex - 1].Kind))
        {
            return MagnitudeUnitKind.Any;
        }

        string owner = tokens[openerIndex - 1].Text;
        if (!string.Equals(owner, "define", StringComparison.Ordinal) ||
            openerIndex < 3 ||
            tokens[openerIndex - 2].Kind != TokenKind.Dot ||
            !IsIdentifierToken(tokens[openerIndex - 3].Kind) ||
            !string.Equals(tokens[openerIndex - 3].Text, "dt", StringComparison.Ordinal))
        {
            return MagnitudeUnitKind.Any;
        }

        // La capacidad de un símbolo de `.data` es siempre Byte:N.
        return MagnitudeUnitKind.Byte;
    }

    private static List<DelimiterFrame> BuildDelimiterStack(IReadOnlyList<Token> tokens, int offset)
    {
        var stack = new List<DelimiterFrame>();
        for (int index = 0; index < tokens.Count; index++)
        {
            Token token = tokens[index];
            if (token.Span.Start >= offset || token.Kind == TokenKind.EndOfFile)
            {
                break;
            }

            switch (token.Kind)
            {
                case TokenKind.OpenParen:
                case TokenKind.OpenBracket:
                case TokenKind.OpenBrace:
                case TokenKind.OpenAngle:
                    stack.Add(new DelimiterFrame(token.Kind, index));
                    break;
                case TokenKind.CloseParen:
                case TokenKind.CloseBracket:
                case TokenKind.CloseBrace:
                case TokenKind.CloseAngle:
                    if (stack.Count > 0)
                    {
                        stack.RemoveAt(stack.Count - 1);
                    }
                    break;
            }
        }

        return stack;
    }

    private static int? FindMemberDot(
        string text,
        IReadOnlyList<Token> tokens,
        IReadOnlyList<CommentTrivia> comments,
        PrefixInfo prefix,
        int offset)
    {
        int boundary = prefix.StartOffset;
        for (int index = tokens.Count - 1; index >= 0; index--)
        {
            Token token = tokens[index];
            if (token.Kind != TokenKind.Dot || token.Span.End > boundary)
            {
                continue;
            }

            int gapStart = token.Span.End;
            int gapEnd = Math.Min(boundary, offset);
            if (!IsTriviaOnly(text, gapStart, gapEnd, comments))
            {
                return null;
            }

            return index;
        }

        return null;
    }

    private static bool IsTriviaOnly(
        string text,
        int start,
        int end,
        IReadOnlyList<CommentTrivia> comments)
    {
        for (int position = start; position < end; position++)
        {
            if (char.IsWhiteSpace(text[position]))
            {
                continue;
            }

            bool inComment = comments.Any(comment =>
                comment.Span.Start <= position && position < comment.Span.End);
            if (!inComment)
            {
                return false;
            }
        }

        return true;
    }

    private static ReceiverPath? GetReceiverPath(IReadOnlyList<Token> tokens, int dotIndex) =>
        ParseReceiverPath(tokens, dotIndex, 0);

    private static ReceiverPath? ParseReceiverPath(
        IReadOnlyList<Token> tokens,
        int dotIndex,
        int depth)
    {
        if (depth > 16 || dotIndex <= 0)
        {
            return null;
        }

        int index = dotIndex - 1;
        if (index < 0)
        {
            return null;
        }

        Token token = tokens[index];
        if (IsIdentifierToken(token.Kind))
        {
            if (index > 0 && tokens[index - 1].Kind == TokenKind.Dot)
            {
                ReceiverPath? basePath = ParseReceiverPath(tokens, index - 1, depth + 1);
                return basePath?.Append(token.Text);
            }

            return new ReceiverPath(token.Text, Array.Empty<string>());
        }

        if (token.Kind == TokenKind.CloseBracket)
        {
            return ParseMemoryOrPortReceiver(tokens, index);
        }

        if (token.Kind != TokenKind.CloseParen)
        {
            return null;
        }

        int opener = FindMatchingOpener(tokens, index, TokenKind.OpenParen, TokenKind.CloseParen);
        if (opener < 0)
        {
            return null;
        }

        if (TryParseRegisterPair(tokens, opener, index, out ReceiverPath? pair))
        {
            return pair;
        }

        if (opener <= 0 || !IsIdentifierToken(tokens[opener - 1].Kind))
        {
            return null;
        }

        int methodDot = opener - 2;
        if (methodDot < 0 || tokens[methodDot].Kind != TokenKind.Dot)
        {
            return null;
        }

        ReceiverPath? callBase = ParseReceiverPath(tokens, methodDot, depth + 1);
        return callBase?.Append(tokens[opener - 1].Text);
    }

    private static ReceiverPath? ParseMemoryOrPortReceiver(
        IReadOnlyList<Token> tokens,
        int closerIndex)
    {
        int opener = FindMatchingOpener(
            tokens,
            closerIndex,
            TokenKind.OpenBracket,
            TokenKind.CloseBracket);
        if (opener < 0)
        {
            return null;
        }

        if (opener > 0 && tokens[opener - 1].Kind == TokenKind.Port)
        {
            return TryParsePortAddress(tokens, opener, closerIndex)
                ? new ReceiverPath(
                    "Port",
                    Array.Empty<string>(),
                    ReceiverShape.Port,
                    Bits: 16)
                : null;
        }

        bool hasExplicitCapacity = TryGetMemoryCapacity(
            tokens,
            opener,
            out int? capacityBits);
        string? addressBase = FindMemoryAddressBase(tokens, opener, closerIndex);

        return new ReceiverPath(
            string.Empty,
            Array.Empty<string>(),
            ReceiverShape.Memory,
            capacityBits,
            hasExplicitCapacity,
            addressBase);
    }

    private static bool TryGetMemoryCapacity(
        IReadOnlyList<Token> tokens,
        int openerIndex,
        out int? bits)
    {
        bits = null;
        if (openerIndex < 3 ||
            tokens[openerIndex - 1].Kind != TokenKind.Integer ||
            tokens[openerIndex - 2].Kind != TokenKind.Colon ||
            tokens[openerIndex - 3].Kind != TokenKind.Magnitude)
        {
            return false;
        }

        Token unit = tokens[openerIndex - 3];
        Token size = tokens[openerIndex - 1];
        var span = new CoreTextSpan(
            unit.Span.Start,
            size.Span.End - unit.Span.Start);
        var syntax = new MagnitudeSyntax(unit, size, span);
        if (!MagnitudeInfo.TryCreate(syntax, out MagnitudeInfo info) ||
            !info.IsByte ||
            info.Bits > int.MaxValue)
        {
            return false;
        }

        bits = checked((int)info.Bits);
        return true;
    }

    private static bool TryParsePortAddress(
        IReadOnlyList<Token> tokens,
        int openerIndex,
        int closerIndex)
    {
        int count = closerIndex - openerIndex - 1;
        if (count == 1)
        {
            Token token = tokens[openerIndex + 1];
            return token.Kind == TokenKind.Integer &&
                token.IntegerValue is >= 0 and <= 0xFFFF;
        }

        return count == 3 &&
            tokens[openerIndex + 1].Kind == TokenKind.Identifier &&
            tokens[openerIndex + 1].Text == "Gpr2" &&
            tokens[openerIndex + 2].Kind == TokenKind.Dot &&
            tokens[openerIndex + 3].Kind == TokenKind.Identifier &&
            tokens[openerIndex + 3].Text == "low16";
    }

    private static string? FindMemoryAddressBase(
        IReadOnlyList<Token> tokens,
        int openerIndex,
        int closerIndex)
    {
        for (int index = openerIndex + 1; index < closerIndex; index++)
        {
            Token token = tokens[index];
            if (IsIdentifierToken(token.Kind))
            {
                return token.Text;
            }
        }

        return null;
    }

    private static bool TryParseRegisterPair(
        IReadOnlyList<Token> tokens,
        int openerIndex,
        int closerIndex,
        out ReceiverPath? pair)
    {
        pair = null;
        int depth = 0;
        int colon = -1;
        for (int index = openerIndex + 1; index < closerIndex; index++)
        {
            Token token = tokens[index];
            if (token.Kind == TokenKind.OpenParen)
            {
                depth++;
            }
            else if (token.Kind == TokenKind.CloseParen)
            {
                depth--;
            }
            else if (token.Kind == TokenKind.Colon && depth == 0)
            {
                colon = index;
                break;
            }
        }

        if (colon <= openerIndex || colon + 1 >= closerIndex ||
            !IsIdentifierToken(tokens[colon - 1].Kind) ||
            !IsIdentifierToken(tokens[colon + 1].Kind))
        {
            return false;
        }

        pair = new ReceiverPath(
            tokens[colon - 1].Text,
            new[] { tokens[colon + 1].Text },
            ReceiverShape.Pair,
            Bits: 128);
        return true;
    }

    private static int FindMatchingOpener(
        IReadOnlyList<Token> tokens,
        int closerIndex,
        TokenKind openerKind,
        TokenKind closerKind)
    {
        int depth = 0;
        for (int index = closerIndex; index >= 0; index--)
        {
            Token token = tokens[index];
            if (token.Kind == closerKind)
            {
                depth++;
            }
            else if (token.Kind == openerKind)
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private static bool IsIdentifierToken(TokenKind kind) => kind is
        TokenKind.Identifier
        or TokenKind.Magnitude
        or TokenKind.DefaultValue
        or TokenKind.If
        or TokenKind.Not
        or TokenKind.And
        or TokenKind.Or
        or TokenKind.Equal
        or TokenKind.Unequal
        or TokenKind.Port;

    private static bool IsTopLevelDot(IReadOnlyList<Token> tokens, int dotIndex)
    {
        int parentheses = 0;
        int brackets = 0;
        int braces = 0;
        int angles = 0;

        for (int index = 0; index < dotIndex; index++)
        {
            switch (tokens[index].Kind)
            {
                case TokenKind.OpenParen: parentheses++; break;
                case TokenKind.CloseParen: parentheses--; break;
                case TokenKind.OpenBracket: brackets++; break;
                case TokenKind.CloseBracket: brackets--; break;
                case TokenKind.OpenBrace: braces++; break;
                case TokenKind.CloseBrace: braces--; break;
                case TokenKind.OpenAngle: angles++; break;
                case TokenKind.CloseAngle: angles--; break;
            }
        }

        if (parentheses != 0 || brackets != 0 || braces != 0 || angles != 0)
        {
            return false;
        }

        // El primer constructor sigue al nombre del programa. Los siguientes
        // encadenan después de la llamada cerrada de la sección anterior.
        if (dotIndex > 0 && tokens[dotIndex - 1].Kind == TokenKind.CloseParen)
        {
            return true;
        }

        return dotIndex == 1 && IsIdentifierToken(tokens[0].Kind);
    }

    private static SectionInfo GetSectionInfo(IReadOnlyList<Token> tokens, int offset)
    {
        var section = SectionInfo.None;
        int parentheses = 0;
        int brackets = 0;
        int braces = 0;
        int angles = 0;

        for (int index = 0; index < tokens.Count; index++)
        {
            Token token = tokens[index];
            if (token.Span.Start >= offset || token.Kind == TokenKind.EndOfFile)
            {
                break;
            }

            if (token.Kind == TokenKind.Dot &&
                parentheses == 0 && brackets == 0 && braces == 0 && angles == 0 &&
                index + 1 < tokens.Count && tokens[index + 1].Span.Start < offset)
            {
                string name = tokens[index + 1].Text;
                if (name is "data" or "execute" or "initialize")
                {
                    section = new SectionInfo(
                        name == "data" ? SectionKind.Data :
                        name == "execute" ? SectionKind.Execute : SectionKind.Initialize);
                }
            }

            switch (token.Kind)
            {
                case TokenKind.OpenParen: parentheses++; break;
                case TokenKind.CloseParen: parentheses--; break;
                case TokenKind.OpenBracket: brackets++; break;
                case TokenKind.CloseBracket: brackets--; break;
                case TokenKind.OpenBrace: braces++; break;
                case TokenKind.CloseBrace: braces--; break;
                case TokenKind.OpenAngle: angles++; break;
                case TokenKind.CloseAngle: angles--; break;
            }
        }

        return section;
    }

    private static string? GetMethodName(IReadOnlyList<Token> tokens, int openerIndex)
    {
        int methodIndex = GetMethodNameIndex(tokens, openerIndex);
        return methodIndex >= 0 ? tokens[methodIndex].Text : null;
    }

    private static bool HasFlowContractStart(
        IReadOnlyList<Token> tokens,
        int openerIndex,
        int offset)
    {
        for (int index = openerIndex + 1; index < tokens.Count; index++)
        {
            Token token = tokens[index];
            if (token.Span.Start >= offset)
            {
                return false;
            }

            if (token.Kind == TokenKind.Arrow &&
                index > openerIndex + 1 &&
                IsIdentifierToken(tokens[index - 1].Kind) &&
                string.Equals(tokens[index - 1].Text, "c", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static ReceiverPath? GetMethodReceiver(IReadOnlyList<Token> tokens, int openerIndex)
    {
        int methodIndex = GetMethodNameIndex(tokens, openerIndex);
        if (methodIndex < 2 ||
            tokens[methodIndex - 1].Kind != TokenKind.Dot)
        {
            return null;
        }

        // En una cadena fluida el método puede seguir a otra llamada:
        // `c.target(sumar).inputs(...)`. El parser de receptor conserva la
        // secuencia `c.target` sin confundir el argumento `sumar` con un
        // miembro adicional.
        return GetReceiverPath(tokens, methodIndex - 1);
    }

    private static int GetMethodNameIndex(IReadOnlyList<Token> tokens, int openerIndex)
    {
        if (openerIndex <= 0)
        {
            return -1;
        }

        int methodIndex = openerIndex - 1;
        if (IsIdentifierToken(tokens[methodIndex].Kind))
        {
            return methodIndex;
        }

        // Los constructores de datos aceptan una magnitud genérica:
        // `dt.define<Byte:8>(...)`.
        if (tokens[methodIndex].Kind != TokenKind.CloseAngle)
        {
            return -1;
        }

        int angleOpener = FindMatchingOpener(
            tokens,
            methodIndex,
            TokenKind.OpenAngle,
            TokenKind.CloseAngle);
        if (angleOpener <= 0 ||
            !IsIdentifierToken(tokens[angleOpener - 1].Kind))
        {
            return -1;
        }

        return angleOpener - 1;
    }

    private static bool TryGetReceiverShape(
        ReceiverPath path,
        RoutineScope scope,
        out ReceiverShape shape)
    {
        if (path.Shape is ReceiverShape.Memory or ReceiverShape.Port)
        {
            shape = path.Shape;
            return true;
        }

        if (path.Shape == ReceiverShape.Pair)
        {
            shape = ReceiverShape.Pair;
            return IsCanonicalPair(path);
        }

        string receiver = path.Base;
        if (LanguageCatalog.Gprs.Any(gpr =>
                string.Equals(gpr.Name, receiver, StringComparison.Ordinal)))
        {
            shape = ReceiverShape.Register;
            return path.Members.Count == 0 ||
                (path.Members.Count == 1 &&
                    IsValidGprView(receiver, path.Members[0]));
        }

        if (scope.Parameters.Any(parameter =>
                string.Equals(parameter.Name, receiver, StringComparison.Ordinal)))
        {
            shape = ReceiverShape.Register;
            return path.Members.Count == 0;
        }

        switch (receiver)
        {
            case "Frf":
                shape = ReceiverShape.Frf;
                return path.Members.Count == 0;
            case "Ipr":
                shape = ReceiverShape.Ipr;
                return path.Members.Count == 0;
            case "Stack":
                shape = ReceiverShape.Stack;
                return path.Members.Count == 0;
            case "Cpu":
                shape = ReceiverShape.Cpu;
                return path.Members.Count == 0;
            default:
                shape = ReceiverShape.None;
                return false;
        }
    }

    private static bool IsFullGprName(string name) =>
        RegisterModel.TryGetGprIndex(name, out int index) &&
        index >= 0 &&
        index < LanguageCatalog.Gprs.Count;

    private static bool IsCanonicalPair(ReceiverPath path) =>
        path.Members.Count == 1 &&
        IsFullGprName(path.Base) &&
        IsFullGprName(path.Members[0]) &&
        ((path.Base == "Gpr2" && path.Members[0] == "Gpr0") ||
            (path.Base == "Gpr0" && path.Members[0] == "Gpr2"));

    private static bool IsPairVerbAllowed(ReceiverPath path, string method)
    {
        if (path.Members.Count != 1)
        {
            return false;
        }

        return method switch
        {
            "multiplyWide" or "signExtend" =>
                path.Base == "Gpr2" && path.Members[0] == "Gpr0",
            "divide" or "divideSigned" =>
                path.Base == "Gpr0" && path.Members[0] == "Gpr2",
            _ => false,
        };
    }

    private static string? FindFlowTargetName(IReadOnlyList<Token> tokens, int beforeIndex)
    {
        int limit = Math.Min(beforeIndex - 1, tokens.Count - 1);
        int chainStart = -1;
        for (int index = limit; index >= 0; index--)
        {
            if (tokens[index].Kind == TokenKind.Arrow)
            {
                chainStart = index + 1;
                break;
            }
        }

        if (chainStart < 0 ||
            chainStart >= tokens.Count ||
            !IsIdentifierToken(tokens[chainStart].Kind) ||
            !string.Equals(tokens[chainStart].Text, "c", StringComparison.Ordinal))
        {
            return null;
        }

        for (int index = chainStart; index <= limit; index++)
        {
            Token token = tokens[index];
            if (!IsIdentifierToken(token.Kind) ||
                !string.Equals(token.Text, "target", StringComparison.Ordinal) ||
                index == 0 ||
                tokens[index - 1].Kind != TokenKind.Dot)
            {
                continue;
            }

            int argument = index + 1;
            if (argument < tokens.Count && tokens[argument].Kind == TokenKind.OpenParen)
            {
                argument++;
            }

            return argument < tokens.Count && IsIdentifierToken(tokens[argument].Kind)
                ? tokens[argument].Text
                : null;
        }

        return null;
    }

    private sealed record Candidate(
        string Label,
        CompletionItemKind Kind,
        string? Detail,
        MarkupContent? Documentation,
        string InsertText,
        string FilterText,
        string Category,
        string SortText,
        FluentItemMetadata Metadata);

    private sealed record PrefixInfo(string Text, int StartOffset, int EndOffset);

    private sealed record ReceiverPath(
        string Base,
        IReadOnlyList<string> Members,
        ReceiverShape Shape = ReceiverShape.None,
        int? Bits = null,
        bool HasExplicitCapacity = false,
        string? AddressBase = null)
    {
        public static ReceiverPath Empty { get; } = new(
            string.Empty,
            Array.Empty<string>());

        public ReceiverPath Append(string member) =>
            new(
                Base,
                Members.Append(member).ToArray(),
                Shape,
                Bits,
                HasExplicitCapacity,
                AddressBase);
    }

    private sealed record DelimiterFrame(TokenKind Kind, int TokenIndex);

    private sealed record LocalRoutine(
        string Name,
        int ParameterCount,
        bool HasReturn,
        IReadOnlyList<LocalRoutineParameter> Parameters,
        string? ReturnDisplay);

    private sealed record LocalRoutineParameter(int RegisterIndex, int Bits);

    private sealed record LocalLabel(string Name, int StartOffset);

    private sealed record SectionInfo(SectionKind Kind)
    {
        public static SectionInfo None { get; } = new(SectionKind.None);
    }

    private enum SectionKind
    {
        None,
        Data,
        Execute,
        Initialize,
    }

    private enum MagnitudeUnitKind
    {
        Any,
        Bit,
        Byte,
    }

    private sealed class RoutineScope
    {
        public static RoutineScope Empty { get; } = new(
            null,
            null,
            Array.Empty<RoutineParameter>(),
            Array.Empty<LocalLabel>(),
            false);

        public RoutineScope(
            RoutineDefinitionSyntax? definition,
            RoutineSymbol? semanticRoutine,
            IReadOnlyList<RoutineParameter> parameters,
            IReadOnlyList<LocalLabel> labels,
            bool isInBody)
        {
            Definition = definition;
            SemanticRoutine = semanticRoutine;
            Parameters = parameters;
            Labels = labels;
            IsInBody = isInBody;
        }

        public RoutineDefinitionSyntax? Definition { get; }
        public RoutineSymbol? SemanticRoutine { get; }
        public IReadOnlyList<RoutineParameter> Parameters { get; }
        public IReadOnlyList<LocalLabel> Labels { get; }
        public bool IsInBody { get; }
    }

    private enum CompletionKind
    {
        None,
        Root,
        Data,
        Execute,
        RoutinesBlock,
        RoutineDescriptor,
        RoutineBody,
        Member,
        Label,
        Memory,
        PortAddress,
        Magnitude,
        FlowCall,
        FlowContract,
        FlowTarget,
        FlowTargetLabel,
        FlowInputs,
        FlowOutput,
        Argument,
    }

    private sealed record CompletionAnalysis
    {
        private CompletionAnalysis(
            CompletionKind kind,
            PrefixInfo prefix,
            RoutineScope scope,
            SectionInfo section,
            string contextLabel,
            string receiver,
            string receiverKind,
            string method,
            bool hasLeadingDot,
            ReceiverPath? receiverPath = null,
            string? targetName = null,
            MagnitudeUnitKind magnitudeKind = MagnitudeUnitKind.Any,
            bool hasExplicitCapacity = false,
            bool isLabelDeclaration = false)
        {
            Kind = kind;
            Prefix = prefix;
            Scope = scope;
            Section = section;
            ContextLabel = contextLabel;
            Receiver = receiver;
            ReceiverKind = receiverKind;
            Method = method;
            HasLeadingDot = hasLeadingDot;
            ReceiverPath = receiverPath ?? ReceiverPath.Empty;
            TargetName = targetName ?? string.Empty;
            MagnitudeKind = magnitudeKind;
            HasExplicitCapacity = hasExplicitCapacity;
            IsLabelDeclaration = isLabelDeclaration;
        }

        public CompletionKind Kind { get; }
        public PrefixInfo Prefix { get; }
        public RoutineScope Scope { get; }
        public SectionInfo Section { get; }
        public string ContextLabel { get; }
        public string Receiver { get; }
        public string ReceiverKind { get; }
        public string Method { get; }
        public bool HasLeadingDot { get; }
        public ReceiverPath ReceiverPath { get; }
        public string TargetName { get; }
        public MagnitudeUnitKind MagnitudeKind { get; }
        public bool HasExplicitCapacity { get; }
        public bool IsLabelDeclaration { get; }
        public string PrefixText => Prefix.Text;
        public int ReplaceStart => Prefix.StartOffset;
        public int ReplaceEnd => Prefix.EndOffset;

        public static CompletionAnalysis None { get; } = new(
            CompletionKind.None,
            new PrefixInfo(string.Empty, 0, 0),
            RoutineScope.Empty,
            SectionInfo.None,
            "Sin sugerencias",
            string.Empty,
            string.Empty,
            string.Empty,
            false);

        public static CompletionAnalysis Root(
            PrefixInfo prefix,
            RoutineScope scope,
            SectionInfo section,
            bool hasLeadingDot,
            string contextLabel) => new(
                CompletionKind.Root,
                prefix,
                scope,
                section,
                contextLabel,
                string.Empty,
                string.Empty,
                string.Empty,
                hasLeadingDot);

        public static CompletionAnalysis Data(
            PrefixInfo prefix,
            RoutineScope scope,
            string contextLabel) => new(
                CompletionKind.Data,
                prefix,
                scope,
                SectionInfo.None,
                contextLabel,
                string.Empty,
                string.Empty,
                string.Empty,
                false);

        public static CompletionAnalysis Execute(
            PrefixInfo prefix,
            RoutineScope scope,
            string contextLabel) => new(
                CompletionKind.Execute,
                prefix,
                scope,
                SectionInfo.None,
                contextLabel,
                string.Empty,
                string.Empty,
                string.Empty,
                false);

        public static CompletionAnalysis RoutinesBlock(
            PrefixInfo prefix,
            RoutineScope scope,
            string contextLabel) => new(
                CompletionKind.RoutinesBlock,
                prefix,
                scope,
                SectionInfo.None,
                contextLabel,
                string.Empty,
                string.Empty,
                string.Empty,
                false);

        public static CompletionAnalysis RoutineDescriptor(
            PrefixInfo prefix,
            RoutineScope scope,
            string contextLabel) => new(
                CompletionKind.RoutineDescriptor,
                prefix,
                scope,
                SectionInfo.None,
                contextLabel,
                string.Empty,
                string.Empty,
                string.Empty,
                false);

        public static CompletionAnalysis RoutineBody(
            PrefixInfo prefix,
            RoutineScope scope,
            string contextLabel) => new(
                CompletionKind.RoutineBody,
                prefix,
                scope,
                SectionInfo.None,
                contextLabel,
                string.Empty,
                string.Empty,
                string.Empty,
                false);

        public static CompletionAnalysis Member(
            PrefixInfo prefix,
            RoutineScope scope,
            string receiver,
            string receiverKind,
            string contextLabel,
            SectionInfo section,
            ReceiverPath? receiverPath = null,
            string? targetName = null) => new(
                CompletionKind.Member,
                prefix,
                scope,
                section,
                contextLabel,
                receiver,
                receiverKind,
                string.Empty,
                false,
                receiverPath,
                targetName);

        public static CompletionAnalysis Label(
            PrefixInfo prefix,
            RoutineScope scope,
            string contextLabel,
            bool isDeclaration = false) => new(
                CompletionKind.Label,
                prefix,
                scope,
                SectionInfo.None,
                contextLabel,
                string.Empty,
                string.Empty,
                string.Empty,
                false,
                isLabelDeclaration: isDeclaration);

        public static CompletionAnalysis Memory(
            PrefixInfo prefix,
            RoutineScope scope,
            string contextLabel,
            bool hasExplicitCapacity = false) => new(
                CompletionKind.Memory,
                prefix,
                scope,
                SectionInfo.None,
                contextLabel,
                string.Empty,
                string.Empty,
                string.Empty,
                false,
                hasExplicitCapacity: hasExplicitCapacity);

        public static CompletionAnalysis PortAddress(
            PrefixInfo prefix,
            RoutineScope scope,
            string contextLabel) => new(
                CompletionKind.PortAddress,
                prefix,
                scope,
                SectionInfo.None,
                contextLabel,
                string.Empty,
                string.Empty,
                string.Empty,
                false);

        public static CompletionAnalysis Magnitude(
            PrefixInfo prefix,
            RoutineScope scope,
            string contextLabel,
            MagnitudeUnitKind magnitudeKind = MagnitudeUnitKind.Any) => new(
                CompletionKind.Magnitude,
                prefix,
                scope,
                SectionInfo.None,
                contextLabel,
                string.Empty,
                string.Empty,
                string.Empty,
                false,
                magnitudeKind: magnitudeKind);

        public static CompletionAnalysis FlowCall(
            PrefixInfo prefix,
            RoutineScope scope,
            string contextLabel) => new(
                CompletionKind.FlowCall,
                prefix,
                scope,
                SectionInfo.None,
                contextLabel,
                string.Empty,
                string.Empty,
                string.Empty,
                false);

        public static CompletionAnalysis FlowContract(
            PrefixInfo prefix,
            RoutineScope scope,
            string contextLabel) => new(
                CompletionKind.FlowContract,
                prefix,
                scope,
                SectionInfo.None,
                contextLabel,
                string.Empty,
                string.Empty,
                string.Empty,
                false);

        public static CompletionAnalysis FlowTarget(
            PrefixInfo prefix,
            RoutineScope scope,
            string contextLabel) => new(
                CompletionKind.FlowTarget,
                prefix,
                scope,
                SectionInfo.None,
                contextLabel,
                string.Empty,
                string.Empty,
                string.Empty,
                false);

        public static CompletionAnalysis FlowTargetLabel(
            PrefixInfo prefix,
            RoutineScope scope,
            string contextLabel) => new(
                CompletionKind.FlowTargetLabel,
                prefix,
                scope,
                SectionInfo.None,
                contextLabel,
                string.Empty,
                string.Empty,
                string.Empty,
                false);

        public static CompletionAnalysis FlowInputs(
            PrefixInfo prefix,
            RoutineScope scope,
            string contextLabel,
            string? targetName) => new(
                CompletionKind.FlowInputs,
                prefix,
                scope,
                SectionInfo.None,
                contextLabel,
                targetName ?? string.Empty,
                string.Empty,
                string.Empty,
                false);

        public static CompletionAnalysis FlowOutput(
            PrefixInfo prefix,
            RoutineScope scope,
            string contextLabel,
            string? targetName) => new(
                CompletionKind.FlowOutput,
                prefix,
                scope,
                SectionInfo.None,
                contextLabel,
                targetName ?? string.Empty,
                string.Empty,
                string.Empty,
                false);

        public static CompletionAnalysis Argument(
            PrefixInfo prefix,
            RoutineScope scope,
            string receiver,
            string method,
            string contextLabel,
            ReceiverPath? receiverPath = null) => new(
                CompletionKind.Argument,
                prefix,
                scope,
                SectionInfo.None,
                contextLabel,
                receiver,
                string.Empty,
                method,
                false,
                receiverPath);
    }
}
