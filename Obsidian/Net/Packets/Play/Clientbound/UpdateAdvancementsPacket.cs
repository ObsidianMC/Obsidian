using Obsidian.API.Advancements;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

//TODO finish this packet PLEASE
public partial class UpdateAdvancementsPacket
{
    [Field(0)]
    public bool Reset { get; set; }

    [Field(1)]
    public List<Advancement> Added { get; set; } = [];

    [Field(2)]
    public List<string> Removed { get; set; } = [];

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteBoolean(this.Reset);

        writer.WriteVarInt(this.Added.Count);
        foreach (var advancement in this.Added)
            writer.WriteAdvancement(advancement);

        //Not sure what this is for
        writer.WriteVarInt(this.Removed.Count);
        foreach (var removed in this.Removed)
            writer.WriteString(removed);

        //Write progress for advancements
        foreach (var advancement in this.Added)
        {
            writer.WriteVarInt(advancement.Criteria.Count);

            foreach (var criteria in advancement.Criteria)
            {
                writer.WriteString(criteria.Identifier);

                writer.WriteBoolean(criteria.Achieved);

                if (criteria.Achieved)
                    writer.WriteLong(criteria.AchievedAt!.Value.ToUnixTimeMilliseconds());
            }
        }
    }
}
