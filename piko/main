# main.py  --  laeuft automatisch beim Einschalten des Pico
# Zeigt, wie man die lampe-Bibliothek benutzt.

from time import sleep

from machine import UART, Pin
import time


# Konfiguriere UART1 (Pico TX = Pin 4, Pico RX = Pin 5)
# Die Standard-Baudrate für den RAK3172 (RUI3) ist 115200.
lora_uart = UART(0, baudrate=115200, tx=Pin(0), rx=Pin(1))

# lampe.py  --  Bibliothek fuer die Lampen-Kommunikation (laeuft auf dem Pico)
#
# Diese Datei kommt auf den Pico. Sie wird nicht direkt gestartet, sondern
# von main.py importiert. Sie kapselt die ganze UART-Kommunikation mit dem Pi.
#
# Benutzung (in main.py):
#     from lampe import Lampe
#     lampe = Lampe()
#     helligkeit, dauer = lampe.lese()      # Werte vom Pi holen
#     ok = lampe.setze_helligkeit(80)       # Helligkeit auf 80 setzen

from machine import UART, Pin
from time import sleep
import json

class Lampe:
    def __init__(self, tx_pin=4, rx_pin=5, baudrate=9600):
        # UART0 mit GP16 (TX) und GP17 (RX)
        self.uart = UART(
            1,
            baudrate=baudrate,
            tx=Pin(tx_pin),
            rx=Pin(rx_pin),
            bits=8,
            parity=None,
            stop=1,
        )

    def _frage(self, befehl):
        """Sendet einen Befehl an den Pi und gibt die Antwort als String zurueck (oder None)."""
        # alten Puffer leeren, damit keine alte Antwort drin haengt
        while self.uart.any():
            self.uart.read()

        self.uart.write(befehl + "\n")
        sleep(0.5)

        antwort = self.uart.readline()
        if not antwort:
            return None
        try:
            return antwort.decode("utf-8").strip()
        except Exception:
            return None

    def lese(self):
        """Liest aktuelle Helligkeit und Dauer vom Pi.

        Rueckgabe: (helligkeit, dauer) als Strings, oder None bei Fehler.
        Beispiel-Antwort vom Pi: "64 00;00 56"
        """
        antwort = self._frage("GET")
        if not antwort or ";" not in antwort:
            return None
        helligkeit, dauer = antwort.split(";", 1)
        return helligkeit.strip(), dauer.strip()

    def setze_helligkeit(self, wert):
        """Sagt dem Pi, die Helligkeit auf <wert> zu setzen.

        Rueckgabe: True wenn der Pi mit "OK" geantwortet hat, sonst False.
        """
        antwort = self._frage("SET " + str(wert))
        if antwort and antwort.startswith("OK"):
            return True
        return False

    def setze_programm(self, brightness_on, brightness_after_hours):
        """Schickt ein komplettes Helligkeits-Programm an den Pi.

        brightness_on: Zahl, Grundhelligkeit beim Einschalten
        brightness_after_hours: Liste von Paaren (stunde, helligkeit), z.B.
            [(6, 100), (22, 50), (0, 20), (4, 10)]

        Wird als JSON uebertragen. Rueckgabe: True bei "OK", sonst False.
        """
        config = {
            "brightnessOn": brightness_on,
            "brightnessAfterHours": [
                {"hour": h, "thenBrightness": b} for (h, b) in brightness_after_hours
            ],
        }
        # json.dumps erzeugt eine einzige Zeile ohne Zeilenumbrueche -> passt fuer UART
        text = json.dumps(config)
        antwort = self._frage("SETCFG " + text)
        if antwort and antwort.startswith("OK"):
            return True
        return False


lampe = Lampe()
#lampe.setze_helligkeit(25)

# Konfiguriere UART1 (Pico TX = Pin 4, Pico RX = Pin 5)
# Die Standard-Baudrate für den RAK3172 (RUI3) ist 115200.
lora_uart = UART(0, baudrate=115200, tx=Pin(0), rx=Pin(1))

lamps_dict = \
        {"Lampe1_a": {"device_eui": "11DAF29BEE739281", "join_eui": "52FE2E02FF435854",
                      "app_key": "F9C8C14DB8B357FF1AD158233F2CFEBE"},
         "LED_2": {"device_eui": "058F765DEEE4C078", "join_eui": "D615D621ED9C765A",
                   "app_key": "2BF20406D9ECB278A922F2EE5D896916"}}

def routine():
    # Check whether UART communication works
    send_at_command("AT+VER=?")

    # Set the device specific WAN keys
    authorize_lamp(lamps_dict["Lampe1_a"])

    # Attempt a LoRaWAN join
    join_wan()

    # Check for messages from the WAN server
    while True:
        read_queue()
        time.sleep(10)


def send_at_command(cmd, wait_ms=2000):
    print(f"Sende: {cmd}")
    # Kommando senden mit Carriage Return und Line Feed
    lora_uart.write(cmd + "\r\n")
    time.sleep_ms(wait_ms)

    # Antwort lesen
    if lora_uart.any():
        response = lora_uart.read().decode('utf-8').strip()
        print(f"Antwort:\n{response}\n")
        return response
    else:
        print("Keine Antwort vom Modul.\n")
        return None

def authorize_lamp(devicekeys = lamps_dict["Lampe1_a"]):
    # 2. LoRaWAN-Modus aktivieren (1 = LoRaWAN, 0 = P2P)
    send_at_command("AT+NWM=1")

    send_at_command("AT+DEVEUI=" + devicekeys["device_eui"])
    send_at_command("AT+APPKEY=" + devicekeys["app_key"])
    send_at_command("AT+APPEUI=" + devicekeys["join_eui"])

def join_wan():
    # 4. Join-Prozess starten
    send_at_command("AT+JOIN=1:0:10:16", wait_ms=12000)

def read_queue():
    print("Sende Dummy-Uplink, um Downlink abzuholen...")
    lora_uart.write("AT+SEND=1:00\r\n")

    # Wir lauschen dynamisch für 10 Sekunden (10000 Millisekunden)
    timeout_ms = 10000
    start_time = time.ticks_ms()

    print("Lese Antworten aus dem Buffer (Warte bis zu 10 Sekunden)...")

    # Solange die 10 Sekunden nicht abgelaufen sind...
    while time.ticks_diff(time.ticks_ms(), start_time) < timeout_ms:
        if lora_uart.any():
            response = lora_uart.read().decode("utf-8").strip()
            
            print("LORAWAN:", response)
            
            if "RX_1" in response:
                data_hex = int(response.split(":")[-1], 16)
                lampe.setze_helligkeit(data_hex)
                print("Helligkeit gelesen und gesetzt:", data_hex)

        # Kurze Pause, um die CPU des Picos nicht zu 100% auszulasten
        time.sleep_ms(100)

    print("Lauschen beendet.")

routine()
