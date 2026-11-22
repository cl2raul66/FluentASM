using FluentASM.Core.Diagnostics;
using FluentASM.Core.Syntax.Ast;

namespace FluentASM.Core.Syntax;

/// <summary>
/// Analizador sintáctico descendente recursivo de FluentASM (Fase 1).
/// </summary>
/// <remarks>
/// <para>
/// Consume los tokens del <see cref="Lexer"/> y construye un
/// <see cref="CompilationUnitSyntax"/> siguiendo el flujo <em>type-state</em>
/// <c>[.data(...)] .execute(...) .initialize()</c> de <c>FLUENT_SYNTAX.md</c>.
/// </para>
/// <para>
/// La recuperación es local: ante una construcción inválida se emite un
/// diagnóstico <c>FASM11xx</c>, se conserva un nodo parcial y se continúa con la
/// siguiente sentencia o sección. Los errores de sintaxis nunca lanzan excepciones.
/// </para>
/// </remarks>
public sealed class Parser
{
    private readonly IReadOnlyList<Token> tokens;
    private readonly DiagnosticBag diagnostics;
    private int index;
    private int previousEnd;

    private Parser(IReadOnlyList<Token> tokens, DiagnosticBag diagnostics)
    {
        this.tokens = tokens;
        this.diagnostics = diagnostics;
    }

    /// <summary>
    /// Analiza una secuencia de tokens terminada en <see cref="TokenKind.EndOfFile"/>.
    /// </summary>
    /// <param name="tokens">Tokens producidos por el lexer.</param>
    /// <param name="diagnostics">Bolsa compartida opcional; si se omite se crea una nueva.</param>
    public static ParseResult Parse(IReadOnlyList<Token> tokens, DiagnosticBag? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        var bag = diagnostics ?? new DiagnosticBag();
        if (tokens.Count == 0)
        {
            var empty = new CompilationUnitSyntax(null, null, null, null, new TextSpan(0, 0));
            bag.Error(
                DiagnosticIds.SyntaxUnexpectedToken,
                "El archivo fuente no contiene tokens.",
                null,
                new TextSpan(0, 0));
            return new ParseResult(empty, bag);
        }

        var parser = new Parser(tokens, bag);
        return new ParseResult(parser.ParseCompilationUnit(), bag);
    }

    /// <summary>
    /// Analiza un resultado léxico y acumula los diagnósticos sintácticos en la misma bolsa.
    /// </summary>
    public static ParseResult Parse(LexResult lexResult)
    {
        ArgumentNullException.ThrowIfNull(lexResult);
        return Parse(lexResult.Tokens, lexResult.Diagnostics);
    }

    private Token Current => tokens[Math.Min(index, tokens.Count - 1)];

    private bool IsAtEnd => Current.Kind == TokenKind.EndOfFile;

    private Token Peek(int offset) => tokens[Math.Clamp(index + offset, 0, tokens.Count - 1)];

    private bool Check(TokenKind kind) => Current.Kind == kind;

    private Token Advance()
    {
        var token = Current;
        if (!IsAtEnd)
        {
            index++;
        }

        previousEnd = token.Span.End;
        return token;
    }

    private bool Match(TokenKind kind)
    {
        if (!Check(kind))
        {
            return false;
        }

        Advance();
        return true;
    }

    private Token Expect(TokenKind kind, string expectation)
    {
        if (Check(kind))
        {
            return Advance();
        }

        Error(
            DiagnosticIds.SyntaxUnexpectedToken,
            $"Se esperaba {expectation} y se encontró {Describe(Current)}.",
            Current);
        return Missing(kind, Current);
    }

    private Token ExpectIdentifier(string expectation)
    {
        if (IsIdentifierLike(Current.Kind))
        {
            return Advance();
        }

        Error(
            DiagnosticIds.SyntaxUnexpectedToken,
            $"Se esperaba {expectation} y se encontró {Describe(Current)}.",
            Current);
        return Missing(TokenKind.Identifier, Current);
    }

    private TextSpan SpanFrom(int start) => new(start, Math.Max(previousEnd, start) - start);

    private static TextSpan SpanBetween(TextSpan first, TextSpan second) =>
        new(first.Start, Math.Max(second.End, first.Start) - first.Start);

    private void Error(string id, string message, Token token) =>
        diagnostics.Error(id, message, token.Location, token.Span);

    private void Error(string id, string message, TextSpan span) =>
        diagnostics.Error(id, message, null, span);

    private static Token Missing(TokenKind kind, Token anchor) => new()
    {
        Kind = kind,
        Text = string.Empty,
        Span = new TextSpan(anchor.Span.Start, 0),
        Location = anchor.Location,
    };

