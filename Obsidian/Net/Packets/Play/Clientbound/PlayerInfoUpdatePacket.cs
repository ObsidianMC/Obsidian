using Obsidian.Net.Actions.PlayerInfo;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class PlayerInfoUpdatePacket
{
    [Field(0)]
    public PlayerInfoAction Actions { get; private set; }

    /// <remarks>
    /// All action lists must set the same types of InfoAction set
    /// </remarks>
    [Field(1)]
    public Dictionary<Guid, List<InfoAction>> Players { get; set; } = [];

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

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteByte((sbyte)this.Actions);
        writer.WriteVarInt(Players.Count);
        foreach (var (uuid, actions) in this.Players)
        {
            var orderedActions = actions.OrderBy(x => (int)x.Type).ToList();

            writer.WriteUuid(uuid);

            for (int i = 0; i < orderedActions.Count; i++)
            {
                orderedActions[i].Write(writer);
            }
        }
    }
}



