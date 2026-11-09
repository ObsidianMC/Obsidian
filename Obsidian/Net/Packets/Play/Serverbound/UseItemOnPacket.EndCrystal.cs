using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.WorldData;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class UseItemOnPacket
{
    // Vanilla's EndCrystalItem.useOn: a crystal goes on top of obsidian or bedrock, where there's room for it.
    private async ValueTask<UseResult> PlaceEndCrystalAsync(BlockUse use)
    {
        var level = use.Level;
        if (use.Block.RegistryId is not BlockIds.Obsidian and not BlockIds.Bedrock)
            return UseResult.Fail;

        var above = this.Position + Vector.Up;
        var terrain = new MobTerrain(level);
        if (level.IsOutsideBuildHeight(above.Y) || terrain.GetBlock(above)?.IsAir != true)
            return UseResult.Fail;

        var bounds = new BoundingBox((VectorD)above, (VectorD)above + new VectorD(1, 2, 1));
        // Vanilla's AABB overlap, which boxes that only touch don't count as.
        var occupied = level.GetEntitiesInRange((VectorD)above, 4)
            .Any(entity => MobTerrain.Overlaps(bounds, entity.Dimension.CreateBBFromPosition(entity.Position)));
        if (occupied)
            return UseResult.Fail;

        var crystal = new EndCrystal
        {
            Level = level,
            EntityId = Server.GetNextEntityId(),
            Position = (VectorD)above + new VectorD(0.5, 0, 0.5),
            ShowBottom = false
        };
        level.SpawnEntity(crystal);
        level.EmitGameEvent(MobGameEvent.EntityPlace, crystal.Position, use.Player);

        await use.ConsumeItemAsync();
        return UseResult.Success;
    }
}
