import json
# geogr. Koordinaten zwischen Koordinatenreferenzsysteme transformieren
from pyproj import Transformer

INPUT_GEOJSON = "Leuchtstelle2.geojson"
OUTPUT_JSON = "data_wgs84.json"

INPUT_SYSTEM = "epsg:25832"     # ETRS89 (Rechtswert, Hochwert)
OUTPUT_SYSTEM = "epsg:4326"     # WGS84 (Längengrad, Breitengrad)

# Koordinaten Transformator einrichten
transformer = Transformer.from_crs(INPUT_SYSTEM, OUTPUT_SYSTEM, always_xy=True)

try:
    with open(INPUT_GEOJSON, "r", encoding="utf-8") as f:
        geojson_data = json.load(f)
except FileNotFoundError:
    print(f"Fehler: Die Datei '{INPUT_GEOJSON}' wurde nicht gefunden.")
    exit()

filtered_data = []

for light in geojson_data.get("features", []):
    # Sicherstellen, dass Geometrie und Eigenschaften existieren
    if light.get("geometry") and light.get("properties"):
        
        properties = light["properties"]
        geometry = light["geometry"]
        
        if geometry.get("type") == "Point":
            # UTM-Koordinaten extrahieren [Rechtswert, Hochwert]
            utm_x, utm_y = geometry["coordinates"]
            # Umrechnen in WGS84 [Längengrad (lon), Breitengrad (lat)]
            lon, lat = transformer.transform(utm_x, utm_y)
            
            light_data = {
                "id_jena": properties.get("nummer_jena"), # ID der Laterne in Jena
                "gid": properties.get("gid"),             # interne Datenbank ID
                "lat": round(lat, 6), # Breitengrad
                "long": round(lon, 6)  # Längengrad
            }
            filtered_data.append(light_data)

# temp json
with open(OUTPUT_JSON, "w", encoding="utf-8") as f:
    json.dump(filtered_data, f, indent=4, ensure_ascii=False)