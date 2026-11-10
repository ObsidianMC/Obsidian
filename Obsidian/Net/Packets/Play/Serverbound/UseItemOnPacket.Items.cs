using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;
using System.Collections.Frozen;

namespace Obsidian.Net.Packets.Play.Serverbound;

// The held item's use on a block, after the block's own use passed: vanilla's ItemStack.useOn.
public partial class UseItemOnPacket
{
    private delegate ValueTask<UseResult> ItemUseOn(UseItemOnPacket packet, BlockUse use);

    // The vanilla item classes whose useOn Obsidian implements, by the class name item_use_on_classes.json records.
    private static readonly Dictionary<string, ItemUseOn> itemUseOnOwners = new()
    {
        ["EndCrystalItem"] = (packet, use) => packet.PlaceEndCrystalAsync(use),
        ["MinecartItem"] = (packet, use) => packet.PlaceMinecartAsync(use),
        ["SpawnEggItem"] = (packet, use) => packet.UseSpawnEggAsync(use),
        ["EnderEyeItem"] = (packet, use) => packet.UseIgniterAsync(use),
        ["FlintAndSteelItem"] = (packet, use) => packet.UseIgniterAsync(use),
        ["FireChargeItem"] = (packet, use) => packet.UseIgniterAsync(use),
        ["BoneMealItem"] = (_, use) => UseBoneMealAsync(use),
        ["HangingEntityItem"] = (packet, use) => packet.HangAsync(use)
    };

    // Each item's own useOn, by item id, looked up once per click. Fish buckets aren't vanilla useOn items (vanilla
    // empties them from the use packet that follows), but Obsidian empties them here.
    private static readonly FrozenDictionary<int, ItemUseOn> itemUsesOn = BindItemUsesOn();

    private static FrozenDictionary<int, ItemUseOn> BindItemUsesOn()
    {
        var uses = new Dictionary<int, ItemUseOn>();
        foreach (var (item, owner) in ItemUseOnBehaviors.Owners)
        {
            var separator = owner.LastIndexOfAny(['.', '$']);
            var name = owner[(separator + 1)..];
            if (itemUseOnOwners.TryGetValue(name, out var use) && ItemsRegistry.TryGet(item, out var registered))
                uses[registered.Id] = use;
        }

        int[] buckets =
        [
            ItemIds.AxolotlBucket, ItemIds.TadpoleBucket, ItemIds.CodBucket, ItemIds.SalmonBucket,
            ItemIds.TropicalFishBucket, ItemIds.PufferfishBucket
        ];
        foreach (var bucket in buckets)
            uses[bucket] = (packet, use) => packet.EmptyFishBucketAsync(use);

        return uses.ToFrozenDictionary();
    }

    /// <summary>
    /// Vanilla's <c>ItemStack.useOn</c>: a plugin's item first, then, for players who may build, the item class's own
    /// use, the stack's block transformer, and placement.
    /// </summary>
    private async ValueTask<UseResult> UseItemAsync(BlockUse use)
    {
        var player = use.Player;
        var item = use.Item!;
        if (Plugins.ItemBehaviors.CanUseItems(player) && await this.UsePluginItemAsync(use))
        {
            use.AwardsItemUse = false;
            return UseResult.Success;
        }

        if (!use.MayBuild)
        {
            player.StartEating(use.Hand);
            use.ResyncHand = true;
            return UseResult.Pass;
        }

        if (!use.CanCommit)
            return UseResult.Fail;

        var result = UseResult.Pass;
        if (itemUsesOn.TryGetValue(item.Holder.Id, out var own))
            result = await own(this, use);
        else if (use.Level.Portals.HasIgniter(item))
            result = await this.UseIgniterAsync(use);
        else if (item.GetComponent<EntityDataComponent>(DataComponentType.EntityData) is not null)
            result = await this.UseSpawnEggAsync(use);

        if (result != UseResult.Pass)
            return result;

        // 26.3's Item.useOn: the stack's block_transformer component.
        var transformed = await ItemUseOnBehaviors.UseOnAsync(use.Level, player, item, use.HandSlot, use.Position,
            use.Face);
        if (transformed)
            return UseResult.Success;

        player.StartEating(use.Hand);
        return await this.PlaceAsync(use);
    }

