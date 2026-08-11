import { resolveLocale } from '../i18n/locale'

// ADR-087 -- reads back the server's own NEGOTIATED locale
// (EventStore.Host.Core/HostCoreExtensions.cs's RequestLocalizationMiddleware,
// echoed as a real Content-Language response header) rather than trusting
// `navigator.language` directly, so the client's own translation-key
// resolution stays driven by the same RFC 9110 §12 negotiation the server
// already performs -- one negotiated value, not two independently-decided
// ones. Any reachable endpoint answers this (locale negotiation happens in
// middleware ahead of authentication/routing); `/openapi.json` is used
// simply because it's already real, always-anonymous, and side-effect-free.
export async function negotiateLocale(hostBaseUrl: string, acceptLanguage: string): Promise<string> {
  try {
    const response = await fetch(`${hostBaseUrl}/openapi.json`, { headers: { 'Accept-Language': acceptLanguage } })
    return resolveLocale(response.headers.get('Content-Language'))
  } catch {
    return 'en-US' // offline/unreachable -- the same server-side DefaultRequestCulture fallback, chosen client-side instead since no request ever completed
  }
}
