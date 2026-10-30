namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private static Feature EndPlatform() => (context, _, origin) =>
    {
        var level = context.Level;

        // 26.3 attempts the whole platform even when its origin is outside the writable region.
        // WorldGenRegion checks each write, allowing the portion that overlaps the region to be placed.
        for (var z = -2; z <= 2; z++)
        {
            for (var x = -2; x <= 2; x++)
            {
                for (var y = -1; y <= 2; y++)
                {
                    var position = origin + new Vector(x, y, z);
                    var block = y == -1 ? BlocksRegistry.ObsidianBlock : BlocksRegistry.Air;

                    if (level.GetBlock(position).RegistryId != block.RegistryId)
                    {
                        level.SetBlock(position, block);
                    }
                }
            }
        }

        return true;
    };
}
