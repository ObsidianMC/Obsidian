using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Plugins;
using Obsidian.WorldData.Fluids;
using Microsoft.Extensions.Logging;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    // Protected by the block tick lock. Repeated shape updates must not enqueue duplicate drops.
    private readonly HashSet<Vector> pendingDestruction = [];

    /// <inheritdoc/>
    public bool DestroyBlock(Vector position, bool dropItems, IEntity? breaker = null)
    {
        lock (this.Fluids.TickLock)
        {
            var block = this.BlockUpdates.GetBlock(position);
            if (block.IsAir || !this.pendingDestruction.Add(position))
                return false;

            var entity = this.GetResidentBlockEntity(position);
            this.EnqueueEntityAction(async () =>
            {
                try
                {
                    await this.DestroyCapturedBlockAsync(position, block, entity, dropItems, breaker);
                }
                catch (Exception exception)
                {
                    BlockDestructionFailed(this.Logger, exception, block.UnlocalizedName, position);
                }
                finally
                {
                    lock (this.Fluids.TickLock)
                        this.pendingDestruction.Remove(position);
                }
            });

            return true;
        }
    }

    /// <inheritdoc/>
    public bool SpawnItem(VectorD position, ItemStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        if (stack.IsAir || stack.Count <= 0)
            return false;

        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y) || !double.IsFinite(position.Z))
            throw new ArgumentOutOfRangeException(nameof(position));

        lock (this.Fluids.TickLock)
        {
            var power = Globals.Random.NextSingle() * 0.1f;
            var direction = Globals.Random.NextSingle() * 6.2f;
            var motion = new VectorD(-Math.Sin(direction) * power, 0.2f, Math.Cos(direction) * power);
            return this.SpawnItem(position, stack, motion);
        }
    }

    // Both direct drops and block resources use a detached stack and explicit host ownership.
    private bool SpawnItem(VectorD position, ItemStack stack, VectorD motion) => this.TrySpawnEntity(new ItemEntity
    {
        EntityId = Server.GetNextEntityId(),
        Level = this,
        Position = position,
        Item = new ItemStack(stack, stack.Count),
        Motion = motion
    }, hostOwned: true);

    private void PopBlockResource(Vector position, ItemStack stack)
    {
        lock (this.Fluids.TickLock)
        {
            // Vanilla Block.popResource: three location draws, then the item's two horizontal motion draws.
            // Use the level random, as the existing environmental redstone drops do, to keep seed ordering.
            var random = this.GrowthRandom;
            var x = position.X + 0.5 + random.NextDouble() * 0.5 - 0.25;
            var y = position.Y + 0.5 + random.NextDouble() * 0.5 - 0.25 - ItemEntityWidth / 2;
            var z = position.Z + 0.5 + random.NextDouble() * 0.5 - 0.25;
            var motion = new VectorD(random.NextDouble() * 0.2 - 0.1, 0.2, random.NextDouble() * 0.2 - 0.1);
            this.SpawnItem(new VectorD(x, y, z), stack, motion);
        }
    }

    // Only called by the serial tick queue: no inherited async context may bypass the simulation gate.
    private async ValueTask DestroyCapturedBlockAsync(
        Vector position, IBlock block, IBlockEntity? expectedEntity, bool dropItems, IEntity? breaker)
    {
        if (await this.GetBlockAsync(position) is not IBlock current
            || current.GetHashCode() != block.GetHashCode()
            || !ReferenceEquals(this.GetResidentBlockEntity(position), expectedEntity))
            return;

        // A queued source can unload meanwhile. Never read its plugin implementation after that generation closes.
        if (breaker is not null && !PluginEntities.HasLoadedType(breaker))
            breaker = null;

        var parts = await TwoPartBlockBreak.PrepareAsync(this, position, block);
        await using var captured = dropItems
            ? await BlockEntityComponents.CaptureAsync(this, parts.DropPosition, null, onLevelTick: true)
            : BlockEntityComponents.None;
        if (!captured.CanBreak)
            return;

        // Vanilla Level.destroyBlock evaluates an empty tool, bypasses harvesting checks, and keeps the fluid.
        var drops = dropItems && this.LevelData.GetBooleanRule("block_drops", true)
            ? await BlockLoot.GetDropsAsync(this, parts.DropPosition, parts.DropState, null,
                breaker: breaker, checkTool: false, blockEntityComponents: captured.Components,
                dynamicDrops: captured.DynamicDrops)
            : [];
        var replacement = FluidState.Of(block).CreateLegacyBlock();
        if (!await this.TrySetBlockAsync(position, replacement, BlockUpdateFlags.All,
            state => state.GetHashCode() == block.GetHashCode()
                && ReferenceEquals(this.GetResidentBlockEntity(position), expectedEntity)
                && (captured.Source is null
                    || ReferenceEquals(this.GetResidentBlockEntity(parts.DropPosition), captured.Source))))
            return;

        await parts.RemovePartnerAsync(this);
        BlockSoundPlayback.Play(this, block, position, breaking: true, actor: 0);
        this.EmitGameEvent(MobGameEvent.BlockDestroy, (VectorD)position + 0.5, breaker, affectedBlock: block);

        foreach (var stack in drops)
            this.PopBlockResource(parts.DropPosition, stack);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not destroy {Block} at {Position}")]
    private static partial void BlockDestructionFailed(
        ILogger logger, Exception exception, string block, Vector position);
}
