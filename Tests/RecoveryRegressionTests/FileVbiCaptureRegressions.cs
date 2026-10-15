#nullable enable
using TeletextRecoveReese.Core;

internal static class FileVbiCaptureRegressions
{
    public static async Task RunAsync()
    {
        foreach (bool wide in new[] { false, true })
        {
            string path = Path.Combine(Path.GetTempPath(), $"file-vbi-test-{Guid.NewGuid():N}.vbi");
            try
            {
                // Three fields plus a partial fourth; ignore its incomplete sample.
                int lineBytes = wide ? 8 : 4;
                byte[] source = Enumerable.Range(0, 14 * lineBytes + 1).Select(i => (byte)i).ToArray();
                await File.WriteAllBytesAsync(path, source);
                var options = new VbiCaptureOptions("Test", 27000000, 4, 0, 1, wide, 4, 1, 3);
                await using var input = new FileVbiCaptureStream(path, options);
                byte[]? preview = null;
                input.RawFrameCaptured = frame => preview = frame;
                var expected = new List<byte>();
                for (int line = 0; line < 14; line++)
                    if (line % 4 is 1 or 2)
                        expected.AddRange(source.Skip(line * lineBytes).Take(lineBytes));
                if (input.Length != expected.Count) throw new Exception("File VBI selected length is wrong.");
                var actual = new List<byte>();
                var buffer = new byte[3]; // Reads cross line and frame boundaries.
                int count;
                while ((count = await input.ReadAsync(buffer.AsMemory())) > 0)
                    actual.AddRange(buffer.Take(count));
                if (!actual.SequenceEqual(expected)) throw new Exception("File VBI discarded or altered source samples.");
                if (input.CapturedFrames != 2 || preview is null || preview.Length != 16)
                    throw new Exception("File VBI preview frames are wrong.");
                input.Position = 0;
                var firstFrame = new byte[4 * lineBytes];
                await input.ReadExactlyAsync(firstFrame);
                byte[] levels = wide
                    ? firstFrame.Where((_, index) => index % 2 == 1).ToArray()
                    : firstFrame;
                if (!preview!.SequenceEqual(levels)) throw new Exception("File VBI preview sample depth is wrong.");
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                try
                {
                    _ = await input.ReadAsync(buffer.AsMemory(), cancellation.Token);
                    throw new Exception("File VBI ignored cancellation.");
                }
                catch (OperationCanceledException) { }
                Console.WriteLine($"PASS: file VBI {(wide ? "UInt16" : "UInt8")} selection, preview, rewind and cancellation");
            }
            finally { File.Delete(path); }
        }
    }
}
