using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Entities.Factories;
using Obsidian.Nbt;
using Obsidian.WorldData;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class UseItemOnPacket
{
    /// <summary>
    /// A bucket of fish, an axolotl or tadpoles emptied in front of the clicked face: water and the mob it held.
    /// Vanilla empties these from the use packet that follows a click; Obsidian empties them here.
    /// </summary>
    private async ValueTask<UseResult> EmptyFishBucketAsync(BlockUse use)
    {
        var level = use.Level;
        var bucket = use.Item!;
        var destination = this.Position + this.Face.ToVector();
        if (level.IsOutsideBuildHeight(destination.Y))
            return UseResult.Fail;

        var terrain = new MobTerrain(level);
        var existing = terrain.GetBlock(destination);
        if (existing is null || !existing.IsAir && !existing.Is(BlockIds.Water))
            return UseResult.Fail;

        var type = bucket.Holder.Id switch
        {
            ItemIds.AxolotlBucket => EntityType.Axolotl,
            ItemIds.TadpoleBucket => EntityType.Tadpole,
            ItemIds.CodBucket => EntityType.Cod,
            ItemIds.SalmonBucket => EntityType.Salmon,
            ItemIds.TropicalFishBucket => EntityType.TropicalFish,
            _ => EntityType.Pufferfish
        };
        var mob = EntitySpawner.CreateMob(level, type)!;
        mob.EntityId = Server.GetNextEntityId();
        mob.Position = (VectorD)destination + new VectorD(0.5f, 0, 0.5f);
        if (!terrain.IsFree(mob.Dimension.CreateBBFromPosition(mob.Position)))
            return UseResult.Fail;

        await level.SetBlockAsync(destination, BlocksRegistry.Water, true);
        mob.InitializeAi();
        mob.ReadSave(SavedBucketMob(mob, bucket));
        mob.PersistenceRequired = true;
        level.SpawnEntity(mob);
        level.EmitGameEvent(MobGameEvent.FluidPlace, (VectorD)destination + new VectorD(0.5, 0.5, 0.5), use.Player);

        if (use.Player.GameMode != GameMode.Creative)
        {
            use.Player.Inventory.SetItem(use.HandSlot, ItemsRegistry.GetSingleItem(ItemsRegistry.Bucket));
            await use.Player.SyncInventorySlotAsync(use.HandSlot);
        }

        // Vanilla doesn't count these as uses on the block.
        use.AwardsItemUse = false;
        return UseResult.Success;
    }

    // The mob's save with what the bucket kept of it, marked as coming from a bucket.
    private static NbtCompound SavedBucketMob(Mob mob, ItemStack bucket)
    {
        var saved = EntityNbt.Save(mob)!;
        var component = bucket.GetComponent<BucketEntityDataComponent>(DataComponentType.BucketEntityData);
        if (component is not null)
        {
            foreach (var (name, value) in component.Value)
            {
                if (name is "id" or "UUID" or "Pos" or "Motion" or "Rotation")
                    continue;

                saved.Remove(name);
                saved.Add(name, value);
            }
        }

        saved.Remove("FromBucket");
        saved.Add(new NbtTag<bool>("FromBucket", true));
        return saved;
    }
}
