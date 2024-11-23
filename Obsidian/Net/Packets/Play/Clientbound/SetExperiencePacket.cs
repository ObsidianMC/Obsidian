using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SetExperiencePacket(float experienceBar, int level, int totalExperience)
{
    [Field(0)]
    public float ExperienceBar { get; } = experienceBar;

    [Field(1), VarLength]
    public int Level { get; } = level;

    [Field(2), VarLength]
    public int TotalExperience { get; } = totalExperience;

    public static SetExperiencePacket FromLevel(int level) => new(0, level, XpHelper.TotalExperienceFromLevel(level));

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteFloat(this.ExperienceBar);

        writer.WriteVarInt(this.Level);
        writer.WriteVarInt(this.TotalExperience);
    }
}
