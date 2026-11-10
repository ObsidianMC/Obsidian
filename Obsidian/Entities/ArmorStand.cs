namespace Obsidian.Entities;

[MinecraftEntity("minecraft:armor_stand")]
public sealed partial class ArmorStand : Living
{
    // Vanilla's armor stand has 20 health, its max_health attribute.
    public ArmorStand()
    {
        Type = EntityType.ArmorStand;
        Health = 20;
    }

    public StandProperties StandProperties { get; set; }

    public Rotation Head { get; set; }
    public Rotation Body { get; set; }
    public Rotation LeftArm { get; set; }
    public Rotation RightArm { get; set; }
    public Rotation LeftLeg { get; set; }
    public Rotation RightLeft { get; set; }

    // Vanilla's ArmorStand.thunderHit does nothing: lightning neither burns nor hurts it.
    internal override ValueTask ThunderHitAsync(LightningBolt bolt) => ValueTask.CompletedTask;
}

[Flags]
public enum StandProperties
{
    IsSmall = 0x01,
    HasArms = 0x04,
    NoBasePlate = 0x08,
    SetMarker = 0x10
}
