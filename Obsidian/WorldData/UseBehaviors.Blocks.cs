using Obsidian.API.Containers;
using Obsidian.API.Inventory;
using Obsidian.API.Events;
using Obsidian.API.Crafting;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData.Redstone;

namespace Obsidian.WorldData;

// The use behaviours of blocks without a menu.
internal static partial class UseBehaviors
{
    /// <summary>
    /// Vanilla's <c>DoorBlock</c>, <c>TrapDoorBlock</c> and <c>FenceGateBlock</c> <c>useWithoutItem</c>: the block
    /// opens or closes, unless it's iron. Adventure players open them too.
    /// </summary>
    private sealed class OpenableUse : IBlockUseBehavior
    {
        public ValueTask<UseResult> UseWithoutItemAsync(BlockUse use)
        {
            var level = use.Level;
            string? sound;
            lock (level.Fluids.TickLock)
            {
                if (!use.CanCommit || !level.BlockUpdates.GetBlock(use.Position).IsSameState(use.Block))
                    return ValueTask.FromResult(UseResult.Fail);

                sound = VanillaPlacement.Use(level.BlockUpdates, use.Position, use.Block, use.Player);
            }

            if (sound is null)
                return ValueTask.FromResult(UseResult.Pass);

            // Vanilla plays it with the acting player as the source, so their own client plays it and nobody else's.
            level.PacketBroadcaster.BroadcastToLevelInRange(level, use.Position, new SoundPacket
            {
                SoundLocation = sound,
                SoundPosition = new SoundPosition(use.Center.X, use.Center.Y, use.Center.Z),
                Category = SoundCategory.Blocks,
                Volume = 1,
                Pitch = 0.9f + Random.Shared.NextSingle() * 0.1f,
                Seed = Random.Shared.NextInt64()
            }, use.Player.EntityId);

            var opened = sound.EndsWith(".open", StringComparison.Ordinal);
            var gameEvent = opened ? MobGameEvent.BlockOpen : MobGameEvent.BlockClose;
            level.EmitGameEvent(gameEvent, use.Center, use.Player);

            return ValueTask.FromResult(UseResult.Success);
        }
    }

    /// <summary>
    /// Vanilla's <c>useWithoutItem</c> of levers, buttons, repeaters, comparators and daylight detectors. A pressed
    /// button takes the click without reacting; adventure players can't change repeaters or comparators.
    /// </summary>
    private sealed class RedstoneComponentUse : IBlockUseBehavior
    {
        public async ValueTask<UseResult> UseWithoutItemAsync(BlockUse use)
        {
            var block = use.Block;
            if (block.BlockClass() == "ButtonBlock" && block.GetProperty("powered") == "true")
                return UseResult.Consume;

            if (!await use.Level.UseRedstoneAsync(use.Position, use.Player, use.MayBuild))
                return UseResult.Pass;

            if (block.BlockClass() == DaylightDetector.BlockClassName)
                use.Level.EmitGameEvent(MobGameEvent.BlockChange, use.Center, use.Player);

            return UseResult.Success;
        }
    }

    /// <summary>
    /// Vanilla's <c>NoteBlock</c>: an item from <c>noteblock_top_instruments</c> used on its top goes to its own use
    /// (a head is placed); otherwise using it tunes it one note up and plays it, and hitting it plays it.
    /// </summary>
    internal sealed class NoteBlockUse : IBlockUseBehavior, IBlockAttackBehavior
    {
        public ValueTask<UseResult> UseItemOnAsync(BlockUse use)
        {
            var topInstrument = use.HasItem
                && TagsRegistry.Item.NoteblockTopInstruments.Entries.Contains(use.Item!.Holder.Id);
            if (topInstrument && use.Face == BlockFace.Up)
                return ValueTask.FromResult(UseResult.Pass);

            return ValueTask.FromResult(UseResult.TryWithEmptyHand);
        }

        public async ValueTask<UseResult> UseWithoutItemAsync(BlockUse use)
        {
            var note = int.TryParse(use.Block.GetProperty("note"), out var number) ? number : 0;
            await use.Level.SetBlockAsync(use.Position, use.Block.WithProperty("note", (note + 1) % 25), true);
            use.Level.PlayNoteBlock(use.Position, use.Player);
            use.Award("minecraft:tune_noteblock");

            return UseResult.Success;
        }

