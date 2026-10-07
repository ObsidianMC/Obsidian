using Obsidian.Entities;
using Obsidian.Entities.AI;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    private void CheckWither(Vector position, IBlock block)
    {
        if (block.Material is not Material.WitherSkeletonSkull and not Material.WitherSkeletonWallSkull ||
            LevelData.Difficulty == Difficulty.Peaceful) return;
        EnqueueEntityAction(async () =>
        {
            var terrain = new MobTerrain(this);
            foreach (var axis in new[] { new Vector(1, 0, 0), new Vector(0, 0, 1) })
            for (var offset = -1; offset <= 1; offset++)
            {
                var center = position + axis * offset;
                var body = center - new Vector(0, 1, 0);
                var basePosition = center - new Vector(0, 2, 0);
                bool IsSkull(Vector point) => terrain.GetBlock(point)?.Material is Material.WitherSkeletonSkull or Material.WitherSkeletonWallSkull;
                bool IsSoul(Vector point) => terrain.GetBlock(point)?.Material is Material.SoulSand or Material.SoulSoil;
                if (!IsSkull(center - axis) || !IsSkull(center) || !IsSkull(center + axis) ||
                    !IsSoul(body - axis) || !IsSoul(body) || !IsSoul(body + axis) || !IsSoul(basePosition) ||
                    terrain.GetBlock(basePosition - axis)?.IsAir != true || terrain.GetBlock(basePosition + axis)?.IsAir != true) continue;
                foreach (var point in new[] { center - axis, center, center + axis, body - axis, body, body + axis, basePosition })
                    await SetBlockAsync(point, BlocksRegistry.Air, true);
                var wither = new Wither { Level = this, EntityId = Server.GetNextEntityId(),
                    Position = new VectorD(basePosition.X + 0.5, basePosition.Y + 0.55, basePosition.Z + 0.5),
                    Yaw = axis.X == 0 ? 90 : 0 };
                SpawnEntity(wither);
                wither.BeginSpawnCharge();
                return;
            }
        });
    }

    private void CheckSnowGolem(Vector position, IBlock block)
    {
        if (block.Material is not Material.CarvedPumpkin and not Material.JackOLantern)
            return;
        EnqueueEntityAction(async () =>
        {
            var terrain = new MobTerrain(this);
            var middle = new Vector(position.X, position.Y - 1, position.Z);
            var bottom = new Vector(position.X, position.Y - 2, position.Z);
            if (terrain.GetBlock(position)?.Material is not Material.CarvedPumpkin and not Material.JackOLantern ||
                terrain.GetBlock(middle)?.Material != Material.SnowBlock || terrain.GetBlock(bottom)?.Material != Material.SnowBlock)
                return;
            await SetBlockAsync(position, BlocksRegistry.Air, true);
            await SetBlockAsync(middle, BlocksRegistry.Air, true);
            await SetBlockAsync(bottom, BlocksRegistry.Air, true);
            SpawnEntity(new SnowGolem { Level = this, EntityId = Server.GetNextEntityId(),
                Position = new VectorD(position.X + 0.5f, bottom.Y + 0.05f, position.Z + 0.5f) });
        });
    }
}
