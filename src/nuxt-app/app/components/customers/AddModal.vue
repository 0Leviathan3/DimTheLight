<script setup lang="ts">
import * as z from 'zod'
import type { FormSubmitEvent } from '@nuxt/ui'

const schema = z.object({
  name: z.string().min(2, 'Zu kurz'),
  email: z.string().email('Ungültige E-Mail')
})
const open = ref(false)

type Schema = z.output<typeof schema>

const state = reactive<Partial<Schema>>({
  name: '',
  email: ''
})

const toast = useToast()
async function onSubmit(event: FormSubmitEvent<Schema>) {
  toast.add({ title: 'Erfolg', description: `Neuer Kunde ${event.data.name} hinzugefügt`, color: 'success' })
  open.value = false
}
</script>

<template>
  <UModal v-model:open="open" title="Neuer Kunde" description="Einen neuen Kunden in die Datenbank aufnehmen">
    <UButton label="Neuer Kunde" icon="i-lucide-plus" />

    <template #body>
      <UForm
        :schema="schema"
        :state="state"
        class="space-y-4"
        @submit="onSubmit"
      >
        <UFormField label="Name" placeholder="Max Mustermann" name="name">
          <UInput v-model="state.name" class="w-full" />
        </UFormField>
        <UFormField label="E-Mail" placeholder="max.mustermann@beispiel.de" name="email">
          <UInput v-model="state.email" class="w-full" />
        </UFormField>
        <div class="flex justify-end gap-2">
          <UButton
            label="Abbrechen"
            color="neutral"
            variant="subtle"
            @click="open = false"
          />
          <UButton
            label="Erstellen"
            color="primary"
            variant="solid"
            type="submit"
          />
        </div>
      </UForm>
    </template>
  </UModal>
</template>
