using Obsidian.API.Utilities;
using Obsidian.Nbt;
using Obsidian.Net;
using System.IO;
using Xunit;

namespace Obsidian.Tests;

/// <summary>Network NBT as vanilla's FriendlyByteBuf writes and reads it: no root name, compounds or nothing.</summary>
public class NetworkNbt
{
    [Fact]
    public void CompoundsReadBackInPlace()
    {
        using var buffer = new NetworkBuffer();

        Extensions.WriteNbtCompound(buffer, new NbtCompound { new NbtTag<int>("x", 5) });
        buffer.WriteByte((byte)0x7F); // The packet's next field.

        using var reader = new NetworkBuffer(buffer.ToArray());

        Assert.Equal(5, reader.ReadNbtCompound().GetInt("x"));
        Assert.Equal(0x7F, reader.ReadByte());
    }

    [Fact]
    public void OptionalCompoundsAreAnEndTagOrACompound()
    {
        using var absent = new NetworkBuffer([0x00]);
        Assert.Null(absent.ReadOptionalNbtCompound());

        // Even an absent compound has its end tag; without it the packet is cut short.
        using var missing = new NetworkBuffer([]);
        Assert.Throws<EndOfStreamException>(() => missing.ReadOptionalNbtCompound());

        // A string root tag, which vanilla's readNbt rejects too.
        using var notCompound = new NetworkBuffer([0x08, 0x00, 0x01, (byte)'a']);
        Assert.Throws<InvalidDataException>(() => notCompound.ReadOptionalNbtCompound());
    }
}
