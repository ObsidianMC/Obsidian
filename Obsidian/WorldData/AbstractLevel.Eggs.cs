using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    internal void TrackMobEgg(Vector position, IBlock block)
    {
        if (GetLoadedChunk(position.X >> 4, position.Z >> 4) is not Chunk chunk) return;
        if (block.Material == Material.SnifferEgg)
            chunk.MobEggTicks.TryAdd(position, SnifferEggDelay(position));
        else if (block.Material == Material.TurtleEgg)
            chunk.MobEggTicks.TryAdd(position, 0);
        else chunk.MobEggTicks.TryRemove(position, out _);
    }

    internal void RegisterMobEggs(Chunk chunk)
    {
        var sections = chunk.Sections;
        for (var sectionIndex = 0; sectionIndex < sections.Length; sectionIndex++)
        {
            var section = sections[sectionIndex];
            if (section.IsEmpty) continue;
            var palette = section.BlockStateContainer.Palette;
            var hasEggs = false;
            for (var index = 0; index < palette.Count; index++)
                if (palette.GetValueFromIndex(index)?.Material is Material.SnifferEgg or Material.TurtleEgg) { hasEggs = true; break; }
            if (!hasEggs) continue;
            for (var y = 0; y < 16; y++)
            for (var z = 0; z < 16; z++)
            for (var x = 0; x < 16; x++)
            {
                var block = section.GetBlock(x, y, z);
                if (block.Material is not Material.SnifferEgg and not Material.TurtleEgg) continue;
                var point = new Vector(chunk.X * 16 + x, chunk.MinY + sectionIndex * 16 + y, chunk.Z * 16 + z);
                chunk.MobEggTicks.TryAdd(point, block.Material == Material.SnifferEgg ? SnifferEggDelay(point) : 0);
            }
        }
    }

    private int SnifferEggDelay(Vector position) =>
        (GameEventBlock(position - new Vector(0, 1, 0))?.Material == Material.MossBlock ? 4000 : 8000) + Random.Shared.Next(300);

    internal async ValueTask TickMobEggsAsync()
    {
        foreach (var chunk in Regions.Values.SelectMany(region => region.GeneratedChunks()).OfType<Chunk>())
        {
            if (chunk.MobEggTicks.IsEmpty || !IsMobTicking(new VectorD(chunk.X * 16 + 8, 0, chunk.Z * 16 + 8))) continue;
            foreach (var (point, delay) in chunk.MobEggTicks.ToArray())
            {
                var block = GameEventBlock(point);
                if (block?.Material is not Material.SnifferEgg and not Material.TurtleEgg)
                { chunk.MobEggTicks.TryRemove(point, out _); continue; }
                if (block.Material == Material.SnifferEgg)
                {
                    if (delay > 1) { chunk.MobEggTicks[point] = delay - 1; continue; }
                }
                else
                {
                    var randomTicks = LevelData.GetIntegerRule("random_tick_speed", 3);
                    if (randomTicks <= 0 || Random.Shared.NextDouble() >= 1 - Math.Pow(4095d / 4096, randomTicks)) continue;
                    var time = ((DayTime % 24000) + 24000) % 24000;
                    if (time is < 15600 or > 16560 && Random.Shared.Next(500) != 0) continue;
                    if (GameEventBlock(point - new Vector(0, 1, 0))?.Material is not (Material.Sand or Material.RedSand)) continue;
                }
                var hatch = int.TryParse(block.GetProperty("hatch"), out var value) ? value : 0;
                if (hatch < 2)
                {
                    await SetBlockAsync(point, block.WithProperty("hatch", hatch + 1), true);
                    if (block.Material == Material.SnifferEgg) chunk.MobEggTicks[point] = SnifferEggDelay(point);
                    EmitGameEvent(MobGameEvent.BlockChange, (VectorD)point + new VectorD(0.5, 0.5, 0.5));
                    continue;
                }
                chunk.MobEggTicks.TryRemove(point, out _);
                await SetBlockAsync(point, BlocksRegistry.Air, true);
                PacketBroadcaster.QueuePacketToLevelInRange(this, (VectorD)point, new LevelEventPacket(2001, point, block.GetHashCode()));
                var count = block.Material == Material.TurtleEgg && int.TryParse(block.GetProperty("eggs"), out var eggs) ? eggs : 1;
                for (var index = 0; index < count; index++)
                {
                    var position = (VectorD)point + new VectorD(0.3 + index * 0.2, 0, 0.3);
                    var child = GetNewEntitySpawner().WithEntityType(block.Material == Material.SnifferEgg ? EntityType.Sniffer : EntityType.Turtle).AtPosition(position).AsBaby().Spawn();
                    if (child is Sniffer sniffer) sniffer.Age = -48000;
                    if (child is Turtle turtle) turtle.SetHome(point);
                }
            }
        }
    }

    internal async ValueTask TrampleTurtleEggAsync(IEntity entity, bool landed = false)
    {
        if (entity.Type is EntityType.Turtle or EntityType.Bat || entity is not ILiving ||
            entity is IPlayer { GameMode: GameMode.Spectator } || entity is not IPlayer && !LevelData.GetBooleanRule("mob_griefing")) return;
        if (Random.Shared.Next(landed ? 3 : 100) != 0) return;
        var origin = (Vector)entity.Position.Floor();
        foreach (var point in new[] { origin, origin - new Vector(0, 1, 0) })
        {
            var block = GameEventBlock(point);
            if (block?.Material != Material.TurtleEgg) continue;
            if (landed && entity.Type is EntityType.Zombie or EntityType.Husk or EntityType.Drowned or EntityType.ZombifiedPiglin) return;
            var eggs = int.TryParse(block.GetProperty("eggs"), out var count) ? count : 1;
            await SetBlockAsync(point, eggs == 1 ? BlocksRegistry.Air : block.WithProperty("eggs", eggs - 1), true);
            PacketBroadcaster.QueuePacketToLevelInRange(this, (VectorD)point, new LevelEventPacket(2001, point, block.GetHashCode()));
            EmitGameEvent(MobGameEvent.BlockDestroy, (VectorD)point + new VectorD(0.5, 0.5, 0.5), entity);
            return;
        }
    }
}
