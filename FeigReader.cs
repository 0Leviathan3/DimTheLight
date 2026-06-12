// FeigReader.cs — Feig OBID CPR30-LCN9620 USB NFC Reader (Linux, ISO 15693)
//
// Protokoll: Feig Advanced Communication Protocol (ACP), USB-Bulk-Transport
//   OUT: 0x02, IN: 0x81, kein STX/ETX
//   Frame: [LEN_H][LEN_L][COM_ADR=0x00][CMD][DATA...][CRC16_H][CRC16_L]
//   LEN = Gesamtlänge inkl. LEN-Feld
//   CRC = CRC16-CCITT-False (identisch zur OSRAM-NFC-CRC)
//
// Tag-Kommunikation: ISO 15693 via Feig-Befehl 0xB0
//   Inventory → UID des Tags ermitteln
//   Read Multiple Blocks (ISO 0x23) → Bytes lesen
//   Write Single Block (ISO 0x21) → 4-Byte-Block schreiben
//
// Linux-Voraussetzungen:
//   sudo apt install libusb-1.0-0
//   udev-Regel für Nicht-Root-Zugriff (einmalig):
//     echo 'SUBSYSTEM=="usb", ATTR{idVendor}=="0ab1", MODE="0666"' \
//       | sudo tee /etc/udev/rules.d/99-feig.rules
//     sudo udevadm control --reload-rules && sudo udevadm trigger

using System;
using LibUsbDotNet;
using LibUsbDotNet.Main;

namespace OsramNfcTest;

// ─── Interface ───────────────────────────────────────────────────────────────

interface INfcReader : IDisposable
{
    // Liest 'length' Bytes ab Byte-Adresse 'address' vom NFC-Chip
    byte[] Read(int address, int length);

    // Schreibt 'data' ab Byte-Adresse 'address' (muss Vielfaches von 4 sein)
    void Write(int address, byte[] data);

    // UID des aktuell erkannten ISO 15693 Tags (LSB-first), oder null
    byte[]? TagUid { get; }
}

// ─── Feig ACP Protokoll-Helfer ────────────────────────────────────────────────

static class FeigAcp
{
    // CRC16-CCITT-False (Poly=0x1021, Init=0xFFFF) — gleiche Impl. wie NfcReadTest
    public static ushort Crc16(ReadOnlySpan<byte> data)
    {
        const ushort poly = 0x1021;
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            ushort t = (ushort)(((crc >> 8) ^ b) << 8);
            for (int i = 0; i < 8; i++)
                t = (t & 0x8000) != 0 ? (ushort)((t << 1) ^ poly) : (ushort)(t << 1);
            crc = (ushort)((crc << 8) ^ t);
        }
        return crc;
    }

    // Frame bauen: [LEN_H][LEN_L][0x00][CMD][data...][CRC_H][CRC_L]
    public static byte[] BuildFrame(byte cmd, ReadOnlySpan<byte> data = default)
    {
        int total = 6 + data.Length;
        var f = new byte[total];
        f[0] = (byte)(total >> 8);
        f[1] = (byte)(total & 0xFF);
        f[2] = 0x00;    // COM-ADR für USB
        f[3] = cmd;
        data.CopyTo(f.AsSpan(4));
        ushort crc = Crc16(f.AsSpan(0, total - 2));
        f[total - 2] = (byte)(crc >> 8);
        f[total - 1] = (byte)(crc & 0xFF);
        return f;
    }

    // Response parsen: [LEN_H][LEN_L][ADR][CMD][STATUS][payload...][CRC_H][CRC_L]
    // Gibt false zurück wenn zu kurz oder CRC-Fehler.
    public static bool ParseResponse(ReadOnlySpan<byte> rsp,
        out byte status, out ReadOnlySpan<byte> payload)
    {
        status  = 0xFF;
        payload = ReadOnlySpan<byte>.Empty;
        if (rsp.Length < 7) return false;
        int len = (rsp[0] << 8) | rsp[1];
        if (len < 7 || len > rsp.Length) return false;
        ushort crcCalc = Crc16(rsp[..(len - 2)]);
        ushort crcRcv  = (ushort)((rsp[len - 2] << 8) | rsp[len - 1]);
        if (crcCalc != crcRcv) return false;
        status  = rsp[4];
        payload = len > 7 ? rsp[5..(len - 2)] : ReadOnlySpan<byte>.Empty;
        return true;
    }
}

