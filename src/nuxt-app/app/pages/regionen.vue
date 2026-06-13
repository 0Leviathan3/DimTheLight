<script setup lang="ts">
import * as turf from '@turf/turf'

interface DrawnShape {
  coordinates: [number, number][]
}

interface Region {
  id: string
  name: string
  color: string
  shapes: DrawnShape[]
  pinIds: string[]
  createdAt: string
}

const isModalOpen = ref(false)
const regionName = ref('')
const regionColor = ref('#3b82f6')
const isDrawingMode = ref(false)

// Temporär gespeicherte Zeichnungsdaten
const pendingDrawing = ref<{ shapes: DrawnShape[]; pinIds: string[] } | null>(null)

// Gespeicherte Regionen
const regions = ref<Region[]>([])

// Ausgewählte Region für Detail-Ansicht
const selectedRegion = ref<Region | null>(null)
const isDetailOpen = ref(false)

// ID der Region, die gerade erweitert wird (null = neue Region)
const editingRegionId = ref<string | null>(null)

// Punkte laden (für pinIds-Neuberechnung beim Erweitern)
const points = ref<[number, number, string][]>([])
const { data: pointsData } = await useFetch('/api/getPoints')
points.value = (pointsData.value as [number, number, string][]) || []

// Alle Pins in mehreren Shapes finden
function findPinsInShapes(shapes: DrawnShape[]): string[] {
  const seen = new Set<string>()
  const selectedIds: string[] = []

  shapes.forEach(({ coordinates }) => {
    const polygonCoords = coordinates.map(([lat, lng]) => [lng, lat] as [number, number])
    polygonCoords.push(polygonCoords[0]!)
    const turfPolygon = turf.polygon([polygonCoords])

    points.value.forEach(([pLat, pLng, id]) => {
      if (seen.has(id)) return
      const turfPoint = turf.point([pLng, pLat])
      if (turf.booleanPointInPolygon(turfPoint, turfPolygon)) {
        selectedIds.push(id)
        seen.add(id)
      }
    })
  })

  return selectedIds
}

// Zeichenmodus starten (neue Region)
function startDrawing() {
  editingRegionId.value = null
  isDrawingMode.value = true
}

// Bestehende Region erweitern
function startExtending(region: Region) {
  editingRegionId.value = region.id
  regionColor.value = region.color
  isDetailOpen.value = false
  isDrawingMode.value = true
}

// Region wurde auf der Karte gezeichnet (mehrere Lassos)
function onRegionDrawn(payload: { shapes: DrawnShape[]; pinIds: string[] }) {
  if (editingRegionId.value) {
    // Bestehende Region erweitern
    const region = regions.value.find(r => r.id === editingRegionId.value)
    if (region) {
      region.shapes.push(...payload.shapes)
      // PinIds komplett neu berechnen über alle Shapes
      region.pinIds = findPinsInShapes(region.shapes)
      // selectedRegion aktualisieren falls offen
      if (selectedRegion.value?.id === region.id) {
        selectedRegion.value = { ...region }
      }
    }
    editingRegionId.value = null
  } else {
    // Neue Region → Modal öffnen
    pendingDrawing.value = payload
    isModalOpen.value = true
  }
}

// Region speichern
function saveRegion() {
  if (!regionName.value || !pendingDrawing.value) return

  const newRegion: Region = {
    id: crypto.randomUUID(),
    name: regionName.value,
    color: regionColor.value,
    shapes: pendingDrawing.value.shapes,
    pinIds: pendingDrawing.value.pinIds,
    createdAt: new Date().toLocaleDateString('de-DE', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    })
  }

  regions.value.push(newRegion)

  // Zurücksetzen
  isModalOpen.value = false
  regionName.value = ''
  regionColor.value = '#3b82f6'
  pendingDrawing.value = null
}

// Modal abbrechen
function cancelSave() {
  isModalOpen.value = false
  pendingDrawing.value = null
  regionName.value = ''
}

