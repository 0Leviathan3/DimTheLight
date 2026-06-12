// Program.cs — Einstiegspunkt
//
// Versucht den echten Feig CPR30-LCN9620 USB-Reader zu öffnen.
// Falls nicht verfügbar (kein USB / kein Tag), läuft der Simulations-Modus.
//
// Feig-Reader setzt ISO 15693 voraus (OSRAM NFC2 und NFC3 Chips).

using System;
using OsramNfcTest;

// ── Feig-Reader öffnen ────────────────────────────────────────────────────────

INfcReader reader;
bool useReal = true;

try
{
    reader = FeigCpr30Reader.Open();
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"[WARNUNG] Echter Reader nicht verfügbar: {ex.Message}");
    Console.WriteLine("[WARNUNG] Starte Simulations-Modus.\n");
    Console.ResetColor();
    useReal = false;
    reader  = BuildSimulatedNfc2Reader();
}

using (reader)
{
    if (useReal)
    {
        RunRealReader(reader);
    }
    else
    {
        // Simulierter Modus: Beispieldaten aus NfcReadTest/NfcWriteTest
        NfcReadTestMain.RunSimulated();
    }
}

// ── Echten Reader verwenden ───────────────────────────────────────────────────

static void RunRealReader(INfcReader reader)
{
    // Kurze Pause nach der Tag-Erkennung
    System.Threading.Thread.Sleep(100);

    // ── NFC2: kompletten Chip-Dump lesen (0–1023 Bytes via ISO 15693, max Block 255)
    Console.WriteLine("\n═══════════════════════════════════════");
    Console.WriteLine("  NFC2 — Chip-Dump lesen (1024 Bytes)");
    Console.WriteLine("═══════════════════════════════════════");
    try
    {
        // ISO 15693 mit 1-Byte-Blockadressen unterstützt max. 256 Blöcke × 4 = 1024 Bytes.
        // NFC2-Chips können bis 2048 Bytes haben; für den vollständigen Dump ggf. zweimal
        // lesen (Blöcke 0–255 und 256–511, letzteres erfordert Extended Protocol, s. FeigReader.cs).
        byte[] raw = reader.Read(0, 1024);
        NfcReadTest.ReadNfc2(raw);
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[FEHLER] NFC2-Lesen: {ex.Message}");
        Console.ResetColor();
    }

    // ── NFC3: 52-Byte Tag-Speicher lesen
    Console.WriteLine("\n═══════════════════════════════════════");
    Console.WriteLine("  NFC3 — Tag-Speicher lesen (52 Bytes)");
    Console.WriteLine("═══════════════════════════════════════");
    try
    {
        byte[] raw3 = reader.Read(0, 52);
        NfcReadTest.ReadNfc3(raw3);
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[FEHLER] NFC3-Lesen: {ex.Message}");
        Console.ResetColor();
    }

    // ── NFC3: Beispiel-Schreiben (auskommentieren um tatsächlich zu schreiben)
    //
    // var values = new Nfc3Values
    // {
    //     Gtin         = 0x000400001234L,
    //     FwVersion    = 5,
    //     HwVersion    = 2,
    //     Current      = 700,
    //     OnOffCounter = 42,
    //     ...
    // };
    // byte[] toWrite = NfcWriteTest.BuildNfc3TagData(values);
    // Console.WriteLine("\nSchreibe NFC3 Tag-Daten...");
    // reader.Write(0, toWrite);
    // Console.WriteLine("Schreiben abgeschlossen.");
}

// ── Simulations-Helfer ────────────────────────────────────────────────────────

static SimulatedNfcReader BuildSimulatedNfc2Reader()
{
    byte[] mem = new byte[2048];
    Array.Fill(mem, (byte)0xFF);

    // Device ID (Adresse 0, 20 Bytes)
    mem[0] = 0x07;                                  // ManufacturerCode = OSRAM
    mem[1] = 0x00; mem[2] = 0x04;                   // GTIN MSBs
    mem[3] = 0x00; mem[4] = 0x00;
    mem[5] = 0x12; mem[6] = 0x34;                   // GTIN = 0x000400001234
    mem[7] = 0x03;  mem[8]  = 0x01;                 // Fw 3.1
    mem[9] = 0x02;  mem[10] = 0x01;                 // Hw 2, NfcVers 1
    mem[11] = 0x02;                                 // MemoryBankCount = 2
    mem[12] = 0x00; mem[13] = 0x60;                 // StatusRegAddr  = 0x0060
    mem[14] = 0x00; mem[15] = 0x64;                 // ControlRegAddr = 0x0064
    mem[16] = 0x00; mem[17] = 0x80;                 // ProtectedMemAddr = 0x0080
    ushort devCrc = NfcReadTest.CalcCrc16(mem.AsSpan(0, 18));
    mem[18] = (byte)(devCrc >> 8);
    mem[19] = (byte)(devCrc & 0xFF);

    // TOC (Adresse 20): 2 Einträge à 6 Bytes + CRC
    mem[20] = 0x00; mem[21] = 0x01; mem[22] = 0x01;
    mem[23] = 24;   mem[24] = 0x00; mem[25] = 0x80;
    mem[26] = 0x00; mem[27] = 0xC9; mem[28] = 0x01;
    mem[29] = 16;   mem[30] = 0x00; mem[31] = 0xA0;
    ushort tocCrc = NfcReadTest.CalcCrc16(mem.AsSpan(20, 12));
    mem[32] = (byte)(tocCrc >> 8);
    mem[33] = (byte)(tocCrc & 0xFF);

    // Status Register (0x60): ECG an
    mem[0x60] = 0xA0;  mem[0x61] = 0x00;
    ushort srCrc = NfcReadTest.CalcCrc16(mem.AsSpan(0x60, 2));
    mem[0x62] = (byte)(srCrc >> 8);
    mem[0x63] = (byte)(srCrc & 0xFF);

    // Control Register (0x64): PRR=URR=MLR=0
    Array.Fill(mem, (byte)0x00, 0x64, 9);
    ushort crCrc = NfcReadTest.CalcCrc16(mem.AsSpan(0x64, 9));
    mem[0x6D] = (byte)(crCrc >> 8);
    mem[0x6E] = (byte)(crCrc & 0xFF);

    return new SimulatedNfcReader(mem);
}
