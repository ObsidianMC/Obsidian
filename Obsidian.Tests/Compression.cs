using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Utilities;
using System.IO;
using System.IO.Compression;
using Xunit;
using Xunit.Abstractions;

namespace Obsidian.Tests;
public sealed class Compression(ITestOutputHelper output)
{
    private static readonly string[] messages = ["Test", "Another Test", "Lorem sit amet, consectetur adipiscing elit.", "Overlayed"];

    private readonly ITestOutputHelper output = output;


    [Fact(DisplayName = "Compressed Successfully")]
    public void CompressionTest()
    {
        using var buffer = new NetworkBuffer();

        var bundledPacket = new BundledPacket([]);

        for (var i = 0; i < messages.Length; i++)
        {
            var message = messages[i];
            bundledPacket.Packets.Add(new SystemChatPacket(message, i % 2 == 0));
        }

        buffer.WriteCompressedPacket(bundledPacket, 256);
        buffer.Reset();

        AssertDelimiter(buffer);

        for (int i = 0; i < messages.Length; i++)
        {
            var message = messages[i];

            var innerPacketLength = buffer.ReadVarInt();
            var innerDataLength = buffer.ReadVarInt();

            using var innerStream = new NetworkBuffer(ReadCompressed(buffer, innerDataLength, innerPacketLength));

            //Make sure packet id matches.
            Assert.Equal(115, innerStream.ReadVarInt());

            var chatMessage = innerStream.ReadChat();

            this.output.WriteLine($"Text: {chatMessage}");

            Assert.Equal(message, chatMessage.Text);
            Assert.Equal(i % 2 == 0, innerStream.ReadBoolean());
        }

        AssertDelimiter(buffer);
    }

    private static byte[] ReadCompressed(NetworkBuffer readStream, int dataLength, int packetLength)
    {
        packetLength -= dataLength.GetVarIntLength();
        var totalLength = dataLength != 0 ? dataLength : packetLength;

        var packetData = new byte[totalLength];
        var packetDataBuffer = readStream.Read(totalLength);

        if (dataLength != 0)
        {
            using var compressedData = new MemoryStream(packetDataBuffer.Data);

            compressedData.Position = 0;

            using var zlibStream = new ZLibStream(compressedData, CompressionMode.Decompress);

            zlibStream.ReadExactly(packetData);
        }
        else
        {
            packetData = packetDataBuffer.Data;
        }

        return packetData;
    }

    private static void AssertDelimiter(NetworkBuffer readStream)
    {
        var packetLength = readStream.ReadVarInt();
        var dataLength = readStream.ReadVarInt();

        using var packetStream = new NetworkBuffer(ReadCompressed(readStream, dataLength, packetLength));

        Assert.Equal(0, packetStream.ReadVarInt());
    }
}
