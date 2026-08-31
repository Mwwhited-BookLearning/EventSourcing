grammar UserFlowDsl;

// Option G2's own wholly custom grammar, docs/comparisons/user-flow-dsl.md,
// deliberately NOT PlantUML syntax, a real ANTLR4 .g4 file compiled via the
// real ANTLR4 Java tool (Antlr4BuildTasks NuGet package handles this as an
// MSBuild step, downloading its own JRE, per direct request). Named for the
// DSL itself, not for any one scenario written in it — AdverseEventReview.flow
// (Flows/) is just one instance of this grammar, not the grammar's namesake.

flow: step+ EOF;

step
    : action
    | ifStep
    ;

action: 'do' STRING ';';

ifStep: 'if' STRING 'then' '{' thenSteps+=step+ '}' ('else' '{' elseSteps+=step+ '}')?;

STRING: '"' ~["\r\n]* '"';
WS: [ \t\r\n]+ -> skip;
