using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SetExperiencePacket : IClientboundPacket
{
    [Field(0)]
    public float ExperienceBar { get; }

    [Field(1), VarLength]
    public int Level { get; }

    [Field(2), VarLength]
    public int TotalExperience { get; }

    public int Id => 0x5C;

    public SetExperiencePacket(float experienceBar, int level, int totalExperience)
    {
        ExperienceBar = experienceBar;
        Level = level;
        TotalExperience = totalExperience;
    }

    public static SetExperiencePacket FromLevel(int level) => new(0, level, XpHelper.TotalExperienceFromLevel(level));
}
