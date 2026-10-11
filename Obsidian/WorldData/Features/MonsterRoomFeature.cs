namespace Obsidian.WorldData.Features;

/// <summary>
/// Cobblestone dungeons with a spawner and up to two chests with the <c>simple_dungeon</c> loot table, like vanilla's
/// MonsterRoomFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:monster_room")]
public sealed class MonsterRoomFeature : ConfiguredFeatureBase
{
    private static readonly BlockSet featuresCannotReplace = new("#minecraft:features_cannot_replace");

    // Vanilla's MonsterRoomFeature.MOBS: zombies are twice as likely.
    private static readonly string[] mobs = ["minecraft:skeleton", "minecraft:zombie", "minecraft:zombie", "minecraft:spider"];

    public override string Type => "minecraft:monster_room";

    private static IBlock CaveAir => field ??= BlocksRegistry.Get(Material.CaveAir);

    private static IBlock Cobblestone => field ??= BlocksRegistry.Get(Material.Cobblestone);

    private static IBlock MossyCobblestone => field ??= BlocksRegistry.Get(Material.MossyCobblestone);

    private static IBlock Chest => field ??= BlocksRegistry.Get(Material.Chest);

    private static IBlock Spawner => field ??= BlocksRegistry.Get(Material.Spawner);

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin))
            return false;

        Func<IBlock, bool> canReplace = state => !featuresCannotReplace.Contains(state);
        var radiusX = random.NextInt(2) + 2;
        var minX = -radiusX - 1;
        var maxX = radiusX + 1;
        var radiusZ = random.NextInt(2) + 2;
        var minZ = -radiusZ - 1;
        var maxZ = radiusZ + 1;

        var openings = 0;
        for (var x = minX; x <= maxX; x++)
        {
            for (var y = -1; y <= 4; y++)
            {
                for (var z = minZ; z <= maxZ; z++)
                {
                    var position = origin + new Vector(x, y, z);
                    var solid = level.GetBlock(position).IsSolid();
                    if ((y == -1 || y == 4) && !solid)
                        return false;

                    if ((x == minX || x == maxX || z == minZ || z == maxZ) && y == 0 && level.GetBlock(position).IsAir
                        && level.GetBlock(position + Vector.Up).IsAir)
                    {
                        openings++;
                    }
                }
            }
        }

        if (openings < 1 || openings > 5)
            return false;

        for (var x = minX; x <= maxX; x++)
        {
            for (var y = 3; y >= -1; y--)
            {
                for (var z = minZ; z <= maxZ; z++)
                {
                    var position = origin + new Vector(x, y, z);
                    var existing = level.GetBlock(position);

                    if (x == minX || y == -1 || z == minZ || x == maxX || y == 4 || z == maxZ)
                    {
                        if (position.Y >= level.MinY && !level.GetBlock(position + Vector.Down).IsSolid())
                            level.SetBlock(position, CaveAir);
                        else if (existing.IsSolid() && existing.Material != Material.Chest)
                        {
                            FeatureHelpers.SafeSetBlock(level, position, y == -1 && random.NextInt(4) != 0 ? MossyCobblestone : Cobblestone,
                                canReplace);
                        }
                    }
                    else if (existing.Material is not (Material.Chest or Material.Spawner))
                    {
                        FeatureHelpers.SafeSetBlock(level, position, CaveAir, canReplace);
                    }
                }
            }
        }

        for (var chest = 0; chest < 2; chest++)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var position = new Vector(origin.X + random.NextInt(radiusX * 2 + 1) - radiusX, origin.Y,
                    origin.Z + random.NextInt(radiusZ * 2 + 1) - radiusZ);
                if (!level.GetBlock(position).IsAir)
                    continue;

                var walls = 0;
                foreach (var face in FeatureHelpers.Horizontal)
                {
                    if (level.GetBlock(position.Offset(face)).IsSolid())
                        walls++;
                }

                if (walls != 1)
                    continue;

                FeatureHelpers.SafeSetBlock(level, position, FeatureHelpers.Reorient(level, position, Chest), canReplace);
                FeatureHelpers.SetLootTable(level, random, position, "minecraft:chests/simple_dungeon");
                break;
            }
        }

        FeatureHelpers.SafeSetBlock(level, origin, Spawner, canReplace);
        FeatureHelpers.SetSpawnerEntity(level, origin, () => mobs[random.NextInt(mobs.Length)]);
        return true;
    }
}
