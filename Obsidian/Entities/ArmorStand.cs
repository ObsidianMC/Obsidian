namespace Obsidian.Entities;

[MinecraftEntity("minecraft:armor_stand")]
public sealed partial class ArmorStand : Living
{
    public StandProperties StandProperties { get; set; }

    public Rotation Head { get; set; }
    public Rotation Body { get; set; }
    public Rotation LeftArm { get; set; }
    public Rotation RightArm { get; set; }
    public Rotation LeftLeg { get; set; }
    public Rotation RightLeft { get; set; }
}

[Flags]
public enum StandProperties
{
    IsSmall = 0x01,
    HasArms = 0x04,
    NoBasePlate = 0x08,
    SetMarker = 0x10
}
