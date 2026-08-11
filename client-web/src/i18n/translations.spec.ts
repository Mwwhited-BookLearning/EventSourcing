import { describe, expect, it } from 'vitest'
import { placeholderTranslations, resolveTranslations } from './translations'

describe('resolveTranslations (ADR-087 -- placeholder/test resources only, real content is domain-owned)', () => {
  it('resolves the exact-match resource set for a supported locale', () => {
    expect(resolveTranslations('fr-FR')).toEqual(placeholderTranslations['fr-FR'])
  })

  it('resolves the RTL locale\'s own resource set', () => {
    expect(resolveTranslations('ar-SA')).toEqual(placeholderTranslations['ar-SA'])
  })

  it('falls back to en-US for an unsupported locale', () => {
    expect(resolveTranslations('de-DE')).toEqual(placeholderTranslations['en-US'])
  })

  it('falls back to an empty object when even en-US is missing from a caller-supplied resource set', () => {
    expect(resolveTranslations('de-DE', {})).toEqual({})
  })

  it('uses a caller-supplied resource set instead of the placeholder default', () => {
    const custom = { 'en-US': { greeting: 'Hi' } }
    expect(resolveTranslations('en-US', custom)).toEqual({ greeting: 'Hi' })
  })
})
