// ADR-087 -- the client-side consequence of the server's Accept-Language
// negotiation (EventStore.Host.Core/HostCoreExtensions.cs): reads the
// NEGOTIATED locale back from a response's own `Content-Language` header
// (never `navigator.language` directly -- that would bypass the server's
// own negotiation/fallback-to-default logic entirely) and answers whether
// it's a right-to-left script, so a `ViewDefinition` template renders
// under the correct `dir` without a second, mirrored stylesheet.
export function resolveLocale(contentLanguageHeader: string | null): string {
  return contentLanguageHeader?.split(',')[0]?.trim() ?? 'en-US'
}

// A short, real list of RTL scripts (Arabic, Hebrew, Persian/Farsi, Urdu)
// -- checked against the locale's own primary language subtag, not the
// full BCP 47 tag, so "ar-SA"/"ar-EG"/etc. all match uniformly.
const RTL_LANGUAGE_SUBTAGS = new Set(['ar', 'he', 'fa', 'ur'])

export function isRtlLocale(locale: string): boolean {
  const languageSubtag = locale.split('-')[0]?.toLowerCase()
  return languageSubtag !== undefined && RTL_LANGUAGE_SUBTAGS.has(languageSubtag)
}