    private static string Describe(Token token) =>
        token.Kind == TokenKind.EndOfFile ? "el final del archivo" : $"'{token.Text}'";

    private static bool IsIdentifierLike(TokenKind kind) => kind is
        TokenKind.Identifier
        or TokenKind.DefaultValue
        or TokenKind.If
        or TokenKind.Not
        or TokenKind.And
        or TokenKind.Or
        or TokenKind.Equal
        or TokenKind.Unequal
        or TokenKind.Port;

    private static bool IsParameterToken(Token token) =>
        token.Kind == TokenKind.Identifier || token.Kind == TokenKind.Underscore;

    private static bool StartsPostfixArguments(TokenKind kind) =>
        kind is TokenKind.OpenParen or TokenKind.OpenAngle;

    private static bool IsClosingToken(Token token) => token.Kind is
        TokenKind.CloseParen
        or TokenKind.CloseBracket
        or TokenKind.CloseBrace
        or TokenKind.EndOfFile;

    private static bool CanStartReceiver(Token token) => token.Kind is
        TokenKind.Identifier
        or TokenKind.Underscore
        or TokenKind.OpenParen
        or TokenKind.OpenBracket
        or TokenKind.Magnitude
        or TokenKind.Port;

    private CompilationUnitSyntax ParseCompilationUnit()
    {
        var start = Current.Span.Start;
        Token? projectName = null;

        if (Check(TokenKind.Identifier))
        {
            projectName = Advance();
        }
        else
        {
            Error(
                DiagnosticIds.SyntaxUnexpectedToken,
                $"Se esperaba el nombre del proyecto y se encontró {Describe(Current)}.",
                Current);
        }

        DataSectionSyntax? data = null;
        ExecuteSectionSyntax? execute = null;
        Token? initialize = null;
        var seenInitialize = false;

        while (!IsAtEnd)
        {
            if (!Check(TokenKind.Dot))
            {
                Error(
                    DiagnosticIds.SyntaxUnexpectedToken,
                    $"Se esperaba una sección fluida iniciada por '.' y se encontró {Describe(Current)}.",
                    Current);
                SynchronizeSection();
                continue;
            }

            var sectionName = Peek(1);
            if (sectionName.Kind != TokenKind.Identifier)
            {
                Error(
                    DiagnosticIds.SyntaxIncompleteTypeState,
                    $"Se esperaba el nombre de una sección después de '.' y se encontró {Describe(sectionName)}.",
                    sectionName);
                Advance();
                continue;
            }

            if (seenInitialize)
            {
                Error(
                    DiagnosticIds.SyntaxIncompleteTypeState,
                    "Después de .initialize() no es válido encadenar más secciones.",
                    Current);
                SkipFluidCall();
                continue;
            }

            switch (sectionName.Text)
            {
                case "data":
                    if (data is not null)
                    {
                        Error(
                            DiagnosticIds.SyntaxIncompleteTypeState,
                            "La sección .data(...) solo puede declararse una vez.",
                            sectionName);
                    }
                    else if (execute is not null)
                    {
                        Error(
                            DiagnosticIds.SyntaxIncompleteTypeState,
                            "La sección .data(...) debe aparecer antes de .execute(...).",
                            sectionName);
                    }

                    var parsedData = ParseDataSection();
                    data ??= parsedData;
                    break;

                case "execute":
                    if (execute is not null)
                    {
                        Error(
                            DiagnosticIds.SyntaxIncompleteTypeState,
                            "La sección .execute(...) solo puede declararse una vez.",
                            sectionName);
                    }

                    var parsedExecute = ParseExecuteSection();
                    execute ??= parsedExecute;
                    break;

                case "initialize":
                    initialize = ParseInitialize();
                    seenInitialize = true;
                    break;

                default:
                    Error(
                        DiagnosticIds.SyntaxIncompleteTypeState,
                        $"Sección desconocida '.{sectionName.Text}'; se esperaba .data, .execute o .initialize.",
                        sectionName);
                    SkipFluidCall();
                    break;
            }
        }

        if (execute is null)
        {
            Error(
                DiagnosticIds.SyntaxIncompleteTypeState,
                "Falta la sección obligatoria .execute(...).",
                SpanFrom(start));
        }

        if (!seenInitialize)
        {
            Error(
                DiagnosticIds.SyntaxIncompleteTypeState,
                "Falta el cierre obligatorio .initialize() después de .execute(...).",
                SpanFrom(start));
        }

        return new CompilationUnitSyntax(projectName, data, execute, initialize, SpanFrom(start));
    }

