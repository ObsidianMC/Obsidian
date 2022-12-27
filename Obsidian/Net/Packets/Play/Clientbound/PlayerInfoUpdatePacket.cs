using Obsidian.Net.Actions.PlayerInfo;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class PlayerInfoUpdatePacket : IClientboundPacket
{
    [Field(0)]
    public BitSet UsedActions { get; } = new();

    [Field(1)]
    public Dictionary<Guid, List<InfoAction>> Actions { get; set; } = new();

    public int Id => 0x36;

    public PlayerInfoUpdatePacket(Dictionary<Guid, List<InfoAction>> infoActions)
    {
        this.Actions = new(infoActions);

        this.InitializeBitSet();
    }

    public PlayerInfoUpdatePacket(Guid uuid, InfoAction infoAction)
    {
        Actions.Add(uuid, new() { infoAction });

        this.InitializeBitSet();
    }

    private void InitializeBitSet()
    {
        foreach (var (_, actions) in this.Actions)
        {
            foreach (var action in actions)
            {
                var index = (int)action.Type;

                if (!this.UsedActions.GetBit(index))
                    this.UsedActions.SetBit(index, true);
            }
        }
    }

    public void Serialize(MinecraftStream stream)
    {
        using var packetStream = new MinecraftStream();

        packetStream.WriteBitSet(this.UsedActions, true);
        packetStream.WriteVarInt(Actions.Count);

        foreach(var (uuid, actions) in this.Actions)
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

