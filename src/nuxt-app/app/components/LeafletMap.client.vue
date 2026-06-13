<script setup lang="ts">
import { ref, shallowRef, watch, onBeforeUnmount } from 'vue'
import 'leaflet/dist/leaflet.css'
import 'leaflet.markercluster/dist/MarkerCluster.css'
import 'leaflet.markercluster/dist/MarkerCluster.Default.css'
import L from 'leaflet'
// import markerIconUrl from 'leaflet/dist/images/marker-icon.png?url'
// import markerIcon2xUrl from 'leaflet/dist/images/marker-icon-2x.png?url'
// import markerShadowUrl from 'leaflet/dist/images/marker-shadow.png?url'

import 'leaflet.markercluster'
import { LMap, LTileLayer } from '@vue-leaflet/vue-leaflet'
import * as turf from '@turf/turf'


// delete (L.Icon.Default.prototype as any)._getIconUrl
// L.Icon.Default.mergeOptions({
//   iconUrl: markerIconUrl,
//   iconRetinaUrl: markerIcon2xUrl,
//   shadowUrl: markerShadowUrl,
//   iconSize: [76, 100],        // Standard: [25, 41]
//   iconAnchor: [25, 82],      // Standard: [12, 41] — Spitze des Pins
//   popupAnchor: [1, -68],     // Standard: [1, -34]
//   shadowSize: [82, 82],      // Standard: [41, 41]
// })

// --- Typen ---
export interface DrawnShape {
  coordinates: [number, number][] // [lat, lng][]
}

export interface RegionData {
  shapes: DrawnShape[]
  pinIds: string[]
}

// Props
const props = defineProps<{
  searchQuery: string
  regionColor?: string
  savedRegions?: Array<{
    id: string
    name: string
    color: string
    shapes: DrawnShape[]
    pinIds: string[]
  }>
}>()

// Two-way binding für den Zeichenmodus
const isDrawingMode = defineModel<boolean>('isDrawingMode', { default: false })

// Events
const emit = defineEmits<{
  'region-drawn': [payload: RegionData]
  'pin-click': [payload: { pid: number, lat: number, long: number, light_point_nr: string, brightness: number }]
}>()

const mapRef = ref(null)
const zoom = ref(13)
const center = ref<[number, number]>([50.927, 11.586])
const url = 'https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png'
const attribution = '&copy; <a href="https://carto.com/">CARTO</a>'

// Punkte laden: [lat, lng, id, brightness]
const allPoints = shallowRef<[number, number, number, string, number][]>([])

async function loadPoints() {
  try {
    const response = await $fetch<{ rows: any[], success: boolean }>('/api/dbController')
    console.log('[loadPoints] API Response:', response)

    if (response.success && Array.isArray(response.rows)) {
      allPoints.value = response.rows.map((row: any) => [
        row.pid,
        row.lat,
        row.long,
        row.light_point_nr,
        row.brightness ?? 100
      ])
      console.log('[loadPoints] allPoints gesetzt:', allPoints.value.length)
    } else {
      console.warn('[loadPoints] Unerwartetes Format:', response)
    }
  } catch (e) {
    console.error('[loadPoints] Fehler:', e)
  }
}

loadPoints()

// Pin-Info Panel
const isOpen = ref(false)
const selectedPin = ref<{ pid: number, lat: number, long: number, light_point_nr: string, brightness: number } | null>(null)

// Zeichenvariablen
let map: L.Map | null = null
let clusterGroup: L.MarkerClusterGroup | null = null
let isDrawing = false
let currentPolyline: L.Polyline | null = null
let drawCoords: L.LatLng[] = []

// Gezeichnete Formen (mehrere Lassos pro Session)
const drawnShapes = ref<DrawnShape[]>([])
const drawnShapeLayers: L.Polygon[] = []

// Live-Zähler
const currentPinCount = ref(0)

// Gespeicherte Region-Layer
const regionLayers: L.Layer[] = []

// --- Hilfsfunktionen ---
const filteredPoints = computed(() => {
  if (!props.searchQuery) {
    return allPoints.value
  }
  return allPoints.value.filter(point => 
    point[3].toLowerCase().includes(props.searchQuery.toLowerCase())
  )
})

function updateMarkers() {
  if (!clusterGroup) return

  // 1. Alte Marker komplett von der Karte fegen
  clusterGroup.clearLayers()

  // 2. Die aktuell gefilterten Marker neu bauen
  const markers: L.Marker[] = []
  filteredPoints.value.forEach(([pid, lat, long, light_point_nr, brightness]) => {
    const marker = L.marker([lat, long], {

    })
    marker.on('click', (e) => {
      L.DomEvent.stopPropagation(e)
      selectedPin.value = { pid, lat, long, light_point_nr, brightness}
      isOpen.value = true
      emit('pin-click', { pid, lat, long, light_point_nr, brightness})
    })
    markers.push(marker)
  })
  clusterGroup!.addLayers(markers)
}