    private void SynchronizeSection()
    {
        while (!IsAtEnd && !Check(TokenKind.Dot))
        {
            Advance();
        }
    }

    private void SkipFluidCall()
    {
        Match(TokenKind.Dot);
        if (Check(TokenKind.Identifier))
        {
            Advance();
        }

        if (!Check(TokenKind.OpenParen))
        {
            return;
        }

        var depth = 0;
        while (!IsAtEnd)
        {
            if (Check(TokenKind.OpenParen))
            {
                depth++;
            }
            else if (Check(TokenKind.CloseParen))
            {
                depth--;
                Advance();
                if (depth == 0)
                {
                    return;
                }

                continue;
            }

            Advance();
        }
    }

    private DataSectionSyntax ParseDataSection()
    {
        var start = Current.Span.Start;
        Advance(); // '.'
        Advance(); // 'data'

        var content = ParseSectionLambda("data");
        var definitions = new List<DataDefinitionSyntax>();

        if (content?.Body is BlockBodySyntax block)
        {
            foreach (var statement in block.Statements)
            {
                if (statement is DataDefinitionStatementSyntax definition)
                {
                    definitions.Add(definition.Definition);
                }
                else
                {
                    Error(
                        DiagnosticIds.SyntaxInvalidDataDefinition,
                        "En .data(...) solo se admiten definiciones dt.define<Magnitud>(d -> ...).",
                        statement.Span);
                }
            }
        }
        else if (content is not null)
        {
            Error(
                DiagnosticIds.SyntaxInvalidDataDefinition,
                "La sección .data(...) debe contener un bloque '{ ... }'.",
                content.Span);
        }

        return new DataSectionSyntax(content, definitions, SpanFrom(start));
    }

    private ExecuteSectionSyntax ParseExecuteSection()
    {
        var start = Current.Span.Start;
        Advance(); // '.'
        Advance(); // 'execute'

        var content = ParseSectionLambda("execute");
        var routines = new List<RoutineDefinitionSyntax>();

        if (content?.Body is BlockBodySyntax block)
        {
            foreach (var statement in block.Statements)
            {
                if (statement is not ActionStatementSyntax action)
                {
                    Error(
                        DiagnosticIds.SyntaxInvalidRoutine,
                        "En .execute(...) solo se admite ex.routines(rt -> { ... }).",
                        statement.Span);
                    continue;
                }

                if (action.Verb.Text != "routines")
                {
                    Error(
                        DiagnosticIds.SyntaxInvalidRoutine,
                        $"En .execute(...) se esperaba 'routines' y se encontró '{action.Verb.Text}'.",
                        action.Verb);
                    continue;
                }

                if (action.Arguments.Count == 0
                    || action.Arguments[0] is not LambdaExpressionSyntax routinesLambda
                    || routinesLambda.Body is not BlockBodySyntax routinesBlock)
                {
                    Error(
                        DiagnosticIds.SyntaxInvalidRoutine,
                        "ex.routines(...) requiere una lambda 'rt -> { ... }' o '_ -> { ... }'.",
                        action.Verb);
                    continue;
                }

                if (routinesLambda.Parameters.Count == 1
                    && routinesLambda.Parameters[0].Name.Kind == TokenKind.Underscore)
                {
                    routines.Add(new RoutineDefinitionSyntax(
                        null,
                        null,
                        routinesLambda,
                        routinesBlock.Statements,
                        true,
                        routinesLambda.Span));
                    continue;
                }

                foreach (var routineStatement in routinesBlock.Statements)
                {
                    if (routineStatement is ActionStatementSyntax define && define.Verb.Text == "define")
                    {
                        routines.Add(BuildRoutineDefinition(define));
                    }
                    else
                    {
                        Error(
                            DiagnosticIds.SyntaxInvalidRoutine,
                            "En ex.routines(...) solo se admite rt.define(d -> { ... }).",
                            routineStatement.Span);
                    }
                }
            }
        }
        else if (content is not null)
        {
            Error(
                DiagnosticIds.SyntaxInvalidRoutine,
                "La sección .execute(...) debe contener un bloque '{ ... }'.",
                content.Span);
        }

        return new ExecuteSectionSyntax(content, routines, SpanFrom(start));
    }

    private Token ParseInitialize()
    {
        Advance(); // '.'
        var initialize = Advance(); // 'initialize'
        Expect(TokenKind.OpenParen, "'('");
        Expect(TokenKind.CloseParen, "')'");
        return initialize;
    }

