using Obsidian.Entities;

namespace Obsidian.Net.Packets.Play.Serverbound;

/// <summary>
/// A player's pick-block (middle click) on a block.
/// </summary>
public partial class PickItemFromBlockPacket
{
    public Vector Position { get; private set; }

    /// <summary>
    /// Whether the client asked for the block's data too (a creative player's control-pick).
    /// </summary>
    public bool IncludeData { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.Position = reader.ReadPosition();
        this.IncludeData = reader.ReadBoolean();
    }

    public override ValueTask HandleAsync(IServer server, IClientPlayer player) =>
        player is PlayerBase picker
            ? picker.PickBlockAsync(this.Position, this.IncludeData)
            : ValueTask.CompletedTask;
}
