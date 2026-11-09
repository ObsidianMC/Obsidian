using Obsidian.API.Containers;
using Obsidian.API.Inventory;
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
                sound = VanillaPlacement.Use(level.BlockUpdates, use.Position, use.Block, use.Player);

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

            jukebox.Set(new ItemStack(use.Item!, 1).ToNbt("RecordItem"));
            jukebox.Set("IsPlaying", true);
            jukebox.Set("ObsidianSongElapsed", 0);

            var playing = use.Block.WithProperty("has_record", true);
            await use.Level.SetBlockAsync(use.Position, playing, true);
            use.Level.EmitGameEvent(MobGameEvent.BlockChange, use.Center);
            use.Level.StartJukebox(use.Position, song);

            await use.ConsumeItemAsync();
            use.Level.EmitGameEvent(MobGameEvent.BlockChange, use.Center, use.Player);
            use.Award("minecraft:play_record");

            return UseResult.Success;
        }

        public async ValueTask<UseResult> UseWithoutItemAsync(BlockUse use)
        {
            if (use.Block.GetProperty("has_record") != "true")
                return UseResult.Pass;

            if (Jukebox(use) is DataBlockEntity jukebox
                && jukebox.SnapshotData().TryGetTag<NbtCompound>("RecordItem", out var record)
                && record.ItemFromNbt() is { IsAir: false } stack)
            {
                await EjectAsync(use, jukebox, stack);
            }

            return UseResult.Success;
        }

        private static async ValueTask EjectAsync(BlockUse use, DataBlockEntity jukebox, ItemStack stack)
        {
            var level = use.Level;
            level.StopJukebox(use.Position);
            lock (jukebox.Data)
                jukebox.Data.Remove("RecordItem");

            await level.SetBlockAsync(use.Position, use.Block.WithProperty("has_record", false), true);

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
            if (!await use.Level.UseCampfireAsync(use.Player, use.Position, use.HandSlot))
                return UseResult.TryWithEmptyHand;

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
            if (!await use.Level.UseComposterAsync(use.Player, use.Position, use.Block, use.HandSlot))
                return UseResult.TryWithEmptyHand;

            use.Player.Statistics.Increment(used, type: "minecraft:used");
            use.Level.EmitGameEvent(MobGameEvent.BlockChange, use.Center, use.Player);

            return UseResult.Success;
        }

        public async ValueTask<UseResult> UseWithoutItemAsync(BlockUse use)
        {
            if (Composters.LevelOf(use.Block) != Composters.Ready)
                return UseResult.Pass;

            await use.Level.UseComposterAsync(use.Player, use.Position, use.Block, use.HandSlot);
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

            await use.Level.UseRespawnAnchorAsync(use.Player, use.Position, use.Block, use.HandSlot);
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
            var (used, consume) = Signs.Use(use.Level, use.Position, use.Block, use.Player, use.Item);
            if (!used)
                return UseResult.Pass;

            if (consume)
            {
                use.Player.Statistics.Increment(use.Item!.Holder.UnlocalizedName, type: "minecraft:used");
                await use.ConsumeItemAsync();
            }

            return UseResult.SuccessServer;
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

            await use.Level.SetBlockAsync(use.Position, BlocksRegistry.Air, true);
            PrimedTnt.Prime(use.Level, use.Position, use.Player);
            use.Level.EmitGameEvent(MobGameEvent.PrimeFuse, use.Center, use.Player);

            var item = use.Item!;
            await ItemDamage.UseIgniterAsync(use, item);
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

            if (await use.Level.GetOrCreateBlockEntityAsync(use.Position) is not LecternBlockEntity lectern)
                return UseResult.TryWithEmptyHand;

            lectern.PlaceBook(use.Item!);
            await use.ConsumeItemAsync();
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
