using Antlr4.Runtime;
using Antlr4.Runtime.Tree;

namespace EventStore.Flows;

// ADR-101: real ANTLR4 parsing (Grammar/PlantUmlActivityDiagram.g4,
// Antlr4BuildTasks, Listener pattern), replacing the hand-rolled
// recursive-descent parser this project's own spike
// (spikes/user-flow-dsl/PlantUmlNativeSpike/PlantUmlActivityParser.cs)
// originally proved the shape with -- same public API, so nothing else in
// EventStore.Flows (FlowInterpreter, FlowDefinition, ...) needed to change
// for this swap.
public static class PlantUmlActivityParser
{
    public static IReadOnlyList<ActivityNode> Parse(string source)
    {
        var lexer = new PlantUmlActivityDiagramLexer(new AntlrInputStream(source));
        var errorListener = new ThrowingErrorListener();
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(errorListener);

        var parser = new PlantUmlActivityDiagramParser(new CommonTokenStream(lexer));
        parser.RemoveErrorListeners();
        parser.AddErrorListener(errorListener);

        var tree = parser.diagram();
        var listener = new ActivityAstBuilderListener();
        ParseTreeWalker.Default.Walk(listener, tree);
        return listener.Result;
    }

    // The old hand-rolled parser threw NotSupportedException on anything
    // outside its subset, loudly, immediately -- a real ANTLR syntax error
    // must fail exactly the same way, not the library's own default
    // (print to stderr, keep going with a best-effort parse tree).
    private sealed class ThrowingErrorListener : BaseErrorListener, IAntlrErrorListener<int>
    {
        // Parser errors (offending symbol already tokenized as an IToken) --
        // overrides BaseErrorListener's own virtual member.
        public override void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e) =>
            Throw(line, charPositionInLine, msg);

        // Lexer errors (offending symbol is still a raw codepoint, not yet
        // an IToken) -- BaseErrorListener only implements IAntlrErrorListener<IToken>,
        // so the lexer's own int-typed interface needs a separate, explicit implementation.
        void IAntlrErrorListener<int>.SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e) =>
            Throw(line, charPositionInLine, msg);

        private static void Throw(int line, int charPositionInLine, string msg) =>
            throw new NotSupportedException($"Unsupported Activity Diagram syntax at line {line}:{charPositionInLine} (Option G1's own deliberately narrow subset doesn't cover this): {msg}");
    }
}
