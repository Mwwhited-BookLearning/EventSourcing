using System.Text.RegularExpressions;

namespace EventStore.ViewRegistry;

// ADR-087 -- "a ViewDefinition's rendered text must reference a
// translation key, never a hardcoded literal" (docs/features/
// mvvm-client.md's own Internationalization section, this build item's
// exit criteria). Registration-time structural check, the same posture
// EnumFallbackSchemaValidator/MaskingSchemaValidator already establish
// for a registration payload -- pure text-shape validation, no claims
// involved. Deliberately a regex heuristic, not a real HTML parser:
// `client-web/src/components/entity/TemplateRenderer.vue`'s own runtime
// interpolation is ALREADY regex-based (`docs/06-solution-structure.md`'s
// "small injected binding runtime... zero extra machinery" framing) --
// matching that same level of mechanism here, not reaching for a new
// HTML-parsing dependency this codebase has no other use for.
internal static class TranslationKeyValidator
{
    // Matches EITHER an ordinary data-field binding ({{ carrier }},
    // optionally with a {{ field:date }}/{{ field:number }} format
    // modifier) or a translation-key reference ({{ t:carrier_label }}) --
    // all three are legitimate, non-literal content per
    // TemplateRenderer.vue's own interpolation syntax (that component's
    // own INTERPOLATION regex, verbatim); only text OUTSIDE all three
    // counts as a hardcoded literal. The date/number suffix was missing
    // here until found by actually registering a template using it --
    // every such template was rejected at registration time, even though
    // the client fully supports rendering it once registered.
    private static readonly Regex InterpolationExpression = new(@"\{\{\s*(?:t:[\w.]+|[\w.]+(?::(?:date|number))?)\s*\}\}", RegexOptions.Compiled);
    private static readonly Regex HtmlTagOrComment = new(@"<!--.*?-->|<[^>]+>", RegexOptions.Compiled | RegexOptions.Singleline);

    public static void Validate(string templateContent, List<string> errors)
    {
        var withoutInterpolations = InterpolationExpression.Replace(templateContent, "");
        var textOnly = HtmlTagOrComment.Replace(withoutInterpolations, "");

        if (!string.IsNullOrWhiteSpace(textOnly))
            errors.Add($"templateContent contains hardcoded text outside of a {{{{ t:key }}}} translation-key reference or a {{{{ field }}}} data binding (found: \"{textOnly.Trim()}\") -- ADR-087 requires every rendered string to reference a translation key, never a literal");
    }
}
