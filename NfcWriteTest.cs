// =============================================================================
// NfcWriteTest.cs — Schreibt alle relevanten NFC-Werte auf den Chip
//
// Verwendung NFC2 (offline, ECG ausgeschaltet):
//   var bytes = NfcWriteTest.BuildNfc2ControlRegister(prrBitmask: 0b11, urr: 0, mlr: 0);
//   reader.Write(controlRegAddr, bytes);        // PRR setzen → ECG programmiert beim nächsten Einschalten
//
// Verwendung NFC2 (online, ECG eingeschaltet):
//   Schritt 1: PollBis(PRR == URR == MLR == 0)
//   Schritt 2: byte[] cr = NfcWriteTest.BuildNfc2ControlRegister(prr: 0, urr: 0, mlr: challenge);
//              reader.Write(controlRegAddr, cr);
//   Schritt 3: PollBis(MLR == (challenge + 127) & 0xFF)
//   Schritt 4: reader.Write(bankAddr, NfcWriteTest.BuildMemoryBank(data));
//              reader.Write(controlRegAddr, NfcWriteTest.BuildNfc2ControlRegister(prr: bitmask, urr: 0, mlr: 0));
//   Schritt 5: PollBis(PRR == MLR == 0)
//
// Verwendung NFC3:
//   var values = new Nfc3Values { Current = 700, ... };
//   byte[] bytes = NfcWriteTest.BuildNfc3TagData(values);
//   for (int block = 0; block < 52 / 4; block++)
//       reader.Write(block * 4, bytes[(block*4)..(block*4+4)]);
//
// Abhängigkeiten: nur System.*
// =============================================================================

using System;
using System.Runtime.InteropServices;

namespace OsramNfcTest;

// =============================================================================
// STRUCTS — identisch zu NfcReadTest.cs (exakte Chip-Byte-Reihenfolge)
// =============================================================================

// NFC2 — Device Identification Registers (20 Bytes, BIG-ENDIAN)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct Nfc2DeviceIdW
{
    public byte ManufacturerCode;
    public byte Gtin0, Gtin1, Gtin2, Gtin3, Gtin4, Gtin5; // [1..6] BE
    public byte FwMajor, FwMinor, HwMajor, NfcVersion;
    public byte MemoryBankCount;
    public byte StatusAddrHi,   StatusAddrLo;    // [12..13] BE
    public byte ControlAddrHi,  ControlAddrLo;   // [14..15] BE
    public byte ProtectedAddrHi, ProtectedAddrLo; // [16..17] BE
    public byte Crc16Hi, Crc16Lo;                 // [18..19] BE — vom Schreiber befüllen
}

// NFC2 — TOC-Eintrag (6 Bytes, BIG-ENDIAN)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct Nfc2TocEntryW
{
    public byte MbAttribut;         // bit4 = NoCrc
    public byte MpcId;              // 201–207 = Monitoring
    public byte MpcVersion;
    public byte MbLength;           // Nutzdaten-Länge ohne CRC
    public byte MbBaseAddrHi;
    public byte MbBaseAddrLo;
}

