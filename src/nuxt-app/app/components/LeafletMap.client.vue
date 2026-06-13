<script setup lang="ts">
import 'leaflet/dist/leaflet.css'
import 'leaflet.markercluster/dist/MarkerCluster.css'
import 'leaflet.markercluster/dist/MarkerCluster.Default.css'
import L from 'leaflet'
import 'leaflet.markercluster'
import { LMap, LTileLayer } from '@vue-leaflet/vue-leaflet'

const mapRef = ref(null)
const zoom = ref(13)
const center = ref<[number, number]>([50.927, 11.586])
const url = 'https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png'
const attribution = '&copy; <a href="https://carto.com/">CARTO</a>'

let points = [] as [number, number][]

const { data } = await useFetch('/data/data_wgs84.json')

points = data.map(item => [item.lat, item.long]);


// Koordinaten der Straßenbeleuchtungen


function onMapReady() {
  const map = (mapRef.value as any).leafletObject

  const clusterGroup = L.markerClusterGroup()

  points.forEach(([lat, lng]) => {
    clusterGroup.addLayer(L.marker([lat, lng]))
  })

  map.addLayer(clusterGroup)
}

</script>

<template>
  <div class="h-full w-full">
    <l-map
      ref="mapRef"
      :zoom="zoom"
      :center="center"
      style="height: 100%; width: 100%;"
      @ready="onMapReady"
    >
      <l-tile-layer :url="url" :attribution="attribution" />
    </l-map>
  </div>
</template>