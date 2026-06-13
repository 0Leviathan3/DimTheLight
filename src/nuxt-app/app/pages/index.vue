<script setup lang="ts">
import { ref } from 'vue'

const searchQuery = ref('')

</script>

<template>
  <UDashboardPanel id="home">
    <template #header>
      <UDashboardNavbar title="Startseite" :ui="{ right: 'gap-3' }">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
      </UDashboardNavbar>
    </template>
    
    <template #body>
      <div class="flex flex-wrap items-center justify-between gap-1.5">
        <UInput
          class="max-w-sm"
          icon="i-lucide-search"
          placeholder="Lampen filtern..."
          v-model="searchQuery"
        />
      </div>
      <ClientOnly fallback="Karte wird geladen...">
        <LeafletMap :search-query="searchQuery" />     
      </ClientOnly>
    </template>
  </UDashboardPanel>
</template>
