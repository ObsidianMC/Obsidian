using Obsidian.WorldData;

namespace Obsidian.Net.Packets.Play.Serverbound;

/// <summary>
/// The lines a player wrote in the sign editor, for one side of the sign.
/// </summary>
public partial class SignUpdatePacket
{
    public Vector Position { get; private set; }

    public bool IsFrontText { get; private set; }

    public string[] Lines { get; private set; } = [];

    public override void Populate(INetStreamReader reader)
    {
        this.Position = reader.ReadPosition();
        this.IsFrontText = reader.ReadBoolean();

        var lines = new string[4];
        for (var i = 0; i < lines.Length; i++)
            lines[i] = reader.ReadString();

        this.Lines = lines;
    }

    public override async ValueTask HandleAsync(IServer server, IClientPlayer player)
    {
        if (player.Level is not AbstractLevel level)
            return;

        // On the level's tick, like other changes to its blocks.
        var (position, front, lines) = (this.Position, this.IsFrontText, this.Lines);
        level.EnqueueEntityAction(() =>
        {
            Signs.Edit(level, position, player, front, lines);
            return ValueTask.CompletedTask;
        });

        await ValueTask.CompletedTask;
    }
}
