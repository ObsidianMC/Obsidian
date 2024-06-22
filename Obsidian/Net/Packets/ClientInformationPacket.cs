using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets;
public sealed partial class ClientInformationPacket : IServerboundPacket
{
    [Field(0)]
    public string Locale { get; private set; } = null!;

    [Field(1)]
    public sbyte ViewDistance { get; private set; }

    [Field(2), ActualType(typeof(int)), VarLength]
    public ChatMode ChatMode { get; private set; }

    [Field(3)]
    public bool ChatColors { get; private set; }

    [Field(4), ActualType(typeof(byte))]
    public PlayerBitMask DisplayedSkinParts { get; private set; } // Skin parts that are displayed. Might not be necessary to decode?

    [Field(5), ActualType(typeof(int)), VarLength]
    public MainHand MainHand { get; private set; }

    [Field(6)]
    public bool EnableTextFiltering { get; private set; }

    [Field(7)]
    public bool AllowServerListings { get; private set; }

    //0x0A for play state
    public int Id { get; init; } = 0x00;

    public async ValueTask HandleAsync(Server server, Player player)
    {
        player.ClientInformation = new()
        {
            Locale = this.Locale,
            ViewDistance = sbyte.Min(ViewDistance, (sbyte) server.Configuration.ViewDistance),
            ChatMode = this.ChatMode,
            ChatColors = this.ChatColors,
            DisplayedSkinParts = this.DisplayedSkinParts,
            MainHand = this.MainHand,
            EnableTextFiltering = this.EnableTextFiltering,
            AllowServerListings = this.AllowServerListings
        };

        await player.client.SendInfoAsync();
    }
}
