using Obsidian.Entities;
using Obsidian.Entities.AI;

namespace Obsidian.WorldData;

internal sealed partial class MobSpawner
{
    private void TickPhantoms(IPlayer[] players)
    {
        if (level.DimensionName != "minecraft:overworld" || !level.LevelData.GetBooleanRule("spawn_phantoms") ||
            !level.LevelData.GetBooleanRule("spawn_monsters") || level.LevelData.Difficulty == Difficulty.Peaceful) return;
        if (--phantomDelay > 0) return;
        phantomDelay = (60 + random.Next(60)) * 20;
        var time = ((level.DayTime % 24000) + 24000) % 24000;
        if (time is < 13000 or > 23000) return;
        var terrain = new MobTerrain(level);
        foreach (var player in players.OfType<Player>())
        {
            if (!player.Alive || player.Position.Y < 63 || terrain.GetSkyLight((Vector)player.Position.Floor()) < 15 ||
                random.Next(Math.Max(1, player.TimeSinceRest)) < 72000) continue;
            var point = player.Position + new VectorD(random.Next(-10, 11), 20 + random.Next(15), random.Next(-10, 11));
            if (terrain.GetBlock((Vector)point.Floor()) is not { IsAir: true }) continue;
            var count = 1 + random.Next((int)level.LevelData.Difficulty + 1);
            for (var index = 0; index < count; index++)
            {
                var phantom = new Phantom { Level = level, Position = point, EntityId = Server.GetNextEntityId() };
                phantom.InitializeAi();
                if (random.NextSingle() * 3 > phantom.EffectiveDifficulty) break;
                phantom.AttackTarget = player;
                level.SpawnEntity(phantom);
            }
        }
    }
}
