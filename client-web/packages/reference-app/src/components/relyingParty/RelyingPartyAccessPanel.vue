<script setup lang="ts">
import { ref } from 'vue'
import { NAlert, NButton, NCard, NForm, NFormItem, NInput } from 'naive-ui'
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
    <n-card>
      <h2>Relying-Party Access</h2>
      <p>
        A customer delegates a capped, entity-scoped, time-boxed grant to a relying party, who exchanges it for an access token and reveals exactly the one masked field named
        &mdash; never a blanket grant (ADR-043/044).
      </p>
      <!-- `aria-label` on each input, matching its own visible
           `n-form-item` label text -- found via this panel's own
           Playwright playbook (a real, previously-undiscovered gap, not
           assumed): `label-for` is not an actual supported prop on this
           Naive UI version's `NFormItem` (verified directly against
           node_modules/naive-ui's own FormItem.mjs -- no `for`/`htmlFor`
           wiring exists in it at all), so the visible label text was
           never actually associated with its input, for a screen reader
           or for `GetByLabel`. Direct `aria-label` on the input itself
           sidesteps the missing association entirely. -->
      <n-form>
        <fieldset>
          <legend>Customer (granter)</legend>
          <n-form-item label="Granter actor ID">
            <n-input v-model:value="granterActorId" :input-props="({ 'aria-label': 'Granter actor ID' } as any)" />
          </n-form-item>
        </fieldset>

        <fieldset>
          <legend>Grant</legend>
          <n-form-item label="Entity ID">
            <n-input v-model:value="entityId" :input-props="({ 'aria-label': 'Entity ID' } as any)" />
          </n-form-item>
          <n-form-item label="Event ID (the specific event to reveal a field from)">
            <n-input v-model:value="eventId" placeholder="a GUID" :input-props="({ 'aria-label': 'Event ID (the specific event to reveal a field from)' } as any)" />
          </n-form-item>
          <n-form-item label="Field path">
            <n-input v-model:value="fieldPath" :input-props="({ 'aria-label': 'Field path' } as any)" />
          </n-form-item>
          <n-form-item label="Capability claim">
            <n-input v-model:value="capabilityClaim" :input-props="({ 'aria-label': 'Capability claim' } as any)" />
          </n-form-item>
        </fieldset>

        <fieldset>
          <legend>Relying party (grantee)</legend>
          <n-form-item label="Grantee actor ID">
            <n-input v-model:value="granteeActorId" :input-props="({ 'aria-label': 'Grantee actor ID' } as any)" />
          </n-form-item>
          <n-form-item label="Grantee OAuth client ID">
            <n-input v-model:value="granteeClientId" :input-props="({ 'aria-label': 'Grantee OAuth client ID' } as any)" />
          </n-form-item>
          <n-form-item label="Grantee OAuth client secret">
            <n-input v-model:value="granteeClientSecret" type="password" :input-props="({ 'aria-label': 'Grantee OAuth client secret' } as any)" />
          </n-form-item>
        </fieldset>

        <n-button type="primary" :disabled="inProgress" @click="submit">Delegate &amp; Reveal</n-button>
      </n-form>

      <p v-if="statusMessage" data-testid="relying-party-status">{{ statusMessage }}</p>
      <p v-if="revealedValue !== null" data-testid="relying-party-revealed-value">Revealed value: {{ revealedValue }}</p>
      <n-alert v-if="errorMessage" type="error" data-testid="relying-party-error" role="alert">{{ errorMessage }}</n-alert>
    </n-card>
  </section>
</template>
