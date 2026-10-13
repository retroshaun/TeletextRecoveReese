namespace TeletextRecoveReese.Core;

public sealed record CharacterSetPacket(byte[] RawPacket, int PacketIndex, bool IsMagazineDefault)
{
    public int DesignationCode => Hamming.Decode84(RawPacket[2]).Value;

    public static bool TryRead(byte[] packet, out int primary, out int secondary)
    {
        primary = secondary = 0;
        if (packet.Length != 42) return false;
        var low = Hamming.Decode84(packet[0]);
        var high = Hamming.Decode84(packet[1]);
        if (low.UncorrectableError || high.UncorrectableError
            || ((low.Value | (high.Value << 4)) >> 3) is not (28 or 29)) return false;
        var dc = Hamming.Decode84(packet[2]);
        if (dc.UncorrectableError || dc.Value is not (0 or 4)) return false;
        var first = Hamming.Decode24_18(packet[3], packet[4], packet[5]);
        var second = Hamming.Decode24_18(packet[6], packet[7], packet[8]);
        if (first.UncorrectableError || second.UncorrectableError || (first.Value & 0x7F) != 0)
            return false;
        primary = (first.Value >> 7) & 0x7F;
        secondary = ((first.Value >> 14) & 15) | ((second.Value & 7) << 4);
        return true;
    }
}
