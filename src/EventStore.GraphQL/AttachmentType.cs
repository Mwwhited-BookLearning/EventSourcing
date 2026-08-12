namespace EventStore.GraphQL;

// ADR-032's own Decision, verbatim field list and casing: "entity(id) {
// attachments { contentHash, filename, mimeType, sizeBytes } }" --
// Filename is named as one word here (not FileName, the EF entity's own
// property name) specifically so HotChocolate's default camelCase field-
// naming convention produces "filename", matching that literal query text
// exactly rather than "fileName". Named "Attachment" (not, say,
// "AttachmentGraphType") deliberately -- HotChocolate's convention-based
// type registration (AddType<T>(), no attribute override) names the
// GraphQL type after the CLR class exactly, and EntityQueryTypeModule's
// own TypeReference.Parse("[Attachment!]!") needs to resolve to THIS
// name; confirmed only by actually running this (a prior "AttachmentGraphType"
// name failed schema build with "Unable to resolve type reference
// `[Attachment!]!`"), not assumed. A distinct CLR type from
// EventStore.Domain.Streaming.Attachment (the EF entity) despite the same
// simple name -- different namespaces, no conflict.
public record Attachment(string ContentHash, string? Filename, string MimeType, long SizeBytes);
