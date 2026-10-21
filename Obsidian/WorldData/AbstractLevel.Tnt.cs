using Obsidian.Entities;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    /// <summary>
    /// Vanilla's TntBlock.explode for the TNT that fire has just replaced: with <c>tnt_explodes</c>, the block is
    /// removed and a <see cref="PrimedTnt"/> falls from its centre with a small random hop. Without the rule, nothing
    /// happens and the fire stays.
    /// </summary>
    internal void PrimeTnt(Vector position)
    {
        if (!LevelData.GetBooleanRule("tnt_explodes"))
            return;

        this.BlockUpdates.RemoveBlock(position);
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
        this.TrySpawnEntity(tnt, hostOwned: true);
    }
}
