import { afterEach, describe, expect, it, vi } from 'vitest'
import { negotiateLocale } from './localeClient'

describe('negotiateLocale (ADR-087 -- reads back the server\'s own negotiated Content-Language)', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('resolves to the locale echoed in the response\'s Content-Language header', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ headers: new Headers({ 'Content-Language': 'fr-FR' }) })
    vi.stubGlobal('fetch', fetchMock)

    const locale = await negotiateLocale('https://host.example', 'fr-FR,en;q=0.5')

    expect(locale).toBe('fr-FR')
    expect(fetchMock).toHaveBeenCalledWith('https://host.example/openapi.json', { headers: { 'Accept-Language': 'fr-FR,en;q=0.5' } })
  })

  it('falls back to en-US when the request throws (offline/unreachable), never propagating the error', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('network down')))

    const locale = await negotiateLocale('https://host.example', 'ar-SA')

    expect(locale).toBe('en-US')
  })

  it('falls back to en-US when the response carries no Content-Language header at all', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ headers: new Headers() }))

    const locale = await negotiateLocale('https://host.example', 'de-DE')

    expect(locale).toBe('en-US')
  })
})
