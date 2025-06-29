using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Common;
public partial record class ClientInformationPacket
{
    [Field(0)]
    public string Locale { get; private set; } = null!;

    [Field(1)]
    public sbyte ViewDistance { get; private set; }

    [Field(2), ActualType(typeof(int)), VarLength]
    public ChatVisibility ChatVisibility { get; private set; }

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

    [Field(8), ActualType(typeof(int)), VarLength]
    public ParticleStatus ParticleStatus { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        Locale = reader.ReadString();
        ViewDistance = reader.ReadSignedByte();
        ChatVisibility = reader.ReadVarInt<ChatVisibility>();
        ChatColors = reader.ReadBoolean();
        DisplayedSkinParts = reader.ReadUnsignedByte<PlayerBitMask>();
        MainHand = reader.ReadVarInt<MainHand>();
        EnableTextFiltering = reader.ReadBoolean();
        AllowServerListings = reader.ReadBoolean();
        ParticleStatus = reader.ReadVarInt<ParticleStatus>();
    }

    public override ValueTask HandleAsync(IServer server, IPlayer player)
    {
        player.ClientInformation = new()
        {
            Locale = Locale,
            ViewDistance = sbyte.Min(ViewDistance, (sbyte)server.Configuration.ViewDistance),
            ChatMode = ChatVisibility,
            ChatColors = ChatColors,
            DisplayedSkinParts = DisplayedSkinParts,
            MainHand = MainHand,
            EnableTextFiltering = EnableTextFiltering,
            AllowServerListings = AllowServerListings,
            ParticleStatus = ParticleStatus
        };

        return default;
    }
}
