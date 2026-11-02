using System.IO.Pipes;
using System.Text;
using Serilog;

namespace GuardianConnect.Shared;

// Defines the data protocol for reading and writing strings on our stream
public class StreamString
{
    private readonly Stream ioStream;
    private readonly UnicodeEncoding streamEncoding;

    public StreamString(Stream ioStream)
    {
        this.ioStream = ioStream;
        streamEncoding = new UnicodeEncoding();
    }


    /// <summary>
    /// Reads the two-byte length prefix. Null when the pipe is no longer
    /// connected. ReadByte returns -1 at end of stream, when the other end has
    /// closed the pipe; the server side can still report IsConnected at that
    /// point, so -1 is checked as well.
    /// </summary>
    /// <exception cref="EndOfStreamException">The other end closed the pipe.</exception>
    private int? ReadLengthPrefix()
    {
        var high = ioStream.ReadByte();
        var low = high < 0 ? -1 : ioStream.ReadByte();
        if (!((PipeStream)ioStream).IsConnected)
        {
            Log.Information("ReadByte stream disconnected");
            return null;
        }
        if (high < 0 || low < 0) throw new EndOfStreamException("Pipe closed by the other end.");
        return high * 256 + low;
    }

    public string ReadString()
    {
        if (ReadLengthPrefix() is not { } len) return "";

        var inBuffer = new byte[len];
        ioStream.ReadExactly(inBuffer, 0, len);

        return streamEncoding.GetString(inBuffer);
    }


    public async Task<string> ReadStringAsync()
    {
        if (ReadLengthPrefix() is not { } len) return "";

        var inBuffer = new byte[len];

        // ReadExactlyAsync (NOT ReadAsync): a named-pipe ReadAsync is
        // allowed to return fewer bytes than requested on a single call,
        // and earlier this method ignored the actual byte count and
        // decoded the whole buffer anyway. Any unread bytes from the
        // short read stayed in the pipe and were then mis-interpreted as
        // the two-byte length prefix of the next message, throwing the
        // framing permanently off (UTF-16 reads of off-by-N stream
        // positions produce garbled high-codepoint characters and
        // bogus message lengths). Short reads were rare with small
        // IKEv2-era responses but reliably broke the WG-disconnect
        // flow once WG actually connected end-to-end. ReadExactlyAsync
        // loops internally until the requested byte count is satisfied.
        await ioStream.ReadExactlyAsync(inBuffer, 0, len).ConfigureAwait(false);

        return streamEncoding.GetString(inBuffer);
    }

    public int WriteString(string outString)
    {
        var outBuffer = streamEncoding.GetBytes(outString);
        var len = outBuffer.Length;
        if (len > ushort.MaxValue) len = ushort.MaxValue;
        ioStream.WriteByte((byte)(len / 256));
        ioStream.WriteByte((byte)(len & 255));
        ioStream.Write(outBuffer, 0, len);
        ioStream.Flush();

        return outBuffer.Length + 2;
    }
}