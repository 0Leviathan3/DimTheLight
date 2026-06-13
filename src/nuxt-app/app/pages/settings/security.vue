<script setup lang="ts">
import * as z from 'zod'
import type { FormError } from '@nuxt/ui'

const passwordSchema = z.object({
  current: z.string().min(8, 'Muss mindestens 8 Zeichen lang sein'),
  new: z.string().min(8, 'Muss mindestens 8 Zeichen lang sein')
})

type PasswordSchema = z.output<typeof passwordSchema>

const password = reactive<Partial<PasswordSchema>>({
  current: '',
  new: ''
})

const validate = (state: Partial<PasswordSchema>): FormError[] => {
  const errors: FormError[] = []
  if (state.current && state.new && state.current === state.new) {
    errors.push({ name: 'new', message: 'Die Passwörter müssen unterschiedlich sein' })
  }
  return errors
}
</script>

<template>
  <UPageCard
    title="Passwort"
    description="Bestätige dein aktuelles Passwort, bevor du ein neues festlegst."
    variant="subtle"
  >
    <UForm
      :schema="passwordSchema"
      :state="password"
      :validate="validate"
      class="flex flex-col gap-4 max-w-xs"
    >
      <UFormField name="current">
        <UInput
          v-model="password.current"
          type="password"
          placeholder="Aktuelles Passwort"
          class="w-full"
        />
      </UFormField>

      <UFormField name="new">
        <UInput
          v-model="password.new"
          type="password"
          placeholder="Neues Passwort"
          class="w-full"
        />
      </UFormField>

      <UButton label="Aktualisieren" class="w-fit" type="submit" />
    </UForm>
  </UPageCard>

  <UPageCard
    title="Konto"
    description="Du möchtest unseren Dienst nicht mehr nutzen? Hier kannst du dein Konto löschen. Diese Aktion kann nicht rückgängig gemacht werden. Alle mit diesem Konto verbundenen Informationen werden dauerhaft gelöscht."
    class="bg-linear-to-tl from-error/10 from-5% to-default"
  >
    <template #footer>
      <UButton label="Konto löschen" color="error" />
    </template>
  </UPageCard>
</template>
