using Obsidian.API.Inventory;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    // Matches 26.3 Block.popResourceFromFace: spawn outside the clicked face, with motion away from that face.
    internal void PopTransformerResource(Vector position, BlockFace face, ItemStack stack)
    {
        var random = this.GrowthRandom;
        var direction = face.ToVector();
        var halfSize = ItemEntityWidth / 2;
        var xOffset = direction.X == 0 ? random.NextDouble() * 0.5 - 0.25 : direction.X * (0.5 + halfSize);
        var yOffset = direction.Y == 0 ? random.NextDouble() * 0.5 - 0.25 : direction.Y * (0.5 + halfSize);
        var zOffset = direction.Z == 0 ? random.NextDouble() * 0.5 - 0.25 : direction.Z * (0.5 + halfSize);
        var point = new VectorD(
            position.X + 0.5 + xOffset,
            position.Y + 0.5 + yOffset - halfSize,
            position.Z + 0.5 + zOffset);

        var xMotion = direction.X == 0 ? random.NextDouble() * 0.2 - 0.1 : direction.X * 0.1;
        var yMotion = direction.Y == 0 ? random.NextDouble() * 0.1 : direction.Y * 0.1 + 0.1;
        var zMotion = direction.Z == 0 ? random.NextDouble() * 0.2 - 0.1 : direction.Z * 0.1;
        var motion = new VectorD(xMotion, yMotion, zMotion);

        this.SpawnItem(point, stack, motion);
    }
}