        public ValueTask AttackAsync(AbstractLevel level, Vector position, IBlock block, PlayerBase player)
        {
            level.PlayNoteBlock(position, player);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Vanilla's <c>JukeboxBlock</c>: an empty jukebox takes the disc the player holds and plays it; one with a disc
    /// gives it back out.
    /// </summary>
    private sealed class JukeboxUse : IBlockUseBehavior
    {
        public async ValueTask<UseResult> UseItemOnAsync(BlockUse use)
        {
            var song = use.HasItem ? JukeboxSongsRegistry.GetSong(use.Item!) : -1;
            var full = use.Block.GetProperty("has_record") == "true";
            if (full || song < 0 || Jukebox(use) is not DataBlockEntity jukebox)
                return UseResult.TryWithEmptyHand;

            if (!use.TryCommit(() =>
            {
                if (jukebox.SnapshotData().HasTag("RecordItem"))
                    return false;

                jukebox.Set(new ItemStack(use.Item!, 1).ToNbt("RecordItem"));
                jukebox.Set("IsPlaying", true);
                jukebox.Set("ObsidianSongElapsed", 0);
                use.Level.BlockUpdates.SetBlock(use.Position, use.Block.WithProperty("has_record", true));
                return true;
            }, consumeItem: true))
                return UseResult.Fail;

            await use.Player.SyncInventorySlotAsync(use.HandSlot);
            use.Level.EmitGameEvent(MobGameEvent.BlockChange, use.Center);
            use.Level.StartJukebox(use.Position, song);

            use.Level.EmitGameEvent(MobGameEvent.BlockChange, use.Center, use.Player);
            use.Award("minecraft:play_record");

            return UseResult.Success;
        }

        public ValueTask<UseResult> UseWithoutItemAsync(BlockUse use)
        {
            if (use.Block.GetProperty("has_record") != "true")
                return ValueTask.FromResult(UseResult.Pass);

            ItemStack? stack = null;
            if (Jukebox(use) is not DataBlockEntity jukebox || !use.TryCommit(() =>
            {
                if (!jukebox.SnapshotData().TryGetTag<NbtCompound>("RecordItem", out var record)
                    || record.ItemFromNbt() is not { IsAir: false } stored)
                    return false;

                stack = stored;
                jukebox.Remove("RecordItem");
                use.Level.BlockUpdates.SetBlock(use.Position, use.Block.WithProperty("has_record", false));
                return true;
            }))
                return ValueTask.FromResult(UseResult.Fail);

            Eject(use, stack!);
            return ValueTask.FromResult(UseResult.Success);
        }

        private static void Eject(BlockUse use, ItemStack stack)
        {
            var level = use.Level;
            level.StopJukebox(use.Position);

            var motion = new VectorD(
                (Random.Shared.NextDouble() - 0.5) * 0.2,
                0.2,
                (Random.Shared.NextDouble() - 0.5) * 0.2);
            level.SpawnEntity(new ItemEntity
            {
                Level = level,
                EntityId = Obsidian.Server.GetNextEntityId(),
                Position = (VectorD)use.Position + new VectorD(0.5, 1, 0.5),
                Item = stack,
                Motion = motion,
                CanPickup = false
            });
        }

        private static DataBlockEntity? Jukebox(BlockUse use)
        {
            var position = use.Position;
            var chunk = use.Level.GetLoadedChunk(position.X >> 4, position.Z >> 4);

            return chunk?.GetBlockEntity(position.X, position.Y, position.Z) as DataBlockEntity;
        }
    }

    /// <summary>Vanilla's <c>CampfireBlock.useItemOn</c>: a food it cooks goes in its first free slot.</summary>
    private sealed class CampfireUse : IBlockUseBehavior
    {
        public async ValueTask<UseResult> UseItemOnAsync(BlockUse use)
        {
            if (use.BlockEntity is not CampfireBlockEntity campfire)
                return UseResult.TryWithEmptyHand;

            if (use.HasItem && TagsRegistry.Item.DousesCampfires.Entries.Contains(use.Item!.Holder.Id)
                && use.Block.GetProperty("lit") == "true")
            {
                if (!use.TryCommit(() =>
                {
                    if (!use.Level.BlockUpdates.SetBlock(use.Position, use.Block.WithProperty("lit", false)))
                        return false;

                    if (use.Player.GameMode != GameMode.Creative)
                        CombatItems.HurtItem(use.Item!, 1, false);
                    if (use.Item.Count == 0)
                        use.Player.Inventory.RemoveItem(use.HandSlot);

                    return true;
                }))
                    return UseResult.Fail;

                use.Level.BroadcastLevelEvent(1009, use.Position, 0);
                use.Level.EmitGameEvent(MobGameEvent.BlockChange, use.Center, use.Player);
                await use.Player.SyncInventorySlotAsync(use.HandSlot);
                return UseResult.Success;
            }

            if (!use.TryCommit(() =>
            {
                var recipe = use.Item is null ? null : use.Level.CookingRecipes.FindCooking(
                    CraftingType.CampfireCooking, use.Item);
                return recipe is not null && campfire.Place(use.Item!, recipe.CookingTime);
            }, consumeItem: true, destination: campfire.Inventory))
                return UseResult.TryWithEmptyHand;

            await use.Player.SyncInventorySlotAsync(use.HandSlot);
            campfire.SendUpdate();

            use.Award("minecraft:interact_with_campfire");
            use.Level.EmitGameEvent(MobGameEvent.BlockChange, use.Center, use.Player);

            return UseResult.SuccessServer;
        }
    }

    /// <summary>
    /// Vanilla's <c>ComposterBlock</c>: a compostable item goes in while it isn't full, and a ready one gives its
    /// bone meal out, whatever is held.
    /// </summary>
    private sealed class ComposterUse : IBlockUseBehavior
    {
        public async ValueTask<UseResult> UseItemOnAsync(BlockUse use)
        {
            var level = Composters.LevelOf(use.Block);
            var compostable = use.HasItem && Composters.LayersOf(use.Item!) is not null;
            if (level >= Composters.Full || !compostable)
                return UseResult.TryWithEmptyHand;

            var used = use.Item!.Holder.UnlocalizedName;
            if (!use.TryCommit(() =>
            {
                var layers = Composters.LayersOf(use.Item!);
                if (layers is null)
                    return false;

                use.Level.Fill(use.Position, layers.Value);
                return true;
            }, consumeItem: true))
                return UseResult.TryWithEmptyHand;

            await use.Player.SyncInventorySlotAsync(use.HandSlot);

            use.Player.Statistics.Increment(used, type: "minecraft:used");
            use.Level.EmitGameEvent(MobGameEvent.BlockChange, use.Center, use.Player);

            return UseResult.Success;
        }

        public async ValueTask<UseResult> UseWithoutItemAsync(BlockUse use)
        {
            if (Composters.LevelOf(use.Block) != Composters.Ready)
                return UseResult.Pass;

            if (!use.TryCommit(() =>
            {
                use.Level.TakeBoneMeal(use.Position);
                return true;
            }))
                return UseResult.Fail;

            use.Level.EmitGameEvent(MobGameEvent.BlockChange, use.Center, use.Player);

            return UseResult.Success;
        }
    }

    /// <summary>
    /// Vanilla's <c>BedBlock.useWithoutItem</c>: the player sleeps or sets their spawn, or the bed explodes where its
    /// environment attributes say beds don't work.
    /// </summary>
    private sealed class BedUse : IBlockUseBehavior
    {
        public async ValueTask<UseResult> UseWithoutItemAsync(BlockUse use)
        {
            var used = await use.Level.UseBedAsync(use.Player, use.Position, use.Block);

            return used ? UseResult.SuccessServer : UseResult.Pass;
        }
    }

    /// <summary>
    /// Vanilla's <c>RespawnAnchorBlock</c>: glowstone charges it, and a charged one sets the player's spawn where
    /// anchors work and explodes elsewhere.
    /// </summary>
    private sealed class RespawnAnchorUse : IBlockUseBehavior
    {
        public async ValueTask<UseResult> UseItemOnAsync(BlockUse use)
        {
            var charges = int.TryParse(use.Block.GetProperty("charges"), out var parsed) ? parsed : 0;
            var charging = use.Item is { Count: > 0, Holder.Id: ItemIds.Glowstone }
                && charges < AbstractLevel.MaxAnchorCharges;
            if (!charging)
                return UseResult.TryWithEmptyHand;

            if (!use.TryCommit(() => use.Level.BlockUpdates.SetBlock(use.Position,
                use.Block.WithProperty("charges", charges + 1)), consumeItem: true))
                return UseResult.Fail;

            await use.Player.SyncInventorySlotAsync(use.HandSlot);
            use.Level.BlockUpdates.PlaySound(use.Position, "minecraft:block.respawn_anchor.charge");
            use.Level.EmitGameEvent(MobGameEvent.BlockChange, use.Center, use.Player);

            return UseResult.Success;
        }

        public async ValueTask<UseResult> UseWithoutItemAsync(BlockUse use)
        {
            var used = await use.Level.UseRespawnAnchorAsync(use.Player, use.Position, use.Block, use.HandSlot);

            return used ? UseResult.SuccessServer : UseResult.Pass;
        }
    }

    /// <summary>
    /// Vanilla's <c>SignBlock</c>: dyes, ink sacs and honeycomb change the text, and anything else opens the side
    /// the player faces to be written on.
    /// </summary>
    private sealed class SignUse : IBlockUseBehavior
    {
        public async ValueTask<UseResult> UseItemOnAsync(BlockUse use)
        {
            Action? complete = null;
            if (!use.HasItem || !use.MayBuild || !use.TryCommit(() =>
            {
                var (used, after) = Signs.Use(use.Level, use.Position, use.Block, use.Player, use.Item);
                complete = after;
                return used;
            }, consumeItem: true))
                return UseResult.TryWithEmptyHand;

            complete?.Invoke();
            use.Player.Statistics.Increment(use.Item!.Holder.UnlocalizedName, type: "minecraft:used");
            await use.Player.SyncInventorySlotAsync(use.HandSlot);
            use.Level.EmitGameEvent(MobGameEvent.BlockChange, use.Center, use.Player);
            return UseResult.Success;
        }

        public ValueTask<UseResult> UseWithoutItemAsync(BlockUse use)
        {
            var used = use.TryCommit(() =>
                Signs.UseWithoutItem(use.Level, use.Position, use.Block, use.Player));
            return ValueTask.FromResult(used ? UseResult.SuccessServer : UseResult.Pass);
        }
    }

    /// <summary>Vanilla's <c>TntBlock.useItemOn</c>: flint and steel or a fire charge primes it.</summary>
    private sealed class TntUse : IBlockUseBehavior
    {
        public async ValueTask<UseResult> UseItemOnAsync(BlockUse use)
        {
            var igniter = use.Item is { Count: > 0, Holder.Id: ItemIds.FlintAndSteel or ItemIds.FireCharge };
            if (!igniter || !use.Level.LevelData.GetBooleanRule("tnt_explodes"))
                return UseResult.TryWithEmptyHand;

            if (!use.TryCommit(() =>
            {
                if (!use.Level.BlockUpdates.SetBlock(use.Position, BlocksRegistry.Air))
                    return false;

                ItemDamage.ApplyIgniterCost(use, use.Item!);
                return true;
            }))
                return UseResult.Fail;

            PrimedTnt.Prime(use.Level, use.Position, use.Player);
            use.Level.EmitGameEvent(MobGameEvent.PrimeFuse, use.Center, use.Player);

            var item = use.Item!;
            await use.Player.SyncInventorySlotAsync(use.HandSlot);
            use.Player.Statistics.Increment(item.Holder.UnlocalizedName, type: "minecraft:used");

            return UseResult.Success;
        }
    }

    /// <summary>
    /// Vanilla's <c>FenceBlock.useWithoutItem</c>: the mobs the player leads are tied to it, whatever the player
    /// holds. Without any, the held item's use runs.
    /// </summary>
    private sealed class FenceUse : IBlockUseBehavior
    {
        public ValueTask<UseResult> UseWithoutItemAsync(BlockUse use)
        {
            var tied = LeashKnot.TiePlayerMobs(use.Level, use.Player, use.Position);

            return ValueTask.FromResult(tied ? UseResult.SuccessServer : UseResult.Pass);
        }
    }

    /// <summary>
    /// Vanilla's <c>LecternBlock</c>: an empty lectern takes the written or writable book the player holds; one with
    /// a book opens it.
    /// </summary>
    private sealed class LecternUse : IBlockUseBehavior, IBlockMenu
    {
        public bool OpensForSpectators => true;

        public string? Statistic(IBlock block) => "minecraft:interact_with_lectern";

        public async ValueTask<UseResult> UseItemOnAsync(BlockUse use)
        {
            var book = use.HasItem && TagsRegistry.Item.LecternBooks.Entries.Contains(use.Item!.Holder.Id);
            if (use.Block.GetProperty("has_book") == "true" || !book)
                return UseResult.TryWithEmptyHand;

            var interaction = new PlayerInteractEventArgs(use.Player, use.Server)
            {
                Item = use.Item,
                Block = use.Block,
                BlockLocation = use.Position,
                Hand = use.Hand,
                OpensBlockMenu = false
            };
            await use.Server.EventDispatcher.ExecuteEventAsync(interaction);
            if (interaction.IsCancelled)
            {
                use.ResyncHand = true;
                return UseResult.Fail;
            }

            if (use.BlockEntity is not LecternBlockEntity lectern || !use.TryCommit(() =>
            {
                if (lectern.Lectern.Book is not null)
                    return false;

                lectern.PlaceBook(use.Item!);
                return true;
            }, consumeItem: true, destination: lectern.Inventory))
                return UseResult.Fail;

            use.Level.BlockUpdates.PlaySound(use.Position, "minecraft:item.book.put");
            await use.Player.SyncInventorySlotAsync(use.HandSlot);
            use.Level.EmitGameEvent(MobGameEvent.BlockChange, use.Center, use.Player);

            return UseResult.Success;
        }

        public async ValueTask<UseResult> UseWithoutItemAsync(BlockUse use)
        {
            if (use.Block.GetProperty("has_book") != "true")
                return UseResult.Pass;

            await BlockMenus.RequestAsync(use);
            return UseResult.Success;
        }

        public async ValueTask<BaseContainer?> CreateAsync(IPlayer player, AbstractLevel level, IBlock block,
            Vector position)
        {
            var stored = await level.GetOrCreateBlockEntityAsync(position) as LecternBlockEntity;

            return stored?.Lectern.Book is null ? null : new LecternContainer();
        }
    }
}
