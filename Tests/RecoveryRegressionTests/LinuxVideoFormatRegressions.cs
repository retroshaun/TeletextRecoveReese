using System.Reflection;
using TeletextRecoveReese.Core;

internal static class LinuxVideoFormatRegressions
{
    private const uint Yuyv = 0x56595559, Uyvy = 0x59565955, Yu12 = 0x32315559,
        Yv12 = 0x32315659, Grey = 0x59455247, Mjpg = 0x47504A4D;

    private static byte[] Negotiate(byte[] current, uint[] supported, Func<byte[], bool> set)
    {
        try
        {
            return (byte[])typeof(LinuxVbiCaptureStream).GetMethod("NegotiateVideoPreviewFormat",
                BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { current, supported, set })!;
        }
        catch (TargetInvocationException ex) { throw ex.InnerException!; }
    }

    private static byte[] Current(uint pixelFormat)
    {
        var format = new byte[208];
        BitConverter.TryWriteBytes(format.AsSpan(0, 4), 1u);
        BitConverter.TryWriteBytes(format.AsSpan(8, 4), 720u);
        BitConverter.TryWriteBytes(format.AsSpan(12, 4), 576u);
        BitConverter.TryWriteBytes(format.AsSpan(16, 4), pixelFormat);
        return format;
    }

    public static void Run()
    {
        foreach (var test in new[]
        {
            (Current: Yuyv, Supported: new[] { Yu12, Yuyv }, Expected: Yuyv),
            (Current: Yu12, Supported: new[] { Yu12, Yuyv }, Expected: Yuyv),
            (Current: Yu12, Supported: new[] { Yu12, Uyvy }, Expected: Uyvy),
            (Current: Yv12, Supported: new[] { Yu12, Yv12 }, Expected: Yv12),
            (Current: Yu12, Supported: new[] { Yu12 }, Expected: Yu12),
            (Current: Mjpg, Supported: new[] { Grey }, Expected: Grey),
        })
        {
            int calls = 0;
            byte[] current = Current(test.Current), original = (byte[])current.Clone();
            byte[] result = Negotiate(current, test.Supported, candidate =>
            {
                calls++;
                uint requested = BitConverter.ToUInt32(candidate, 16);
                if (!test.Supported.Contains(requested) || requested != test.Expected)
                    throw new Exception("Requested an unsupported/unexpected video format.");
                BitConverter.TryWriteBytes(candidate.AsSpan(24, 4), 1440u);
                BitConverter.TryWriteBytes(candidate.AsSpan(28, 4), 829440u);
                return true;
            });
            if (calls != 1 || BitConverter.ToUInt32(result, 16) != test.Expected
                || BitConverter.ToUInt32(result, 28) != 829440 || !current.SequenceEqual(original))
                throw new Exception("Video format was not initialized or returned geometry was lost.");
        }
        Console.WriteLine("PASS: video formats are enumerated before selection; packed, planar and GREY formats initialize even when unchanged");

        var attempted = new List<uint>();
        var fallback = Negotiate(Current(Yuyv), new[] { Yuyv, Uyvy }, candidate =>
        {
            uint requested = BitConverter.ToUInt32(candidate, 16);
            attempted.Add(requested);
            return requested == Uyvy;
        });
        if (!attempted.SequenceEqual(new[] { Yuyv, Uyvy }) || BitConverter.ToUInt32(fallback, 16) != Uyvy)
            throw new Exception("Rejected YUYV did not fall back to another advertised format.");
        Console.WriteLine("PASS: a rejected video format falls back to another advertised displayable format");

        try
        {
            Negotiate(Current(Mjpg), new[] { Mjpg }, _ => throw new Exception("Requested an unsupported compressed format."));
            throw new Exception("Unsupported-only video device was accepted.");
        }
        catch (NotSupportedException) { }
        try
        {
            Negotiate(Current(Yuyv), new[] { Yuyv }, _ => false);
            throw new Exception("A rejected format was used without initialization.");
        }
        catch (IOException) { }
        Console.WriteLine("PASS: unsupported and uninitializable video formats fail explicitly instead of using stale capture geometry");
    }
}
