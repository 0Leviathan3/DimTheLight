<script setup lang="ts">

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
        />
      </div>
      <ClientOnly fallback="Karte wird geladen...">
        <LeafletMap />
      </ClientOnly>
    </template>
  </UDashboardPanel>
</template>
