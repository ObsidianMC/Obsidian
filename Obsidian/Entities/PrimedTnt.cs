using Obsidian.Entities.AI;
using Obsidian.WorldData;

namespace Obsidian.Entities;

/// <summary>
/// Vanilla's PrimedTnt: a lit block of TNT that falls, then explodes when its fuse runs out.
/// </summary>
[MinecraftEntity("minecraft:tnt")]
public sealed partial class PrimedTnt : Entity
{
    // Vanilla's PrimedTnt: the fuse a primed block starts with, the explosion's power, and the body's height.
    public const int DefaultFuse = 80;
    public const float ExplosionPower = 4f;
    private const float Height = 0.98f;

    // Vanilla's gravity and drag, then the ground friction that scales the motion while it rests.
    private const float Gravity = 0.04f;
    private const float AirDrag = 0.98f;

    public int Fuse { get; set; } = DefaultFuse;

    public PrimedTnt() => Type = EntityType.Tnt;

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(8, EntityMetadataType.VarInt);
        writer.WriteVarInt(Fuse);
        writer.WriteEntityMetadataType(9, EntityMetadataType.BlockState);
        writer.WriteVarInt(BlocksRegistry.GetDefault(BlockIds.Tnt).DefaultId);
    }

    public async override ValueTask TickAsync()
    {
        await base.TickAsync();

        var terrain = new MobTerrain(Level);
        var next = EntityMovement.Move(this, terrain, VectorD.Zero, Gravity, AirDrag);
        if (next != Position)
            await UpdateAsync(next, MovementFlags);

        if (MovementFlags.HasFlag(MovementFlags.OnGround))
            Motion = new VectorD(Motion.X * 0.7, Motion.Y * -0.5, Motion.Z * 0.7);

        if (--Fuse > 0)
            return;

        await RemoveAsync();
        if (Level is AbstractLevel level)
            await level.ExplodeAsync(this, ExplosionPower, interaction: AbstractLevel.ExplosionInteraction.Tnt);
    }
}
