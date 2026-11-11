using Obsidian.Entities;
using Obsidian.WorldData;

namespace Obsidian.Net.Packets.Play.Serverbound;

/// <summary>Generates the structure selected by a jigsaw editor.</summary>
public partial class JigsawGeneratePacket
{
    public Vector Position { get; private set; }

    public int Levels { get; private set; }

    public bool KeepJigsaws { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.Position = reader.ReadPosition();
        this.Levels = reader.ReadVarInt();
        this.KeepJigsaws = reader.ReadBoolean();
    }

    public override async ValueTask HandleAsync(IServer server, IClientPlayer player)
    {
        if (!CommandBlockMinecart.CanEdit(player) || player.Level is not AbstractLevel level)
            return;

        await JigsawBlocks.GenerateAsync(level, this.Position, this.Levels, this.KeepJigsaws);
    }
}
