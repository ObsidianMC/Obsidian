using Obsidian.Net.Actions.PlayerInfo;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class PlayerInfoUpdatePacket : IClientboundPacket
{
    [Field(0)]
    public BitSet UsedActions { get; } = new();

    /// <remarks>
    /// All action lists must set the same types of InfoAction set
    /// </remarks>
    [Field(1)]
    public Dictionary<Guid, List<InfoAction>> Actions { get; set; } = new();

    public int Id => 0x3A;

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
        var enumValues = Enum.GetValues<PlayerInfoAction>();
        var usedEnums = this.Actions.Values.First().Select(x => x.Type).Distinct();

        for (int i = 0; i < enumValues.Length; i++)
        {
            var usingBit = usedEnums.Contains(enumValues[i]);
            this.UsedActions.SetBit(i, usingBit);
        }
    }

    public void Serialize(MinecraftStream stream)
    {
        using var packetStream = new MinecraftStream();

        packetStream.WriteBitSet(this.UsedActions, true);
        packetStream.WriteVarInt(Actions.Count);
        foreach (var (uuid, actions) in this.Actions)
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

