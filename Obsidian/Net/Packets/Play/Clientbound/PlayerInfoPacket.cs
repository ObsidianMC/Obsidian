using Obsidian.Net.Actions.PlayerInfo;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class PlayerInfoPacket : IClientboundPacket
{
    [Field(0), ActualType(typeof(int)), VarLength]
    public PlayerInfoAction Action { get; }

    [Field(1)]
    public List<InfoAction> Actions { get; set; } = new();

    public int Id => 0x37;

    public PlayerInfoPacket(PlayerInfoAction action, List<InfoAction> infoActions)
    {
        Action = action;
        Actions.AddRange(infoActions);
    }

    public PlayerInfoPacket(PlayerInfoAction action, InfoAction infoAction)
    {
        Action = action;
        Actions.Add(infoAction);
    }
}

public enum PlayerInfoAction : int
{
    AddPlayer,

    UpdateGamemode,
    UpdateLatency,
    UpdateDisplayName,

    RemovePlayer
}
