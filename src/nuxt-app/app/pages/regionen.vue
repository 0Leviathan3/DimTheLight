<script setup lang="ts">
import type { DropdownMenuItem } from '@nuxt/ui'

  const items = [[{
  label: 'Neue Region',
  icon: 'i-lucide-send',
  to: '/regionen'
}]] satisfies DropdownMenuItem[][]

</script>

<template>
  <UDashboardPanel id="home">
    <template #header>
      <UDashboardNavbar title="Regionen" :ui="{ right: 'gap-3' }">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>

        <template #right> 
          <UDropdownMenu :items="items">
            <UButton icon="i-lucide-plus" size="md" class="rounded-full" />
          </UDropdownMenu>
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <ClientOnly fallback="Karte wird geladen...">
        <LeafletMap />
      </ClientOnly>
    </template>
  </UDashboardPanel>
</template>
