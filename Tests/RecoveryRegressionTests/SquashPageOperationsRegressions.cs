using System.Numerics;
using TeletextRecoveReese.Core;

internal static class SquashPageOperationsRegressions
{
    private static byte Odd(int value) => (byte)(value | (BitOperations.PopCount((uint)value) % 2 == 0 ? 128 : 0));
    private static byte[] Packet(int row)
    {
        var packet = Enumerable.Repeat(Odd(32), 42).ToArray();
        int address = row << 3 | 1;
        packet[0] = Hamming.Encode84(address & 15); packet[1] = Hamming.Encode84(address >> 4);
        if (row == 0) for (int index = 2; index < 10; index++) packet[index] = Hamming.Encode84(0);
        return packet;
    }
    public static void Run()
    {
        var source = new TeletextPage { Magazine = 1, PageNumber = 0, SubPage = 0x1234 };
        byte[] header = Packet(0);
        PageStore.RewriteSubpageHeader(header, 0x1234);
        header[5] = Hamming.Encode84(Hamming.Decode84(header[5]).Value | 8);
        PageAssembler.ApplyRow(source, 0, header, 10);
        var body = Packet(1); body[2] = Odd('A');
        PageAssembler.ApplyRow(source, 1, body, 11);
        PageAssembler.ApplyRow(source, 25, Packet(25), 12);
        PageAssembler.SetFastextLink(source, 0, 0x234, 0x0001);
        PageAssembler.SetFastextLink(source, 1, 0x856, 0x0002);
        byte[] originalHeader = (byte[])source.RawRows[0]!.Clone();
        byte[] originalFastext = (byte[])source.FastextPacket!.Clone();
        var moved = SquashPageOperations.Renumber(source, 8, 0xAB);
        if (moved.Magazine != 8 || moved.PageNumber != 0xAB || moved.SubPage != 0x1234
            || moved.Grid[0, 1].Character != 'A' || moved.RawRows[25] is null
            || moved.RawRowPacketIndices.Any(index => index != -1)
            || Hamming.Decode84(moved.RawRows[0]![2]).Value != 11
            || Hamming.Decode84(moved.RawRows[0]![3]).Value != 10
            || Hamming.Decode84(moved.RawRows[0]![5]).Value != Hamming.Decode84(header[5]).Value)
            throw new Exception("Renumber lost content, subpage, CRC row or header flags.");
        if (moved.FastextLinks[0].PageNumber != 0x234 || moved.FastextLinks[1].PageNumber != 0x856
            || moved.FastextLinks[0].SubPage != 1 || moved.FastextLinks[1].SubPage != 2)
            throw new Exception("Moving to another magazine changed Fastext targets.");
        foreach (var raw in moved.RawRows.Where(row => row is not null).Append(moved.FastextPacket))
            if ((Hamming.Decode84(raw![0]).Value & 7) != 0)
                throw new Exception("A moved packet still has the source magazine.");
        if (!source.RawRows[0]!.SequenceEqual(originalHeader) || !source.FastextPacket!.SequenceEqual(originalFastext)
            || source.Magazine != 1 || source.RawRowPacketIndices[0] != 10)
            throw new Exception("Renumber changed source data.");
        moved.RawRows[1]![2] = Odd('B');
        if (source.Grid[0, 1].Character != 'A' || source.RawRows[1]![2] != Odd('A'))
            throw new Exception("Renumbered page shares mutable source packets.");
        Console.WriteLine("PASS: renumber preserves body, subpage, header flags, CRC row and absolute Fastext targets without changing source packets");
    }
}
