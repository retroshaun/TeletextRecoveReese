namespace TeletextRecoveReese.Core;

public enum CaptureFileKind { Unknown, SquashedTeletext, FullBroadcast, RawVbi }

/// <summary>Sniffs packets without constructing pages, grids or enhancements.</summary>
public static class CaptureFileDetector
{
    private const int BufferSize = TeletextPacket.Length * 4096;

    public static async Task<CaptureFileKind> DetectAsync(
        Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        long initialPosition = stream.CanSeek ? stream.Position : 0;
        try
        {
            if (stream.CanSeek) stream.Position = 0;
            var buffer = new byte[BufferSize];
            var addresses = new HashSet<(int Magazine, int Page, int Subpage)>();
            int count = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken);
            if (count == 0) return CaptureFileKind.Unknown;
            var first = Inspect(buffer, count, addresses, cancellationToken);
            // --keep-empty captures can begin with several buffers of padding.
            // Look past that prefix before rejecting a declared T42 capture.
            for (int probe = 1; extension == ".t42" && first.Packets == 0
                 && count == buffer.Length && probe < 8; probe++)
            {
                count = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken);
                first = Inspect(buffer, count, addresses, cancellationToken);
            }
            // Raw sample data can accidentally decode as individual MRAG bytes.
            // Require both consistent packet alignment and a valid page header.
            bool t42 = first.Packets > 0 && first.Headers > 0 && first.Valid * 100L >= first.Packets * 70L;
            if (!t42)
                return extension is ".vbi" or ".tbc" ? CaptureFileKind.RawVbi : CaptureFileKind.Unknown;
            if (first.Repeated) return CaptureFileKind.FullBroadcast;

            // A broadcast normally repeats an address early, so stop immediately
            // at that evidence. Proving a squash requires checking all headers;
            // this sequential scan still avoids the expensive page assembly.
            while (count == buffer.Length)
            {
                count = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken);
                if (Inspect(buffer, count, addresses, cancellationToken).Repeated)
                    return CaptureFileKind.FullBroadcast;
            }
            return CaptureFileKind.SquashedTeletext;
        }
        finally
        {
            if (stream.CanSeek) stream.Position = initialPosition;
        }
    }

    private static (int Packets, int Valid, int Headers, bool Repeated) Inspect(
        byte[] buffer, int length, HashSet<(int, int, int)> addresses, CancellationToken cancellationToken)
    {
        int packets = 0, valid = 0, headers = 0;
        bool repeated = false;
        for (int offset = 0; offset + TeletextPacket.Length <= length; offset += TeletextPacket.Length)
        {
            if ((offset % (TeletextPacket.Length * 256)) == 0) cancellationToken.ThrowIfCancellationRequested();
            var packet = buffer.AsSpan(offset, TeletextPacket.Length);
            if (TeletextPacket.IsPadding(packet)) continue;
            packets++;
            var low = Hamming.Decode84(packet[0]);
            var high = Hamming.Decode84(packet[1]);
            if (low.UncorrectableError || high.UncorrectableError) continue;
            int address = low.Value | (high.Value << 4);
            valid++;
            if ((address >> 3) != 0) continue;
            int page = 0, subpage = 0;
            bool headerValid = true;
            for (int index = 2; index < 10; index++)
            {
                var nibble = Hamming.Decode84(packet[index]);
                if (nibble.UncorrectableError) { headerValid = false; break; }
                if (index < 4) page |= nibble.Value << ((index - 2) * 4);
                else if (index < 8) subpage |= nibble.Value << ((index - 4) * 4);
            }
            if (!headerValid) continue;
            headers++;
            if (!addresses.Add((address & 7, page, subpage & 0x3F7F))) repeated = true;
        }
        return (packets, valid, headers, repeated);
    }
}