// ─── Feig CPR30-LCN9620 Reader ────────────────────────────────────────────────

sealed class FeigCpr30Reader : INfcReader
{
    private const int  VidFeig      = 0x0AB1;
    private const int  PidCpr30     = 0x0003;
    private const int  BlockSize    = 4;      // ISO 15693: 4 Bytes/Block
    private const int  MaxBlocksPerReq = 255; // ISO 15693 max pro Transaktion
    private const int  TimeoutMs    = 3000;

    // Feig ACP Befehlscodes
    private const byte CmdRfReset   = 0x65;
    private const byte CmdIso15693  = 0xB0;

    // ISO 15693 Befehlscodes (Standard)
    private const byte Iso15693Inventory        = 0x01;
    private const byte Iso15693ReadMultiBlocks  = 0x23;
    private const byte Iso15693WriteSingleBlock = 0x21;

    // Flags für Inventory (Bit1=HighRate, Bit2=InventoryFlag, Bit5=SingleSlot)
    private const byte FlagsInventory = 0x26;
    // Flags für addressed Read/Write (Bit1=HighRate, Bit5=AddressFlag)
    private const byte FlagsAddressed = 0x22;

    private UsbDevice?         _dev;
    private UsbEndpointWriter? _epOut;
    private UsbEndpointReader? _epIn;
    private byte[]?            _uid;

    public byte[]? TagUid => _uid;

    // ── Öffnen ───────────────────────────────────────────────────────────────

    public static FeigCpr30Reader Open()
    {
        var r = new FeigCpr30Reader();
        r.Connect();
        return r;
    }

    private void Connect()
    {
        var finder = new UsbDeviceFinder(VidFeig, PidCpr30);
        _dev = UsbDevice.OpenUsbDevice(finder);

        if (_dev == null)
            throw new InvalidOperationException(
                $"Feig CPR30 nicht gefunden (VID=0x{VidFeig:X4} PID=0x{PidCpr30:X4}).\n" +
                "Prüfe:\n" +
                "  1) USB-Kabel und Stromversorgung des Readers\n" +
                "  2) USB-Zugriff ohne root — einmalig einrichten:\n" +
                "       echo 'SUBSYSTEM==\"usb\", ATTR{idVendor}==\"0ab1\", MODE=\"0666\"' \\\n" +
                "         | sudo tee /etc/udev/rules.d/99-feig.rules\n" +
                "       sudo udevadm control --reload-rules && sudo udevadm trigger\n" +
                "     Danach USB-Stecker neu einstecken.");

        // Unter Linux: Konfiguration setzen und Interface claimen
        if (_dev is IUsbDevice whole)
        {
            bool cfgOk = whole.SetConfiguration(1);
            bool ifOk  = whole.ClaimInterface(0);
            Console.Error.WriteLine($"[Feig] SetConfiguration(1)={cfgOk}, ClaimInterface(0)={ifOk}");
        }

        _epOut = _dev.OpenEndpointWriter(WriteEndpointID.Ep02);
        _epIn  = _dev.OpenEndpointReader(ReadEndpointID.Ep01);

        // Protokoll-Check mit verschiedenen Frame-Varianten
        ProbeProtocol();

        RfReset();

        _uid = DoInventory()
            ?? throw new InvalidOperationException(
                "Kein ISO 15693 Tag im Lesefeld des CPR30 erkannt.\n" +
                "Lege einen NFC2/NFC3-Chip vor den Reader.");

        Console.WriteLine($"[Feig] Tag erkannt — UID: {BitConverter.ToString(_uid)}");
    }

    // ── INfcReader ────────────────────────────────────────────────────────────

    public byte[] Read(int address, int length)
    {
        if (_uid == null) throw new InvalidOperationException("Kein Tag.");
        int firstBlk = address / BlockSize;
        int numBlks  = (length + BlockSize - 1) / BlockSize;
        var result   = new byte[numBlks * BlockSize];
        int done     = 0;
        while (done < numBlks)
        {
            int batch = Math.Min(numBlks - done, MaxBlocksPerReq);
            var chunk = ReadBlocks(firstBlk + done, batch);
            chunk.CopyTo(result, done * BlockSize);
            done += batch;
        }
        // Auf exakt 'length' kürzen
        return result.Length > length ? result[..length] : result;
    }