    private LambdaExpressionSyntax? ParseSectionLambda(string sectionName)
    {
        Expect(TokenKind.OpenParen, "'('");
        if (!IsLambdaStart())
        {
            Error(
                DiagnosticIds.SyntaxIncompleteTypeState,
                $"La sección .{sectionName}(...) debe recibir una lambda como 'param -> {{ ... }}'.",
                Current);
            return null;
        }

        var lambda = ParseLambdaExpression();
        Expect(TokenKind.CloseParen, "')'");
        return lambda;
    }

    private bool IsLambdaStart()
    {
        if (IsParameterToken(Current) && Peek(1).Kind == TokenKind.Arrow)
        {
            return true;
        }

        if (!Check(TokenKind.OpenParen))
        {
            return false;
        }

        var depth = 0;
        for (var offset = 0; ; offset++)
        {
            var token = Peek(offset);
            if (token.Kind == TokenKind.EndOfFile)
            {
                return false;
            }

            if (token.Kind == TokenKind.OpenParen)
            {
                depth++;
            }
            else if (token.Kind == TokenKind.CloseParen)
            {
                depth--;
                if (depth == 0)
                {
                    return Peek(offset + 1).Kind == TokenKind.Arrow;
                }
            }
        }
    }

    private LambdaExpressionSyntax ParseLambdaExpression()
    {
        var start = Current.Span.Start;
        var parameters = ParseLambdaParameters();
        Expect(TokenKind.Arrow, "'->'");

        LambdaBodySyntax body = Check(TokenKind.OpenBrace)
            ? ParseBlockBody()
            : ParseFluentChainBody();

        return new LambdaExpressionSyntax(parameters, body, SpanFrom(start));
    }

    private IReadOnlyList<LambdaParameterSyntax> ParseLambdaParameters()
    {
        var parameters = new List<LambdaParameterSyntax>();
        if (IsParameterToken(Current))
        {
            var name = Advance();
            parameters.Add(new LambdaParameterSyntax(name, null, name.Span));
            return parameters;
        }

        Expect(TokenKind.OpenParen, "'('");
        if (Match(TokenKind.CloseParen))
        {
            return parameters;
        }

        while (!IsAtEnd && !Check(TokenKind.CloseParen))
        {
            var before = index;
            var start = Current.Span.Start;

            if (!IsParameterToken(Current))
            {
                Error(
                    DiagnosticIds.SyntaxInvalidSignature,
                    $"Se esperaba el nombre de un parámetro y se encontró {Describe(Current)}.",
                    Current);
                if (!IsAtEnd)
                {
                    Advance();
                }
            }
            else
            {
                var name = Advance();
                MagnitudeSyntax? magnitude = null;
                if (Match(TokenKind.OpenAngle))
                {
                    magnitude = ParseMagnitude();
                    Expect(TokenKind.CloseAngle, "'>'");
                }

                parameters.Add(new LambdaParameterSyntax(name, magnitude, SpanFrom(start)));
            }

            if (index == before)
            {
                break;
            }

            if (!Match(TokenKind.Comma))
            {
                break;
            }
        }

        Expect(TokenKind.CloseParen, "')'");
        return parameters;
    }

    private BlockBodySyntax ParseBlockBody()
    {
        var start = Current.Span.Start;
        Expect(TokenKind.OpenBrace, "'{'");
        var statements = new List<StatementSyntax>();

        while (!IsAtEnd && !Check(TokenKind.CloseBrace))
        {
            var before = index;
            var statement = ParseStatement();
            if (statement is not null)
            {
                statements.Add(statement);
            }

            if (index == before)
            {
                Error(
                    DiagnosticIds.SyntaxUnexpectedToken,
                    $"No se pudo interpretar {Describe(Current)}.",
                    Current);
                Advance();
            }
        }

        Expect(TokenKind.CloseBrace, "'}'");
        return new BlockBodySyntax(statements, SpanFrom(start));
    }

    private StatementSyntax? ParseStatement()
    {
        if (Check(TokenKind.Label))
        {
            var start = Current.Span.Start;
            var label = Advance();
            Expect(TokenKind.Colon, "':'");
            return new LabelStatementSyntax(label, SpanFrom(start));
        }

        return ParseActionStatement();
    }

