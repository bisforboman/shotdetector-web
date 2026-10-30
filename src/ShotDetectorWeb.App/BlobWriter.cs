using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace ShotDetectorWeb.App;

/// <summary>
/// A write-only stream whose bytes become a JavaScript <c>Blob</c>: each write is copied out to the host as it comes
/// (wwwroot/blob-write.js: a <c>Uint8Array</c> in a list), and <see cref="ToBlob"/> makes the <c>Blob</c> of them, so
/// the text written (the stats CSV, README "What the heap holds") is never whole in .NET's heap.
/// </summary>
/// <remarks>
/// Written through a <see cref="StreamWriter"/> of <see cref="BufferSize"/> characters: each part is a call into
/// JavaScript and a copy there, so they are big enough that the calls don't count, and small beside the heap.
/// </remarks>
[SupportedOSPlatform("browser")]
sealed partial class BlobWriter : Stream
{
    /// <summary>The <see cref="StreamWriter"/>'s buffer in characters, and so about each part's size in bytes (ASCII).</summary>
    internal const int BufferSize = 1 << 18;

    readonly JSObject _parts = NewParts();
    long _length;

    /// <summary>
    /// A <c>Blob</c> of <paramref name="type"/> made of what <paramref name="write"/> writes to its writer, in UTF-8
    /// without a byte order mark (as a JavaScript string becomes in a <c>Blob</c>).
    /// </summary>
    public static JSObject Write(Action<TextWriter> write, string type)
    {
        using var stream = new BlobWriter();
        using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), BufferSize, leaveOpen: true))
            write(writer);
        return stream.ToBlob(type);
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _length;

    public override long Position
    {
        get => _length;
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> source)
    {
        if (source.IsEmpty)
            return;
        // A MemoryView marshals a Span, not a ReadOnlySpan; blob-write.js only copies from it.
        Append(_parts, MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(source), source.Length));
        _length += source.Length;
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    /// <summary>The <c>Blob</c> of the parts written so far, of <paramref name="type"/>.</summary>
    public JSObject ToBlob(string type) => MakeBlob(_parts, type);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _parts.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>A new, empty list of parts.</summary>
    [JSImport("parts", "host")]
    private static partial JSObject NewParts();

    /// <summary>Adds a copy of <paramref name="bytes"/> (a .NET Span, valid only during the call) to <paramref name="parts"/>.</summary>
    [JSImport("append", "host")]
    private static partial void Append(JSObject parts, [JSMarshalAs<JSType.MemoryView>] Span<byte> bytes);

    /// <summary>The <c>Blob</c> of <paramref name="parts"/>, of <paramref name="type"/>.</summary>
    [JSImport("blob", "host")]
    private static partial JSObject MakeBlob(JSObject parts, string type);
}
