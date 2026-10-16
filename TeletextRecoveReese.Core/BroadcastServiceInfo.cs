using System.Numerics;

namespace TeletextRecoveReese.Core;

public sealed record BroadcastServiceInfo(
    int Format,
    string Status,
    int StatusParityErrors,
    DateOnly? Date,
    TimeOnly? UtcTime,
    TimeSpan? LocalOffset,
    ushort? NetworkId,
    string? InitialPage,
    bool InitialPageDamaged)
{
    public DateTime? UtcDateTime => Date is { } date && UtcTime is { } time
        ? DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Utc) : null;
    public DateTime? LocalDateTime => UtcDateTime is { } utc && LocalOffset is { } offset
        ? utc + offset : null;
}

public static class BroadcastServiceDecoder
{
    // EN 300 706, tables 18 and 19. T42 starts at the packet address,
    // omitting the first three transmission bytes (CRI and framing code).
    public static bool TryDecode(ReadOnlySpan<byte> packet, out BroadcastServiceInfo info)
    {
        info = null!;
        if (!TryDecodeHeader(packet, out int format)) return false;
        int errors = 0;
        Span<char> text = stackalloc char[20];
        for (int index = 0; index < text.Length; index++)
        {
            byte value = packet[22 + index];
            bool valid = BitOperations.PopCount((uint)value) % 2 == 1;
            if (!valid) errors++;
            text[index] = valid ? CharacterSets.Decode((byte)(value & 127)) : '\uFFFD';
        }
        Span<int> initial = stackalloc int[6];
        bool damaged = false;
        for (int index = 0; index < initial.Length; index++)
        {
            int decoded = DecodeNibble[packet[3 + index]];
            initial[index] = Math.Max(decoded, 0);
            damaged |= decoded < 0;
        }
        string? page = null;
        if (!damaged && !(initial[0] == 15 && initial[1] == 15))
        {
            int magazine = (initial[3] >> 3) | ((initial[5] >> 2) << 1);
            if (magazine == 0) magazine = 8;
            int subpage = initial[2] | (initial[3] & 7) << 4
                | initial[4] << 8 | (initial[5] & 3) << 12;
            page = $"{magazine}{initial[1]:X}{initial[0]:X}"
                + (subpage == 0x3F7F ? string.Empty : $" / {subpage:X4}");
        }
        DateOnly? date = format == 1 && BroadcastServiceData.TryDecodeFormat1Date(packet, out var decodedDate)
            ? decodedDate : null;
        TimeOnly? time = null;
        TimeSpan? offset = null;
        ushort? network = null;
        if (format == 1)
        {
            int hours = DecodeDecimal(packet[15]);
            int minutes = DecodeDecimal(packet[16]);
            int seconds = DecodeDecimal(packet[17]);
            if (hours is >= 0 and <= 23 && minutes is >= 0 and <= 59 && seconds is >= 0 and <= 59)
                time = new TimeOnly(hours, minutes, seconds);
            int halfHours = (packet[11] >> 1) & 31;
            offset = TimeSpan.FromMinutes(halfHours * 30 * ((packet[11] & 64) != 0 ? -1 : 1));
            // NI is sent most significant bit first, unlike the surrounding data.
            network = (ushort)((ReverseBits(packet[9]) << 8) | ReverseBits(packet[10]));
        }
        info = new BroadcastServiceInfo(format, new string(text), errors, date, time, offset, network, page, damaged);
        return true;
    }

    // Use nearest valid teletext codewords for this decoder: distance one is
    // correctable, distance two must never produce a plausible service field.
    private static readonly int[] DecodeNibble = BuildDecodeTable();

    private static int[] BuildDecodeTable()
    {
        var table = Enumerable.Repeat(-1, 256).ToArray();
        for (int nibble = 0; nibble < 16; nibble++)
        {
            int code = Hamming.Encode84(nibble);
            table[code] = nibble;
            for (int bit = 0; bit < 8; bit++) table[code ^ (1 << bit)] = nibble;
        }
        return table;
    }

    internal static bool TryDecodeHeader(ReadOnlySpan<byte> packet, out int format)
    {
        format = 0;
        if (packet.Length != 42) return false;
        int low = DecodeNibble[packet[0]];
        int high = DecodeNibble[packet[1]];
        int designation = DecodeNibble[packet[2]];
        if (low < 0 || high < 0 || designation is < 0 or > 3
            || (low | high << 4) != (30 << 3)) return false;
        format = designation < 2 ? 1 : 2;
        return true;
    }

    private static int DecodeDecimal(byte value)
    {
        int tens = (value >> 4) - 1;
        int units = (value & 15) - 1;
        return tens is >= 0 and <= 9 && units is >= 0 and <= 9 ? tens * 10 + units : -1;
    }

    private static int ReverseBits(byte value)
    {
        int reversed = 0;
        for (int bit = 0; bit < 8; bit++) reversed = (reversed << 1) | ((value >> bit) & 1);
        return reversed;
    }
}

public sealed record BroadcastServicePacket(long PacketNumber, BroadcastServiceInfo Data);
public sealed record BroadcastServiceRun(BroadcastServicePacket First, BroadcastServicePacket Last, long Count);

public sealed class BroadcastServiceHistory(int? maximumPackets = null)
{
    private readonly List<BroadcastServicePacket> _packets = new();
    private readonly List<BroadcastServiceRun> _runs = new();
    public IReadOnlyList<BroadcastServicePacket> Packets => _packets;
    public IReadOnlyList<BroadcastServiceRun> Runs => _runs;
    public long TotalReceived { get; private set; }
    public BroadcastServiceInfo? LastFormat1 { get; private set; }

    public bool Feed(ReadOnlySpan<byte> packet, long packetNumber)
    {
        if (!BroadcastServiceDecoder.TryDecode(packet, out var data)) return false;
        TotalReceived++;
        if (data.Format == 1) LastFormat1 = data;
        var item = new BroadcastServicePacket(packetNumber, data);
        _packets.Add(item);
        AppendRun(item);
        if (maximumPackets is { } maximum && _packets.Count > maximum)
        {
            _packets.RemoveAt(0);
            _runs.Clear();
            foreach (var retained in _packets) AppendRun(retained);
        }
        return true;
    }

    private void AppendRun(BroadcastServicePacket item)
    {
        if (_runs.Count > 0 && SameService(_runs[^1].Last.Data, item.Data))
            _runs[^1] = _runs[^1] with { Last = item, Count = _runs[^1].Count + 1 };
        else
            _runs.Add(new BroadcastServiceRun(item, item, 1));
    }

    private static bool SameService(BroadcastServiceInfo left, BroadcastServiceInfo right) =>
        left with { Date = null, UtcTime = null } == right with { Date = null, UtcTime = null }
        && left.Date.HasValue == right.Date.HasValue && left.UtcTime.HasValue == right.UtcTime.HasValue;
}
