<script setup lang="ts">
import { ref } from 'vue'
import { useRelyingPartyAccess } from '@eventstore/mvvm-client'

// ADR-043/044 "Delegated Grants, RBAC, Federated Claims" -- Meridian's
// Workflow B (Relying-Party Access), the first client-web UI surface for
// it (TODO.md). See useRelyingPartyAccess.ts's own header comment for the
// full mechanism this panel drives end to end: a customer's own freshly-
// generated DID key self-issues a UCAN delegation (no pre-existing
// account of their own needed), a relying party exchanges it for a
// scoped access token, then reveals exactly the one masked field the
// delegation names, for exactly the one entity it names -- nothing
// broader. Deliberately a raw, ungated form (like EventComposer.vue),
// not a guided wizard -- this demonstrates the underlying mechanism
// directly, not a polished end-user product flow.
const props = defineProps<{ hostBaseUrl: string; authBaseUrl: string; appId: string }>()

const access = useRelyingPartyAccess({ hostBaseUrl: props.hostBaseUrl, authBaseUrl: props.authBaseUrl, appId: props.appId })

const granterActorId = ref('applicant-1001')
const entityId = ref(`${props.appId}:applicantidentity:applicant-1001`)
const eventId = ref('')
const fieldPath = ref('$.ClaimedLegalName')
const capabilityClaim = ref('identity:pii-read')
const granteeActorId = ref('colleague-1')
const granteeClientId = ref('colleague-client')
const granteeClientSecret = ref('colleague-client-secret')

const inProgress = ref(false)
const statusMessage = ref('')
const revealedValue = ref<string | null>(null)
const errorMessage = ref('')

async function submit(): Promise<void> {
  inProgress.value = true
  statusMessage.value = 'Registering the customer’s own DID key as a trust root, then delegating and exchanging for an access token…'
  revealedValue.value = null
  errorMessage.value = ''

  const result = await access.grantAndReveal({
    granterActorId: granterActorId.value,
    granteeActorId: granteeActorId.value,
    granteeClientId: granteeClientId.value,
    granteeClientSecret: granteeClientSecret.value,
    capability: { claim: capabilityClaim.value, entityScope: entityId.value },
    entityId: entityId.value,
    eventId: eventId.value,
    fieldPath: fieldPath.value,
  })

  inProgress.value = false
  statusMessage.value = result.issuerDid ? `Customer DID (JWK thumbprint): ${result.issuerDid}` : ''
  if (result.ok) {
    revealedValue.value = result.value ?? '(erased)'
  } else {
    errorMessage.value = result.error ?? 'Delegated access failed.'
  }
}
</script>

<template>
  <section aria-label="Relying-Party Access">
    <h2>Relying-Party Access</h2>
    <p>
      A customer delegates a capped, entity-scoped, time-boxed grant to a relying party, who exchanges it for an access token and reveals exactly the one masked field named
      &mdash; never a blanket grant (ADR-043/044).
    </p>
    <form @submit.prevent="submit">
      <fieldset>
        <legend>Customer (granter)</legend>
        <label>
          Granter actor ID
          <input v-model="granterActorId" type="text" required />
        </label>
      </fieldset>

      <fieldset>
        <legend>Grant</legend>
        <label>
          Entity ID
          <input v-model="entityId" type="text" required />
        </label>
        <label>
          Event ID (the specific event to reveal a field from)
          <input v-model="eventId" type="text" required placeholder="a GUID" />
        </label>
        <label>
          Field path
          <input v-model="fieldPath" type="text" required />
        </label>
        <label>
          Capability claim
          <input v-model="capabilityClaim" type="text" required />
        </label>
      </fieldset>

      <fieldset>
        <legend>Relying party (grantee)</legend>
        <label>
          Grantee actor ID
          <input v-model="granteeActorId" type="text" required />
        </label>
        <label>
          Grantee OAuth client ID
          <input v-model="granteeClientId" type="text" required />
        </label>
        <label>
          Grantee OAuth client secret
          <input v-model="granteeClientSecret" type="password" required />
        </label>
      </fieldset>

      <button type="submit" :disabled="inProgress">Delegate &amp; Reveal</button>
    </form>

    <p v-if="statusMessage" data-testid="relying-party-status">{{ statusMessage }}</p>
    <p v-if="revealedValue !== null" data-testid="relying-party-revealed-value">Revealed value: {{ revealedValue }}</p>
    <p v-if="errorMessage" data-testid="relying-party-error" role="alert">{{ errorMessage }}</p>
  </section>
</template>
