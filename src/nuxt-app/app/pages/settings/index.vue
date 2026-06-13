<script setup lang="ts">
import * as z from 'zod'
import type { FormSubmitEvent } from '@nuxt/ui'

// gRPC imports wurden in den serverseitigen API-Endpunkt verschoben


const fileRef = ref<HTMLInputElement>()

const profileSchema = z.object({
  name: z.string().min(2, 'Zu kurz'),
  email: z.string().email('Ungültige E-Mail'),
  username: z.string().min(2, 'Zu kurz'),
  avatar: z.string().optional(),
  bio: z.string().optional()
})

type ProfileSchema = z.output<typeof profileSchema>

const profile = reactive<Partial<ProfileSchema>>({
  name: 'Dim The Light',
  email: 'ben@nuxtlabs.com',
  username: 'dimTheLight',
  avatar: undefined,
  bio: undefined
})
const toast = useToast()
async function onSubmit(event: FormSubmitEvent<ProfileSchema>) {
  toast.add({
    title: 'Erfolg',
    description: 'Deine Einstellungen wurden aktualisiert.',
    icon: 'i-lucide-check',
    color: 'success'
  })
  console.log(event.data)
}

function onFileChange(e: Event) {
  const input = e.target as HTMLInputElement

  if (!input.files?.length) {
    return
  }

  profile.avatar = URL.createObjectURL(input.files[0]!)
}

function onFileClick() {
  fileRef.value?.click()
}


const testInput = ref('')

// Beispiel-Funktion, um den Downlink über unser neues API-Backend auszuführen:
async function triggerDownlink() {
  try {
    const response = await $fetch('/api/enqueue_downlink', {
      method: 'POST',
      body: { payload: testInput.value }
    })
    toast.add({
      title: 'Downlink gesendet!',
      description: 'ID: ' + response.id,
      color: 'success'
    })
  } catch (error) {
    toast.add({
      title: 'Fehler beim Senden',
      description: String(error),
      color: 'error'
    })
  }
}

</script>

<template>
  <UForm
    id="settings"
    :schema="profileSchema"
    :state="profile"
    @submit="onSubmit"
  >
    <UPageCard
      title="Profil"
      variant="naked"
      orientation="horizontal"
      class="mb-4"
    >
      <UButton
        form="settings"
        label="Änderungen speichern"
        color="neutral"
        type="submit"
        class="w-fit lg:ms-auto"
      />
    </UPageCard>

    <UPageCard variant="subtle">
      <UFormField
        name="name"
        label="Name"
        description="Ich weiß nicht, was ich dazu sagen soll."
        required
        class="flex max-sm:flex-col justify-between items-start gap-4"
      >
        <UInput
          v-model="profile.name"
          autocomplete="off"
        />
      </UFormField>
      <USeparator />
      <UFormField
        name="email"
        label="E-Mail"
        description="Wird zur Anmeldung verwendet."
        required
        class="flex max-sm:flex-col justify-between items-start gap-4"
      >
        <UInput
          v-model="profile.email"
          type="email"
          autocomplete="off"
        />
      </UFormField>
    </UPageCard>
      <!-- Testbereich für Downlink-Auslösung -->
      <UInput v-model="testInput" placeholder="0x123456789abcdef oder Wert" />
      <UButton
        name="test_btn"
        label="Test senden"
        @click="triggerDownlink()"
      />
  </UForm>
</template>
