namespace TeletextRecoveReese.Core;

// Presents the selected lines of successive file fields as the same two-field
// frames used by live capture. Decoder bytes retain the original sample depth;
// the raw image callback receives eight-bit levels for the shared preview UI.
public sealed class FileVbiCaptureStream : LiveVbiCaptureStream
{
    private readonly FileStream _source;
    private readonly VbiCaptureOptions _options;
    private readonly int _lineBytes;
    private readonly byte[] _frame;
    private int _frameOffset;
    private int _frameLength;
    private long _position;
    private long _capturedFrames;

    public FileVbiCaptureStream(string path, VbiCaptureOptions options)
    {
        if (options.LineLength <= 0 || options.FieldLines <= 0
            || options.FieldRangeStart < 0 || options.FieldRangeEnd > options.FieldLines
            || options.FieldRangeEnd <= options.FieldRangeStart)
            throw new ArgumentException("Invalid VBI field layout.", nameof(options));
        _options = options;
        _lineBytes = checked(options.LineLength * (options.IsUInt16 ? 2 : 1));
        _frame = new byte[checked(LinesPerFrame * _lineBytes)];
        _source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        long physicalLines = _source.Length / _lineBytes;
        long selectedLines = physicalLines / options.FieldLines * FirstFieldLines
            + Math.Clamp(physicalLines % options.FieldLines - options.FieldRangeStart, 0, FirstFieldLines);
        Length = selectedLines * _lineBytes;
    }

    public override uint SamplingRate => checked((uint)_options.SampleRate);
    public override int SamplesPerLine => _options.LineLength;
    public override int FirstFieldLines => _options.FieldRangeEnd - _options.FieldRangeStart;
    public override int SecondFieldLines => FirstFieldLines;
    public override long CapturedFrames => Interlocked.Read(ref _capturedFrames);
    public override Action<byte[]>? RawFrameCaptured { get; set; }
    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length { get; }
    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        long target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        if (target < 0 || target > Length) throw new ArgumentOutOfRangeException(nameof(offset));
        long selectedLine = target / _lineBytes;
        long field = selectedLine / FirstFieldLines;
        long physicalLine = field * _options.FieldLines + _options.FieldRangeStart
            + selectedLine % FirstFieldLines;
        _source.Position = physicalLine * _lineBytes + target % _lineBytes;
        _position = target;
        _frameOffset = _frameLength = 0;
        return target;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (buffer.Length == 0 || _position >= Length) return 0;
        if (_frameOffset == _frameLength)
        {
            Array.Clear(_frame);
            _frameOffset = _frameLength = 0;
            long nextPosition = _position;
            while (_frameLength < _frame.Length && nextPosition < Length)
            {
                long selectedLine = nextPosition / _lineBytes;
                long physicalLine = selectedLine / FirstFieldLines * _options.FieldLines
                    + _options.FieldRangeStart + selectedLine % FirstFieldLines;
                int withinLine = (int)(nextPosition % _lineBytes);
                int count = Math.Min(_lineBytes - withinLine, _frame.Length - _frameLength);
                _source.Position = physicalLine * _lineBytes + withinLine;
                await _source.ReadExactlyAsync(_frame.AsMemory(_frameLength, count), cancellationToken).ConfigureAwait(false);
                _frameLength += count;
                nextPosition += count;
            }
            Interlocked.Increment(ref _capturedFrames);
            if (RawFrameCaptured is { } callback)
            {
                var preview = new byte[LinesPerFrame * SamplesPerLine];
                if (_options.IsUInt16)
                    for (int index = 0; index < preview.Length; index++)
                        preview[index] = _frame[index * 2 + 1];
                else
                    _frame.CopyTo(preview, 0);
                callback(preview);
            }
        }
        int available = Math.Min(buffer.Length, _frameLength - _frameOffset);
        _frame.AsMemory(_frameOffset, available).CopyTo(buffer);
        _frameOffset += available;
        _position += available;
        return available;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (disposing) _source.Dispose();
        base.Dispose(disposing);
    }
    public override async ValueTask DisposeAsync()
    {
        await _source.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
