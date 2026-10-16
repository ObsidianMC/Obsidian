using System.Collections.Immutable;
using Obsidian.API.Blocks;
using Obsidian.API.Events;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.WorldData;
using Obsidian.WorldData.Redstone;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class UseItemOnPacket
{
    // Blocks whose screen opens through the interact event when used without sneaking. Vanilla has no tag for these.
    private static readonly HashSet<int> interactableBlocks =
    [
        BlockIds.Chest, BlockIds.EnderChest, BlockIds.TrappedChest, BlockIds.Anvil, BlockIds.ChippedAnvil,
        BlockIds.DamagedAnvil, BlockIds.Hopper, BlockIds.Smoker, BlockIds.Furnace, BlockIds.CraftingTable,
        BlockIds.Barrel, BlockIds.BlastFurnace, BlockIds.Grindstone, BlockIds.BrewingStand
    ];

    [Field(0), ActualType(typeof(int)), VarLength]
    public InteractionHand Hand { get; private set; } // Hand it was placed from. 0 = Main, 1 = Off

    [Field(1)]
    public Vector Position { get; private set; }

    [Field(2), ActualType(typeof(int)), VarLength]
    public BlockFace Face { get; private set; }

    [Field(3), DataFormat(typeof(float))]
    public VectorD Cursor { get; private set; }

    [Field(6)]
    public bool InsideBlock { get; private set; }

    [Field(7)]
    public bool IsWorldBorderHit { get; private set; }

    [Field(8), VarLength]
    public int Sequence { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.Hand = reader.ReadVarInt<InteractionHand>();
        this.Position = reader.ReadPosition();
        this.Face = reader.ReadVarInt<BlockFace>();
        this.Cursor = reader.ReadAbsoluteFloatPositionF();
        this.InsideBlock = reader.ReadBoolean();
        this.IsWorldBorderHit = reader.ReadBoolean();
        this.Sequence = reader.ReadVarInt();
    }

    public async override ValueTask HandleAsync(IServer server, IClientPlayer player)
    {
        var handSlot = Hand == InteractionHand.OffHand ? 45 : player.CurrentHeldItemSlot;
        var bucket = player.Inventory.GetItem(handSlot);
        if (await TryPlaceEndCrystalAsync(player, handSlot, bucket)) return;
        if (bucket is
            {
                Count: > 0,
                Holder.Id: ItemIds.AxolotlBucket or ItemIds.TadpoleBucket or ItemIds.CodBucket
                    or ItemIds.SalmonBucket or ItemIds.TropicalFishBucket or ItemIds.PufferfishBucket
            }
            && player.Health > 0
            && player.GameMode != GameMode.Spectator)
        {
            var destination = Position + Face.ToVector();
            if ((player.Position - (VectorD)destination).MagnitudeSquared() > 36 || player.Level is not Obsidian.WorldData.AbstractLevel bucketLevel || bucketLevel.IsOutsideBuildHeight(destination.Y)) return;
            var terrain = new Obsidian.Entities.AI.MobTerrain(bucketLevel);
            var existing = terrain.GetBlock(destination);
            if (existing == null || !existing.IsAir && !existing.Is(BlockIds.Water)) return;
            var type = bucket.Holder.Id switch
            {
                ItemIds.AxolotlBucket => EntityType.Axolotl,
                ItemIds.TadpoleBucket => EntityType.Tadpole,
                ItemIds.CodBucket => EntityType.Cod,
                ItemIds.SalmonBucket => EntityType.Salmon,
                ItemIds.TropicalFishBucket => EntityType.TropicalFish,
                _ => EntityType.Pufferfish
            };
            var mob = Obsidian.Entities.Factories.EntitySpawner.CreateMob(bucketLevel, type)!;
            mob.EntityId = Server.GetNextEntityId();
            mob.Position = (VectorD)destination + new VectorD(0.5f, 0, 0.5f);
            if (!terrain.IsFree(mob.Dimension.CreateBBFromPosition(mob.Position))) return;
            await bucketLevel.SetBlockAsync(destination, BlocksRegistry.Water, true);
            mob.InitializeAi();
            var saved = Obsidian.Entities.EntityNbt.Save(mob)!;
            if (bucket.GetComponent<Obsidian.API.Inventory.DataComponents.BucketEntityDataComponent>(DataComponentType.BucketEntityData) is { } component)
                foreach (var (name, value) in component.Value)
                    if (name is not ("id" or "UUID" or "Pos" or "Motion" or "Rotation")) { saved.Remove(name); saved.Add(name, value); }
            saved.Remove("FromBucket");
            saved.Add(new Obsidian.Nbt.NbtTag<bool>("FromBucket", true));
            mob.ReadSave(saved);
            mob.PersistenceRequired = true;
            bucketLevel.SpawnEntity(mob);
            if (player.GameMode != GameMode.Creative)
            {
                player.Inventory.SetItem(handSlot, ItemsRegistry.GetSingleItem(ItemsRegistry.Bucket));
                await player.Client.QueuePacketAsync(new ContainerSetSlotPacket { ContainerId = 0, Slot = (short)handSlot, SlotData = player.Inventory.GetItem(handSlot) });
            }
            player.Client.SendPacket(new BlockChangedAckPacket { SequenceID = Sequence });
            return;
        }
        var currentItem = bucket;
        var progressItem = bucket is not null ? new ItemStack(bucket, bucket.Count) : null;
        var position = this.Position;

        var b = await player.Level.GetBlockAsync(position);

        if (b is null)
            return;

        if (await TryUseMusicBlockAsync(player, handSlot, bucket, b)) return;

        if (player.Level is AbstractLevel cookingLevel
            && await cookingLevel.UseCampfireAsync(player, position, handSlot))
        {
            player.Client.SendPacket(new BlockChangedAckPacket { SequenceID = this.Sequence });
            return;
        }

        // Like vanilla's, a plugin's block reacts to being used before the item does, unless the player sneaks.
        if (!player.Sneaking && Plugins.ItemBehaviors.CanUseItems(player))
        {
            var usedBlock = await Plugins.BlockBehaviors.UseAsync(new BlockUseContext
            {
                Player = player,
                Position = position,
                Block = b,
                Face = this.Face,
                Hand = this.Hand,
                ClickedAt = this.Cursor
            });

            if (usedBlock)
            {
                player.Client.SendPacket(new BlockChangedAckPacket { SequenceID = this.Sequence });
                return;
            }
        }

        // Vanilla's redstone components react to being used with the main hand: levers, buttons, repeaters,
        // comparators and dust. Adventure mode can't change repeaters, comparators or dust.
        if (!player.Sneaking
            && this.Hand == InteractionHand.MainHand
            && player.GameMode != GameMode.Spectator
            && player.Level is AbstractLevel redstoneLevel
            && redstoneLevel.BlockUpdates.UseRedstone(position, player, player.GameMode != GameMode.Adventure))
        {
            player.Client.SendPacket(new BlockChangedAckPacket { SequenceID = this.Sequence });
            return;
        }

        // Beds and respawn anchors, which explode where their environment attributes say they don't work.
        if (!player.Sneaking
            && player.GameMode != GameMode.Spectator
            && player.Level is AbstractLevel useLevel
            && (await useLevel.UseBedAsync(player, position, b)
                || await useLevel.UseRespawnAnchorAsync(player, position, b, handSlot)))
        {
            player.Client.SendPacket(new BlockChangedAckPacket { SequenceID = this.Sequence });
            return;
        }

        if (interactableBlocks.Contains(b.RegistryId) && !player.Sneaking)
        {
            await server.EventDispatcher.ExecuteEventAsync(new PlayerInteractEventArgs(player, server)
            {
                Item = currentItem,
                Block = b,
                BlockLocation = this.Position,
            });

            return;
        }

        // Like vanilla, the block reacts first (above); then the item does, here a plugin's item.
        if (currentItem is { Count: > 0 } && Plugins.ItemBehaviors.CanUseItems(player))
        {
            var used = await Plugins.ItemBehaviors.UseOnBlockAsync(new ItemUseOnBlockContext
            {
                Player = player,
                Stack = currentItem,
                Hand = this.Hand,
                Position = position,
                Face = this.Face,
                Block = b,
                ClickedAt = this.Cursor
            });

            if (used)
            {
                if (player is Entities.Player progressing)
                    await progressing.FireProgressAsync("item_used_on_block",
                        item: progressItem, block: b, position: this.Position);

                player.Client.SendPacket(new BlockChangedAckPacket { SequenceID = this.Sequence });
                return;
            }
        }

        if (await this.TryUseSpawnEggAsync(player, handSlot, currentItem, b))
            return;

        if (player.Level is AbstractLevel level && currentItem is { Count: > 0 } &&
            (currentItem.Holder.Id is ItemIds.EnderEye or ItemIds.FlintAndSteel or ItemIds.FireCharge
                || level.Portals.HasIgniter(currentItem)))
        {
            var used = false;
            if (player.GameMode != GameMode.Spectator && (uint)this.Face <= (uint)BlockFace.East)
            {
                if (currentItem.Is(ItemsRegistry.EnderEye))
                    used = await level.Portals.TryInsertEyeAsync(position);
                else if (player.GameMode != GameMode.Adventure)
                {
                    var firePosition = position + this.Face.ToVector();
                    var existing = await level.GetBlockAsync(firePosition);
                    if (existing is { IsAir: true } && !level.IsOutsideBuildHeight(firePosition.Y))
                    {
                        used = await level.Portals.TryIgniteAsync(firePosition, igniter: currentItem);
                        if (!used
                            && currentItem.Holder.Id is ItemIds.FlintAndSteel or ItemIds.FireCharge
                            && await level.GetBlockAsync(firePosition + Vector.Down) is IBlock below
                            && below.HasFullTopCollisionFace())
                        {
                            var fire = below.RegistryId is BlockIds.SoulSand or BlockIds.SoulSoil
                                ? BlocksRegistry.SoulFire
                                : BlocksRegistry.Fire;

                            await level.SetBlockAsync(firePosition, fire, true);
                            used = true;
                        }
                    }
                }
            }
            if (used)
            {
                if (currentItem.Holder.Id is ItemIds.FlintAndSteel or ItemIds.FireCharge)
                {
                    var soundPosition = position + this.Face.ToVector();
                    level.PacketBroadcaster.BroadcastToLevelInRange(level, soundPosition, new SoundPacket
                    {
                        SoundLocation = currentItem.Is(ItemsRegistry.FlintAndSteel)
                            ? "minecraft:item.flintandsteel.use" : "minecraft:item.firecharge.use",
                        SoundPosition = new SoundPosition(soundPosition.X + 0.5, soundPosition.Y + 0.5, soundPosition.Z + 0.5),
                        Category = SoundCategory.Blocks,
                        Volume = 1,
                        Pitch = currentItem.Is(ItemsRegistry.FlintAndSteel)
                            ? 0.8f + Random.Shared.NextSingle() * 0.4f
                            : 0.8f + (Random.Shared.NextSingle() - Random.Shared.NextSingle()) * 0.2f,
                        Seed = Random.Shared.NextInt64()
                    });
                }
                if (player is Entities.Player progressing)
                {
                    progressing.Statistics.Increment(currentItem.Holder.UnlocalizedName, type: "minecraft:used");
                    await progressing.FireProgressAsync("item_used_on_block",
                        item: progressItem, block: b, position: this.Position);
                }

                await this.ConsumePortalItemAsync(player, currentItem);
            }
            player.Client.SendPacket(new BlockChangedAckPacket { SequenceID = this.Sequence });
            return;
        }

        // Like vanilla's BoneMealItem.useOn: bone meal grows a block that takes it, and is used up unless the player
        // is in creative mode, on the level's tick (see UseBoneMeal). Adventure mode can't use items on blocks.
        if (player.Level is AbstractLevel growLevel
            && currentItem is { Count: > 0, Holder.Id: ItemIds.BoneMeal }
            && player.GameMode is GameMode.Survival or GameMode.Creative
            && growLevel.UseBoneMeal(position, player, handSlot))
        {
            player.Client.SendPacket(new BlockChangedAckPacket { SequenceID = this.Sequence });
            return;
        }

        if (player is Obsidian.Entities.Player eatingPlayer)
            eatingPlayer.StartEating(Hand);

        if (currentItem is null || player.Level is not AbstractLevel placeLevel)
            return;

        IBlock block;
        if (currentItem.Holder.Block is BlockType pluginBlock)
        {
            // A plugin's block item places its block, for players that may use items.
            if (currentItem.Count <= 0 || !Plugins.ItemBehaviors.CanUseItems(player))
                return;

            block = pluginBlock.DefaultState;
        }
        else if (currentItem.Is(ItemsRegistry.WaterBucket))
        {
            block = BlocksRegistry.Water;
        }
        else if (currentItem.Is(ItemsRegistry.LavaBucket))
        {
            block = BlocksRegistry.Lava;
        }
        else if (!BlocksRegistry.TryGetBlock(currentItem.Holder, out var placed)
            && !VanillaRedstone.TryGetPlacedBlock(currentItem.Holder, out placed))
        {
            // The item isn't a block, or it's a plugin's item without one, which does what its behaviour does.
            return;
        }
        else
        {
            block = placed;
        }

        // Like vanilla's BlockPlaceContext: a block goes into a clicked block it can replace (tall grass, say, but not
        // a block of its own kind), and otherwise next to it on the clicked face, where there must be room. Buckets
        // pour next to the clicked block.
        var pouring = block.Is(BlockIds.Water) || block.Is(BlockIds.Lava);
        var replacesClicked = !pouring && b.CanBeReplaced() && b.RegistryId != block.RegistryId;
        if (!replacesClicked)
            position += this.Face.ToVector();

        var target = await placeLevel.GetBlockAsync(position);
        if (placeLevel.IsOutsideBuildHeight(position.Y) || !pouring && target?.CanBeReplaced() != true)
        {
            await this.RejectPlacementAsync(player, handSlot);
            return;
        }

        if (block.Is(BlockIds.EndPortalFrame))
        {
            string[] facings = ["north", "east", "south", "west"];
            block = block.WithProperty("facing", facings[(int)Math.Floor((player.Yaw.Degrees + 45) / 90) % 4]);
        }

        // Vanilla's redstone components face and attach as the player clicks and looks, where they can stay.
        if (placeLevel.BlockUpdates.GetRedstonePlacement(block, position, player, this.Face, replacesClicked)
            is not IBlock redstoneState)
        {
            await this.RejectPlacementAsync(player, handSlot);
            return;
        }

        block = redstoneState;

        if (currentItem.Holder.Block is BlockType)
        {
            var context = new BlockPlaceContext
            {
                Level = placeLevel.BlockUpdates,
                Player = player,
                Hand = this.Hand,
                Item = currentItem,
                Block = block,
                Position = position,
                ClickedPosition = this.Position,
                ClickedFace = this.Face,
                ClickedAt = this.Cursor,
                ReplacesClicked = replacesClicked,
                HorizontalDirection = PlacementDirections.Horizontal(player.Yaw.Degrees),
                NearestLookDirection = PlacementDirections.Nearest(player.Yaw.Degrees, player.Pitch.Degrees)
            };

            // Like the block's other hooks, while the level's block updates wait, so what it reads stays as it read.
            IBlock? state;
            lock (placeLevel.Fluids.TickLock)
                state = Plugins.BlockBehaviors.GetStateForPlacement(context, placeLevel.Logger);

            if (state is null)
            {
                await this.RejectPlacementAsync(player, handSlot);
                return;
            }

            block = state;
        }

        // Like vanilla's buckets, water poured where it evaporates (the nether) hisses instead of being placed.
        if (block.Is(BlockIds.Water) && placeLevel.Evaporates(player, position))
        {
            player.Client.SendPacket(new BlockChangedAckPacket { SequenceID = this.Sequence });
            return;
        }

        var placing = new BlockPlaceEventArgs(server, player, block, position, placeLevel, this.Hand)
        {
            Sequence = this.Sequence
        };

        await server.EventDispatcher.ExecuteEventAsync(placing);
        if (placing.IsCancelled)
        {
            await this.RejectPlacementAsync(player, handSlot);
            return;
        }

        // What the placed item holds goes to its block entity, like a shulker box's items.
        var placement = new BlockPlacement(player, handSlot, currentItem);

        // The event's handlers may have filled the target or changed the player's hand while it ran: both are checked
        // again in the same step as the block is placed, and the item is only used once it is.
        var stored = await placeLevel.TrySetBlockAsync(
            position,
            block,
            BlockUpdateFlags.All,
            current => (pouring || current.CanBeReplaced())
                && ReferenceEquals(player.Inventory.GetItem(handSlot), currentItem)
                && currentItem.Count > 0,
            placement);

        if (!stored)
        {
            await this.RejectPlacementAsync(player, handSlot);
            return;
        }

        if (player is Entities.Player progressPlayer)
        {
            progressPlayer.Statistics.Increment(currentItem.Holder.UnlocalizedName, type: "minecraft:used");
            await progressPlayer.FireProgressAsync("placed_block",
                item: progressItem, block: block, position: position);
            await progressPlayer.FireProgressAsync("item_used_on_block",
                item: progressItem, block: b, position: this.Position);
        }

        placeLevel.BlockUpdates.OnPlacedByPlayer(position, block);

        if (!pouring)
            BlockSoundPlayback.Play(placeLevel, block, position, breaking: false, player.EntityId);

        var center = (VectorD)position + new VectorD(0.5, 0.5, 0.5);
        placeLevel.EmitGameEvent(MobGameEvent.BlockPlace, center, player, affectedBlock: block);
        player.Client.SendPacket(new BlockChangedAckPacket
        {
            SequenceID = Sequence
        });
    }

    // A placement that didn't happen: the player's client, which predicted it, gets the block and the item back.
    private async ValueTask RejectPlacementAsync(IClientPlayer player, int handSlot)
    {
        player.Client.SendPacket(new BlockChangedAckPacket { SequenceID = this.Sequence });

        var target = this.Position + this.Face.ToVector();
        foreach (var position in new[] { this.Position, target })
        {
            if (await player.Level.GetBlockAsync(position) is IBlock block)
                player.Client.SendPacket(new BlockUpdatePacket(position, block.GetHashCode()));
        }

        await player.SendInventorySlotAsync(handSlot);
    }

    private async ValueTask ConsumePortalItemAsync(IClientPlayer player, ItemStack item)
    {
        var slot = this.Hand == InteractionHand.MainHand ? player.CurrentHeldItemSlot : (short)45;
        if (item.Is(ItemsRegistry.EnderEye) || player.GameMode != GameMode.Creative)
        {
            if (!item.Is(ItemsRegistry.FlintAndSteel))
                player.Inventory.RemoveItem(slot, 1);
            else if (!item.Unbreakable && ShouldDamageTool(item))
            {
                var damage = ComponentBuilder.Damage with { Value = item.Damage + 1 };
                item[DataComponentType.Damage] = damage;
                var maxDamage = item.GetComponent<SimpleDataComponent<int>>(DataComponentType.MaxDamage)?.Value ?? item.Holder.MaxDamage;
                if (damage.Value >= maxDamage)
                    player.Inventory.RemoveItem(slot, 1);
            }
        }
        await player.Client.QueuePacketAsync(new ContainerSetSlotPacket
        {
            Slot = slot,
            StateId = player.Inventory.StateId++,
            SlotData = player.Inventory.GetItem(slot)
        });
    }

    private static bool ShouldDamageTool(ItemStack item)
    {
        var enchantments = item.GetComponent<SimpleDataComponent<ImmutableArray<Enchantment>>>(
            DataComponentType.Enchantments)?.Value;
        var unbreaking = enchantments?.FirstOrDefault(enchantment => enchantment.Id == EnchantmentsRegistry.Unbreaking.Id).Level ?? 0;
        return Globals.Random.NextDouble() < 1.0 / (Math.Max(0, unbreaking) + 1.0);
    }
}
