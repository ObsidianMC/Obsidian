using Obsidian.Entities;
using Obsidian.Entities.AI;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
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
                Position = new VectorF(position.X + 0.5f, bottom.Y + 0.05f, position.Z + 0.5f) });
        });
    }
}
