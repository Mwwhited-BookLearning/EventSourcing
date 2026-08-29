<script setup lang="ts">
import { NButton, NCard, NForm, NFormItem, NInput, NSpace } from 'naive-ui'
import EntityView from '../components/entity/EntityView.vue'
import { useAppState } from '../appState'

const { config, viewActions, currentEntityId, amountInput, statusMessage, submitAmountCommand } = useAppState()
</script>

<template>
  <n-space vertical size="large">
    <n-card v-if="currentEntityId" title="Entity">
      <EntityView :entity-id="currentEntityId" :instance-id="config.instanceId" :entity-type="config.entityType" :view-actions="viewActions" />
    </n-card>
    <n-card v-else>
      <p>Waiting for the first event on this subscription…</p>
    </n-card>

    <n-card title="Dispatch a command">
      <n-form inline :show-feedback="false">
        <n-form-item label="Amount">
          <n-input v-model:value="amountInput" data-testid="amount-input" />
        </n-form-item>
        <n-form-item>
          <n-button type="primary" :disabled="!currentEntityId" data-testid="set-amount" @click="submitAmountCommand">Set Amount</n-button>
        </n-form-item>
      </n-form>
      <p>{{ statusMessage }}</p>
    </n-card>
  </n-space>
</template>
