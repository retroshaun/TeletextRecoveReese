namespace TeletextRecoveReese.Core;

public static class SquashPageOperations
{
    /// <summary>Build an independent, unsourced page at a new address.</summary>
    public static TeletextPage Renumber(TeletextPage source, int magazine, int pageNumber)
    {
        if (magazine is < 1 or > 8 || pageNumber is < 0 or > 255)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));
        var result = new TeletextPage
        {
            Magazine = magazine, PageNumber = pageNumber, SubPage = source.SubPage,
            NationalOptionOverride = source.NationalOptionOverride,
            SecondaryNationalOptionOverride = source.SecondaryNationalOptionOverride,
        };
        byte[] Move(byte[] raw)
        {
            var copy = (byte[])raw.Clone();
            int address = Hamming.Decode84(raw[0]).Value | Hamming.Decode84(raw[1]).Value << 4;
            address = (address & ~7) | (magazine & 7);
            copy[0] = Hamming.Encode84(address & 15);
            copy[1] = Hamming.Encode84(address >> 4);
            return copy;
        }
        for (int row = 0; row < source.RawRows.Length; row++)
        {
            if (source.RawRows[row] is not { } raw) continue;
            byte[] copy = Move(raw);
            if (row == 0)
            {
                copy[2] = Hamming.Encode84(pageNumber & 15);
                copy[3] = Hamming.Encode84(pageNumber >> 4);
            }
            PageAssembler.ApplyRow(result, row, copy);
        }
        foreach (var charset in source.EffectiveCharacterSetPackets)
            PageAssembler.ApplyCharacterSetPacket(result, Move(charset.RawPacket));
        PageAssembler.ReplaceEnhancementPackets(result,
            source.EnhancementPackets.Select(packet => (Move(packet.RawPacket), -1)));
        if (source.FastextPacket is { } fastext)
        {
            byte[] copy = Move(fastext);
            // Fastext magazines are relative to the containing page's magazine.
            int delta = (source.Magazine & 7) ^ (magazine & 7);
            for (int link = 0; link < 6; link++)
            {
                int offset = 3 + link * 6;
                if (offset + 5 >= copy.Length) break;
                int low = Hamming.Decode84(copy[offset + 2]).Value | Hamming.Decode84(copy[offset + 3]).Value << 4;
                int high = Hamming.Decode84(copy[offset + 4]).Value | Hamming.Decode84(copy[offset + 5]).Value << 4;
                low ^= (delta & 1) << 7;
                high ^= (delta & 6) << 5;
                copy[offset + 2] = Hamming.Encode84(low & 15);
                copy[offset + 3] = Hamming.Encode84(low >> 4);
                copy[offset + 4] = Hamming.Encode84(high & 15);
                copy[offset + 5] = Hamming.Encode84(high >> 4);
            }
            PageAssembler.ApplyFastextPacket(result, copy);
        }
        PageAssembler.SetNationalOptionOverride(result, source.NationalOptionOverride);
        return result;
    }
}