    private ValueTask<bool> UsePluginItemAsync(BlockUse use) =>
        Plugins.ItemBehaviors.UseOnBlockAsync(new ItemUseOnBlockContext
        {
            Player = use.Player,
            Stack = use.Item!,
            Hand = use.Hand,
            Position = use.Position,
            Face = use.Face,
            Block = use.Block,
            ClickedAt = use.Cursor
        });

    // Vanilla's BoneMealItem.useOn: bone meal grows a block that takes it, and is used up unless the player is in
    // creative mode, with rewards and the response following the committed action on the level's tick.
    private static ValueTask<UseResult> UseBoneMealAsync(BlockUse use)
    {
        if (!Growth.IsValidBonemealTarget(use.Level, use.Position, use.Block))
            return ValueTask.FromResult(UseResult.Pass);

        use.CompletionDeferred = true;
        use.Level.EnqueueEntityAction(CompleteQueuedUseAsync);
        async ValueTask CompleteQueuedUseAsync()
        {
            if (!use.CanCommit)
            {
                await CompleteAsync(use, UseResult.Fail);
                return;
            }

            var ran = false;
            var handled = await use.Player.TryRunBlockUseAsync(async () =>
            {
                ran = true;
                BlockUse execution;
                bool committed;
                lock (use.Level.Fluids.TickLock)
                {
                    execution = use.ForQueuedAction();
                    committed = execution.TryCommit(() =>
                    {
                        if (!Growth.IsValidBonemealTarget(execution.Level, execution.Position, execution.Block))
                            return false;

                        Growth.ApplyBonemeal(execution.Level, execution.Position, execution.Block,
                            execution.Level.GrowthRandom);
                        return true;
                    }, consumeItem: true);
                }

                if (committed)
                {
                    execution.Level.BroadcastLevelEvent(Growth.BoneMealEvent, execution.Position, Growth.BoneMealParticles);
                    execution.Level.EmitGameEvent(MobGameEvent.ItemInteractFinish, execution.Player.Position,
                        execution.Player);
                    execution.Player.Statistics.Increment(execution.Item!.Holder.UnlocalizedName, type: "minecraft:used");
                    await FireAsync(execution, "item_used_on_block");
                }

                await CompleteAsync(execution, committed ? UseResult.SuccessServer : UseResult.Fail);
            });
            if (!handled)
                use.Level.EnqueueEntityAction(CompleteQueuedUseAsync);
            else if (!ran)
                await CompleteAsync(use, UseResult.Fail);
        }
        return ValueTask.FromResult(UseResult.SuccessServer);
    }

    // Vanilla's HangingEntityItem.useOn: item frames and paintings hang in front of the face used.
    private async ValueTask<UseResult> HangAsync(BlockUse use)
    {
        var level = use.Level;
        var hangAt = use.Position + use.Face.ToVector();
        HangingEntity? hanging = use.Item!.Holder.Id switch
        {
            ItemIds.Painting => await Entities.Painting.CreateAsync(level, hangAt, use.Face),
            ItemIds.GlowItemFrame => new GlowItemFrame { Level = level },
            _ => new ItemFrame { Level = level }
        };

        if (hanging is ItemFrame frame)
            frame.Hang(hangAt, use.Face);

        if (hanging is null || !await HangingEntity.PlaceAsync(hanging, level, use.Player, use.HandSlot, use))
        {
            use.ResyncHand = true;
            return UseResult.Fail;
        }

        level.EmitGameEvent(MobGameEvent.EntityPlace, hanging.Position, use.Player);
        return UseResult.Success;
    }

