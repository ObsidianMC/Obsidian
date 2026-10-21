using Obsidian.Entities;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    /// <summary>
    /// Vanilla's TntBlock.prime: with <c>tnt_explodes</c>, a <see cref="PrimedTnt"/> falls from the block's centre
    /// with a small random hop. The caller decides what happens to the block itself. Returns false when the rule is off.
    /// A blast passes its own fuse, like vanilla's <c>TntBlock.wasExploded</c>.
    /// </summary>
    internal bool PrimeTnt(Vector position, int? fuse = null)
    {
        if (!LevelData.GetBooleanRule("tnt_explodes"))
            return false;

        this.BlockUpdates.PlaySound(position, "minecraft:entity.tnt.primed");

        // Vanilla's random hop: a random direction at 0.02 blocks per tick sideways, and 0.2 up.
        var angle = this.GrowthRandom.NextFloat() * MathF.Tau;
        var tnt = new PrimedTnt
        {
            EntityId = Server.GetNextEntityId(),
            Level = this,
            Position = new VectorD(position.X + 0.5, position.Y, position.Z + 0.5),
            Motion = new VectorD(-MathF.Sin(angle) * 0.02f, 0.2, -MathF.Cos(angle) * 0.02f)
        };
        if (fuse is int ticks)
            tnt.Fuse = ticks;
        this.TrySpawnEntity(tnt, hostOwned: true);
        return true;
    }
}
