using System.IO.Pipes;
using System.Text;
using Serilog;

namespace GuardianConnect.Shared;

// Defines the data protocol for reading and writing strings on our stream
public class StreamString
{
    private Stream ioStream;
    private UnicodeEncoding streamEncoding;

    public StreamString(Stream ioStream)
    {
        this.ioStream = ioStream;
        streamEncoding = new UnicodeEncoding();
    }


    public string ReadString()
    {
        int len = 0;

        len = ioStream.ReadByte() * 256;
        len += ioStream.ReadByte();
        if (!((PipeStream)ioStream).IsConnected)
        {
            Log.Information("ReadByte stream disconnected");
            return "";
        }

        byte[] inBuffer = new byte[len];
        ioStream.ReadExactly(inBuffer, 0, len);

        return streamEncoding.GetString(inBuffer);
    }


    public async Task<string> ReadStringAsync()
    {
        int len = 0;

        len = ioStream.ReadByte() * 256;
        len += ioStream.ReadByte();
        if (!((PipeStream)ioStream).IsConnected)
        {
            Log.Information("ReadByte stream disconnected");
            return "";
        }

        byte[] inBuffer = new byte[len];
        var readAsync = await ioStream.ReadAsync(inBuffer, 0, len);

        string s = streamEncoding.GetString(inBuffer);

        return Task.FromResult(s).Result;
    }

    public int WriteString(string outString)
    {
        byte[] outBuffer = streamEncoding.GetBytes(outString);
        int len = outBuffer.Length;
        if (len > UInt16.MaxValue)
        {
            len = (int)UInt16.MaxValue;
        }
        ioStream.WriteByte((byte)(len / 256));
        ioStream.WriteByte((byte)(len & 255));
        ioStream.Write(outBuffer, 0, len);
        ioStream.Flush();

        return outBuffer.Length + 2;
    }
}