    public void Write(int address, byte[] data)
    {
        if (_uid == null) throw new InvalidOperationException("Kein Tag.");
        if (data.Length % BlockSize != 0)
            throw new ArgumentException(
                $"data.Length ({data.Length}) muss Vielfaches von {BlockSize} sein.");
        for (int i = 0; i < data.Length; i += BlockSize)
            WriteBlock((address + i) / BlockSize, data.AsSpan(i, BlockSize));
    }

    // ── Private: Feig-Kommandos ───────────────────────────────────────────────

    // Protokoll-Sondierung: probiert verschiedene bekannte Feig-Frame-Varianten
    // aus, um die richtige Rahmung für diesen Reader zu ermitteln.
    private void ProbeProtocol()
    {
        Console.Error.WriteLine("[Feig] Protokoll-Sondierung...");

        // Prüfen ob Reader nach Verbindungsaufbau spontan etwas schickt
        var buf = new byte[512];
        _epIn!.Read(buf, 500, out int n);
        if (n > 0)
            Console.Error.WriteLine($"[Feig] Spontane Nachricht beim Öffnen: {ToHex(buf[..n])}");

        void TryVariant(char name, byte[] frame)
        {
            Console.Error.Write($"[Feig] Variante {name}: {ToHex(frame)} → Write=");
            var wec = _epOut!.Write(frame, 500, out _);
            Console.Error.Write($"{wec} Read=");
            _epIn!.Read(buf, 1200, out n);
            if (n > 0)
            {
                Console.Error.WriteLine($"OK [{ToHex(buf[..n])}]");
                _protocolVariant = name;
            }
            else
                Console.Error.WriteLine("Timeout");
        }

        TryVariant('A', BuildVariantA(0x01)); // LEN inkl. sich selbst
        TryVariant('B', BuildVariantB(0x01)); // LEN exkl. sich selbst
        TryVariant('C', BuildVariantC(0x01)); // STX + LEN + ... + ETX
        TryVariant('D', BuildVariantD(0x01)); // kein LEN

        if (_protocolVariant == 'A')
        {
            Console.Error.WriteLine("[Feig] Keine der Protokoll-Varianten hat geantwortet.");
            Console.Error.WriteLine("[Feig] Mögliche Ursache: Feig-Vendor-spezifisches USB-Protokoll" +
                                    " unterscheidet sich von Standard-ACP. Prüfe Feig-Doku für CPR30-LCN9620.");
        }
    }

    private char _protocolVariant = 'A'; // durch ProbeProtocol gesetzt

    // Variante A: LEN = total (inkl. LEN-Feld), COM_ADR + CMD + CRC
    private static byte[] BuildVariantA(byte cmd, byte[]? data = null)
    {
        data ??= Array.Empty<byte>();
        int total = 6 + data.Length;
        var f = new byte[total];
        f[0] = (byte)(total >> 8); f[1] = (byte)(total & 0xFF);
        f[2] = 0x00; f[3] = cmd;
        data.CopyTo(f, 4);
        var crc = FeigAcp.Crc16(f.AsSpan(0, total - 2));
        f[total-2] = (byte)(crc >> 8); f[total-1] = (byte)(crc & 0xFF);
        return f;
    }

    // Variante B: LEN = total MINUS LEN-Feld (d.h. LEN = total - 2)
    private static byte[] BuildVariantB(byte cmd, byte[]? data = null)
    {
        data ??= Array.Empty<byte>();
        int total = 6 + data.Length;
        var f = new byte[total];
        int lenField = total - 2;
        f[0] = (byte)(lenField >> 8); f[1] = (byte)(lenField & 0xFF);
        f[2] = 0x00; f[3] = cmd;
        data.CopyTo(f, 4);
        var crc = FeigAcp.Crc16(f.AsSpan(0, total - 2));
        f[total-2] = (byte)(crc >> 8); f[total-1] = (byte)(crc & 0xFF);
        return f;
    }