    private StatementSyntax? ParseActionStatement()
    {
        if (!CanStartReceiver(Current))
        {
            Error(
                DiagnosticIds.SyntaxInvalidStatement,
                $"Se esperaba una etiqueta o una acción fluida y se encontró {Describe(Current)}.",
                Current);
            return null;
        }

        var start = Current.Span.Start;
        var receiver = ParseReceiver();

        if (!Check(TokenKind.Dot))
        {
            Error(
                DiagnosticIds.SyntaxInvalidStatement,
                $"Se esperaba '.' entre el receptor y el verbo y se encontró {Describe(Current)}.",
                Current);
            return null;
        }

        Advance(); // '.'
        var verb = ExpectIdentifier("un verbo");

        if (Match(TokenKind.OpenAngle))
        {
            var capacity = ParseMagnitude();
            Expect(TokenKind.CloseAngle, "'>'");
            var arguments = ParseArgumentList();
            var definition = BuildDataDefinition(verb, capacity, arguments, start);
            return new DataDefinitionStatementSyntax(definition, SpanFrom(start));
        }

        var callArguments = ParseArgumentList();
        return new ActionStatementSyntax(receiver, verb, callArguments, SpanFrom(start));
    }

    private ExpressionSyntax ParseReceiver()
    {
        var start = Current.Span.Start;

        if (Check(TokenKind.OpenParen))
        {
            Advance();
            var high = ParseChainExpression();
            Expect(TokenKind.Colon, "':'");
            var low = ParseChainExpression();
            Expect(TokenKind.CloseParen, "')'");
            return new RegisterPairSyntax(high, low, SpanFrom(start));
        }

        if (Check(TokenKind.Port))
        {
            return ParsePortOperand();
        }

        if (Check(TokenKind.Magnitude))
        {
            var capacity = ParseMagnitude();
            if (!Check(TokenKind.OpenBracket))
            {
                Error(
                    DiagnosticIds.SyntaxInvalidAddress,
                    "La magnitud de un operando de memoria debe ir seguida de '['.",
                    Current);
                return capacity;
            }

            return ParseMemoryOperand(start, capacity);
        }

        if (Check(TokenKind.OpenBracket))
        {
            return ParseMemoryOperand(start, null);
        }

        return ParseChainExpression(stopAtCall: true);
    }

    private ExpressionSyntax ParseChainExpression(bool stopAtCall = false)
    {
        var start = Current.Span.Start;
        if (!IsIdentifierLike(Current.Kind) && !Check(TokenKind.Underscore))
        {
            Error(
                DiagnosticIds.SyntaxInvalidStatement,
                $"Se esperaba un identificador y se encontró {Describe(Current)}.",
                Current);
            var anchor = Current;
            if (!IsClosingToken(anchor))
            {
                Advance();
            }

            return new ErrorExpressionSyntax("identificador ausente", anchor.Span);
        }

        var identifier = Advance();
        ExpressionSyntax expression = new IdentifierExpressionSyntax(identifier);

        while (Check(TokenKind.Dot) && IsIdentifierLike(Peek(1).Kind))
        {
            if (stopAtCall && StartsPostfixArguments(Peek(2).Kind))
            {
                break;
            }

            Advance(); // '.'
            var member = Advance();
            expression = new MemberAccessSyntax(expression, member, SpanFrom(start));
        }

        return expression;
    }

    private FluentChainBodySyntax ParseFluentChainBody()
    {
        var start = Current.Span.Start;
        var receiver = ParseChainExpression(stopAtCall: true);
        var calls = new List<FluentCallSyntax>();

        while (Check(TokenKind.Dot) && IsIdentifierLike(Peek(1).Kind))
        {
            Advance(); // '.'
            var name = Advance();
            var arguments = ParseArgumentList();
            calls.Add(new FluentCallSyntax(name, arguments, SpanFrom(name.Span.Start)));
        }

        if (calls.Count == 0)
        {
            Error(
                DiagnosticIds.SyntaxInvalidStatement,
                "La configuración fluida debe encadenar al menos una llamada '.verbo(...)'.",
                Current);
        }

        return new FluentChainBodySyntax(receiver, calls, SpanFrom(start));
    }

    private IReadOnlyList<ExpressionSyntax> ParseArgumentList()
    {
        var arguments = new List<ExpressionSyntax>();
        Expect(TokenKind.OpenParen, "'('");
        if (Match(TokenKind.CloseParen))
        {
            return arguments;
        }

        while (!IsAtEnd && !Check(TokenKind.CloseParen))
        {
            var before = index;
            arguments.Add(ParseArgument());
            if (index == before)
            {
                Advance();
            }

            if (!Match(TokenKind.Comma))
            {
                break;
            }
        }

        Expect(TokenKind.CloseParen, "')'");
        return arguments;
    }

