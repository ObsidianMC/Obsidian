using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Utilities;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Obsidian.Tests.Utilities;
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

    [Fact(DisplayName = "Packets round-trip through compressed framing at every level")]
    public void CompressedFramingRoundTrip()
    {
        FramingCase[] cases =
        [
            new(MessageLength: 10, Level: NetworkBuffer.DefaultCompressionLevel),
            new(MessageLength: 4000, Level: NetworkBuffer.DefaultCompressionLevel),
            new(MessageLength: 4000, Level: 0),
            new(MessageLength: 4000, Level: 1),
            new(MessageLength: 4000, Level: 9),
            new(MessageLength: 4000, Level: 42)
        ];

        FixtureCases.AssertAll(cases, row => row.ToString(), AssertCompressedFramingRoundTrip);
    }

    // A level outside -1 to 9 is clamped rather than failing each packet.
    private sealed record FramingCase(int MessageLength, int Level);

    private static void AssertCompressedFramingRoundTrip(FramingCase row)
    {
        var message = new string('x', row.MessageLength);
        var chat = new SystemChatPacket(message, false);
        var bodies = new List<NetworkBuffer>();
        NetworkBuffer.SerializeBodies(chat, writesPluginEntityData: false, bodies);

        using var buffer = new NetworkBuffer();
        var framer = new PacketFramer(row.Level);
        framer.Write(buffer, new OutgoingFrame(bodies.Single(), CompressionThreshold: 256));
        buffer.Reset();

        var frameLength = buffer.ReadVarInt();
        var packet = NetworkBuffer.ReadCompressedPacket(buffer.Read(frameLength).GetBuffer());

        // Only the packet above the threshold is compressed, which, at any level but 0, makes this one far smaller
        // than its text.
        if (row.MessageLength > 256 && row.Level != 0)
            Assert.True(frameLength < row.MessageLength / 4);

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
