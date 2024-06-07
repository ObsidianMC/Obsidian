using Obsidian.Net.Actions.PlayerInfo;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class PlayerInfoUpdatePacket : IClientboundPacket
{
    [Field(0)]
    public PlayerInfoAction Actions { get; private set; }

    /// <remarks>
    /// All action lists must set the same types of InfoAction set
    /// </remarks>
    [Field(1)]
    public Dictionary<Guid, List<InfoAction>> Players { get; set; } = [];

    public int Id => 0x3E;

    public PlayerInfoUpdatePacket(Dictionary<Guid, List<InfoAction>> infoActions)
    {
        this.Players = new(infoActions);

        this.InitActions();
    }

    public PlayerInfoUpdatePacket(Guid uuid, InfoAction infoAction)
    {
        Players.Add(uuid, [infoAction]);

        this.InitActions();
    }

    private void InitActions()
    {
        var usedEnums = this.Players.Values.First().Select(x => x.Type).Distinct();
        foreach(var usedEnum in usedEnums)
            this.Actions |= usedEnum;
    }

    public void Serialize(MinecraftStream stream)
    {
        using var packetStream = new MinecraftStream();

        packetStream.WriteByte((sbyte)this.Actions);
        packetStream.WriteVarInt(Players.Count);
        foreach (var (uuid, actions) in this.Players)
        {
            var orderedActions = actions.OrderBy(x => (int)x.Type).ToList();

            packetStream.WriteUuid(uuid);

            for (int i = 0; i < orderedActions.Count; i++)
            {
                packetStream.WritePlayerInfoAction(orderedActions[i]);
            }
        }

        stream.Lock.Wait();
        stream.WriteVarInt(Id.GetVarIntLength() + (int)packetStream.Length);
        stream.WriteVarInt(Id);
        packetStream.Position = 0;
        packetStream.CopyTo(stream);
        stream.Lock.Release();
    }
}