    private ExpressionSyntax ParseArgument()
    {
        var start = Current.Span.Start;
        var value = IsLambdaStart() ? ParseLambdaExpression() : ParseExpression();

        if (!Match(TokenKind.If))
        {
            return value;
        }

        var condition = ParseCondition();
        return new ConditionalExpressionSyntax(value, condition, SpanFrom(start));
    }

    private ExpressionSyntax ParseExpression()
    {
        var start = Current.Span.Start;

        if (Check(TokenKind.DefaultValue))
        {
            return new DefaultValueSyntax(Advance());
        }

        if (Check(TokenKind.String))
        {
            return new StringLiteralSyntax(Advance());
        }

        if (Check(TokenKind.Integer))
        {
            return new IntegerLiteralSyntax(Advance());
        }

        if (Check(TokenKind.Label))
        {
            return new LabelReferenceSyntax(Advance());
        }

        if (Check(TokenKind.Not))
        {
            return new NotValueSyntax(Advance());
        }

        if (Check(TokenKind.Magnitude))
        {
            var magnitude = ParseMagnitude();
            return Check(TokenKind.OpenBracket)
                ? ParseMemoryOperand(start, magnitude)
                : magnitude;
        }

        if (Check(TokenKind.Port))
        {
            return ParsePortOperand();
        }

        if (Check(TokenKind.OpenBracket))
        {
            return ParseMemoryOperand(start, null);
        }

        if (Check(TokenKind.OpenParen))
        {
            Advance();
            var first = ParseExpression();
            if (Match(TokenKind.Colon))
            {
                var second = ParseExpression();
                Expect(TokenKind.CloseParen, "')'");
                return new RegisterPairSyntax(first, second, SpanFrom(start));
            }

            Expect(TokenKind.CloseParen, "')'");
            return first;
        }

        if (IsIdentifierLike(Current.Kind) || Check(TokenKind.Underscore))
        {
            return ParseChainExpression();
        }

        Error(
            DiagnosticIds.SyntaxInvalidStatement,
            $"Se esperaba un operando y se encontró {Describe(Current)}.",
            Current);
        var anchor = Current;
        if (!IsClosingToken(anchor))
        {
            Advance();
        }

        return new ErrorExpressionSyntax("operando no reconocido", anchor.Span);
    }

    private PortOperandSyntax ParsePortOperand()
    {
        var start = Current.Span.Start;
        var port = Advance();
        Expect(TokenKind.OpenBracket, "'['");

        ExpressionSyntax address = Check(TokenKind.Integer)
            ? new IntegerLiteralSyntax(Advance())
            : ParseChainExpression();

        Expect(TokenKind.CloseBracket, "']'");
        return new PortOperandSyntax(port, address, SpanFrom(start));
    }

    private MemoryOperandSyntax ParseMemoryOperand(int start, MagnitudeSyntax? capacity)
    {
        var address = ParseAddressExpression();
        return new MemoryOperandSyntax(capacity, address, SpanFrom(start));
    }

    private AddressExpressionSyntax ParseAddressExpression()
    {
        var start = Current.Span.Start;
        Expect(TokenKind.OpenBracket, "'['");
        var baseExpression = ParseChainExpression();
        AddressIndexSyntax? addressIndex = null;
        long displacement = 0;

        while (!IsAtEnd && !Check(TokenKind.CloseBracket))
        {
            if (Match(TokenKind.Plus))
            {
                if (Check(TokenKind.Integer))
                {
                    displacement += Advance().IntegerValue;
                    continue;
                }

                if (addressIndex is null && IsIdentifierLike(Current.Kind))
                {
                    addressIndex = ParseAddressIndex();
                    continue;
                }

                Error(
                    DiagnosticIds.SyntaxInvalidAddress,
                    "Se esperaba un desplazamiento entero o un índice de registro tras '+'.",
                    Current);
                if (IsClosingToken(Current))
                {
                    break;
                }

                Advance();
                continue;
            }

            if (Match(TokenKind.Minus))
            {
                if (Check(TokenKind.Integer))
                {
                    displacement -= Advance().IntegerValue;
                    continue;
                }

                Error(
                    DiagnosticIds.SyntaxInvalidAddress,
                    "Se esperaba un desplazamiento entero tras '-'.",
                    Current);
                if (IsClosingToken(Current))
                {
                    break;
                }

                Advance();
                continue;
            }

            if (Check(TokenKind.Integer))
            {
                displacement += Advance().IntegerValue;
                continue;
            }

            Error(
                DiagnosticIds.SyntaxInvalidAddress,
                $"Se esperaba '+', '-' o ']' en la dirección y se encontró {Describe(Current)}.",
                Current);
            if (IsClosingToken(Current))
            {
                break;
            }

            Advance();
        }

        Expect(TokenKind.CloseBracket, "']'");
        return new AddressExpressionSyntax(baseExpression, addressIndex, displacement, SpanFrom(start));
    }