// Region löschen
function deleteRegion(id: string) {
  regions.value = regions.value.filter(r => r.id !== id)
  if (selectedRegion.value?.id === id) {
    isDetailOpen.value = false
    selectedRegion.value = null
  }
}

// Region anzeigen
function showRegionDetail(region: Region) {
  selectedRegion.value = region
  isDetailOpen.value = true
}
</script>

<template>
  <UDashboardPanel id="regionen">
    <template #header>
      <UDashboardNavbar title="Regionen" :ui="{ right: 'gap-3' }">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>

        <template #right>
          <!-- Hinweis welche Region erweitert wird -->
          <span
            v-if="isDrawingMode && editingRegionId"
            class="text-sm text-gray-500 dark:text-gray-400"
          >
            Erweitere: <strong>{{ regions.find(r => r.id === editingRegionId)?.name }}</strong>
          </span>

          <UButton
            v-if="!isDrawingMode"
            icon="i-lucide-pencil"
            color="primary"
            variant="solid"
            size="md"
            class="rounded-full"
            @click="startDrawing"
          >
            Region zeichnen
          </UButton>
          <UButton
            v-else
            icon="i-lucide-x"
            color="error"
            variant="soft"
            size="md"
            class="rounded-full"
            @click="isDrawingMode = false; editingRegionId = null"
          >
            Abbrechen
          </UButton>
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <div class="flex h-full">
        <!-- Karte -->
        <div class="flex-1 relative">
          <ClientOnly fallback="Karte wird geladen...">
            <LeafletMap
              v-model:is-drawing-mode="isDrawingMode"
              :region-color="regionColor"
              :saved-regions="regions"
              @region-drawn="onRegionDrawn"
            />
          </ClientOnly>
        </div>

        <!-- Seitenleiste mit Regionen -->
        <div class="w-80 border-l border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-900 overflow-y-auto flex flex-col">
          <div class="p-4 border-b border-gray-200 dark:border-gray-700">
            <h3 class="text-sm font-semibold text-gray-500 dark:text-gray-400 uppercase tracking-wider">
              Gespeicherte Regionen ({{ regions.length }})
            </h3>
          </div>

          <div v-if="regions.length === 0" class="flex-1 flex items-center justify-center p-6">
            <div class="text-center text-gray-400 dark:text-gray-500">
              <UIcon name="i-lucide-map" class="w-10 h-10 mx-auto mb-3 opacity-50" />
              <p class="text-sm">Noch keine Regionen.</p>
              <p class="text-xs mt-1">Klicke auf „Region zeichnen" und male Bereiche auf der Karte.</p>
            </div>
          </div>

          <div v-else class="flex-1 divide-y divide-gray-100 dark:divide-gray-800">
            <div
              v-for="region in regions"
              :key="region.id"
              class="p-4 hover:bg-gray-50 dark:hover:bg-gray-800/50 cursor-pointer transition-colors group"
              @click="showRegionDetail(region)"
            >
              <div class="flex items-start gap-3">
                <div
                  class="w-4 h-4 rounded-full mt-0.5 shrink-0"
                  :style="{ backgroundColor: region.color }"
                />
                <div class="flex-1 min-w-0">
                  <p class="text-sm font-medium text-gray-900 dark:text-white truncate">
                    {{ region.name }}
                  </p>
                  <p class="text-xs text-gray-500 dark:text-gray-400 mt-0.5">
                    {{ region.pinIds.length }} Laternen · {{ region.shapes.length }} {{ region.shapes.length === 1 ? 'Bereich' : 'Bereiche' }}
                  </p>
                  <p class="text-xs text-gray-400 dark:text-gray-500">
                    {{ region.createdAt }}
                  </p>
                </div>
                <!-- + Button zum Erweitern -->
                <UButton
                  icon="i-lucide-plus"
                  color="primary"
                  variant="ghost"
                  size="xs"
                  class="opacity-0 group-hover:opacity-100 transition-opacity"
                  @click.stop="startExtending(region)"
                />
                <UButton
                  icon="i-lucide-trash-2"
                  color="error"
                  variant="ghost"
                  size="xs"
                  class="opacity-0 group-hover:opacity-100 transition-opacity"
                  @click.stop="deleteRegion(region.id)"
                />
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- Modal: Region speichern -->
      <UModal v-model:open="isModalOpen">
        <template #content>
          <UCard>
            <template #header>
              <h3 class="text-lg font-semibold">Region benennen</h3>
              <p class="text-sm text-gray-500 mt-1">
                {{ pendingDrawing?.shapes.length || 0 }} {{ (pendingDrawing?.shapes.length || 0) === 1 ? 'Bereich' : 'Bereiche' }}
                mit {{ pendingDrawing?.pinIds.length || 0 }} Laternen.
              </p>
            </template>

            <div class="space-y-4">
              <UFormField label="Name der Region" required>
                <UInput v-model="regionName" placeholder="z. B. Baustelle Lichtenhainer Straße" />
              </UFormField>

              <UFormField label="Farbe" required>
                <div class="flex items-center gap-3">
                  <input
                    type="color"
                    v-model="regionColor"
                    class="h-8 w-10 cursor-pointer rounded border-0 bg-transparent p-0"
                  />
                  <span class="text-sm text-gray-500">{{ regionColor }}</span>
                </div>
              </UFormField>
            </div>

            <template #footer>
              <div class="flex justify-end gap-3">
                <UButton color="neutral" variant="soft" @click="cancelSave">
                  Verwerfen
                </UButton>
                <UButton color="primary" @click="saveRegion" :disabled="!regionName">
                  Region speichern
                </UButton>
              </div>
            </template>
          </UCard>
        </template>
      </UModal>

      <!-- Slideover: Region-Details -->
      <USlideover v-model:open="isDetailOpen" side="right">
        <template #content>
          <div v-if="selectedRegion" class="p-6">
            <div class="flex items-center justify-between mb-6">
              <div class="flex items-center gap-3">
                <div
                  class="w-5 h-5 rounded-full"
                  :style="{ backgroundColor: selectedRegion.color }"
                />
                <h2 class="text-lg font-semibold">{{ selectedRegion.name }}</h2>
              </div>
              <UButton
                icon="i-lucide-x"
                color="neutral"
                variant="ghost"
                size="sm"
                @click="isDetailOpen = false"
              />
            </div>

            <div class="space-y-4">
              <div class="bg-gray-50 dark:bg-gray-800 rounded-lg p-4">
                <p class="text-sm font-medium text-gray-900 dark:text-white">
                  {{ selectedRegion.pinIds.length }} Laternen
                </p>
                <p class="text-xs text-gray-500 mt-1">
                  {{ selectedRegion.shapes.length }} {{ selectedRegion.shapes.length === 1 ? 'Bereich' : 'Bereiche' }}
                  · Erstellt am {{ selectedRegion.createdAt }}
                </p>
              </div>

              <!-- Bereich erweitern Button -->
              <UButton
                icon="i-lucide-plus"
                color="primary"
                variant="soft"
                block
                @click="startExtending(selectedRegion)"
              >
                Bereiche hinzufügen
              </UButton>

              <div>
                <h4 class="text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
                  Laternen-IDs
                </h4>
                <div class="max-h-96 overflow-y-auto space-y-1">
                  <div
                    v-for="pinId in selectedRegion.pinIds"
                    :key="pinId"
                    class="text-xs bg-gray-100 dark:bg-gray-800 px-3 py-1.5 rounded-md text-gray-700 dark:text-gray-300 font-mono"
                  >
                    {{ pinId }}
                  </div>
                </div>
              </div>

              <UButton
                icon="i-lucide-trash-2"
                color="error"
                variant="soft"
                block
                @click="deleteRegion(selectedRegion.id); isDetailOpen = false"
              >
                Region löschen
              </UButton>
            </div>
          </div>
        </template>
      </USlideover>
    </template>
  </UDashboardPanel>
</template>