    // Vanilla's EnderEyeItem, FlintAndSteelItem and FireChargeItem useOn, and plugins' portal igniters: an eye goes
    // in an end portal frame; flint and steel or a fire charge lights a portal or sets fire on the clicked face.
    private async ValueTask<UseResult> UseIgniterAsync(BlockUse use)
    {
        var level = use.Level;
        var item = use.Item!;
        if ((uint)use.Face > (uint)BlockFace.East)
            return UseResult.Pass;

        if (item.Is(ItemsRegistry.EnderEye))
        {
            if (!await level.Portals.TryInsertEyeAsync(use.Position, use))
                return UseResult.Pass;

            await use.Player.SyncInventorySlotAsync(use.HandSlot);
            return UseResult.Success;
        }

        var firePosition = use.Position + use.Face.ToVector();
        var existing = await level.GetBlockAsync(firePosition);
        if (existing is not { IsAir: true } || level.IsOutsideBuildHeight(firePosition.Y))
            return UseResult.Pass;

        var used = await level.Portals.TryIgniteAsync(firePosition, igniter: item, use: use);
        var fireItem = item.Holder.Id is ItemIds.FlintAndSteel or ItemIds.FireCharge;

        // Vanilla's BaseFireBlock.canBePlacedAt: an empty space that fire would survive in.
        if (!used && fireItem)
        {
            used = use.TryCommit(() =>
            {
                if (!level.BlockUpdates.GetBlock(firePosition).IsSameState(existing)
                    || !FireBlockBehavior.CanBePlaced(level.BlockUpdates, firePosition))
                    return false;

                var fire = FireBlockBehavior.StateFor(level.BlockUpdates, firePosition);
                if (!level.BlockUpdates.SetBlock(firePosition, fire))
                    return false;

                ItemDamage.ApplyIgniterCost(use, item);
                return true;
            });
        }

        if (!used)
            return UseResult.Pass;

        if (fireItem)
        {
            PlayIgniterSound(level, item, firePosition);
            level.EmitGameEvent(MobGameEvent.BlockPlace, use.Center, use.Player);
        }

        await use.Player.SyncInventorySlotAsync(use.HandSlot);
        return UseResult.Success;
    }

    private static void PlayIgniterSound(AbstractLevel level, ItemStack item, Vector position)
    {
        var flint = item.Is(ItemsRegistry.FlintAndSteel);
        var pitch = flint
            ? 0.8f + Random.Shared.NextSingle() * 0.4f
            : 0.8f + (Random.Shared.NextSingle() - Random.Shared.NextSingle()) * 0.2f;

        level.PacketBroadcaster.BroadcastToLevelInRange(level, position, new SoundPacket
        {
            SoundLocation = flint ? "minecraft:item.flintandsteel.use" : "minecraft:item.firecharge.use",
            SoundPosition = new SoundPosition(position.X + 0.5, position.Y + 0.5, position.Z + 0.5),
            Category = SoundCategory.Blocks,
            Volume = 1,
            Pitch = pitch,
            Seed = Random.Shared.NextInt64()
        });
    }
}

/// <summary>What using an item costs it.</summary>
internal static class ItemDamage
{
    /// <summary>
    /// Vanilla's flint and steel and fire charge: flint and steel takes a point of damage (Unbreaking may spare it)
    /// and breaks when worn out; anything else is used up. Creative players keep theirs, but eyes of ender are used
    /// up in any mode.
    /// </summary>
    internal static void ApplyIgniterCost(BlockUse use, ItemStack item)
    {
        var player = use.Player;
        var slot = use.HandSlot;
        if (player.GameMode == GameMode.Creative && !item.Is(ItemsRegistry.EnderEye))
            return;

        if (!item.Is(ItemsRegistry.FlintAndSteel))
        {
            player.Inventory.RemoveItem(slot, 1);
        }
        else if (!item.Unbreakable && ShouldDamage(item))
        {
            var damage = ComponentBuilder.Damage with { Value = item.Damage + 1 };
            item[DataComponentType.Damage] = damage;

            var maxDamage = item.GetComponent<SimpleDataComponent<int>>(DataComponentType.MaxDamage)?.Value
                ?? item.Holder.MaxDamage;
            if (damage.Value >= maxDamage)
                player.Inventory.RemoveItem(slot, 1);
        }
    }

    /// <summary>Whether a use damages the tool, which Unbreaking makes less likely.</summary>
    internal static bool ShouldDamage(ItemStack item)
    {
        var enchantments = item.GetComponent<SimpleDataComponent<ImmutableArray<Enchantment>>>(
            DataComponentType.Enchantments)?.Value;
        var unbreakingId = EnchantmentsRegistry.Unbreaking.Id;
        var unbreaking = enchantments?.FirstOrDefault(enchantment => enchantment.Id == unbreakingId).Level ?? 0;

        return Globals.Random.NextDouble() < 1.0 / (Math.Max(0, unbreaking) + 1.0);
    }
}
