using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features.Trees;

/// <summary>
/// 26.3's <c>minecraft:huge_fungus</c>: a crimson or warped fungus on its nylium, a stem (3x3 with sparse corners
/// one time in sixteen and a bit, unless planted) and a hat of hat and decor blocks, with weeping vines under nether
/// wart hats. The constants and draw order are vanilla's.
/// </summary>
internal sealed class HugeFungusFeature(FeatureRegistry registry, JsonElement json)
{
    private const float HugeProbability = 0.06f;

    private readonly IBlock validBase = FeatureRegistry.ReadBlock(json.GetProperty("valid_base_block"));
    private readonly IBlock stem = FeatureRegistry.ReadBlock(json.GetProperty("stem_state"));
    private readonly IBlock hat = FeatureRegistry.ReadBlock(json.GetProperty("hat_state"));
    private readonly IBlock decor = FeatureRegistry.ReadBlock(json.GetProperty("decor_state"));
    private readonly BlockPredicate replaceableBlocks = registry.Predicate(json.GetProperty("replaceable_blocks"));
    private readonly bool planted = json.TryGetProperty("planted", out var planted) && planted.GetBoolean();

    public bool Place(DecorationContext context, IRandomSource random, Vector origin)
    {
        var level = context.Level;

        if (level.GetBlock(origin + Vector.Down).RegistryId != this.validBase.RegistryId)
        {
            return false;
        }

        var height = FeatureHelpers.NextInt(random, 4, 13);

        if (random.NextInt(12) == 0)
        {
            height *= 2;
        }

        if (!this.planted && origin.Y + height + 1 >= context.GeneratorHeight)
        {
            return false;
        }

        var huge = !this.planted && random.NextFloat() < HugeProbability;

        level.SetBlock(origin, BlocksRegistry.Air);
        this.PlaceStem(level, random, origin, height, huge);
        this.PlaceHat(level, random, origin, height, huge);
        return true;
    }

    private bool IsReplaceable(IWorldGenLevel level, Vector position, bool stemBlock)
    {
        if (level.GetBlock(position).CanBeReplaced())
        {
            return true;
        }

        return stemBlock && this.replaceableBlocks(level, position);
    }

    private void PlaceStem(IWorldGenLevel level, IRandomSource random, Vector origin, int height, bool huge)
    {
        var radius = huge ? 1 : 0;

        for (var dx = -radius; dx <= radius; dx++)
        {
            for (var dz = -radius; dz <= radius; dz++)
            {
                var corner = huge && Math.Abs(dx) == radius && Math.Abs(dz) == radius;

                for (var y = 0; y < height; y++)
                {
                    this.PlaceStemBlock(level, random, origin + (dx, y, dz), corner);
                }
            }
        }
    }

    private void PlaceStemBlock(IWorldGenLevel level, IRandomSource random, Vector position, bool corner)
    {
        if (!this.IsReplaceable(level, position, true))
        {
            return;
        }

        if (this.planted)
        {
            if (!level.GetBlock(position + Vector.Down).IsAir)
            {
                level.SetBlock(position, BlocksRegistry.Air);
            }

            level.SetBlock(position, this.stem);
            return;
        }

        if (!corner || random.NextFloat() < 0.1f)
        {
            level.SetBlock(position, this.stem);
        }
    }

    private void PlaceHat(IWorldGenLevel level, IRandomSource random, Vector origin, int height, bool huge)
    {
        var netherWart = this.hat.UnlocalizedName == "minecraft:nether_wart_block";
        var hatHeight = Math.Min(random.NextInt(1 + height / 3) + 5, height);
        var hatStart = height - hatHeight;

        for (var y = hatStart; y <= height; y++)
        {
            var radius = y < height - random.NextInt(3) ? 2 : 1;

            if (hatHeight > 8 && y < hatStart + 4)
            {
                radius = 3;
            }

            if (huge)
            {
                radius++;
            }

            for (var dx = -radius; dx <= radius; dx++)
            {
                for (var dz = -radius; dz <= radius; dz++)
                {
                    var edgeX = dx == -radius || dx == radius;
                    var edgeZ = dz == -radius || dz == radius;
                    var ring = new HatRing(!edgeX && !edgeZ && y != height, edgeX && edgeZ, y < hatStart + 3);

                    this.PlaceHatPosition(level, random, origin + (dx, y, dz), ring, netherWart);
                }
            }
        }
    }

    private void PlaceHatPosition(IWorldGenLevel level, IRandomSource random, Vector position, HatRing ring, bool wart)
    {
        if (!this.IsReplaceable(level, position, false))
        {
            return;
        }

        if (this.planted && !level.GetBlock(position + Vector.Down).IsAir)
        {
            level.SetBlock(position, BlocksRegistry.Air);
        }

        if (ring.Low)
        {
            if (!ring.Inside)
            {
                this.PlaceHatDropBlock(level, random, position, wart);
            }

            return;
        }

        if (ring.Inside)
        {
            this.PlaceHatBlock(level, random, position, 0.1f, 0.2f, wart ? 0.1f : 0.0f);
        }
        else if (ring.Corner)
        {
            this.PlaceHatBlock(level, random, position, 0.01f, 0.7f, wart ? 0.083f : 0.0f);
        }
        else
        {
            this.PlaceHatBlock(level, random, position, 5.0E-4f, 0.98f, wart ? 0.07f : 0.0f);
        }
    }

    private void PlaceHatBlock(
        IWorldGenLevel level, IRandomSource random, Vector position, float decorChance, float hatChance, float vines)
    {
        if (random.NextFloat() < decorChance)
        {
            level.SetBlock(position, this.decor);
            return;
        }

        if (random.NextFloat() < hatChance)
        {
            level.SetBlock(position, this.hat);

            if (random.NextFloat() < vines)
            {
                TryPlaceWeepingVines(level, random, position);
            }
        }
    }

    // The hat's lower rings hang down: hat under hat, otherwise a 15% chance.
    private void PlaceHatDropBlock(IWorldGenLevel level, IRandomSource random, Vector position, bool netherWart)
    {
        if (level.GetBlock(position + Vector.Down).RegistryId == this.hat.RegistryId)
        {
            level.SetBlock(position, this.hat);
            return;
        }

        if (random.NextFloat() < 0.15)
        {
            level.SetBlock(position, this.hat);

            if (netherWart && random.NextInt(11) == 0)
            {
                TryPlaceWeepingVines(level, random, position);
            }
        }
    }

    private static void TryPlaceWeepingVines(IWorldGenLevel level, IRandomSource random, Vector position)
    {
        var below = position + Vector.Down;

        if (!level.GetBlock(below).IsAir)
        {
            return;
        }

        var length = FeatureHelpers.NextInt(random, 1, 5);

        if (random.NextInt(7) == 0)
        {
            length *= 2;
        }

        WeepingVinesFeature.PlaceWeepingVinesColumn(level, random, below, length, 23, 25);
    }

    private readonly record struct HatRing(bool Inside, bool Corner, bool Low);
}
