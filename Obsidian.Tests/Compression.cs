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
            Assert.Equal(bundledPacket.Packets[i].Id, innerStream.ReadVarInt());

            var chatMessage = innerStream.ReadChat();

            this.output.WriteLine($"Text: {chatMessage}");

            Assert.Equal(message, chatMessage.Text);
            Assert.Equal(i % 2 == 0, innerStream.ReadBoolean());
        }

        AssertDelimiter(buffer);
    }

    [Theory(DisplayName = "Packets round-trip through compressed framing")]
    [InlineData(10)]
    [InlineData(4000)]
    public void CompressedFramingRoundTrip(int messageLength)
    {
        var message = new string('x', messageLength);
        var chat = new SystemChatPacket(message, false);
        using var buffer = new NetworkBuffer();
        buffer.WriteCompressedPacket(chat, 256);
        buffer.Reset();

        var frameLength = buffer.ReadVarInt();
        var packet = NetworkBuffer.ReadCompressedPacket(buffer.Read(frameLength).GetBuffer());

        // Only the packet above the threshold is compressed, which makes this one far smaller than its text.
        if (messageLength > 256)
            Assert.True(frameLength < messageLength / 4);

        Assert.Equal(chat.Id, packet.Id);
        Assert.Equal(message, packet.NetworkBuffer.ReadChat().Text);
        Assert.False(packet.NetworkBuffer.ReadBoolean());
    }

    private static byte[] ReadCompressed(NetworkBuffer readStream, int dataLength, int packetLength)
    {
        packetLength -= dataLength.GetVarIntLength();
        var totalLength = dataLength != 0 ? dataLength : packetLength;

        var packetData = new byte[totalLength];
        var packetDataBuffer = readStream.Read(totalLength);

        if (dataLength != 0)
        {
            using var compressedData = new MemoryStream(packetDataBuffer.GetBuffer());

            compressedData.Position = 0;

            using var zlibStream = new ZLibStream(compressedData, CompressionMode.Decompress);

            zlibStream.ReadExactly(packetData);
        }
        else
        {
            packetData = packetDataBuffer.GetBuffer();
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
