// ADR-087 -- PLACEHOLDER/TEST translation resources only, per this ADR's
// own explicit "the actual translated strings/content are explicitly
// domain-owned, out of scope" framing (the same shape a domain's own
// glossary vocabulary already has). A real deployment supplies its own
// resource set per domain/locale; this module exists to prove the
// mechanism (locale-driven key resolution, at least one RTL locale)
// works end to end, not to ship real production copy.
export type TranslationResources = Record<string, Record<string, string>>

export const placeholderTranslations: TranslationResources = {
  'en-US': {
    carrier_label: 'Carrier',
    amount_label: 'Amount',
  },
  'fr-FR': {
    carrier_label: 'Transporteur',
    amount_label: 'Montant',
  },
  'ar-SA': {
    carrier_label: 'شركة الشحن',
    amount_label: 'المبلغ',
  },
}

export function resolveTranslations(locale: string, resources: TranslationResources = placeholderTranslations): Record<string, string> {
  return resources[locale] ?? resources['en-US'] ?? {}
}