    private AddressIndexSyntax ParseAddressIndex()
    {
        var start = Current.Span.Start;
        var register = ParseChainExpression();
        ExpressionSyntax? scale = null;

        if (Match(TokenKind.Star))
        {
            if (Check(TokenKind.Integer))
            {
                scale = new IntegerLiteralSyntax(Advance());
            }
            else
            {
                Error(
                    DiagnosticIds.SyntaxInvalidAddress,
                    "Se esperaba un entero como escala del registro índice.",
                    Current);
            }
        }
        else
        {
            Error(
                DiagnosticIds.SyntaxInvalidAddress,
                "Se esperaba '*' y la escala del registro índice.",
                Current);
        }

        return new AddressIndexSyntax(register, scale, SpanFrom(start));
    }

    private MagnitudeSyntax ParseMagnitude()
    {
        var start = Current.Span.Start;
        if (!Check(TokenKind.Magnitude))
        {
            Error(
                DiagnosticIds.SyntaxInvalidStatement,
                $"Se esperaba una magnitud (Bit, Byte, KiloByte...) y se encontró {Describe(Current)}.",
                Current);
            var anchor = Current;
            if (!IsAtEnd)
            {
                Advance();
            }

            return new MagnitudeSyntax(
                Missing(TokenKind.Magnitude, anchor),
                Missing(TokenKind.Integer, anchor),
                SpanFrom(start));
        }

        var unit = Advance();
        Expect(TokenKind.Colon, "':'");
        var size = Expect(TokenKind.Integer, "un entero");
        return new MagnitudeSyntax(unit, size, SpanFrom(start));
    }

    private ConditionSyntax ParseCondition() => ParseOrCondition();

    private ConditionSyntax ParseOrCondition()
    {
        var left = ParseAndCondition();
        while (Match(TokenKind.Or))
        {
            var op = tokens[index - 1];
            var right = ParseAndCondition();
            left = new BinaryConditionSyntax(left, op, right, SpanBetween(left.Span, right.Span));
        }

        return left;
    }

    private ConditionSyntax ParseAndCondition()
    {
        var left = ParseEqualityCondition();
        while (Match(TokenKind.And))
        {
            var op = tokens[index - 1];
            var right = ParseEqualityCondition();
            left = new BinaryConditionSyntax(left, op, right, SpanBetween(left.Span, right.Span));
        }

        return left;
    }

    private ConditionSyntax ParseEqualityCondition()
    {
        var left = ParseUnaryCondition();
        if (!Check(TokenKind.Equal) && !Check(TokenKind.Unequal))
        {
            return left;
        }

        var op = Advance();
        var right = ParseUnaryCondition();
        return new BinaryConditionSyntax(left, op, right, SpanBetween(left.Span, right.Span));
    }

    private ConditionSyntax ParseUnaryCondition()
    {
        if (Match(TokenKind.Not))
        {
            var not = tokens[index - 1];
            var operand = ParseUnaryCondition();
            return new NotConditionSyntax(not, operand, SpanBetween(not.Span, operand.Span));
        }

        if (!IsIdentifierLike(Current.Kind))
        {
            Error(
                DiagnosticIds.SyntaxInvalidCondition,
                $"Se esperaba una bandera de Frf y se encontró {Describe(Current)}.",
                Current);
            var anchor = Current;
            if (!IsClosingToken(anchor))
            {
                Advance();
            }

            return new FlagConditionSyntax(new ErrorExpressionSyntax("condición no reconocida", anchor.Span));
        }

        return new FlagConditionSyntax(ParseChainExpression());
    }

