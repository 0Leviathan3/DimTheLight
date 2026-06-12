// =============================================================================
// NfcReadTest.cs — Liest NFC-Chip-Daten und zeigt alle interessanten Werte
//
// Verwendung:
//   byte[] raw2048 = reader.Read(0, 2048);   // kompletter NFC2-Chip-Dump
//   NfcReadTest.ReadNfc2(raw2048);
//
//   byte[] raw52 = reader.Read(0, 52);        // NFC3-Datensektion
//   NfcReadTest.ReadNfc3(raw52);
//
// Abhängigkeiten: nur System.*
// =============================================================================

using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace OsramNfcTest;

// =============================================================================
// STRUCTS — Exakte Byte-für-Byte-Abbildung des Chip-Speichers
//           Alle Felder in Chip-Reihenfolge; Kommentare zeigen Byte-Offset.
//
// NFC2: BIG-ENDIAN  (ByteArray.AddUshort/AddUInt32 = MSB zuerst)
// NFC3: LITTLE-ENDIAN (TagField-Encoding << i*8 = LSB zuerst)
// =============================================================================

// -----------------------------------------------------------------------------
// NFC2 — Device Identification Registers
// Chip-Adresse: 0x0000, Größe: 20 Bytes
// -----------------------------------------------------------------------------
[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct Nfc2DeviceId  // 20 bytes
{
    public byte ManufacturerCode;   // [0]      Herstellercode (z.B. 0x07 = OSRAM)
    public byte Gtin0;              // [1]      GTIN MSB (Big-Endian)
    public byte Gtin1;              // [2]
    public byte Gtin2;              // [3]
    public byte Gtin3;              // [4]
    public byte Gtin4;              // [5]
    public byte Gtin5;              // [6]      GTIN LSB
    public byte FwMajor;           // [7]      Firmware-Hauptversion
    public byte FwMinor;           // [8]      Firmware-Unterversion
    public byte HwMajor;           // [9]      Hardware-Version
    public byte NfcVersion;        // [10]     NFC-Interface-Version
    public byte MemoryBankCount;   // [11]     Anzahl Memory Banks im TOC
    public byte StatusAddrHi;      // [12]     Status-Register Adresse High-Byte (BE)
    public byte StatusAddrLo;      // [13]     Status-Register Adresse Low-Byte
    public byte ControlAddrHi;     // [14]     Control-Register Adresse High-Byte (BE)
    public byte ControlAddrLo;     // [15]     Control-Register Adresse Low-Byte
    public byte ProtectedAddrHi;   // [16]     Geschützter Speicher Start High-Byte (BE)
    public byte ProtectedAddrLo;   // [17]     Geschützter Speicher Start Low-Byte
    public byte Crc16Hi;           // [18]     CRC16-CCITT-False High-Byte (BE)
    public byte Crc16Lo;           // [19]     CRC16-CCITT-False Low-Byte

    public long Gtin =>
        ((long)Gtin0 << 40) | ((long)Gtin1 << 32) | ((long)Gtin2 << 24) |
        ((long)Gtin3 << 16) | ((long)Gtin4 << 8)  | Gtin5;

    public ushort StatusRegisterAddress  => (ushort)((StatusAddrHi  << 8) | StatusAddrLo);
    public ushort ControlRegisterAddress => (ushort)((ControlAddrHi << 8) | ControlAddrLo);
    public ushort ProtectedMemoryAddress => (ushort)((ProtectedAddrHi << 8) | ProtectedAddrLo);
    public ushort StoredCrc              => (ushort)((Crc16Hi << 8) | Crc16Lo);
}

// -----------------------------------------------------------------------------
// NFC2 — Table of Content Eintrag
// Chip-Adresse: 0x0014, Größe: 6 Bytes pro Eintrag, dann 2 Bytes CRC
// -----------------------------------------------------------------------------
[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct Nfc2TocEntry  // 6 bytes
{
    // Bit 4 (0x10) = NoCrc-Flag: Bank enthält kein CRC auf dem Chip
    // Bit 3..0    = Bank-Typ-Attribute
    public byte MbAttribut;         // [0]      Attribute-Byte
    // 0x01–0xC8 = Konfigurationsdaten, 0xC9–0xCF (201–207) = Monitoring-Daten
    public byte MpcId;              // [1]      Memory-Page-Class ID
    public byte MpcVersion;         // [2]      Version der Seiten-Klasse
    public byte MbLength;           // [3]      Nutzdaten-Länge in Bytes (ohne CRC)
    public byte MbBaseAddrHi;       // [4]      Startadresse High-Byte (BE)
    public byte MbBaseAddrLo;       // [5]      Startadresse Low-Byte

    public ushort BaseAddress => (ushort)((MbBaseAddrHi << 8) | MbBaseAddrLo);
    public bool   HasNoCrc    => (MbAttribut & 0x10) == 0x10;
    public bool   IsMonitoring => MpcId >= 201 && MpcId <= 207;
}

// -----------------------------------------------------------------------------
// NFC2 — Status Register
// Chip-Adresse: aus DeviceId.StatusRegisterAddress, Größe: 4 Bytes
// -----------------------------------------------------------------------------
[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct Nfc2StatusRegister  // 4 bytes
{
    // Bits [7:4] = ECG-Status: 0xA0 = ECG an, 0x00 = ECG aus
    // Bits [3:0] = Fehlercode
    public byte Status;             // [0]      Status-Byte
    public byte Reserved;           // [1]      Reserviert
    public byte Crc16Hi;            // [2]      CRC16 High-Byte (BE)
    public byte Crc16Lo;            // [3]      CRC16 Low-Byte

    public bool EcgIsOn  => (Status & 0xF0) == 0xA0;
    public bool EcgIsOff => (Status & 0xF0) == 0x00;
    public byte ErrorCode => (byte)(Status & 0x0F);
}

// -----------------------------------------------------------------------------
// NFC2 — Control Register
// Chip-Adresse: aus DeviceId.ControlRegisterAddress, Größe: 11 Bytes
//
// PRR / URR: Bitmask — Bit N = 1 bedeutet "Memory Bank N ist betroffen"
// MLR: Challenge/Response für Online-Programming-Handshake
// -----------------------------------------------------------------------------
[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct Nfc2ControlRegister  // 11 bytes
{
    // PRR: Programming Request Register
    // Offline:  App schreibt Bitmask → ECG programmiert beim nächsten Einschalten
    // Online:   App schreibt Bitmask → ECG programmiert sofort (nach MLR-Handshake)
    public byte PrrByte0;           // [0]      PRR MSB (Big-Endian)
    public byte PrrByte1;           // [1]
    public byte PrrByte2;           // [2]
    public byte PrrByte3;           // [3]      PRR LSB

    // URR: Update Request Register
    // App schreibt Bitmask → ECG liest Memory Banks neu und bestätigt mit URR=0
    public byte UrrByte0;           // [4]      URR MSB (Big-Endian)
    public byte UrrByte1;           // [5]
    public byte UrrByte2;           // [6]
    public byte UrrByte3;           // [7]      URR LSB

    // MLR: Memory Lock Register — Challenge/Response-Handshake für Online-Prog.
    // App schreibt: zufälliger Wert R (nicht 0x81)
    // ECG antwortet: (R + 127) & 0xFF
    // Danach darf App Memory Banks schreiben
    public byte Mlr;                // [8]      Memory Lock Register

    public byte Crc16Hi;            // [9]      CRC16 High-Byte (BE)
    public byte Crc16Lo;            // [10]     CRC16 Low-Byte

    public uint Prr => (uint)((PrrByte0 << 24) | (PrrByte1 << 16) | (PrrByte2 << 8) | PrrByte3);
    public uint Urr => (uint)((UrrByte0 << 24) | (UrrByte1 << 16) | (UrrByte2 << 8) | UrrByte3);
}

// -----------------------------------------------------------------------------
// NFC3 — Kompletter Tag-Speicher
// Chip-Adresse: 0x0000, Größe: 52 Bytes, alles LITTLE-ENDIAN
//
// CLO = Constant Light Output (Lichtstrom-Regelung)
// CLO-Kodierung pro LB-Byte:
//   Bits [3:0] = Zeitnibble  → Zeit in ms = Wert × 8192
//   Bits [7:4] = Level-Nibble (4 LSBs des 6-bit Level-Werts)
//   HB1_8: enthält die 2 MSBs des Level-Werts für alle 8 Kanäle
//   Effektiver Level (6-bit) = (HB-Bits << 4) | LB-Bits[7:4]
//   Helligkeit in % = (Level + 64) × 100 / 128  (Formel aus ConvertTagToClo)
//
// OperatingTime / SwitchOffTime: ECC-kodiert (Hamming-Code, je 4 Datenbits → 8 Bit)
// Dekodierung: CalcValueFromDataPlusEcc (aus LowLevelOperationsNfc3.cs)
// -----------------------------------------------------------------------------
[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct Nfc3TagData  // 52 bytes, alles Little-Endian
{
    public ushort DutyCycleHB1_8;   // [0..1]   CLO High-Bits aller 8 Kanäle (LE)
                                    //          Bits [1:0]   = Kanal 1 MSBs des Level
                                    //          Bits [3:2]   = Kanal 2 MSBs, ...
                                    //          Bits [15:14] = Kanal 8 MSBs
    public ushort DutyCycleImax;    // [2..3]   PWM-Duty-Cycle bei Imax (LE)
    public byte   DutyCycleLB5;     // [4]      CLO Kanal 5 (Nibble-kodiert)
    public byte   DutyCycleLB6;     // [5]      CLO Kanal 6
    public byte   DutyCycleLB7;     // [6]      CLO Kanal 7
    public byte   DutyCycleLB8;     // [7]      CLO Kanal 8
    public byte   DutyCycleLB1;     // [8]      CLO Kanal 1
    public byte   DutyCycleLB2;     // [9]      CLO Kanal 2
    public byte   DutyCycleLB3;     // [10]     CLO Kanal 3
    public byte   DutyCycleLB4;     // [11]     CLO Kanal 4
    public uint   AccessCode2;      // [12..15] Schreibschutz-Code 2 (LE, 0 = kein Schutz)
    public byte   Res16;            // [16..23] Reserviert (8 Bytes)
    public byte   Res17;
    public byte   Res18;
    public byte   Res19;
    public byte   Res20;
    public byte   Res21;
    public byte   Res22;
    public byte   Res23;
    public ushort Current;          // [24..25] Betriebsstrom in mA (LE)
    public byte   Res26;            // [26..27] Reserviert
    public byte   Res27;
    public byte   HwVersion;        // [28]     Hardware-Version
    public byte   FwVersion;        // [29]     Firmware-Version
    public byte   Gtin0;            // [30]     GTIN LSB (Little-Endian!)
    public byte   Gtin1;            // [31]
    public byte   Gtin2;            // [32]
    public byte   Gtin3;            // [33]
    public byte   Gtin4;            // [34]
    public byte   Gtin5;            // [35]     GTIN MSB
    public ushort SwitchOffTime;    // [36..37] Abschaltzeit, ECC-kodiert (LE)
    public ushort OnOffCounter;     // [38..39] Schalt-Zähler (LE)
    public uint   OperatingTime;    // [40..43] Betriebszeit, ECC-kodiert (LE)
    public byte   Assignment;       // [44]     Zuweisung (nur Bits [3:0] verwendet)
    public byte   CheckWriteProt;   // [45]     Schreibschutz-Prüfbyte
    public ushort PwmPeriodImax;    // [46..47] PWM-Periode bei Imax (LE)
    public uint   AccessCode1;      // [48..51] Schreibschutz-Code 1 (LE, 0 = kein Schutz)

    public long GetGtin() =>
        ((long)Gtin5 << 40) | ((long)Gtin4 << 32) | ((long)Gtin3 << 24) |
        ((long)Gtin2 << 16) | ((long)Gtin1 << 8)  | Gtin0;
}

// =============================================================================
// Lese-Logik
// =============================================================================
static class NfcReadTest
{
    // -------------------------------------------------------------------------
    // NFC2: Liest einen kompletten 2048-Byte-Chip-Dump aus und gibt alle
    //       interessanten Werte auf der Konsole aus.
    // -------------------------------------------------------------------------
    public static void ReadNfc2(byte[] raw)
    {
        if (raw.Length < 20)
        {
            Console.WriteLine("ERR: Dump zu kurz für Device ID");
            return;
        }

        // --- Device Identification Registers (Adresse 0, 20 Bytes) ----------
        var devId = MemoryMarshal.Read<Nfc2DeviceId>(raw.AsSpan(0, 20));
        ushort crcCalc = CalcCrc16(raw.AsSpan(0, 18));

        Console.WriteLine("=== NFC2 Device Identification (Adr. 0x0000) ===");
        Console.WriteLine($"  ManufacturerCode  : 0x{devId.ManufacturerCode:X2}");
        Console.WriteLine($"  GTIN              : {devId.Gtin}");
        Console.WriteLine($"  Firmware          : {devId.FwMajor}.{devId.FwMinor}");
        Console.WriteLine($"  Hardware          : {devId.HwMajor}");
        Console.WriteLine($"  NFC-Version       : {devId.NfcVersion}");
        Console.WriteLine($"  Memory Banks      : {devId.MemoryBankCount}");
        Console.WriteLine($"  Status-Reg Adr.   : 0x{devId.StatusRegisterAddress:X4}");
        Console.WriteLine($"  Control-Reg Adr.  : 0x{devId.ControlRegisterAddress:X4}");
        Console.WriteLine($"  Protected-Mem Adr.: 0x{devId.ProtectedMemoryAddress:X4}");
        Console.WriteLine($"  CRC16             : 0x{devId.StoredCrc:X4} " +
                          $"({(devId.StoredCrc == crcCalc ? "OK" : $"FEHLER! erwartet 0x{crcCalc:X4}")})");

        // --- Table of Content (Adresse 20, N × 6 + 2 Bytes) -----------------
        int tocTotalBytes = devId.MemoryBankCount * 6 + 2;
        int tocEnd = 20 + tocTotalBytes;
        if (raw.Length < tocEnd)
        {
            Console.WriteLine("ERR: Dump zu kurz für TOC");
            return;
        }

        ushort tocCrcCalc = CalcCrc16(raw.AsSpan(20, tocTotalBytes - 2));
        ushort tocCrcStored = (ushort)((raw[tocEnd - 2] << 8) | raw[tocEnd - 1]);

        Console.WriteLine($"\n=== NFC2 Table of Content (Adr. 0x0014) " +
                          $"CRC {(tocCrcStored == tocCrcCalc ? "OK" : "FEHLER")} ===");
        Console.WriteLine($"  {"Nr",-4} {"MpcId",-6} {"Attr",-6} {"Vers",-6} {"Länge",-7} {"Adr.",-8} {"Typ"}");

        var tocEntries = new Nfc2TocEntry[devId.MemoryBankCount];
        for (int i = 0; i < devId.MemoryBankCount; i++)
        {
            int offset = 20 + i * 6;
            tocEntries[i] = MemoryMarshal.Read<Nfc2TocEntry>(raw.AsSpan(offset, 6));
            var e = tocEntries[i];
            string typ = e.IsMonitoring ? "MONITORING" : e.HasNoCrc ? "Konfig (kein CRC)" : "Konfig";
            Console.WriteLine($"  [{i}]  0x{e.MpcId:X2}   0x{e.MbAttribut:X2}   {e.MpcVersion,-6} {e.MbLength,-7} 0x{e.BaseAddress:X4}  {typ}");
        }

        // --- Status Register -------------------------------------------------
        int statusAdr = devId.StatusRegisterAddress;
        if (raw.Length >= statusAdr + 4)
        {
            var sr = MemoryMarshal.Read<Nfc2StatusRegister>(raw.AsSpan(statusAdr, 4));
            ushort srCrcCalc = CalcCrc16(raw.AsSpan(statusAdr, 2));
            ushort srCrcStored = (ushort)((sr.Crc16Hi << 8) | sr.Crc16Lo);

            Console.WriteLine($"\n=== NFC2 Status Register (Adr. 0x{statusAdr:X4}) ===");
            Console.WriteLine($"  ECG Status  : {(sr.EcgIsOn ? "AN (online)" : sr.EcgIsOff ? "AUS (offline)" : "UNGÜLTIG")}");
            Console.WriteLine($"  Error Code  : 0x{sr.ErrorCode:X1}");
            Console.WriteLine($"  CRC16       : 0x{srCrcStored:X4} ({(srCrcStored == srCrcCalc ? "OK" : $"FEHLER! erwartet 0x{srCrcCalc:X4}")})");
        }

        // --- Control Register ------------------------------------------------
        int controlAdr = devId.ControlRegisterAddress;
        if (raw.Length >= controlAdr + 11)
        {
            var cr = MemoryMarshal.Read<Nfc2ControlRegister>(raw.AsSpan(controlAdr, 11));
            ushort crCrcCalc = CalcCrc16(raw.AsSpan(controlAdr, 9));
            ushort crCrcStored = (ushort)((cr.Crc16Hi << 8) | cr.Crc16Lo);

            Console.WriteLine($"\n=== NFC2 Control Register (Adr. 0x{controlAdr:X4}) ===");
            Console.WriteLine($"  PRR (Prog-Request)   : 0x{cr.Prr:X8} " +
                              $"({DescribeBankBitmask(cr.Prr, devId.MemoryBankCount)})");
            Console.WriteLine($"  URR (Update-Request) : 0x{cr.Urr:X8}");
            Console.WriteLine($"  MLR (Lock Register)  : 0x{cr.Mlr:X2}");
            Console.WriteLine($"  CRC16                : 0x{crCrcStored:X4} ({(crCrcStored == crCrcCalc ? "OK" : $"FEHLER! erwartet 0x{crCrcCalc:X4}")})");
        }

        // --- Memory Banks (Rohbytes + CRC-Status) ----------------------------
        Console.WriteLine($"\n=== NFC2 Memory Banks ===");
        for (int i = 0; i < tocEntries.Length; i++)
        {
            var e = tocEntries[i];
            int dataLen = e.MbLength;
            int totalLen = e.HasNoCrc ? dataLen : dataLen + 2;
            int bankAdr = e.BaseAddress;

            if (raw.Length < bankAdr + totalLen)
            {
                Console.WriteLine($"  Bank [{i}] MpcId=0x{e.MpcId:X2}: Dump zu kurz");
                continue;
            }

            if (e.HasNoCrc)
            {
                Console.WriteLine($"  Bank [{i}] MpcId=0x{e.MpcId:X2} Adr=0x{bankAdr:X4} " +
                                  $"Len={dataLen}: {FormatBytes(raw, bankAdr, Math.Min(dataLen, 16))}");
            }
            else
            {
                ushort bankCrcCalc = CalcCrc16(raw.AsSpan(bankAdr, dataLen));
                ushort bankCrcStored = (ushort)((raw[bankAdr + dataLen] << 8) | raw[bankAdr + dataLen + 1]);
                string crcStatus = bankCrcStored == bankCrcCalc ? "OK" : "FEHLER";
                Console.WriteLine($"  Bank [{i}] MpcId=0x{e.MpcId:X2} Adr=0x{bankAdr:X4} " +
                                  $"Len={dataLen} CRC={crcStatus}: {FormatBytes(raw, bankAdr, Math.Min(dataLen, 16))}");
            }
        }
    }

    // -------------------------------------------------------------------------
    // NFC3: Liest einen 52-Byte-Tag-Dump und gibt alle Werte aus.
    //       Optional: 4 Bytes Block-Lock ab Adresse 0x44 (= 68).
    // -------------------------------------------------------------------------
    public static void ReadNfc3(byte[] raw, byte[]? blockLock4 = null)
    {
        if (raw.Length < 52)
        {
            Console.WriteLine("ERR: Dump zu kurz für NFC3 (brauche 52 Bytes)");
            return;
        }

        var tag = MemoryMarshal.Read<Nfc3TagData>(raw.AsSpan(0, 52));

        Console.WriteLine("=== NFC3 Tag Daten (Adr. 0x0000, 52 Bytes, Little-Endian) ===");
        Console.WriteLine($"  GTIN            : {tag.GetGtin()}");
        Console.WriteLine($"  Firmware        : {tag.FwVersion}");
        Console.WriteLine($"  Hardware        : {tag.HwVersion}");
        Console.WriteLine($"  Betriebsstrom   : {tag.Current} mA");
        Console.WriteLine($"  PWM-Periode     : {tag.PwmPeriodImax}");
        Console.WriteLine($"  Duty-Cycle Imax : {tag.DutyCycleImax}");
        Console.WriteLine($"  Ein/Aus-Zähler  : {tag.OnOffCounter}");
        Console.WriteLine($"  Zuweisung       : 0x{(tag.Assignment & 0x0F):X1}");

        // Betriebszeit dekodieren (ECC-Hamming-Dekodierung)
        DecodeOperatingTime(tag.OperatingTime, tag.SwitchOffTime,
                           out uint opHours, out uint opMinutes);
        Console.WriteLine($"  Betriebszeit    : {opHours}h {opMinutes}min");

        // CLO-Werte dekodieren
        Console.WriteLine($"  CLO-Einstellungen:");
        for (int ch = 1; ch <= 8; ch++)
        {
            byte lb = ch switch {
                1 => tag.DutyCycleLB1, 2 => tag.DutyCycleLB2,
                3 => tag.DutyCycleLB3, 4 => tag.DutyCycleLB4,
                5 => tag.DutyCycleLB5, 6 => tag.DutyCycleLB6,
                7 => tag.DutyCycleLB7, _ => tag.DutyCycleLB8
            };
            int hbShift = (ch - 1) * 2;
            uint hbBits = ((uint)tag.DutyCycleHB1_8 >> hbShift) & 0x03;
            uint levelRaw = ((lb & 0xF0u) >> 4) | (hbBits << 4);
            uint timeMs = (uint)(lb & 0x0F) * 8192;
            uint levelPct = (levelRaw + 64) * 100 / 128 + 1;
            Console.WriteLine($"    Kanal {ch}: Level={levelPct}%  Zeit={timeMs}ms  (raw: LB=0x{lb:X2} HB-Bits={hbBits})");
        }

        Console.WriteLine($"  AccessCode1     : 0x{tag.AccessCode1:X8} ({(tag.AccessCode1 == 0 ? "kein Schutz" : "gesetzt")})");
        Console.WriteLine($"  AccessCode2     : 0x{tag.AccessCode2:X8} ({(tag.AccessCode2 == 0 ? "kein Schutz" : "gesetzt")})");

        if (blockLock4 != null && blockLock4.Length >= 4)
        {
            uint bl = (uint)(blockLock4[0] | (blockLock4[1] << 8) | (blockLock4[2] << 16) | (blockLock4[3] << 24));
            Console.WriteLine($"\n=== NFC3 Block-Lock (Adr. 0x44, 4 Bytes) ===");
            int blockCount = 52 / 4;
            Console.WriteLine($"  {"Block",-7} {"Adr",-7} {"Lock-Bits",-12} {"Beschreibbar"}");
            for (int b = 0; b < blockCount; b++)
            {
                uint bits = (bl >> (b * 2)) & 0x3;
                bool writable = bits == 0x2;
                Console.WriteLine($"  Block {b,-3} 0x{b * 4:X2}    {bits:b2}           {(writable ? "JA" : "NEIN")}");
            }
        }
    }

    // -------------------------------------------------------------------------
    // Hilfsmethoden
    // -------------------------------------------------------------------------

    // CRC16-CCITT-False (Poly=0x1021, Init=0xFFFF) — identisch zu CrcValidator.cs
    public static ushort CalcCrc16(ReadOnlySpan<byte> data)
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

    // ECC-Dekodierung für NFC3 Betriebszeit (aus LowLevelOperationsNfc3.ConvertOpTimeToHoursMinutes)
    // Je 4 Nibbles in OperatingTime und 2 Nibbles in SwitchOffTime → Stunden + Minuten
    static void DecodeOperatingTime(uint opTime, ushort switchOff,
                                    out uint hours, out uint minutes)
    {
        byte n0 = EccDecode((byte)(opTime & 0xFF));
        byte n1 = EccDecode((byte)((opTime >> 8) & 0xFF));
        byte n2 = EccDecode((byte)((opTime >> 16) & 0xFF));
        byte n3 = EccDecode((byte)((opTime >> 24) & 0xFF));
        byte m0 = EccDecode((byte)(switchOff & 0xFF));
        byte m1 = EccDecode((byte)((switchOff >> 8) & 0xFF));
        uint rawHours = (uint)((n0 + (n1 << 4) + (n2 << 8) + (n3 << 12)) * 4);
        uint rawSwitch = (uint)(m0 + (m1 << 4));
        hours   = rawHours + ((rawSwitch >> 6) & 0x3);
        minutes = rawSwitch & 0x3F;
    }

    // ECC-Dekodierung eines einzelnen Bytes (4 Datenbits + 4 Parity-Bits → 4 Datenbits)
    static byte EccDecode(byte encoded)
    {
        encoded >>= 1;
        byte result = 0;
        if ((encoded & 0x40) == 0x40) result |= 0x08;
        if ((encoded & 0x20) == 0x20) result |= 0x04;
        if ((encoded & 0x10) == 0x10) result |= 0x02;
        if ((encoded & 0x04) == 0x04) result |= 0x01;
        return result;
    }

    static string DescribeBankBitmask(uint mask, byte bankCount)
    {
        if (mask == 0) return "keine";
        var sb = new StringBuilder("Banken: ");
        for (int i = 0; i < bankCount; i++)
            if ((mask & (1u << i)) != 0) sb.Append($"{i} ");
        return sb.ToString().TrimEnd();
    }

    static string FormatBytes(byte[] data, int offset, int count)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < count && offset + i < data.Length; i++)
            sb.Append($"{data[offset + i]:X2} ");
        return sb.ToString().TrimEnd();
    }
}

// =============================================================================
// Beispiel-Einstiegspunkt (zum Testen mit simulierten Daten)
// =============================================================================
class NfcReadTestMain
{
    public static void RunSimulated()
    {
        Console.WriteLine("NFC Read Test — Beispiel mit simulierten Daten");
        Console.WriteLine("In der App: byte[] raw = reader.Read(0, 2048);");
        Console.WriteLine("            NfcReadTest.ReadNfc2(raw);");
        Console.WriteLine();

        // Minimaler NFC2-Fake-Dump (nur Device ID gefüllt, Rest 0xFF)
        byte[] fake2048 = new byte[2048];
        Array.Fill(fake2048, (byte)0xFF);

        // Device ID schreiben (Adresse 0)
        fake2048[0] = 0x07;                     // ManufacturerCode (OSRAM)
        fake2048[1] = 0x00; fake2048[2] = 0x04; // GTIN MSBs
        fake2048[3] = 0x00; fake2048[4] = 0x00;
        fake2048[5] = 0x12; fake2048[6] = 0x34; // GTIN = 0x000400001234
        fake2048[7] = 0x03;                     // FwMajor
        fake2048[8] = 0x01;                     // FwMinor
        fake2048[9] = 0x02;                     // HwMajor
        fake2048[10] = 0x01;                    // NfcVersion
        fake2048[11] = 0x02;                    // MemoryBankCount = 2
        fake2048[12] = 0x00; fake2048[13] = 0x60; // StatusRegAddr = 0x0060
        fake2048[14] = 0x00; fake2048[15] = 0x64; // ControlRegAddr = 0x0064
        fake2048[16] = 0x00; fake2048[17] = 0x80; // ProtectedMemAddr = 0x0080
        ushort devCrc = NfcReadTest.CalcCrc16(fake2048.AsSpan(0, 18));
        fake2048[18] = (byte)(devCrc >> 8);
        fake2048[19] = (byte)(devCrc & 0xFF);

        // TOC (Adresse 20): 2 Einträge
        // Bank 0: MpcId=0x01, Len=24, BaseAddr=0x0080
        fake2048[20] = 0x00; fake2048[21] = 0x01; fake2048[22] = 0x01;
        fake2048[23] = 24;   fake2048[24] = 0x00; fake2048[25] = 0x80;
        // Bank 1: MpcId=0xC9 (201=Monitoring), Len=16, BaseAddr=0x00A0
        fake2048[26] = 0x00; fake2048[27] = 0xC9; fake2048[28] = 0x01;
        fake2048[29] = 16;   fake2048[30] = 0x00; fake2048[31] = 0xA0;
        ushort tocCrc = NfcReadTest.CalcCrc16(fake2048.AsSpan(20, 12));
        fake2048[32] = (byte)(tocCrc >> 8);
        fake2048[33] = (byte)(tocCrc & 0xFF);

        // Status Register (Adresse 0x60): ECG an, kein Fehler
        fake2048[0x60] = 0xA0;  // ECG on (oberes Nibble = 0xA)
        fake2048[0x61] = 0x00;
        ushort srCrc = NfcReadTest.CalcCrc16(fake2048.AsSpan(0x60, 2));
        fake2048[0x62] = (byte)(srCrc >> 8);
        fake2048[0x63] = (byte)(srCrc & 0xFF);

        // Control Register (Adresse 0x64): PRR=0, URR=0, MLR=0
        Array.Fill(fake2048, (byte)0x00, 0x64, 9);
        ushort crCrc = NfcReadTest.CalcCrc16(fake2048.AsSpan(0x64, 9));
        fake2048[0x6D] = (byte)(crCrc >> 8);
        fake2048[0x6E] = (byte)(crCrc & 0xFF);

        NfcReadTest.ReadNfc2(fake2048);

        Console.WriteLine("\n--- NFC3 Beispiel ---");
        byte[] fake52 = new byte[52];
        var tag = new Nfc3TagData
        {
            Current       = 700,        // 700 mA
            HwVersion     = 2,
            FwVersion     = 5,
            OnOffCounter  = 42,
            DutyCycleImax = 0x0064,
            PwmPeriodImax = 0x0190,
        };
        tag.Gtin0 = 0x34; tag.Gtin1 = 0x12; tag.Gtin2 = 0x00;
        tag.Gtin3 = 0x00; tag.Gtin4 = 0x04; tag.Gtin5 = 0x00; // GTIN LE
        MemoryMarshal.Write(fake52.AsSpan(0, 52), in tag);
        NfcReadTest.ReadNfc3(fake52);

        Console.WriteLine("\n\n========================================");
        Console.WriteLine("NFC Write Test — Beispiele");
        Console.WriteLine("========================================");
        NfcWriteTestMain.RunExample();
    }
}
