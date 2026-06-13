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
    def __init__(self, tx_pin=16, rx_pin=17, baudrate=9600):
        # UART0 mit GP16 (TX) und GP17 (RX)
        self.uart = UART(
            0,
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

