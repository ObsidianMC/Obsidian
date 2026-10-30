using Obsidian.API.Inventory;

namespace Obsidian.Entities;

public partial class Mob
{
    private async ValueTask TickBlockHazardsAsync(IBlock? feet)
    {
        if (this is not EnderDragon)
        {
            var body = Dimension.CreateBBFromPosition(Position);
            for (var x = (int)Math.Floor(body.Min.X + 0.001); x <= (int)Math.Floor(body.Max.X - 0.001); x++)
            {
                for (var y = (int)Math.Floor(body.Min.Y + 0.001); y <= (int)Math.Floor(body.Max.Y - 0.001); y++)
                {
                    for (var z = (int)Math.Floor(body.Min.Z + 0.001); z <= (int)Math.Floor(body.Max.Z - 0.001); z++)
                    {
                        if (Terrain.GetBlock(new Vector(x, y, z))?.RegistryId == BlockIds.Cactus)
                            await DamageCombatAsync(this, 1, CombatDamageKind.Melee);
                    }
                }
            }

            var width = Dimension.Width * 0.8 / 2;
            var eye = new BoundingBox(EyePosition - new VectorD(width, 0.0000005, width),
                EyePosition + new VectorD(width, 0.0000005, width));
            if (Terrain.ContainsSuffocatingBlock(eye))
                await DamageCombatAsync(this, 1, CombatDamageKind.Suffocation);
        }

        if (feet?.RegistryId == BlockIds.WitherRose && Level.LevelData.Difficulty != Difficulty.Peaceful)
            AddPotionEffect((int)PotionEffect.Wither - 1, 40);

        var frozen = PowderedSnowTicks;
        if (feet?.RegistryId == BlockIds.PowderSnow && !TagsRegistry.EntityType.FreezeImmuneEntityTypes.Entries.Contains(TypeId) &&
            !armorSlots.Append(EquipmentSlot.Body).Any(slot => TagsRegistry.Item.FreezeImmuneWearables.Entries.Contains(GetEquipment(slot).Holder.Id)))
        {
            PowderedSnowTicks = Math.Min(140, PowderedSnowTicks + 1);
        }
        else
        {
            PowderedSnowTicks = Math.Max(0, PowderedSnowTicks - 2);
        }

        if (frozen != PowderedSnowTicks) SynchronizeMetadata();
        if (PowderedSnowTicks >= 140 && AiTick % 40 == 0)
            await DamageCombatAsync(this, TagsRegistry.EntityType.FreezeHurtsExtraTypes.Entries.Contains(TypeId) ? 5 : 1, CombatDamageKind.Freezing);
    }
}
