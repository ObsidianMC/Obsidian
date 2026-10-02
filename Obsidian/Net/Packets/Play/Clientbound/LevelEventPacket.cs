using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// Vanilla's level event: a sound or particle effect at a block, like 1501 for lava fizzing.
/// </summary>
/// <param name="global">Whether every player hears it at full volume (boss and end portal sounds).</param>
public partial class LevelEventPacket(int type, Vector position, int data, bool global = false)
{
    [Field(0)]
    public int Type { get; } = type;

    [Field(1)]
    public Vector Position { get; } = position;

    [Field(2)]
    public int Data { get; } = data;

    [Field(3)]
    public bool Global { get; } = global;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteInt(this.Type);
        writer.WritePosition(this.Position);
        writer.WriteInt(this.Data);
        writer.WriteBoolean(this.Global);
    }
}
