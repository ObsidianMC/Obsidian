using Obsidian.Entities;

namespace Obsidian.Net.Packets.Play.Serverbound;

/// <summary>
/// The command an operator typed into a command block minecart's screen (vanilla's
/// <c>ServerboundSetCommandMinecartPacket</c>), taken from creative operators only.
/// </summary>
public partial class SetCommandMinecartPacket
{
    public int EntityId { get; private set; }

    public string Command { get; private set; } = string.Empty;

    public bool TrackOutput { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.EntityId = reader.ReadVarInt();
        this.Command = reader.ReadString();
        this.TrackOutput = reader.ReadBoolean();
    }

    public override ValueTask HandleAsync(IServer server, IClientPlayer player)
    {
        if (!CommandBlockMinecart.CanEdit(player)
            || player.GetEntitiesNear(16).FirstOrDefault(entity => entity.EntityId == this.EntityId)
                is not CommandBlockMinecart minecart)
        {
            return default;
        }

        if (minecart.Level is WorldData.AbstractLevel level)
        {
            level.EnqueueEntityAction(() =>
            {
                minecart.Edit(this.Command, this.TrackOutput);
                return default;
            });
        }

        return default;
    }
}
