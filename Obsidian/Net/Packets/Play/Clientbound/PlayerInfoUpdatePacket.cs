using Obsidian.API;
using Obsidian.Net.Actions.PlayerInfo;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class PlayerInfoUpdatePacket : IClientboundPacket
{
    [Field(0)]
    public BitSet UsedActions { get; } = new();

    [Field(1)]
    public List<InfoAction> Actions { get; set; } = new();

    public int Id => 0x36;

    public PlayerInfoUpdatePacket(List<InfoAction> infoActions)
    {
        Actions.AddRange(infoActions);

        this.InitializeBitSet();
    }

    public PlayerInfoUpdatePacket(InfoAction infoAction)
    {
        Actions.Add(infoAction);

        this.InitializeBitSet();
    }

    private void InitializeBitSet()
    {
        foreach (var action in this.Actions)
        {
            var index = (int)action.Type;

            if (!this.UsedActions.GetBit(index))
                this.UsedActions.SetBit(index, true);
        }
    }
}