    // Variante C: STX(1) + LEN(2) + COM_ADR(1) + CMD(1) + DATA + CRC(2) + ETX(1)
    private static byte[] BuildVariantC(byte cmd, byte[]? data = null)
    {
        data ??= Array.Empty<byte>();
        int inner = 4 + data.Length + 2; // COM_ADR+CMD+DATA+CRC
        var f = new byte[3 + inner + 1]; // STX + LEN_H + LEN_L + inner + ETX
        f[0] = 0x02; // STX
        f[1] = (byte)(inner >> 8); f[2] = (byte)(inner & 0xFF);
        f[3] = 0x00; f[4] = cmd;
        data.CopyTo(f, 5);
        var crc = FeigAcp.Crc16(f.AsSpan(3, 2 + data.Length)); // COM_ADR+CMD+DATA
        int crcOff = 5 + data.Length;
        f[crcOff] = (byte)(crc >> 8); f[crcOff+1] = (byte)(crc & 0xFF);
        f[^1] = 0x03; // ETX
        return f;
    }

    // Variante D: [COM_ADR CMD CRC_H CRC_L] kein LEN-Feld
    private static byte[] BuildVariantD(byte cmd, byte[]? data = null)
    {
        data ??= Array.Empty<byte>();
        var f = new byte[4 + data.Length];
        f[0] = 0x00; f[1] = cmd;
        data.CopyTo(f, 2);
        var crc = FeigAcp.Crc16(f.AsSpan(0, 2 + data.Length));
        f[^2] = (byte)(crc >> 8); f[^1] = (byte)(crc & 0xFF);
        return f;
    }

    // GetVersion (0x01): Funktioniert ohne Tag, dient zum Protokoll-Check.
    // Response-Payload: Firmware-Version, Reader-Typ etc.
    public string GetVersion()
    {
        var frame = FeigAcp.BuildFrame(0x01);
        Console.Error.WriteLine($"[Feig] TX GetVersion: {ToHex(frame)}");
        if (!Transact(frame, out var rsp))
            return "(keine Antwort — Protokollformat vermutlich falsch)";
        Console.Error.WriteLine($"[Feig] RX GetVersion: {ToHex(rsp)}");
        if (!FeigAcp.ParseResponse(rsp, out byte status, out var payload))
            return $"(CRC-Fehler oder Formatfehler in Response: {ToHex(rsp)})";
        return $"Status=0x{status:X2} Payload=[{ToHex(payload.ToArray())}]";
    }

    // RF Reset (deaktiviert + reaktiviert das HF-Feld)
    private void RfReset()
    {
        var frame = FeigAcp.BuildFrame(CmdRfReset);
        Console.Error.WriteLine($"[Feig] TX RfReset: {ToHex(frame)}");
        if (!Transact(frame, out var rsp))
        {
            // Timeout bei RF Reset ist auf manchen CPR30-Firmwares normal (kein Ack)
            Console.Error.WriteLine("[Feig] RfReset: kein Ack (ggf. normal für diese Firmware)");
            return;
        }
        Console.Error.WriteLine($"[Feig] RX RfReset: {ToHex(rsp)}");
    }

    private static string ToHex(byte[] b) =>
        b.Length == 0 ? "(leer)" : BitConverter.ToString(b).Replace("-", " ");

    // ISO 15693 Inventory (Single-Slot, non-addressed) → erste gefundene UID
    // Response-Payload: [DSFID:1][UID:8], UID in LSB-first Reihenfolge
    private byte[]? DoInventory()
    {
        byte[] isoReq = [FlagsInventory, Iso15693Inventory, 0x00 /* Mask-Len */];
        var frame = FeigAcp.BuildFrame(CmdIso15693, isoReq);

        if (!Transact(frame, out var rsp)) return null;
        if (!FeigAcp.ParseResponse(rsp, out byte status, out var payload)) return null;
        if (status != 0x00) return null;        // 0x01 = kein Tag, andere = Fehler
        if (payload.Length < 9) return null;    // brauche DSFID(1) + UID(8)

        // UID = die letzten 8 Bytes (DSFID davor, ggf. FLAGS noch weiter vorne)
        return payload[(payload.Length - 8)..].ToArray();
    }

