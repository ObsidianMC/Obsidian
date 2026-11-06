using Obsidian.API.Inventory;
using Obsidian.Entities.AI;

namespace Obsidian.Entities;

public partial class PlayerBase
{
    // The inventory slot of the helmet, whose enchantments (Aqua Affinity) change mining under water.
    private const int HelmetSlot = 5;

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
    internal float DestroyProgress(IBlock block)
    {
        var hardness = BlockPhysics.MiningHardness(block.GetHashCode());
        if (hardness == -1)
            return 0;

        var tool = this.GetHeldItem();
        var divisor = ItemTools.HasCorrectTool(block, tool) ? 30 : 100;

        return this.DestroySpeed(block, tool) / hardness / divisor;
    }

    // Vanilla's Player.getDestroySpeed.
    private float DestroySpeed(IBlock block, ItemStack? tool)
    {
        var speed = ItemTools.MiningSpeed(block, tool);
        if (speed > 1)
        {
            var efficiency = EnchantmentAttributes.Modifiers(tool, "mainhand", "minecraft:mining_efficiency");
            speed += this.GetAttributeValue("minecraft:mining_efficiency", efficiency);
        }

        var haste = Math.Max(this.EffectAmplifier(PotionEffect.Haste), this.EffectAmplifier(PotionEffect.ConduitPower));
        if (haste >= 0)
            speed *= 1 + (haste + 1) * 0.2f;

        var fatigue = this.EffectAmplifier(PotionEffect.MiningFatigue);
        if (fatigue >= 0)
            speed *= (float)Math.Pow(0.3, fatigue + 1);

        speed *= this.GetAttributeValue("minecraft:block_break_speed");

        if (new MobTerrain(this.Level).IsInFluid(EyePosition(this), water: true))
        {
            var helmet = this.Inventory.GetItem(HelmetSlot);
            var aquaAffinity = EnchantmentAttributes.Modifiers(helmet, "head", "minecraft:submerged_mining_speed");
            speed *= this.GetAttributeValue("minecraft:submerged_mining_speed", aquaAffinity);
        }

        if (!this.MovementFlags.HasFlag(MovementFlags.OnGround))
            speed /= 5;

        return speed;
    }
}
