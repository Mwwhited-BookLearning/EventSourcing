// ADR-087 -- PLACEHOLDER/TEST translation resources only, per this ADR's
// own explicit "the actual translated strings/content are explicitly
// domain-owned, out of scope" framing (the same shape a domain's own
// glossary vocabulary already has). A real deployment supplies its own
// resource set per domain/locale; this module exists to prove the
// mechanism (locale-driven key resolution, at least one RTL locale)
// works end to end, not to ship real production copy.
export type TranslationResources = Record<string, Record<string, string>>

// Vitals' Patient and Meridian's ApplicantIdentity Detail ViewDefinition
// templates (Samples.Vitals.Seed/Samples.Meridian.Seed) are exactly the
// "domain-owned" content this ADR's own comment above describes -- their
// {{ t:key }} keys were registered with no matching translation anywhere,
// so every live render showed a literal "[key]" bracket instead (found via
// a UI-playbook screenshot, TODO.md). Added here, the one resource set
// useEntityViewActions.ts actually resolves against (no per-domain
// resource seam exists yet), at the same placeholder-quality bar as the
// pre-existing carrier_label/amount_label pair -- not reviewed production
// copy, just enough for every registered template key to resolve to real
// text instead of its own bracketed name.
export const placeholderTranslations: TranslationResources = {
  'en-US': {
    carrier_label: 'Carrier',
    amount_label: 'Amount',
    patient_detail_title: 'Patient Detail',
    subject_id: 'Subject ID',
    site_id: 'Site ID',
    protocol_id: 'Protocol ID',
    eligibility_status: 'Eligibility Status',
    legal_name: 'Legal Name',
    date_of_birth: 'Date of Birth',
    applicant_detail_title: 'Applicant Detail',
    applicant_id: 'Applicant ID',
    document_type: 'Document Type',
    claimed_legal_name: 'Claimed Legal Name',
    did: 'DID',
  },
  'fr-FR': {
    carrier_label: 'Transporteur',
    amount_label: 'Montant',
    patient_detail_title: 'Détail du patient',
    subject_id: 'ID du sujet',
    site_id: 'ID du site',
    protocol_id: 'ID du protocole',
    eligibility_status: "Statut d'éligibilité",
    legal_name: 'Nom légal',
    date_of_birth: 'Date de naissance',
    applicant_detail_title: 'Détail du demandeur',
    applicant_id: 'ID du demandeur',
    document_type: 'Type de document',
    claimed_legal_name: 'Nom légal déclaré',
    did: 'DID',
  },
  'ar-SA': {
    carrier_label: 'شركة الشحن',
    amount_label: 'المبلغ',
    patient_detail_title: 'تفاصيل المريض',
    subject_id: 'معرف الخاضع',
    site_id: 'معرف الموقع',
    protocol_id: 'معرف البروتوكول',
    eligibility_status: 'حالة الأهلية',
    legal_name: 'الاسم القانوني',
    date_of_birth: 'تاريخ الميلاد',
    applicant_detail_title: 'تفاصيل مقدم الطلب',
    applicant_id: 'معرف مقدم الطلب',
    document_type: 'نوع المستند',
    claimed_legal_name: 'الاسم القانوني المصرح به',
    did: 'المعرف اللامركزي (DID)',
  },
}

export function resolveTranslations(locale: string, resources: TranslationResources = placeholderTranslations): Record<string, string> {
  return resources[locale] ?? resources['en-US'] ?? {}
}
