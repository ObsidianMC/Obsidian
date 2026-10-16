using Obsidian.API.Advancements;

namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>Vanilla 1.21.11 definitions, removals and independent per-player criterion progress.</summary>
public partial class UpdateAdvancementsPacket
{
    public bool Reset { get; set; }
    public List<Advancement> Added { get; set; } = [];
    public List<string> Removed { get; set; } = [];
    public Dictionary<string, Dictionary<string, DateTimeOffset?>> Progress { get; set; } = [];
    public bool ShowAdvancements { get; set; } = true;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteBoolean(this.Reset);
        writer.WriteVarInt(this.Added.Count);
        foreach (var advancement in this.Added)
        {
            writer.WriteString(advancement.Identifier);
            writer.WriteAdvancement(advancement);
        }

        writer.WriteVarInt(this.Removed.Count);
        foreach (var identifier in this.Removed)
            writer.WriteString(identifier);

        writer.WriteVarInt(this.Progress.Count);
        foreach (var (identifier, criteria) in this.Progress)
        {
            writer.WriteString(identifier);
            writer.WriteVarInt(criteria.Count);
            foreach (var (criterion, achieved) in criteria)
            {
                writer.WriteString(criterion);
                writer.WriteBoolean(achieved.HasValue);
                if (achieved is DateTimeOffset date)
                    writer.WriteLong(date.ToUnixTimeMilliseconds());
            }
        }

        writer.WriteBoolean(this.ShowAdvancements);
    }
}
