using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.WorldData;

// Sniffer eggs hatch on delays this file counts down. Turtle eggs hatch on random ticks
// (TurtleEggBlockBehavior); trampling them is here.
public abstract partial class AbstractLevel
{
    internal void TrackMobEgg(Vector position, IBlock block)
    {
        if (GetLoadedChunk(position.X >> 4, position.Z >> 4) is not Chunk chunk)
            return;
        if (block.Is(BlockIds.SnifferEgg))
            chunk.MobEggTicks.TryAdd(position, SnifferEggDelay(position));
        else
            chunk.MobEggTicks.TryRemove(position, out _);
        chunk.MarkUnsaved();
    }

    internal void RegisterMobEggs(Chunk chunk)
    {
        var sections = chunk.Sections;
        for (var sectionIndex = 0; sectionIndex < sections.Length; sectionIndex++)
        {
            var section = sections[sectionIndex];
            if (section.IsEmpty)
                continue;
            var palette = section.BlockStateContainer.Palette;
            var hasEggs = false;
            for (var index = 0; index < palette.Count; index++)
            {
                if (palette.GetValueFromIndex(index)?.RegistryId is BlockIds.SnifferEgg)
                {
                    hasEggs = true;
                    break;
                }
            }

            if (!hasEggs)
                continue;
            for (var y = 0; y < 16; y++)
            {
                for (var z = 0; z < 16; z++)
                {
                    for (var x = 0; x < 16; x++)
                    {
                        var block = section.GetBlock(x, y, z);
                        if (block.RegistryId is not BlockIds.SnifferEgg)
                            continue;
                        var point = new Vector(chunk.X * 16 + x, chunk.MinY + sectionIndex * 16 + y, chunk.Z * 16 + z);
                        if (chunk.MobEggTicks.TryAdd(point, SnifferEggDelay(point)))
                            chunk.MarkUnsaved();
                    }
                }
            }
        }
    }

    // Vanilla's SnifferEggBlock.onPlace: a third of 12000 ticks on a block of #sniffer_egg_hatch_boost, else of
    // 24000, and up to 299 more.
    private int SnifferEggDelay(Vector position)
    {
        var below = GameEventBlock(position - new Vector(0, 1, 0));
        var boosted = below is not null
            && TagsRegistry.Block.SnifferEggHatchBoost.Entries.Contains(below.RegistryId);

        return (boosted ? 4000 : 8000) + Random.Shared.Next(300);
    }

    internal async ValueTask TickMobEggsAsync()
    {
        foreach (var chunk in this.GeneratedChunksThisTick())
        {
            if (!chunk.HasMobEggTicks || !IsMobTicking(new VectorD(chunk.X * 16 + 8, 0, chunk.Z * 16 + 8)))
                continue;
            try
            {
                foreach (var (point, delay) in chunk.MobEggTicks.ToArray())
                {
                    // Saves from before turtle eggs hatched on random ticks track them here too; they're dropped.
                    var block = GameEventBlock(point);
                    if (block?.RegistryId is not BlockIds.SnifferEgg)
                    {
                        chunk.MobEggTicks.TryRemove(point, out _);
                        continue;
                    }
                    if (delay > 1)
                    {
                        chunk.MobEggTicks[point] = delay - 1;
                        continue;
                    }
                    var hatch = int.TryParse(block.GetProperty("hatch"), out var value) ? value : 0;
                    if (hatch < 2)
                    {
                        await SetBlockAsync(point, block.WithProperty("hatch", hatch + 1), true);
                        chunk.MobEggTicks[point] = SnifferEggDelay(point);
                        EmitGameEvent(MobGameEvent.BlockChange, (VectorD)point + new VectorD(0.5, 0.5, 0.5));
                        continue;
                    }
                    chunk.MobEggTicks.TryRemove(point, out _);
                    await SetBlockAsync(point, BlocksRegistry.Air, true);
                    PacketBroadcaster.QueuePacketToLevelInRange(this, (VectorD)point, new LevelEventPacket(2001, point, block.GetHashCode()));
                    var child = GetNewEntitySpawner()
                        .WithEntityType(EntityType.Sniffer)
                        .AtPosition((VectorD)point + new VectorD(0.3, 0, 0.3))
                        .AsBaby()
                        .Spawn();
                    if (child is Sniffer sniffer)
                        sniffer.Age = -48000;
                }
            }
            finally
            {
                // After the delays changed, so a save that takes the mark has them.
                chunk.MarkUnsaved();
            }
        }
    }

    internal async ValueTask TrampleTurtleEggAsync(IEntity entity, bool landed = false)
    {
        if (entity.Type is EntityType.Turtle or EntityType.Bat || entity is not ILiving ||
            entity is IPlayer { GameMode: GameMode.Spectator } || entity is not IPlayer && !LevelData.GetBooleanRule("mob_griefing"))
        {
            return;
        }

        if (Random.Shared.Next(landed ? 3 : 100) != 0)
            return;
        var origin = (Vector)entity.Position.Floor();
        foreach (var point in new[] { origin, origin - new Vector(0, 1, 0) })
        {
            var block = GameEventBlock(point);
            if (block?.RegistryId != BlockIds.TurtleEgg)
                continue;
            if (landed && entity.Type is EntityType.Zombie or EntityType.Husk or EntityType.Drowned or EntityType.ZombifiedPiglin)
                return;
            var eggs = int.TryParse(block.GetProperty("eggs"), out var count) ? count : 1;
            await SetBlockAsync(point, eggs == 1 ? BlocksRegistry.Air : block.WithProperty("eggs", eggs - 1), true);
            PacketBroadcaster.QueuePacketToLevelInRange(this, (VectorD)point, new LevelEventPacket(2001, point, block.GetHashCode()));
            EmitGameEvent(MobGameEvent.BlockDestroy, (VectorD)point + new VectorD(0.5, 0.5, 0.5), entity);
            return;
        }
    }
}
