using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class ClientSettings : IServerboundPacket
{
    [Field(0)]
    public string Locale { get; private set; }

    [Field(1)]
    public sbyte ViewDistance { get; private set; }

    [Field(2)]
    public int ChatMode { get; private set; }

    [Field(3)]
    public bool ChatColors { get; private set; }

    [Field(4)]
    public byte SkinParts { get; private set; } // Skin parts that are displayed. Might not be necessary to decode?

    [Field(5)]
    public int MainHand { get; private set; }

    [Field(6)]
    public bool DisableTextFiltering { get; private set; }

    public int Id => 0x05;

    public ValueTask HandleAsync(Server server, Player player)
    {
        player.client.ClientSettings = this;
        return ValueTask.CompletedTask;
    }
}
