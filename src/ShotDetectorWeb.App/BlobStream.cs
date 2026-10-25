using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace ShotDetectorWeb.App;

/// <summary>
/// A read-only, seekable stream over a JavaScript <c>Blob</c> (the <c>File</c> the user picked), read in slices through
/// the host's synchronous <c>read</c> (wwwroot/blob-read.js: <c>FileReaderSync</c> in the worker), so the file never
/// has to fit in memory (docs/decisions/2026-10-24-the-page.md, "The file in memory").
/// </summary>
/// <remarks>
/// Reads go through a buffer of <see cref="BufferSize"/>: the MP4 demuxer reads each sample at its offset, in file
/// order, a few kilobytes at a time, and every slice read costs a call into JavaScript and a <c>FileReaderSync</c> round
/// trip. A read at least as big as the buffer goes straight into the caller's memory.
/// </remarks>
[SupportedOSPlatform("browser")]
sealed partial class BlobStream : Stream
{
    /// <summary>The read-ahead: big enough that a slice read's fixed cost doesn't count, small beside the heap.</summary>
    internal const int BufferSize = 1 << 20;

    readonly JSObject _blob;
    readonly byte[] _buffer = new byte[BufferSize];
    long _bufferStart;
    int _bufferCount;
    long _position;

    /// <summary>A stream over <paramref name="blob"/>, which stays the caller's (it isn't disposed with the stream).</summary>
    public BlobStream(JSObject blob)
    {
        _blob = blob;
        Length = (long)blob.GetPropertyAsDouble("size");
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length { get; }

    public override long Position
    {
        get => _position;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _position = value;
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> destination)
    {
        if (_position >= Length || destination.IsEmpty)
            return 0;
        destination = destination[..(int)Math.Min(destination.Length, Length - _position)];

        long inBuffer = _position - _bufferStart;
        if (inBuffer < 0 || inBuffer >= _bufferCount)
        {
            if (destination.Length >= BufferSize)
            {
                int read = ReadSlice(_blob, _position, destination);
                _position += read;
                return read;
            }

            _bufferStart = _position;
            _bufferCount = ReadSlice(_blob, _position, _buffer.AsSpan(0, (int)Math.Min(BufferSize, Length - _position)));
            inBuffer = 0;
            if (_bufferCount == 0)
                throw new EndOfStreamException($"The file ended at {_position} bytes, before the {Length} it said it had.");
        }

        int n = Math.Min(destination.Length, _bufferCount - (int)inBuffer);
        _buffer.AsSpan((int)inBuffer, n).CopyTo(destination);
        _position += n;
        return n;
    }

    public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
    {
        SeekOrigin.Begin => offset,
        SeekOrigin.Current => _position + offset,
        SeekOrigin.End => Length + offset,
        _ => throw new ArgumentOutOfRangeException(nameof(origin)),
    };

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <summary>Reads <paramref name="destination"/>'s length of bytes of <paramref name="blob"/> at <paramref name="position"/> (fewer at its end): how many.</summary>
    [JSImport("read", "host")]
    private static partial int ReadSlice(
        JSObject blob,
        [JSMarshalAs<JSType.Number>] long position,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> destination);
}
