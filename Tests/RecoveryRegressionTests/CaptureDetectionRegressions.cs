using TeletextRecoveReese.Core;

internal static class CaptureDetectionRegressions
{
    private static byte[] Header(int page = 0, int subpage = 0)
    {
        var packet = Enumerable.Repeat((byte)0x20, 42).ToArray();
        packet[0] = Hamming.Encode84(1);
        packet[1] = Hamming.Encode84(0);
        for (int offset = 2; offset < 10; offset++) packet[offset] = Hamming.Encode84(0);
        packet[2] = Hamming.Encode84(page & 15);
        packet[3] = Hamming.Encode84(page >> 4);
        for (int offset = 4; offset < 8; offset++) packet[offset] = Hamming.Encode84((subpage >> ((offset - 4) * 4)) & 15);
        return packet;
    }

    private static async Task Check(string name, byte[] data, string filename, CaptureFileKind expected)
    {
        using var stream = new MemoryStream(data);
        if (data.Length > 3) stream.Position = 3;
        long position = stream.Position;
        var actual = await CaptureFileDetector.DetectAsync(stream, filename);
        if (actual != expected || stream.Position != position) throw new Exception($"FAILED: {name}: {actual}");
        Console.WriteLine($"PASS: {name}");
    }

    private sealed class ShortReadStream(byte[] data) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, 7)], cancellationToken);
    }

    public static async Task RunAsync()
    {
        await Check("a single T42 page opens as squash", Header(), "page.t42", CaptureFileKind.SquashedTeletext);
        await Check("distinct T42 subpages are a squash", Header(0, 1).Concat(Header(0, 2)).ToArray(), "page.t42", CaptureFileKind.SquashedTeletext);
        await Check("repeated addresses are a broadcast", Header().Concat(Header()).ToArray(), "capture.t42", CaptureFileKind.FullBroadcast);
        await Check("content takes priority over misleading extension", Header().Concat(Header()).ToArray(), "capture.vbi", CaptureFileKind.FullBroadcast);
        await Check("raw VBI uses its supported extension", Enumerable.Repeat((byte)0x80, 20000).ToArray(), "capture.VBI", CaptureFileKind.RawVbi);
        await Check("TBC captures use the VBI path", new byte[10000], "capture.tbc", CaptureFileKind.RawVbi);
        await Check("random binary data is not guessed as T42", Enumerable.Range(0, 5000).Select(i => (byte)(i * 17)).ToArray(), "data.bin", CaptureFileKind.Unknown);
        await Check("an empty T42 file is not accepted as a page", [], "empty.t42", CaptureFileKind.Unknown);
        await Check("an empty VBI file does not start deconvolution", [], "empty.vbi", CaptureFileKind.Unknown);
        await Check("unsupported TTI text is not mistaken for T42", System.Text.Encoding.ASCII.GetBytes("PN,100\nOL,1,Hello\n"), "page.tti", CaptureFileKind.Unknown);
        await Check("all-zero positional padding does not create repeated pages", new byte[420].Concat(Header()).ToArray(), "page.t42", CaptureFileKind.SquashedTeletext);
        await Check("a full probe of leading padding does not hide T42", new byte[42 * 4096].Concat(Header()).ToArray(), "page.t42", CaptureFileKind.SquashedTeletext);

        var first = Header();
        var second = Header();
        second[5] = Hamming.Encode84(8); // C4 is not part of the subcode.
        await Check("control flags do not hide a repeated address", first.Concat(second).ToArray(), "capture.t42", CaptureFileKind.FullBroadcast);

        var acrossBuffers = new List<byte>();
        // Fill the first probe entirely with valid packets and distinct headers.
        for (int index = 0; index < 4096; index++) acrossBuffers.AddRange(Header(index & 255, (index / 256) << 8));
        acrossBuffers.AddRange(Header());
        await Check("duplicate headers beyond the first probe are detected", acrossBuffers.ToArray(), "capture.t42", CaptureFileKind.FullBroadcast);
        acrossBuffers.RemoveRange(acrossBuffers.Count - 42, 42);
        await Check("probe boundaries do not manufacture duplicate headers", acrossBuffers.ToArray(), "squash.t42", CaptureFileKind.SquashedTeletext);

        using var shortReads = new ShortReadStream(Header().Concat(Header()).ToArray());
        if (await CaptureFileDetector.DetectAsync(shortReads, "capture.t42") != CaptureFileKind.FullBroadcast)
            throw new Exception("FAILED: short reads");
        Console.WriteLine("PASS: short stream reads retain packet alignment");
    }
}