// NFC2 — Control Register (11 Bytes, BIG-ENDIAN)
// Schreiben = PRR/URR/MLR setzen + CRC berechnen
[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct Nfc2ControlRegisterW
{
    public byte PrrByte0, PrrByte1, PrrByte2, PrrByte3; // [0..3] PRR BE
    public byte UrrByte0, UrrByte1, UrrByte2, UrrByte3; // [4..7] URR BE
    public byte Mlr;                                      // [8]
    public byte Crc16Hi, Crc16Lo;                         // [9..10] BE
}

// NFC3 — Kompletter Tag-Speicher (52 Bytes, LITTLE-ENDIAN)
// Alle Felder werden per BuildNfc3TagData() befüllt
[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct Nfc3TagDataW
{
    public ushort DutyCycleHB1_8;   // [0..1]   CLO High-Bits aller 8 Kanäle (LE)
    public ushort DutyCycleImax;    // [2..3]   PWM-Duty-Cycle bei Imax (LE)
    public byte   DutyCycleLB5;     // [4]      CLO Kanal 5
    public byte   DutyCycleLB6;     // [5]      CLO Kanal 6
    public byte   DutyCycleLB7;     // [6]      CLO Kanal 7
    public byte   DutyCycleLB8;     // [7]      CLO Kanal 8
    public byte   DutyCycleLB1;     // [8]      CLO Kanal 1
    public byte   DutyCycleLB2;     // [9]      CLO Kanal 2
    public byte   DutyCycleLB3;     // [10]     CLO Kanal 3
    public byte   DutyCycleLB4;     // [11]     CLO Kanal 4
    public uint   AccessCode2;      // [12..15] Schreibschutz-Code 2 (LE)
    public byte   Res16, Res17, Res18, Res19, Res20, Res21, Res22, Res23; // [16..23]
    public ushort Current;          // [24..25] Betriebsstrom in mA (LE)
    public byte   Res26, Res27;     // [26..27]
    public byte   HwVersion;        // [28]
    public byte   FwVersion;        // [29]
    public byte   Gtin0, Gtin1, Gtin2, Gtin3, Gtin4, Gtin5; // [30..35] LITTLE-ENDIAN!
    public ushort SwitchOffTime;    // [36..37] ECC-kodiert (LE)
    public ushort OnOffCounter;     // [38..39] (LE)
    public uint   OperatingTime;    // [40..43] ECC-kodiert (LE)
    public byte   Assignment;       // [44]     nur Bits [3:0]
    public byte   CheckWriteProt;   // [45]
    public ushort PwmPeriodImax;    // [46..47] (LE)
    public uint   AccessCode1;      // [48..51] Schreibschutz-Code 1 (LE)
}

// =============================================================================
// Eingabe-Werte für NFC3-Schreiboperation (alle in menschenlesbaren Einheiten)
// =============================================================================
class Nfc3Values
{
    public long   Gtin          = 0;
    public byte   FwVersion     = 0;
    public byte   HwVersion     = 0;
    public ushort Current       = 0;        // in mA
    public ushort PwmPeriodImax = 0;
    public ushort DutyCycleImax = 0;
    public ushort OnOffCounter  = 0;
    public byte   Assignment    = 0;        // nur Bits [3:0]

    public uint   OpTimeHours   = 0;        // Betriebszeit: Stunden (max 262140)
    public uint   OpTimeMinutes = 0;        // Betriebszeit: Minuten (0–59)

    // CLO-Einstellungen pro Kanal (8 Kanäle)
    // Level: 51–100 (Prozent), Time: 0–122880 ms (Schritte: 8192 ms)
    public uint CloLvl1 = 100; public uint CloTime1 = 0;
    public uint CloLvl2 = 100; public uint CloTime2 = 0;
    public uint CloLvl3 = 100; public uint CloTime3 = 0;
    public uint CloLvl4 = 100; public uint CloTime4 = 0;
    public uint CloLvl5 = 100; public uint CloTime5 = 0;
    public uint CloLvl6 = 100; public uint CloTime6 = 0;
    public uint CloLvl7 = 100; public uint CloTime7 = 0;
    public uint CloLvl8 = 100; public uint CloTime8 = 0;

    public uint   AccessCode1   = 0;        // 0 = kein Schreibschutz
    public uint   AccessCode2   = 0;
}

// =============================================================================
// Schreib-Logik
// =============================================================================
static class NfcWriteTest
{
    // -------------------------------------------------------------------------
    // NFC2: Erzeugt 11 Bytes für das Control Register (mit CRC)
    //
    // prr: Bitmask welche Memory Banks programmiert werden sollen (Bit N = Bank N)
    // urr: Bitmask welche Memory Banks aktualisiert werden sollen (normalerweise 0)
    // mlr: Memory Lock Register (0 = idle, für Handshake: zufälliger Wert ≠ 0x81)
    // -------------------------------------------------------------------------
    public static byte[] BuildNfc2ControlRegister(uint prr, uint urr, byte mlr)
    {
        // Daten (9 Bytes) BIG-ENDIAN
        var data = new byte[9];
        data[0] = (byte)(prr >> 24);
        data[1] = (byte)(prr >> 16);
        data[2] = (byte)(prr >> 8);
        data[3] = (byte)(prr);
        data[4] = (byte)(urr >> 24);
        data[5] = (byte)(urr >> 16);
        data[6] = (byte)(urr >> 8);
        data[7] = (byte)(urr);
        data[8] = mlr;

        ushort crc = CalcCrc16(data);
        var result = new byte[11];
        data.CopyTo(result, 0);
        result[9]  = (byte)(crc >> 8);  // CRC High-Byte (BE)
        result[10] = (byte)(crc & 0xFF); // CRC Low-Byte
        return result;
    }

    // -------------------------------------------------------------------------
    // NFC2: Erzeugt die Bytes für eine Memory Bank (Nutzdaten + CRC am Ende)
    //
    // data: die rohen Nutzdaten (Länge muss MbLength entsprechen)
    // noCrc: wenn true, wird kein CRC angehängt (NoCrc-Bank, MbAttribut bit4=1)
    // -------------------------------------------------------------------------
    public static byte[] BuildMemoryBank(byte[] data, bool noCrc = false)
    {
        if (noCrc)
            return (byte[])data.Clone();

        ushort crc = CalcCrc16(data);
        var result = new byte[data.Length + 2];
        data.CopyTo(result, 0);
        result[data.Length]     = (byte)(crc >> 8);   // CRC High-Byte (BE)
        result[data.Length + 1] = (byte)(crc & 0xFF); // CRC Low-Byte
        return result;
    }

    // -------------------------------------------------------------------------
    // NFC2: PRR-Bitmask aus Memory-Bank-Indizes berechnen
    //       Bit N = 1 bedeutet "Bank N soll programmiert werden"
    // -------------------------------------------------------------------------
    public static uint BuildPrrBitmask(params int[] bankIndices)
    {
        uint mask = 0;
        foreach (int i in bankIndices)
            mask |= 1u << i;
        return mask;
    }

    // -------------------------------------------------------------------------
    // NFC3: Erzeugt den kompletten 52-Byte-Chip-Inhalt aus Nfc3Values.
    //       Das Array kann blockweise à 4 Bytes auf den Chip geschrieben werden.
    //
    // Schreib-Reihenfolge (LowLevelOperationsNfc3.Write):
    //   for (int block = 0; block < 52 / 4; block++)
    //       reader.Write(block * 4, result[(block*4)..(block*4+4)]);
    //   (Blöcke 3, 12 = AccessCode-Blöcke werden intern gesperrt)
    // -------------------------------------------------------------------------
    public static byte[] BuildNfc3TagData(Nfc3Values v)
    {
        var tag = new Nfc3TagDataW();

        // Grundwerte
        tag.Current       = v.Current;
        tag.HwVersion     = v.HwVersion;
        tag.FwVersion     = v.FwVersion;
        tag.OnOffCounter  = v.OnOffCounter;
        tag.DutyCycleImax = v.DutyCycleImax;
        tag.PwmPeriodImax = v.PwmPeriodImax;
        tag.Assignment    = (byte)(v.Assignment & 0x0F);
        tag.AccessCode1   = v.AccessCode1;
        tag.AccessCode2   = v.AccessCode2;

        // GTIN Little-Endian (LSB zuerst)
        tag.Gtin0 = (byte)(v.Gtin);
        tag.Gtin1 = (byte)(v.Gtin >> 8);
        tag.Gtin2 = (byte)(v.Gtin >> 16);
        tag.Gtin3 = (byte)(v.Gtin >> 24);
        tag.Gtin4 = (byte)(v.Gtin >> 32);
        tag.Gtin5 = (byte)(v.Gtin >> 40);

        // Betriebszeit ECC-kodieren → OperatingTime + SwitchOffTime
        EncodeOperatingTime(v.OpTimeHours, v.OpTimeMinutes,
                            out uint opTime, out ushort switchOff);
        tag.OperatingTime = opTime;
        tag.SwitchOffTime = switchOff;

        // CLO-Einstellungen in Chip-Format konvertieren
        EncodeClo(v, ref tag);

        // Struct → Byte-Array
        var result = new byte[52];
        MemoryMarshal.Write(result.AsSpan(), in tag);
        return result;
    }

    // -------------------------------------------------------------------------
    // Hilfsmethode: Zeigt die Änderungen an einem bestehenden Chip-Dump an,
    //               die BuildNfc3TagData() vornehmen würde.
    // -------------------------------------------------------------------------
    public static void PrintNfc3Diff(byte[] existingRaw, Nfc3Values newValues)
    {
        byte[] newRaw = BuildNfc3TagData(newValues);
        Console.WriteLine("=== NFC3 Änderungen (alt → neu) ===");
        bool anyDiff = false;
        for (int i = 0; i < 52; i++)
        {
            if (existingRaw[i] != newRaw[i])
            {
                Console.WriteLine($"  Byte[{i:D2}] (0x{i:X2}): 0x{existingRaw[i]:X2} → 0x{newRaw[i]:X2}");
                anyDiff = true;
            }
        }
        if (!anyDiff)
            Console.WriteLine("  Keine Änderungen.");
    }

    // =========================================================================
    // Interne Hilfsmethoden (aus LowLevelOperationsNfc3.cs portiert)
    // =========================================================================

    // CRC16-CCITT-False (Poly=0x1021, Init=0xFFFF)
    static ushort CalcCrc16(byte[] data)
    {
        const ushort poly = 0x1021;
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            ushort tmp = (ushort)(((crc >> 8) ^ b) << 8);
            for (int i = 0; i < 8; i++)
                tmp = (tmp & 0x8000) != 0 ? (ushort)((tmp << 1) ^ poly) : (ushort)(tmp << 1);
            crc = (ushort)((crc << 8) ^ tmp);
        }
        return crc;
    }

    // ECC-Kodierung eines 4-bit Werts → 8-bit (aus TagContent.CalcDataPlusEccFromValue)
    static byte EccEncode(byte value)
    {
        byte result = 0;
        byte p2 = 0, p3 = 0, p4 = 0, p5 = 0;

        if ((value & 0x08) != 0) { p2 = 7; result |= 0x40; }
        if ((value & 0x04) != 0) { p3 = 6; result |= 0x20; }
        if ((value & 0x02) != 0) { p4 = 5; result |= 0x10; }
        if ((value & 0x01) != 0) { p5 = 3; result |= 0x04; }

        byte parity = (byte)(p2 ^ p3 ^ p4 ^ p5);
        if ((parity & 4) != 0) result |= 0x08;
        if ((parity & 2) != 0) result |= 0x02;
        if ((parity & 1) != 0) result |= 0x01;

        return (byte)(result << 1);
    }

    // Betriebszeit-Kodierung (aus LowLevelOperationsNfc3.ConvertHoursMinutesToOpTime)
    // Eingabe: Stunden (max 262140), Minuten (0–59)
    // Ausgabe: 32-bit OperatingTime + 16-bit SwitchOffTime (je ECC-kodiert)
    static void EncodeOperatingTime(uint hours, uint minutes,
                                    out uint opTime, out ushort switchOff)
    {
        if (hours   > 262140) hours   = 262140;
        if (minutes > 59)     minutes = 59;

        byte lowBits = (byte)(hours % 4);
        lowBits = (byte)((lowBits << 6) + minutes);

        byte n0 = EccEncode((byte)((hours >> 2) & 0x0F));
        byte n1 = EccEncode((byte)((hours >> 6) & 0x0F));
        byte n2 = EccEncode((byte)((hours >> 10) & 0x0F));
        byte n3 = EccEncode((byte)((hours >> 14) & 0x0F));
        byte m0 = EccEncode((byte)(lowBits & 0x0F));
        byte m1 = EccEncode((byte)((lowBits >> 4) & 0x0F));

        opTime   = (uint)(n0 | (n1 << 8) | (n2 << 16) | (n3 << 24));
        switchOff = (ushort)(m0 | (m1 << 8));
    }

    // CLO-Werte in Chip-Byte-Format konvertieren
    // Aus ConvertCloToTag + LUTable (LowLevelOperationsNfc3.cs)
    static void EncodeClo(Nfc3Values v, ref Nfc3TagDataW tag)
    {
        // Level-Lookup: Prozent (51–100) → 6-bit Chip-Wert
        uint lu1 = CloLevelLookup(v.CloLvl1); uint lu2 = CloLevelLookup(v.CloLvl2);
        uint lu3 = CloLevelLookup(v.CloLvl3); uint lu4 = CloLevelLookup(v.CloLvl4);
        uint lu5 = CloLevelLookup(v.CloLvl5); uint lu6 = CloLevelLookup(v.CloLvl6);
        uint lu7 = CloLevelLookup(v.CloLvl7); uint lu8 = CloLevelLookup(v.CloLvl8);

        // Zeitnibble: Zeit / 8192 (max 15 → max 122880 ms), 4 LSBs des Level
        tag.DutyCycleLB1 = CloEncodeLb(v.CloTime1, lu1);
        tag.DutyCycleLB2 = CloEncodeLb(v.CloTime2, lu2);
        tag.DutyCycleLB3 = CloEncodeLb(v.CloTime3, lu3);
        tag.DutyCycleLB4 = CloEncodeLb(v.CloTime4, lu4);
        tag.DutyCycleLB5 = CloEncodeLb(v.CloTime5, lu5);
        tag.DutyCycleLB6 = CloEncodeLb(v.CloTime6, lu6);
        tag.DutyCycleLB7 = CloEncodeLb(v.CloTime7, lu7);
        tag.DutyCycleLB8 = CloEncodeLb(v.CloTime8, lu8);

        // High-Bits: je 2 MSBs des 6-bit Level-Werts, gepackt in ein 16-bit Wort
        // Kanal 1 in Bits [1:0], Kanal 2 in Bits [3:2], ..., Kanal 8 in Bits [15:14]
        tag.DutyCycleHB1_8 = (ushort)(
            ((lu1 & 0x30) >> 4)       |   // Kanal 1: HB in Bits [1:0]
            ((lu2 & 0x30) >> 2)       |   // Kanal 2: HB in Bits [3:2]
            ((lu3 & 0x30))            |   // Kanal 3: HB in Bits [5:4]
            ((lu4 & 0x30) << 2)       |   // Kanal 4: HB in Bits [7:6]
            ((lu5 & 0x30) << 4)       |   // Kanal 5: HB in Bits [9:8]
            ((lu6 & 0x30) << 6)       |   // Kanal 6: HB in Bits [11:10]
            ((lu7 & 0x30) << 8)       |   // Kanal 7: HB in Bits [13:12]
            ((lu8 & 0x30) << 10)          // Kanal 8: HB in Bits [15:14]
        );
    }

    // Zeit + 4 LSBs des Level → 1 Byte: Bits[3:0]=Zeit, Bits[7:4]=Level-Nibble
    static byte CloEncodeLb(uint timeMs, uint levelRaw)
    {
        if (timeMs > 122880) timeMs = 122880;
        uint timeNibble  = timeMs / 8192;             // max 15
        uint levelNibble = levelRaw & 0x0F;           // 4 LSBs des 6-bit Werts
        return (byte)(timeNibble | (levelNibble << 4));
    }

    // Level-Lookup-Tabelle (aus initLookUpTable, LowLevelOperationsNfc3.cs)
    // Eingabe: CLO-Level in Prozent (51–100) → 6-bit Chip-Wert (1–63)
    static readonly (uint key, uint val)[] _cloTable = {
        (51,1),(52,2),(53,3),(54,5),(55,6),(56,7),(57,8),(58,10),(59,11),
        (60,12),(61,14),(62,15),(63,16),(64,17),(65,19),(66,20),(67,21),
        (68,23),(69,24),(70,25),(71,26),(72,28),(73,29),(74,30),(75,31),
        (76,33),(77,34),(78,35),(79,37),(80,38),(81,39),(82,40),(83,42),
        (84,43),(85,44),(86,46),(87,47),(88,48),(89,49),(90,51),(91,52),
        (92,53),(93,55),(94,56),(95,57),(96,58),(97,60),(98,61),(99,62),(100,63)
    };

    static uint CloLevelLookup(uint levelPct)
    {
        if (levelPct > 100) levelPct = 100;
        if (levelPct < 51)  levelPct = 51;
        foreach (var (key, val) in _cloTable)
            if (key == levelPct) return val;
        return 63; // Fallback
    }
}

// =============================================================================
// Beispiel-Einstiegspunkt
// =============================================================================
// Aufruf aus NfcReadTest.Main() oder separat kompilieren (dann Main umbenennen)
class NfcWriteTestMain
{
    public static void RunExample()
    {
        Console.WriteLine("NFC Write Test — Beispiele\n");

        // ── NFC2: Control Register für Offline-Programming ───────────────────
        // Schreibt PRR=Bitmask(Bank 0 und 1) → ECG programmiert beim nächsten Einschalten
        uint prr = NfcWriteTest.BuildPrrBitmask(0, 1);  // Banks 0 und 1
        byte[] controlReg = NfcWriteTest.BuildNfc2ControlRegister(prr, urr: 0, mlr: 0);

        Console.WriteLine("=== NFC2 Control Register Bytes (Offline-Programming) ===");
        Console.Write("  Byte[0..3] PRR: ");
        for (int i = 0; i < 4; i++) Console.Write($"0x{controlReg[i]:X2} ");
        Console.Write($"\n  Byte[4..7] URR: ");
        for (int i = 4; i < 8; i++) Console.Write($"0x{controlReg[i]:X2} ");
        Console.Write($"\n  Byte[8]    MLR: 0x{controlReg[8]:X2}");
        Console.WriteLine($"\n  Byte[9..10] CRC: 0x{controlReg[9]:X2} 0x{controlReg[10]:X2}");
        Console.WriteLine($"  → reader.Write(controlRegAddr, bytes);  // 11 Bytes schreiben");

        // ── NFC2: Memory Bank mit Nutzdaten ──────────────────────────────────
        byte[] bankData = new byte[24];  // Beispiel: 24 Bytes Nutzdaten (aus Config-Service)
        bankData[0] = 0x00; bankData[1] = 0x00; bankData[2] = 0x00; // SerialNumber (BE, 8 Bytes)
        bankData[3] = 0x00; bankData[4] = 0x00; bankData[5] = 0x00;
        bankData[6] = 0x12; bankData[7] = 0x34;
        byte[] bankBytes = NfcWriteTest.BuildMemoryBank(bankData, noCrc: false);
        Console.WriteLine($"\n=== NFC2 Memory Bank (24 Bytes Daten + 2 Bytes CRC) ===");
        Console.WriteLine($"  {bankBytes.Length} Bytes → reader.Write(mbBaseAddress, bytes);");
        Console.WriteLine($"  CRC: 0x{bankBytes[^2]:X2} 0x{bankBytes[^1]:X2}");

        // ── NFC3: Komplette Tag-Daten schreiben ───────────────────────────────
        var values = new Nfc3Values
        {
            Gtin          = 0x000400001234L,
            FwVersion     = 5,
            HwVersion     = 2,
            Current       = 700,        // 700 mA
            PwmPeriodImax = 0x01F4,
            DutyCycleImax = 0x0064,
            OnOffCounter  = 42,
            Assignment    = 0x01,
            OpTimeHours   = 1200,       // 1200 Stunden Betrieb
            OpTimeMinutes = 30,

            // CLO: alle Kanäle auf 100% Helligkeit, keine Zeitverzögerung
            CloLvl1 = 100, CloTime1 = 0,
            CloLvl2 = 100, CloTime2 = 0,
            CloLvl3 = 85,  CloTime3 = 16384,  // Kanal 3: 85%, 2× 8192 ms
            CloLvl4 = 100, CloTime4 = 0,
            CloLvl5 = 100, CloTime5 = 0,
            CloLvl6 = 100, CloTime6 = 0,
            CloLvl7 = 100, CloTime7 = 0,
            CloLvl8 = 100, CloTime8 = 0,

            AccessCode1 = 0,            // kein Schreibschutz
            AccessCode2 = 0,
        };

        byte[] nfc3Bytes = NfcWriteTest.BuildNfc3TagData(values);

        Console.WriteLine("\n=== NFC3 Tag-Bytes (52 Bytes, Little-Endian) ===");
        Console.WriteLine($"  Offset  Hex     Bedeutung");
        PrintAnnotated(nfc3Bytes, 0,  2, "DutyCycleHB1_8 (CLO High-Bits, 16-bit LE)");
        PrintAnnotated(nfc3Bytes, 2,  2, "DutyCycleImax (16-bit LE)");
        PrintAnnotated(nfc3Bytes, 4,  1, "DutyCycleLB5 (CLO5)");
        PrintAnnotated(nfc3Bytes, 5,  1, "DutyCycleLB6");
        PrintAnnotated(nfc3Bytes, 6,  1, "DutyCycleLB7");
        PrintAnnotated(nfc3Bytes, 7,  1, "DutyCycleLB8");
        PrintAnnotated(nfc3Bytes, 8,  1, "DutyCycleLB1 (CLO1)");
        PrintAnnotated(nfc3Bytes, 9,  1, "DutyCycleLB2");
        PrintAnnotated(nfc3Bytes, 10, 1, "DutyCycleLB3");
        PrintAnnotated(nfc3Bytes, 11, 1, "DutyCycleLB4");
        PrintAnnotated(nfc3Bytes, 12, 4, "AccessCode2 (32-bit LE)");
        PrintAnnotated(nfc3Bytes, 16, 8, "Reserved");
        PrintAnnotated(nfc3Bytes, 24, 2, $"Current = {values.Current} mA (16-bit LE)");
        PrintAnnotated(nfc3Bytes, 26, 2, "Reserved");
        PrintAnnotated(nfc3Bytes, 28, 1, $"HwVersion = {values.HwVersion}");
        PrintAnnotated(nfc3Bytes, 29, 1, $"FwVersion = {values.FwVersion}");
        PrintAnnotated(nfc3Bytes, 30, 6, $"GTIN = {values.Gtin} (48-bit LE)");
        PrintAnnotated(nfc3Bytes, 36, 2, "SwitchOffTime (ECC-kodiert, LE)");
        PrintAnnotated(nfc3Bytes, 38, 2, $"OnOffCounter = {values.OnOffCounter} (16-bit LE)");
        PrintAnnotated(nfc3Bytes, 40, 4, $"OperatingTime = {values.OpTimeHours}h {values.OpTimeMinutes}min (ECC, LE)");
        PrintAnnotated(nfc3Bytes, 44, 1, $"Assignment = 0x{values.Assignment:X1}");
        PrintAnnotated(nfc3Bytes, 45, 1, "CheckWriteProt");
        PrintAnnotated(nfc3Bytes, 46, 2, "PwmPeriodImax (16-bit LE)");
        PrintAnnotated(nfc3Bytes, 48, 4, "AccessCode1 (32-bit LE)");

        Console.WriteLine($"\n  → für jeden 4-Byte-Block (0..12) schreiben:");
        Console.WriteLine($"    for (int b = 0; b < 13; b++)");
        Console.WriteLine($"        reader.Write(b * 4, nfc3Bytes[(b*4)..(b*4+4)]);");
        Console.WriteLine($"    (Block 3 = AccessCode2, Block 12 = AccessCode1 → werden intern gesperrt)");

        // Diff gegen leeren Tag zeigen
        Console.WriteLine();
        NfcWriteTest.PrintNfc3Diff(new byte[52], values);
    }

    static void PrintAnnotated(byte[] data, int offset, int len, string label)
    {
        var hex = new System.Text.StringBuilder();
        for (int i = 0; i < len; i++)
            hex.Append($"{data[offset + i]:X2} ");
        Console.WriteLine($"  [{offset:D2}..{offset + len - 1:D2}]  {hex,-16} {label}");
    }
}
