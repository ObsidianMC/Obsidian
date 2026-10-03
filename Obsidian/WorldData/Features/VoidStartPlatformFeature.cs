namespace Obsidian.WorldData.Features;

/// <summary>
/// The stone spawn platform of the "the void" flat preset, like vanilla's VoidStartPlatformFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:void_start_platform")]
public sealed class VoidStartPlatformFeature : ConfiguredFeatureBase
{
    private const int PlatformRadius = 16;

    private static readonly Vector platformOffset = new(8, 3, 8);

    public override string Type => "minecraft:void_start_platform";

    private static IBlock Stone => field ??= BlocksRegistry.Get(Material.Stone);

    private static IBlock Cobblestone => field ??= BlocksRegistry.Get(Material.Cobblestone);

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin))
            return false;

        var chunkX = origin.X >> 4;
        var chunkZ = origin.Z >> 4;
        if (CheckerboardDistance(chunkX, chunkZ, platformOffset.X >> 4, platformOffset.Z >> 4) > 1)
            return true;

        var center = platformOffset.AtY(origin.Y + platformOffset.Y);
        for (var z = chunkZ << 4; z <= (chunkZ << 4) + 15; z++)
        {
            for (var x = chunkX << 4; x <= (chunkX << 4) + 15; x++)
            {
                if (CheckerboardDistance(center.X, center.Z, x, z) > PlatformRadius)
                    continue;

                var position = new Vector(x, center.Y, z);
                level.SetBlock(position, position == center ? Cobblestone : Stone);
            }
        }

        return true;
    }

    private static int CheckerboardDistance(int x1, int z1, int x2, int z2) => Math.Max(Math.Abs(x1 - x2), Math.Abs(z1 - z2));
}