    private DataDefinitionSyntax BuildDataDefinition(
        Token define,
        MagnitudeSyntax capacity,
        IReadOnlyList<ExpressionSyntax> arguments,
        int start)
    {
        var descriptor = arguments.Count > 0 && arguments[0] is LambdaExpressionSyntax lambda
            ? lambda
            : null;

        if (descriptor is null)
        {
            Error(
                DiagnosticIds.SyntaxInvalidDataDefinition,
                "dt.define<...>(...) requiere una lambda 'd -> d.id(...).value(...)'.",
                define);
            descriptor = EmptyLambda(define.Span);
        }

        Token? name = null;
        var values = new List<ExpressionSyntax>();

        if (descriptor.Body is FluentChainBodySyntax chain)
        {
            foreach (var call in chain.Calls)
            {
                if (call.Name.Text == "id"
                    && call.Arguments.Count > 0
                    && call.Arguments[0] is IdentifierExpressionSyntax identifier)
                {
                    name = identifier.Identifier;
                }
                else if (call.Name.Text == "value")
                {
                    values.AddRange(call.Arguments);
                }
            }
        }
        else
        {
            Error(
                DiagnosticIds.SyntaxInvalidDataDefinition,
                "La lambda de datos debe encadenar d.id(...).value(...).",
                descriptor.Span);
        }

        if (name is null)
        {
            Error(
                DiagnosticIds.SyntaxInvalidDataDefinition,
                "La definición de datos debe declarar d.id(...).",
                descriptor.Span);
        }

        if (values.Count == 0)
        {
            Error(
                DiagnosticIds.SyntaxInvalidDataDefinition,
                "La definición de datos debe declarar d.value(...).",
                descriptor.Span);
        }

        return new DataDefinitionSyntax(name, capacity, values, descriptor, SpanFrom(start));
    }

    private RoutineDefinitionSyntax BuildRoutineDefinition(ActionStatementSyntax define)
    {
        Token? name = null;
        ExpressionSyntax? returns = null;
        LambdaExpressionSyntax? run = null;
        var body = new List<StatementSyntax>();
        var hasDescriptor = define.Arguments.Count > 0
            && define.Arguments[0] is LambdaExpressionSyntax { Body: BlockBodySyntax };

        if (define.Arguments.Count > 0
            && define.Arguments[0] is LambdaExpressionSyntax descriptor
            && descriptor.Body is BlockBodySyntax descriptorBlock)
        {
            foreach (var statement in descriptorBlock.Statements)
            {
                if (statement is not ActionStatementSyntax action)
                {
                    Error(
                        DiagnosticIds.SyntaxInvalidRoutine,
                        "En rt.define(...) solo se admiten d.id, d.returns y d.run.",
                        statement.Span);
                    continue;
                }

                switch (action.Verb.Text)
                {
                    case "id":
                        if (action.Arguments.Count > 0 && action.Arguments[0] is IdentifierExpressionSyntax identifier)
                        {
                            name = identifier.Identifier;
                        }
                        else
                        {
                            Error(
                                DiagnosticIds.SyntaxInvalidRoutine,
                                "d.id(...) requiere un identificador de rutina.",
                                action.Verb);
                        }

                        break;

                    case "returns":
                        if (action.Arguments.Count > 0)
                        {
                            returns = action.Arguments[0];
                        }
                        else
                        {
                            Error(
                                DiagnosticIds.SyntaxInvalidRoutine,
                                "d.returns(...) requiere un receptor de retorno.",
                                action.Verb);
                        }

                        break;

                    case "run":
                        if (action.Arguments.Count > 0 && action.Arguments[0] is LambdaExpressionSyntax runLambda)
                        {
                            run = runLambda;
                            if (runLambda.Body is BlockBodySyntax runBlock)
                            {
                                body.AddRange(runBlock.Statements);
                            }
                            else
                            {
                                Error(
                                    DiagnosticIds.SyntaxInvalidRoutine,
                                    "d.run(...) requiere un bloque de sentencias '{ ... }'.",
                                    runLambda.Span);
                            }
                        }
                        else
                        {
                            Error(
                                DiagnosticIds.SyntaxInvalidRoutine,
                                "d.run(...) requiere una lambda con firma y bloque.",
                                action.Verb);
                        }

                        break;

                    default:
                        Error(
                            DiagnosticIds.SyntaxInvalidRoutine,
                            $"Miembro desconocido '{action.Verb.Text}' en rt.define(...); se esperaba id, returns o run.",
                            action.Verb);
                        break;
                }
            }
        }
        else
        {
            Error(
                DiagnosticIds.SyntaxInvalidRoutine,
                "rt.define(...) requiere una lambda 'd -> { ... }'.",
                define.Verb);
        }

        if (hasDescriptor)
        {
            if (name is null)
            {
                Error(
                    DiagnosticIds.SyntaxInvalidRoutine,
                    "La rutina debe declarar su identificador con d.id(...).",
                    define.Verb);
            }

            if (run is null)
            {
                Error(
                    DiagnosticIds.SyntaxInvalidRoutine,
                    "La rutina debe declarar su cuerpo con d.run(...).",
                    define.Verb);
            }
        }

        return new RoutineDefinitionSyntax(name, returns, run, body, false, define.Span);
    }

    private static LambdaExpressionSyntax EmptyLambda(TextSpan span) =>
        new([], new BlockBodySyntax([], span), span);
}


