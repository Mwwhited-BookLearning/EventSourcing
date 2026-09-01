grammar PlantUmlActivityDiagram;

// ADR-101: real ANTLR4 grammar for the exact PlantUML Activity Diagram
// subset this project's own spike (spikes/user-flow-dsl/
// PlantUmlNativeSpike/PlantUmlActivityParser.cs) already proved works:
// start/stop, :action label;, if (condition) then (yes) ... else (no) ...
// endif. Not line-sensitive, unlike the hand-rolled parser it replaces --
// whitespace (including newlines) is skippable anywhere, a strict
// generalization, never a narrowing, of what the old parser accepted.
// Compiled via the real ANTLR4 Java tool (Antlr4BuildTasks NuGet package,
// downloads its own JRE, no local Java/Docker setup needed) into a
// Listener-pattern parser (not Visitor), per direct request.

diagram: step* EOF;

step
    : action                                                                        # ActionStep
    | STOP                                                                          # StopStep
    | IF condition=PAREN_TEXT THEN PAREN_TEXT thenSteps+=step*
        (ELSE PAREN_TEXT elseSteps+=step*)? ENDIF                                   # IfStepAlt
    ;

action: ACTION_TEXT;

// @startuml/start/@enduml carry no meaning in the AST (the old parser
// skipped them too); skipped at the lexer level, same as whitespace.
STARTUML: '@startuml' -> skip;
ENDUML: '@enduml' -> skip;
START: 'start' -> skip;

STOP: 'stop';
IF: 'if';
THEN: 'then';
ELSE: 'else';
ENDIF: 'endif';

// The whole ':...;' construct is one token, matching the old parser's own
// line-based ActionNode(line[1..^1]) extraction; the listener strips the
// leading ':' and trailing ';' when building the AST.
ACTION_TEXT: ':' ~[;\r\n]* ';';

// Free text between parens -- covers both a real condition ("SeriousAdverseEvent?")
// and the literal "(yes)"/"(no)" branch markers alike; the PARSER rule's
// own token POSITION (right after IF vs. right after THEN/ELSE)
// disambiguates them, not the lexer, exactly matching how the old parser's
// own regex hardcoded "(yes)"'s position rather than its content.
PAREN_TEXT: '(' ~[)\r\n]* ')';

WS: [ \t\r\n]+ -> skip;
