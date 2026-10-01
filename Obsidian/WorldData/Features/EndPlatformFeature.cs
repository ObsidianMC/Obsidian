namespace Obsidian.WorldData.Features;

/// <summary>
/// The 5x5 obsidian arrival platform with clear space above it, like vanilla's EndPlatformFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:end_platform")]
public sealed class EndPlatformFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:end_platform";

    private static IBlock Obsidian => field ??= BlocksRegistry.Get(Material.Obsidian);

    private static IBlock Air => field ??= BlocksRegistry.Get(Material.Air);

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin))
            return false;

        for (var dz = -2; dz <= 2; dz++)
        {
            for (var dx = -2; dx <= 2; dx++)
            {
                for (var dy = -1; dy < 3; dy++)
                {
                    var position = origin + new Vector(dx, dy, dz);
                    var block = dy == -1 ? Obsidian : Air;
                    if (level.GetBlock(position).RegistryId != block.RegistryId)
                        level.SetBlock(position, block);
                }
            }
        }

        return true;
    }
}
