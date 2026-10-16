#nullable enable
using System.Numerics;
using TeletextRecoveReese.Core;

internal static class BroadcastServiceRegressions
{
    public static void Run()
    {
        static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception($"FAILED: {name}");
            Console.WriteLine($"PASS: {name}");
        }
        static byte Parity(char value)
        {
            byte code = (byte)value;
            return (byte)(code | (BitOperations.PopCount(code) % 2 == 0 ? 128 : 0));
        }
        static byte[] Packet(int designation = 0)
        {
            var packet = Enumerable.Repeat((byte)0x20, 42).ToArray();
            packet[0] = Hamming.Encode84(0);
            packet[1] = Hamming.Encode84(15); // Magazine 8, packet 30.
            packet[2] = Hamming.Encode84(designation);
            // Initial page 123, subpage 2345.
            int[] initial = [3, 2, 5, 12, 3, 2];
            for (int i = 0; i < initial.Length; i++) packet[3 + i] = Hamming.Encode84(initial[i]);
            packet[9] = 0x34; packet[10] = 0xFE; // NI 0x2C7F, MSB first.
            packet[11] = 6; // UTC + 01:30.
            packet[12] = 0x05; packet[13] = 0x61; packet[14] = 0x11; // MJD 45000: 1982-01-31.
            packet[15] = 0x34; packet[16] = 0x56; packet[17] = 0x67; // 23:45:56.
            const string message = "WELCOME TO CHANNEL";
            for (int i = 0; i < message.Length; i++) packet[22 + i] = Parity(message[i]);
            return packet;
        }
        var valid = Packet();
        Check(BroadcastServiceDecoder.TryDecode(valid, out var data), "recognize BSDP Format 1");
        Check(data.Status == "WELCOME TO CHANNEL  " && data.StatusParityErrors == 0, "retain all twenty status characters");
        Check(data.Date == new DateOnly(1982, 1, 31) && data.UtcTime == new TimeOnly(23, 45, 56), "decode ETSI MJD reference and UTC digits");
        Check(data.LocalDateTime == new DateTime(1982, 2, 1, 1, 15, 56), "local clock crosses month boundary");
        Check(data.NetworkId == 0x2C7F && data.InitialPage == "123 / 2345", "decode MSB-first NI and Hamming page link");
        valid[11] = 6 | 64;
        BroadcastServiceDecoder.TryDecode(valid, out data);
        Check(data.LocalOffset == TimeSpan.FromMinutes(-90) && data.LocalDateTime == new DateTime(1982, 1, 31, 22, 15, 56), "decode negative half-hour offset");
        valid = Packet(); valid[12] = 0; valid[22] ^= 128;
        Check(BroadcastServiceDecoder.TryDecode(valid, out data) && data.Date is null && data.UtcTime.HasValue
            && data.Status[0] == '�' && data.StatusParityErrors == 1, "damaged date and parity do not hide independent valid fields");
        valid = Packet(); valid[16] = 0xAF; valid[3] ^= 3;
        BroadcastServiceDecoder.TryDecode(valid, out data);
        Check(data.UtcTime is null && data.UtcDateTime is null && data.Date.HasValue && data.InitialPageDamaged
            && data.InitialPage is null, "invalid time and uncorrectable page are marked unavailable");
        valid = Packet(); valid[3] = valid[4] = Hamming.Encode84(15);
        BroadcastServiceDecoder.TryDecode(valid, out data);
        Check(data.InitialPage is null && !data.InitialPageDamaged, "FF initial page means not specified");
        valid = Packet(); valid[0] ^= 1;
        Check(BroadcastServiceDecoder.TryDecode(valid, out _), "correct single-bit packet address errors");
        valid[0] ^= 2;
        Check(!BroadcastServiceDecoder.TryDecode(valid, out _), "reject uncorrectable packet address errors");
        valid = Packet(); valid[0] = Hamming.Encode84(1);
        Check(!BroadcastServiceDecoder.TryDecode(valid, out _) && !BroadcastServiceDecoder.TryDecode(Packet(4), out _)
            && !BroadcastServiceDecoder.TryDecode(new byte[41], out _), "reject other magazines, unsupported designations and short packets");
        foreach (int designation in new[] { 2, 3 })
        {
            valid = Packet(designation);
            Array.Fill(valid, (byte)0xFF, 9, 13);
            Check(BroadcastServiceDecoder.TryDecode(valid, out data) && data.Format == 2 && data.Date is null
                && data.UtcTime is null && data.LocalOffset is null && data.NetworkId is null
                && data.Status.StartsWith("WELCOME"), "Format 2 status is independent of PDC payload");
        }
        var history = new BroadcastServiceHistory();
        valid = Packet(); history.Feed(valid, 10);
        valid[17] = 0x68; history.Feed(valid, 20);
        Check(history.Packets.Count == 2 && history.Runs.Count == 1 && history.Runs[0].Count == 2
            && history.Runs[0].First.PacketNumber == 10 && history.Runs[0].Last.Data.UtcTime == new TimeOnly(23, 45, 57),
            "group repeated service messages while retaining every timestamp and packet position");
        valid[22] = Parity('N'); history.Feed(valid, 30);
        Check(history.Runs.Count == 2, "status change starts a new history run");
        history.Feed(Packet(), 40);
        Check(history.Runs.Count == 3, "returning status remains a separate chronological run");
        var bounded = new BroadcastServiceHistory(2);
        bounded.Feed(Packet(), 1); bounded.Feed(Packet(), 2); bounded.Feed(Packet(), 3);
        Check(bounded.TotalReceived == 3 && bounded.Packets.Count == 2 && bounded.Packets[0].PacketNumber == 2
            && bounded.Runs[0].Count == 2, "live history stays bounded with correct retained repeat counts");
    }
}