    // ISO 15693 Read Multiple Blocks (addressed, 1-Byte-Blockadressen, bis 255 Blöcke)
    // Response-Payload (ohne Option-Flag): einfach [Block0:4][Block1:4]...
    private byte[] ReadBlocks(int firstBlock, int numBlocks)
    {
        if (firstBlock > 255 || firstBlock + numBlocks - 1 > 255)
            throw new ArgumentOutOfRangeException(nameof(firstBlock),
                $"Standard ISO 15693 unterstützt max. Block 255 (Byte-Adresse 1023). " +
                $"Angeforderter Bereich: Block {firstBlock}–{firstBlock + numBlocks - 1}. " +
                $"Für NFC2-Volldumps (2048 Bytes) ggf. 0–1023 genug.");

        // [Flags:1][CMD:1][UID:8][FirstBlock:1][NumBlocks-1:1] = 12 Bytes
        Span<byte> isoReq = stackalloc byte[12];
        isoReq[0] = FlagsAddressed;
        isoReq[1] = Iso15693ReadMultiBlocks;
        _uid.AsSpan().CopyTo(isoReq[2..10]);
        isoReq[10] = (byte)firstBlock;
        isoReq[11] = (byte)(numBlocks - 1);

        var frame = FeigAcp.BuildFrame(CmdIso15693, isoReq);
        if (!Transact(frame, out var rsp))
            throw new Exception($"Kein Response beim Lesen ab Block {firstBlock}");
        if (!FeigAcp.ParseResponse(rsp, out byte status, out var payload))
            throw new Exception("CRC-Fehler in Lese-Response");
        if (status != 0x00)
            throw new Exception($"Feig-Fehler 0x{status:X2} beim Lesen Block {firstBlock}");
        if (payload.Length < numBlocks * BlockSize)
            throw new Exception(
                $"Zu wenig Daten in Response: {payload.Length} Bytes, " +
                $"erwartet {numBlocks * BlockSize}");

        return payload[..(numBlocks * BlockSize)].ToArray();
    }

    // ISO 15693 Write Single Block (addressed)
    // [Flags:1][CMD:1][UID:8][Block#:1][Data:4] = 15 Bytes
    private void WriteBlock(int blockNum, ReadOnlySpan<byte> blockData)
    {
        if (blockData.Length != BlockSize)
            throw new ArgumentException($"Block muss exakt {BlockSize} Bytes haben.");

        Span<byte> isoReq = stackalloc byte[15];
        isoReq[0] = FlagsAddressed;
        isoReq[1] = Iso15693WriteSingleBlock;
        _uid.AsSpan().CopyTo(isoReq[2..10]);
        isoReq[10] = (byte)blockNum;
        blockData.CopyTo(isoReq[11..15]);

        var frame = FeigAcp.BuildFrame(CmdIso15693, isoReq);
        if (!Transact(frame, out var rsp))
            throw new Exception($"Kein Response beim Schreiben Block {blockNum}");
        if (!FeigAcp.ParseResponse(rsp, out byte status, out _))
            throw new Exception("CRC-Fehler in Schreib-Response");
        if (status != 0x00)
            throw new Exception($"Schreib-Fehler: Feig-Status 0x{status:X2} (Block {blockNum})");
    }

    // ── USB Bulk-Transfer ─────────────────────────────────────────────────────

    private bool Transact(byte[] request, out byte[] response)
    {
        response = Array.Empty<byte>();
        if (_epOut == null || _epIn == null) return false;

        var ec = _epOut.Write(request, TimeoutMs, out _);
        if (ec != ErrorCode.Ok)
        {
            Console.Error.WriteLine($"[Feig] USB Write-Fehler: {ec}");
            return false;
        }

        var buf = new byte[512];
        ec = _epIn.Read(buf, TimeoutMs, out int n);
        if (ec != ErrorCode.Ok && ec != ErrorCode.Win32Error)
        {
            Console.Error.WriteLine($"[Feig] USB Read-Fehler: {ec}");
            return false;
        }

        response = buf[..n];
        return n > 0;
    }

    public void Dispose()
    {
        _epOut?.Dispose();
        _epIn?.Dispose();
        if (_dev is IUsbDevice whole)
            whole.ReleaseInterface(0);
        _dev?.Close();
        UsbDevice.Exit();
    }
}

// ─── Simulierter Reader (für Tests ohne Hardware) ─────────────────────────────

sealed class SimulatedNfcReader : INfcReader
{
    private readonly byte[] _mem;
    public byte[]? TagUid => null;

    public SimulatedNfcReader(byte[] memory) => _mem = memory;

    public byte[] Read(int address, int length)
    {
        var buf = new byte[length];
        int avail = Math.Min(length, _mem.Length - address);
        if (avail > 0) Array.Copy(_mem, address, buf, 0, avail);
        return buf;
    }

    public void Write(int address, byte[] data)
    {
        int avail = Math.Min(data.Length, _mem.Length - address);
        if (avail > 0) Array.Copy(data, 0, _mem, address, avail);
    }

    public void Dispose() { }
}
