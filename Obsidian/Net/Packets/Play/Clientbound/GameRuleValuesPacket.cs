namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>Namespaced rule keys and their textual values, for a full snapshot or an incremental update.</summary>
public partial class GameRuleValuesPacket
{
    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.Values.Count);
        foreach (var (name, value) in this.Values)
        {
            writer.WriteString(name);
            writer.WriteString(value);
        }
    }
}
