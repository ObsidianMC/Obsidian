using Obsidian.API.Inventory;
using Obsidian.WorldData;
using Obsidian.WorldData.Fluids;

namespace Obsidian.Entities;

public partial class PlayerBase
{
    /// <summary>
    /// How much of <paramref name="block"/> one tick of mining breaks, like vanilla's
    /// <c>BlockState.getDestroyProgress</c> for a player: 1 or more breaks it on the first tick, which the player's
    /// client then predicts without ever saying it finished, and 0 never breaks it (bedrock).
    /// </summary>
    /// <remarks>
    /// The arithmetic is vanilla's, in single precision and in its order, so that the server agrees with the client on
    /// which blocks break at once: the tool's speed, raised by Efficiency for a tool that's faster than a hand, by
    /// haste or conduit power and lowered by mining fatigue, scaled by the block break speed attribute, slowed with the
    /// eyes in water (unless Aqua Affinity) and off the ground, then divided by the block's hardness and 30 with the
    /// right tool for its drops, or 100 without.
    /// </remarks>
    /// <param name="eyeInWater">
    /// Whether the player's eyes are in water (<see cref="IsEyeInWater"/>), taken before the player's inventory is
    /// locked for the rest: reading the level takes its tick lock, which placement takes before the inventory's.
    /// </param>
    internal float DestroyProgress(IBlock block, bool eyeInWater)
    {
        var hardness = BlockPhysics.MiningHardness(block.GetHashCode());
        if (hardness == -1)
            return 0;

        var tool = this.GetHeldItem();
        var divisor = ItemTools.HasCorrectTool(block, tool) ? 30 : 100;

        return this.DestroySpeed(block, tool, eyeInWater) / hardness / divisor;
    }

    // Vanilla's Player.getDestroySpeed.
    private float DestroySpeed(IBlock block, ItemStack? tool, bool eyeInWater)
    {
        // Efficiency and Aqua Affinity reach these attributes as equipment modifiers (see UpdateEquipmentAttributes),
        // which the player's client is sent, as vanilla's client only knows them from the server.
        var speed = ItemTools.MiningSpeed(block, tool);
        if (speed > 1)
            speed += this.GetAttributeValue("minecraft:mining_efficiency");

        var haste = Math.Max(this.EffectAmplifier(PotionEffect.Haste), this.EffectAmplifier(PotionEffect.ConduitPower));
        if (haste >= 0)
            speed *= 1 + (haste + 1) * 0.2f;

        var fatigue = this.EffectAmplifier(PotionEffect.MiningFatigue);
        if (fatigue >= 0)
            speed *= (float)Math.Pow(0.3, fatigue + 1);

        speed *= this.GetAttributeValue("minecraft:block_break_speed");

        if (eyeInWater)
            speed *= this.GetAttributeValue("minecraft:submerged_mining_speed");

        if (!this.MovementFlags.HasFlag(MovementFlags.OnGround))
            speed /= 5;

        return speed;
    }

    /// <summary>
    /// Vanilla's <c>EntityFluidInteraction</c>: the eyes are in water when the water in their block reaches them, its
    /// surface at the camera's height for it, which fills the block for a source under a sturdy ceiling.
    /// </summary>
    internal bool IsEyeInWater()
    {
        if (this.Level is not AbstractLevel level)
            return false;

        var eye = EyePosition(this);
        var position = (Vector)eye.Floor();
        var above = position + Vector.Up;

        // The level's chunks are only read under its tick lock, as they're tracked and untracked under it.
        lock (level.Fluids.TickLock)
        {
            var fluid = FluidState.Of(level.Fluids.GetBlock(position));
            if (!fluid.IsWater)
                return false;

            // Water above fills the block too, as vanilla's getHeight has it.
            var ceiling = fluid.IsSource && level.IsFaceSturdy(above, BlockFace.Down);
            var full = ceiling || FluidState.Of(level.Fluids.GetBlock(above)).IsWater;
            var height = full ? 1 : fluid.OwnHeight;

            return eye.Y <= position.Y + height;
        }
    }
}
