using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Entities.Factories;
using Obsidian.WorldData.Fluids;

namespace Obsidian.WorldData.Dispensing;

/// <summary>Bucket loot and mob data prepared without holding the dispenser or world commit locks.</summary>
internal sealed class BucketPreparation(IBlock block, IBlockEntity? entity) : IAsyncDisposable
{
    private BlockEntityComponents captured = BlockEntityComponents.None;

    internal IBlock Block { get; } = block;
    internal bool Destroys { get; private set; }
    internal bool Unsupported { get; private set; }
    internal IReadOnlyList<ItemStack> Drops { get; private set; } = [];
    internal Mob? Mob { get; private set; }

    internal bool IsCurrent(DispenseSource source) => this.captured.CanBreak
        && source.Level.BlockUpdates.GetBlock(source.Target).IsSameState(this.Block)
        && ReferenceEquals(source.Level.GetResidentBlockEntity(source.Target), entity);

    internal static async ValueTask<BucketPreparation?> PrepareAsync(DispenseSource source, ItemStack stack)
    {
        var contents = DispenseBehaviors.BucketContents.Of(stack);
        if (contents is null && stack.Holder.Id != ItemIds.Bucket)
            return null;

        var level = source.Level;
        BucketPreparation prepared;
        lock (level.Fluids.TickLock)
        {
            var block = level.BlockUpdates.GetBlock(source.Target);
            prepared = new BucketPreparation(block, level.GetResidentBlockEntity(source.Target));
            prepared.Destroys = contents is { PowderSnow: false }
                && BucketItems.Accepts(block, contents.Fluid, sneaking: false)
                && !block.IsAir && !block.IsLiquidBlock() && !block.IsLiquidBlockContainer()
                && !(contents.Fluid == FluidKind.Water
                    && level.Gameplay(LevelEnvironment.WaterEvaporates, source.Target));
            prepared.Unsupported = stack.Holder.Id == ItemIds.Bucket
                && block.GetProperty("waterlogged") == "true"
                && !PlacementSurvival.CanSurvive(level.BlockUpdates,
                    block.WithProperty("waterlogged", false), source.Target);
        }

        try
        {
            if (prepared.Destroys || prepared.Unsupported)
            {
                prepared.captured = await BlockEntityComponents.CaptureAsync(level, source.Target, null);
                if (prepared.captured.CanBreak && level.LevelData.GetBooleanRule("block_drops", true))
                {
                    var lootBlock = prepared.Unsupported
                        ? prepared.Block.WithProperty("waterlogged", false) : prepared.Block;
                    prepared.Drops = await BlockLoot.GetDropsAsync(level, source.Target, lootBlock, null,
                        checkTool: false, blockEntityComponents: prepared.captured.Components,
                        dynamicDrops: prepared.captured.DynamicDrops);
                }
            }

            if (contents?.Entity is EntityType type)
                prepared.Mob = PrepareMob(level, stack, type);

            return prepared;
        }
        catch
        {
            await prepared.DisposeAsync();
            throw;
        }
    }

    // Read bucket components here, since their value/copy delegates must stay outside the commit locks.
    private static Mob? PrepareMob(AbstractLevel level, ItemStack stack, EntityType type)
    {
        var mob = EntitySpawner.CreateMob(level, type);
        if (mob is null)
            return null;

        if (stack.GetComponent<API.Inventory.DataComponents.BucketEntityDataComponent>(
            DataComponentType.BucketEntityData) is { } data)
        {
            mob.ReadNbt(data.Value);
        }

        switch ((object)mob)
        {
            case Fish fish:
                fish.FromBucket = true;
                break;
            case Axolotl axolotl:
                axolotl.FromBucket = true;
                break;
            case Tadpole tadpole:
                tadpole.FromBucket = true;
                break;
        }

        mob.PersistenceRequired = true;
        mob.CustomName = stack.CustomName ?? mob.CustomName;
        return mob;
    }

    public ValueTask DisposeAsync() => this.captured.DisposeAsync();
}
