using Microsoft.Win32.SafeHandles;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TeletextRecoveReese.Core;

internal static class LinuxVbiReadRegressions
{
    [DllImport("libc", SetLastError = true)]
    private static extern int pipe(int[] descriptors);

    // A pipe supplies real blocking Linux reads without a capture card. Bypass
    // only the V4L2 format ioctls so the production frame-reading code is tested.
    private static (LinuxVbiCaptureStream Capture, FileStream Writer) Create(bool asyncMode)
    {
        var descriptors = new int[2];
        if (pipe(descriptors) != 0) throw new IOException("Could not create test pipe.");
        using var originalReader = new SafeFileHandle(descriptors[0], true);
        var reader = new FileStream($"/proc/self/fd/{descriptors[0]}", FileMode.Open,
            FileAccess.Read, FileShare.ReadWrite, 1,
            asyncMode ? FileOptions.Asynchronous : FileOptions.None);
        var writer = new FileStream(new SafeFileHandle(descriptors[1], true), FileAccess.Write, 1, false);
        var capture = (LinuxVbiCaptureStream)RuntimeHelpers.GetUninitializedObject(typeof(LinuxVbiCaptureStream));
        void Set(string name, object value) => typeof(LinuxVbiCaptureStream)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(capture, value);
        Set("_device", reader);
        Set("_frame", new byte[32]);
        Set("<SamplesPerLine>k__BackingField", 8);
        Set("<FirstFieldLines>k__BackingField", 2);
        Set("<SecondFieldLines>k__BackingField", 2);
        Set("<AsyncModeRead>k__BackingField", asyncMode);
        return (capture, writer);
    }

    public static async Task RunAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        foreach (bool asyncMode in new[] { false, true })
        {
            var (capture, writer) = Create(asyncMode);
            using (capture)
            using (writer)
            {
                int callbacks = 0;
                capture.RawFrameCaptured = frame =>
                {
                    if (frame.Length != 32) throw new Exception("Wrong VBI preview frame size.");
                    callbacks++;
                };
                if (await capture.ReadAsync(Memory<byte>.Empty) != 0 || capture.CapturedFrames != 0)
                    throw new Exception("Empty read consumed a frame.");
                byte[] expected = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();
                byte[] actual = new byte[64];
                var firstRead = capture.ReadAsync(actual.AsMemory(0, 8)).AsTask();
                if (firstRead.IsCompleted) throw new Exception("VBI read completed without data.");
                await writer.WriteAsync(expected.AsMemory(0, 10));
                await Task.Delay(30);
                if (firstRead.IsCompleted || callbacks != 0)
                    throw new Exception("Partial VBI frame was published.");
                await writer.WriteAsync(expected.AsMemory(10, 22));
                int count = await firstRead.WaitAsync(TimeSpan.FromSeconds(3));
                while (count < 32)
                    count += await capture.ReadAsync(actual.AsMemory(count, 32 - count));
                await writer.WriteAsync(expected.AsMemory(32));
                count += await capture.ReadAsync(actual, count, 32, CancellationToken.None);
                if (count != 64 || !actual.SequenceEqual(expected)
                    || capture.CapturedFrames != 2 || callbacks != 2)
                    throw new Exception("VBI frame data or callback count changed.");
                writer.Dispose();
                if (await capture.ReadAsync(actual.AsMemory()) != 0)
                    throw new Exception("VBI EOF was not returned.");
                Console.WriteLine($"PASS: Linux {(asyncMode ? "async" : "blocking")} VBI reads preserve partial frames, byte order, preview and EOF");
            }
        }

        var (waiting, idleWriter) = Create(false);
        using (waiting)
        using (idleWriter)
        using (var cancellation = new CancellationTokenSource())
        {
            var read = waiting.ReadAsync(new byte[8].AsMemory(), cancellation.Token).AsTask();
            cancellation.CancelAfter(100);
            try
            {
                await read.WaitAsync(TimeSpan.FromSeconds(3));
                throw new Exception("Idle synchronous VBI read ignored cancellation.");
            }
            catch (OperationCanceledException) { }
            if (waiting.CapturedFrames != 0)
                throw new Exception("Cancelled VBI read published a frame.");
            Console.WriteLine("PASS: idle blocking VBI read cancels without a signal or another frame");

            using var stop = new CancellationTokenSource();
            var stoppedRead = waiting.ReadAsync(new byte[8].AsMemory(), stop.Token).AsTask();
            stop.Cancel();
            waiting.Dispose();
            try
            {
                await stoppedRead.WaitAsync(TimeSpan.FromSeconds(3));
                throw new Exception("VBI Stop returned a frame instead of cancellation.");
            }
            catch (OperationCanceledException) { }
            Console.WriteLine("PASS: capture Stop cancels and closes an idle blocking reader");
        }
    }
}