function setCursorStyle(drawing: boolean) {
  const container = map?.getContainer()
  if (!container) return
  container.style.cursor = drawing ? 'crosshair' : ''
}

function findPinsInShapes(shapes: DrawnShape[]): string[] {
  const seen = new Set<string>()
  const selectedIds: string[] = []

  shapes.forEach(({ coordinates }) => {
    // Turf: [lng, lat], Leaflet: [lat, lng]
    const polygonCoords = coordinates.map(([lat, lng]) => [lng, lat] as [number, number])
    polygonCoords.push(polygonCoords[0]!) // schließen
    const turfPolygon = turf.polygon([polygonCoords])

    filteredPoints.value.forEach(([pid, lat, long, light_point_nr]) => {
      if (seen.has(light_point_nr)) return
      const turfPoint = turf.point([long, lat])
      if (turf.booleanPointInPolygon(turfPoint, turfPolygon)) {
        selectedIds.push(light_point_nr)
        seen.add(light_point_nr)
      }
    })
  })

  return selectedIds
}

function updatePinCount() {
  currentPinCount.value = findPinsInShapes(drawnShapes.value).length
}

function clearDrawnShapes() {
  drawnShapeLayers.forEach(l => { if (map) map.removeLayer(l) })
  drawnShapeLayers.length = 0
  drawnShapes.value = []
  currentPinCount.value = 0
}

function undoLastShape() {
  if (drawnShapes.value.length === 0) return
  drawnShapes.value.pop()
  const layer = drawnShapeLayers.pop()
  if (layer && map) map.removeLayer(layer)
  updatePinCount()
}

function finishDrawing() {
  if (drawnShapes.value.length === 0) return

  const pinIds = findPinsInShapes(drawnShapes.value)

  emit('region-drawn', {
    shapes: [...drawnShapes.value],
    pinIds
  })

  clearDrawnShapes()
  isDrawingMode.value = false
}

function cancelDrawing() {
  clearDrawnShapes()
  isDrawingMode.value = false
}

defineExpose({ undoLastShape, finishDrawing, cancelDrawing })

watch(filteredPoints, () => {
  updateMarkers()
})

// Watch für Zeichenmodus-Wechsel
watch(isDrawingMode, (val) => {
  setCursorStyle(val)
  if (!val && map) {
    map.dragging.enable()
    if (drawnShapes.value.length > 0) {
      clearDrawnShapes()
    }
  }
})

// Gespeicherte Regionen auf der Karte anzeigen
watch(() => props.savedRegions, (regions) => {
  if (!map) return
  regionLayers.forEach(l => map!.removeLayer(l))
  regionLayers.length = 0

  regions?.forEach((region) => {
    region.shapes.forEach(({ coordinates }) => {
      const polygon = L.polygon(
        coordinates.map(([lat, lng]) => [lat, lng] as L.LatLngTuple),
        {
          color: region.color,
          fillColor: region.color,
          fillOpacity: 0.15,
          weight: 2
        }
      )
      polygon.bindTooltip(region.name, { sticky: true })
      polygon.addTo(map!)
      regionLayers.push(polygon)
    })
  })
}, { deep: true, immediate: false })

function onMapReady() {
  map = (mapRef.value as any).leafletObject

  clusterGroup = L.markerClusterGroup({
    maxClusterRadius: (zoom) => {
      if (zoom >= 30) return 5
      if (zoom >= 13) return 40
      return 80
    }
  })

  map!.addLayer(clusterGroup)

  updateMarkers()

  // Gespeicherte Regionen initial rendern
  if (props.savedRegions?.length) {
    props.savedRegions.forEach((region) => {
      region.shapes.forEach(({ coordinates }) => {
        const polygon = L.polygon(
          coordinates.map(([lat, lng]) => [lat, lng] as L.LatLngTuple),
          {
            color: region.color,
            fillColor: region.color,
            fillOpacity: 0.15,
            weight: 2
          }
        )
        polygon.bindTooltip(region.name, { sticky: true })
        polygon.addTo(map!)
        regionLayers.push(polygon)
      })
    })
  }

  // --- LASSO ZEICHNEN ---

  map!.on('mousedown', (e: L.LeafletMouseEvent) => {
    if (!isDrawingMode.value) return

    isDrawing = true
    drawCoords = [e.latlng]
    map!.dragging.disable()

    const color = props.regionColor || '#3b82f6'
    currentPolyline = L.polyline([e.latlng], {
      color,
      weight: 3,
      opacity: 0.7,
      dashArray: '6, 8'
    }).addTo(map!)
  })

  map!.on('mousemove', (e: L.LeafletMouseEvent) => {
    if (!isDrawing || !currentPolyline) return
    drawCoords.push(e.latlng)
    currentPolyline.addLatLng(e.latlng)
  })

  map!.on('mouseup', () => {
    if (!isDrawing) return
    isDrawing = false
    map!.dragging.enable()

    if (!currentPolyline || !isDrawingMode.value) return

    // Mindestens 3 Punkte für ein Polygon
    if (drawCoords.length < 3) {
      if (currentPolyline) {
        map!.removeLayer(currentPolyline)
        currentPolyline = null
      }
      return
    }

    // Polyline entfernen
    map!.removeLayer(currentPolyline)
    currentPolyline = null

    // Polygon-Vorschau zeichnen
    const color = props.regionColor || '#3b82f6'
    const polygon = L.polygon(
      drawCoords.map(ll => [ll.lat, ll.lng] as L.LatLngTuple),
      {
        color,
        fillColor: color,
        fillOpacity: 0.2,
        weight: 2
      }
    ).addTo(map!)

    // Shape speichern
    const shapeData: DrawnShape = {
      coordinates: drawCoords.map(ll => [ll.lat, ll.lng] as [number, number])
    }

    drawnShapes.value.push(shapeData)
    drawnShapeLayers.push(polygon)
    updatePinCount()

    drawCoords = []
  })
}

