import { describe, expect, it } from 'vitest'
import { isRtlLocale, resolveLocale } from './locale'

describe('resolveLocale (ADR-087 -- reading the server\'s own negotiated Content-Language)', () => {
  it('takes the first locale from a Content-Language header', () => {
    expect(resolveLocale('fr-FR')).toBe('fr-FR')
  })

  it('takes only the first entry of a multi-value Content-Language header', () => {
    expect(resolveLocale('fr-FR, en-US')).toBe('fr-FR')
  })

  it('falls back to en-US when there is no header at all (offline/unreachable)', () => {
    expect(resolveLocale(null)).toBe('en-US')
  })
})

describe('isRtlLocale', () => {
  it('flags ar-SA as RTL', () => {
    expect(isRtlLocale('ar-SA')).toBe(true)
  })

  it('flags he/fa/ur language subtags as RTL regardless of region', () => {
    expect(isRtlLocale('he-IL')).toBe(true)
    expect(isRtlLocale('fa-IR')).toBe(true)
    expect(isRtlLocale('ur-PK')).toBe(true)
  })

  it('does not flag en-US or fr-FR as RTL', () => {
    expect(isRtlLocale('en-US')).toBe(false)
    expect(isRtlLocale('fr-FR')).toBe(false)
  })

  it('is case-insensitive on the language subtag', () => {
    expect(isRtlLocale('AR-SA')).toBe(true)
  })
})