onBeforeUnmount(() => {
  if (map) {
    map.off('mousedown')
    map.off('mousemove')
    map.off('mouseup')
  }
})
</script>

<template>
  <div class="h-full w-full relative">
    <!-- Zeichenmodus-Toolbar -->
    <Transition name="fade">
      <div
        v-if="isDrawingMode"
        class="absolute top-3 left-1/2 -translate-x-1/2 z-[1000] flex items-center gap-2 select-none"
      >
        <!-- Info-Badge -->
        <div class="bg-blue-600 text-white px-4 py-2 rounded-full shadow-lg text-sm font-medium flex items-center gap-2 pointer-events-none">
          <UIcon name="i-lucide-pencil" class="w-4 h-4" />
          <span v-if="drawnShapes.length === 0">Zeichne einen Bereich auf der Karte</span>
          <span v-else>{{ drawnShapes.length }} {{ drawnShapes.length === 1 ? 'Bereich' : 'Bereiche' }} · {{ currentPinCount }} Laternen</span>
        </div>

        <!-- Aktionen (nur wenn Formen vorhanden) -->
        <template v-if="drawnShapes.length > 0">
          <UButton
            icon="i-lucide-undo-2"
            color="neutral"
            variant="solid"
            size="sm"
            class="rounded-full shadow-lg"
            @click="undoLastShape"
          >
            Rückgängig
          </UButton>
          <UButton
            icon="i-lucide-check"
            color="success"
            variant="solid"
            size="sm"
            class="rounded-full shadow-lg"
            @click="finishDrawing"
          >
            Fertig
          </UButton>
          <UButton
            icon="i-lucide-x"
            color="error"
            variant="soft"
            size="sm"
            class="rounded-full shadow-lg"
            @click="cancelDrawing"
          />
        </template>
      </div>
    </Transition>

    <l-map
      ref="mapRef"
      :zoom="zoom"
      :center="center"
      style="height: 100%; width: 100%;"
      @ready="onMapReady"
      maxZoom: 22
    >
      <l-tile-layer :url="url" :attribution="attribution" />
    </l-map>

    <!-- Pin-Info Panel -->
    <Transition name="slide-up">
      <div
        v-if="isOpen && selectedPin"
        class="absolute bottom-0 left-0 right-0 bg-white dark:bg-gray-900 shadow-2xl rounded-t-2xl z-[1000]"
      >
        <div class="flex items-center justify-between px-4 pt-4 pb-2 border-b border-gray-200 dark:border-gray-700">
          <h2 class="text-base font-semibold">Laterne: {{ selectedPin.light_point_nr }}</h2>
          <UButton
            icon="i-lucide-x"
            color="neutral"
            variant="ghost"
            size="sm"
            @click="isOpen = false"
          />
        </div>

        <div class="px-4 py-3 text-sm text-gray-600 dark:text-gray-400">
          <p><span class="font-medium text-gray-900 dark:text-white">Breitengrad:</span> {{ selectedPin.lat.toFixed(5) }}</p>
          <p><span class="font-medium text-gray-900 dark:text-white">Längengrad:</span> {{ selectedPin.long.toFixed(5) }}</p>
        </div>
      </div>
    </Transition>
  </div>
</template>

<style scoped>
.slide-up-enter-active,
.slide-up-leave-active {
  transition: transform 0.25s ease;
}
.slide-up-enter-from,
.slide-up-leave-to {
  transform: translateY(100%);
}
.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.2s ease;
}
.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}
</style>