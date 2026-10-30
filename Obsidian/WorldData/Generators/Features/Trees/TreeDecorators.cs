using Obsidian.Nbt;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Features.Tree;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features.Trees;

/// <summary>Scatters blocks on solid, uncovered ground around the trunk's base.</summary>
internal sealed class PlaceOnGroundDecorator(FeatureRegistry registry, JsonElement json) : TreeDecorator
{
    private readonly int tries = Field(json, "tries", 128);
    private readonly int radius = Field(json, "radius", 2);
    private readonly int height = Field(json, "height", 1);
    private readonly StateProvider provider = registry.Provider(json.GetProperty("block_state_provider"));

    public override void Place(TreeDecoration context)
    {
        var lowest = context.LowestTrunkOrRoot();

        if (lowest.Count == 0)
        {
            return;
        }

        var first = lowest[0];
        var y = first.Y;
        var minX = first.X;
        var maxX = first.X;
        var minZ = first.Z;
        var maxZ = first.Z;

        foreach (var position in lowest)
        {
            if (position.Y != y)
            {
                continue;
            }

            minX = Math.Min(minX, position.X);
            maxX = Math.Max(maxX, position.X);
            minZ = Math.Min(minZ, position.Z);
            maxZ = Math.Max(maxZ, position.Z);
        }

        var random = context.Random;

        for (var i = 0; i < this.tries; i++)
        {
            var spotX = random.NextIntBetweenInclusive(minX - this.radius, maxX + this.radius);
            var spotY = random.NextIntBetweenInclusive(y - this.height, y + this.height);
            var spotZ = random.NextIntBetweenInclusive(minZ - this.radius, maxZ + this.radius);

            this.TryPlaceAbove(context, new Vector(spotX, spotY, spotZ));
        }
    }

    private void TryPlaceAbove(TreeDecoration context, Vector ground)
    {
        var level = context.Level;
        var above = ground + Vector.Up;
        var aboveBlock = level.GetBlock(above);
        var groundBlock = level.GetBlock(ground);

        var free = aboveBlock.IsAir || TreeBlocks.IsVine(aboveBlock);
        var solid = TreeBlocks.IsSolidRender(groundBlock) && !context.Tags.Leaves.Contains(groundBlock.RegistryId);
        var uncovered = level.GetHeight(HeightmapType.MotionBlockingNoLeaves, ground.X, ground.Z) <= above.Y;

        if (free && solid && uncovered)
        {
            var state = FeatureRegistry.State(this.provider, level, context.Random, above);
            context.SetBlock(above, state);
        }
    }

    private static int Field(JsonElement json, string name, int fallback) =>
        json.TryGetProperty(name, out var value) ? value.GetInt32() : fallback;
}

/// <summary>A bee nest with two or three bees on the trunk, just below the leaves.</summary>
internal sealed class BeehiveDecorator(JsonElement json) : TreeDecorator
{
    private static readonly BlockFace[] spawnDirections = [BlockFace.East, BlockFace.South, BlockFace.West];

    private readonly float probability = json.GetProperty("probability").GetSingle();

    public override void Place(TreeDecoration context)
    {
        var leaves = context.Leaves;
        var logs = context.Logs;

        if (logs.Count == 0)
        {
            return;
        }

        var random = context.Random;

        if (random.NextFloat() >= this.probability)
        {
            return;
        }

        var nestY = leaves.Count > 0
            ? Math.Max(leaves[0].Y - 1, logs[0].Y + 1)
            : Math.Min(logs[0].Y + 1 + random.NextInt(3), logs[^1].Y);
        var candidates = new List<Vector>();

        foreach (var log in logs)
        {
            if (log.Y != nestY)
            {
                continue;
            }

            foreach (var direction in spawnDirections)
            {
                candidates.Add(log.Offset(direction));
            }
        }

        if (candidates.Count == 0)
        {
            return;
        }

        TreeDecoration.Shuffle(candidates, random);

        foreach (var candidate in candidates)
        {
            if (!context.IsAir(candidate) || !context.IsAir(candidate + Vector.South))
            {
                continue;
            }

            var nest = BlocksRegistry.Get("minecraft:bee_nest").WithProperty("facing", "south");
            context.SetBlock(candidate, nest);
            AddBees(context, candidate);
            return;
        }
    }

    private static void AddBees(TreeDecoration context, Vector position)
    {
        var random = context.Random;

        if (context.Level.GetBlockEntity(position) is not DataBlockEntity { Id: "minecraft:beehive" } hive)
        {
            return;
        }

        var beeCount = 2 + random.NextInt(2);
        var bees = new NbtList(NbtTagType.Compound, "bees");

        for (var i = 0; i < beeCount; i++)
        {
            var entity = new NbtCompound("entity_data") { new NbtTag<string>("id", "minecraft:bee") };
            var ticksInHive = new NbtTag<int>("ticks_in_hive", random.NextInt(599));
            var minTicksInHive = new NbtTag<int>("min_ticks_in_hive", 600);

            bees.Add(new NbtCompound { entity, ticksInHive, minTicksInHive });
        }

        hive.Set(bees);
    }
}

/// <summary>Vines hanging from the sides of leaves, up to five blocks long.</summary>
internal sealed class LeaveVineDecorator(JsonElement json) : TreeDecorator
{
    private readonly float probability = json.GetProperty("probability").GetSingle();

    public override void Place(TreeDecoration context)
    {
        var random = context.Random;

        foreach (var leaf in context.Leaves)
        {
            if (random.NextFloat() < this.probability)
            {
                AddHangingVine(context, leaf + Vector.West, BlockFace.East);
            }

            if (random.NextFloat() < this.probability)
            {
                AddHangingVine(context, leaf + Vector.East, BlockFace.West);
            }

            if (random.NextFloat() < this.probability)
            {
                AddHangingVine(context, leaf + Vector.North, BlockFace.South);
            }

            if (random.NextFloat() < this.probability)
            {
                AddHangingVine(context, leaf + Vector.South, BlockFace.North);
            }
        }
    }

    private static void AddHangingVine(TreeDecoration context, Vector position, BlockFace face)
    {
        if (!context.IsAir(position))
        {
            return;
        }

        context.PlaceVine(position, face);

        var below = position + Vector.Down;

        for (var remaining = 4; context.IsAir(below) && remaining > 0; remaining--)
        {
            context.PlaceVine(below, face);
            below += Vector.Down;
        }
    }
}

/// <summary>Vines on two thirds of the trunk's sides.</summary>
internal sealed class TrunkVineDecorator : TreeDecorator
{
    public override void Place(TreeDecoration context)
    {
        var random = context.Random;

        foreach (var log in context.Logs)
        {
            if (random.NextInt(3) > 0)
            {
                PlaceIfAir(context, log + Vector.West, BlockFace.East);
            }

            if (random.NextInt(3) > 0)
            {
                PlaceIfAir(context, log + Vector.East, BlockFace.West);
            }

            if (random.NextInt(3) > 0)
            {
                PlaceIfAir(context, log + Vector.North, BlockFace.South);
            }

            if (random.NextInt(3) > 0)
            {
                PlaceIfAir(context, log + Vector.South, BlockFace.North);
            }
        }
    }

    private static void PlaceIfAir(TreeDecoration context, Vector position, BlockFace face)
    {
        if (context.IsAir(position))
        {
            context.PlaceVine(position, face);
        }
    }
}

/// <summary>Mangrove propagules and similar: blocks below leaves, kept apart by an exclusion box.</summary>
internal sealed class AttachedToLeavesDecorator(FeatureRegistry registry, JsonElement json) : TreeDecorator
{
    private readonly float probability = json.GetProperty("probability").GetSingle();
    private readonly int exclusionRadiusXz = json.GetProperty("exclusion_radius_xz").GetInt32();
    private readonly int exclusionRadiusY = json.GetProperty("exclusion_radius_y").GetInt32();
    private readonly StateProvider provider = registry.Provider(json.GetProperty("block_provider"));
    private readonly int requiredEmptyBlocks = json.GetProperty("required_empty_blocks").GetInt32();
    private readonly BlockFace[] directions = Faces(json.GetProperty("directions"));

    public override void Place(TreeDecoration context)
    {
        var excluded = new HashSet<Vector>();
        var random = context.Random;
        var leaves = context.Leaves.ToList();

        TreeDecoration.Shuffle(leaves, random);

        foreach (var leaf in leaves)
        {
            var direction = this.directions[random.NextInt(this.directions.Length)];
            var position = leaf.Offset(direction);

            if (excluded.Contains(position) || random.NextFloat() >= this.probability)
            {
                continue;
            }

            if (!this.HasRequiredEmptyBlocks(context, leaf, direction))
            {
                continue;
            }

            this.Exclude(excluded, position);

            var state = FeatureRegistry.State(this.provider, context.Level, random, position);
            context.SetBlock(position, state);
        }
    }

    private void Exclude(HashSet<Vector> excluded, Vector position)
    {
        for (var x = -this.exclusionRadiusXz; x <= this.exclusionRadiusXz; x++)
        {
            for (var y = -this.exclusionRadiusY; y <= this.exclusionRadiusY; y++)
            {
                for (var z = -this.exclusionRadiusXz; z <= this.exclusionRadiusXz; z++)
                {
                    excluded.Add(position + (x, y, z));
                }
            }
        }
    }

    private bool HasRequiredEmptyBlocks(TreeDecoration context, Vector leaf, BlockFace direction)
    {
        for (var i = 1; i <= this.requiredEmptyBlocks; i++)
        {
            if (!context.IsAir(leaf.Offset(direction, i)))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Blocks on air beside logs, one random direction per log.</summary>
internal sealed class AttachedToLogsDecorator(FeatureRegistry registry, JsonElement json) : TreeDecorator
{
    private readonly float probability = json.GetProperty("probability").GetSingle();
    private readonly StateProvider provider = registry.Provider(json.GetProperty("block_provider"));
    private readonly BlockFace[] directions = Faces(json.GetProperty("directions"));

    public override void Place(TreeDecoration context)
    {
        var random = context.Random;
        var logs = context.Logs.ToList();

        TreeDecoration.Shuffle(logs, random);

        foreach (var log in logs)
        {
            var direction = this.directions[random.NextInt(this.directions.Length)];
            var position = log.Offset(direction);

            if (random.NextFloat() <= this.probability && context.IsAir(position))
            {
                var state = FeatureRegistry.State(this.provider, context.Level, random, position);
                context.SetBlock(position, state);
            }
        }
    }
}

/// <summary>Podzol and similar: circles of the provider's block replacing dirt around the trunk's base.</summary>
internal sealed class AlterGroundDecorator(FeatureRegistry registry, JsonElement json) : TreeDecorator
{
    private readonly StateProvider provider = registry.Provider(json.GetProperty("provider"));

    public override void Place(TreeDecoration context)
    {
        var lowest = context.LowestTrunkOrRoot();

        if (lowest.Count == 0)
        {
            return;
        }

        var baseY = lowest[0].Y;

        foreach (var position in lowest)
        {
            if (position.Y != baseY)
            {
                continue;
            }

            this.PlaceCircle(context, position + (-1, 0, -1));
            this.PlaceCircle(context, position + (2, 0, -1));
            this.PlaceCircle(context, position + (-1, 0, 2));
            this.PlaceCircle(context, position + (2, 0, 2));

            for (var i = 0; i < 5; i++)
            {
                var spot = context.Random.NextInt(64);
                var x = spot % 8;
                var z = spot / 8;

                if (x == 0 || x == 7 || z == 0 || z == 7)
                {
                    this.PlaceCircle(context, position + (-3 + x, 0, -3 + z));
                }
            }
        }
    }

    private void PlaceCircle(TreeDecoration context, Vector center)
    {
        for (var dx = -2; dx <= 2; dx++)
        {
            for (var dz = -2; dz <= 2; dz++)
            {
                if (Math.Abs(dx) != 2 || Math.Abs(dz) != 2)
                {
                    this.PlaceBlockAt(context, center + (dx, 0, dz));
                }
            }
        }
    }

    private void PlaceBlockAt(TreeDecoration context, Vector position)
    {
        for (var dy = 2; dy >= -3; dy--)
        {
            var candidate = position + (0, dy, 0);
            var block = context.Level.GetBlock(candidate);

            if (context.Tags.Dirt.Contains(block.RegistryId))
            {
                var state = FeatureRegistry.State(this.provider, context.Level, context.Random, position);
                context.SetBlock(candidate, state);
                return;
            }

            if (!block.IsAir && dy < 0)
            {
                return;
            }
        }
    }
}

/// <summary>Pale oak: a pale moss patch at the trunk's base, and pale hanging moss under logs and leaves.</summary>
internal sealed class PaleMossDecorator(JsonElement json) : TreeDecorator
{
    private readonly float leavesProbability = json.GetProperty("leaves_probability").GetSingle();
    private readonly float trunkProbability = json.GetProperty("trunk_probability").GetSingle();
    private readonly float groundProbability = json.GetProperty("ground_probability").GetSingle();

    public override void Place(TreeDecoration context)
    {
        var random = context.Random;
        var shuffledLogs = context.Logs.ToList();

        TreeDecoration.Shuffle(shuffledLogs, random);

        if (shuffledLogs.Count == 0)
        {
            return;
        }

        var lowest = shuffledLogs[0];

        foreach (var log in shuffledLogs)
        {
            if (log.Y < lowest.Y)
            {
                lowest = log;
            }
        }

        if (random.NextFloat() < this.groundProbability)
        {
            var decoration = context.Context;
            var patch = decoration.Registry.Feature(JsonSerializer.SerializeToElement("minecraft:pale_moss_patch"));
            patch(decoration, random, lowest + Vector.Up);
        }

        foreach (var log in context.Logs)
        {
            if (random.NextFloat() < this.trunkProbability)
            {
                AddMossHanger(context, log + Vector.Down);
            }
        }

        foreach (var leaf in context.Leaves)
        {
            if (random.NextFloat() < this.leavesProbability)
            {
                AddMossHanger(context, leaf + Vector.Down);
            }
        }
    }

    private static void AddMossHanger(TreeDecoration context, Vector position)
    {
        if (!context.IsAir(position))
        {
            return;
        }

        var moss = BlocksRegistry.Get("minecraft:pale_hanging_moss");

        while (context.IsAir(position + Vector.Down) && !(context.Random.NextFloat() < 0.5))
        {
            context.SetBlock(position, moss.WithProperty("tip", false));
            position += Vector.Down;
        }

        context.SetBlock(position, moss.WithProperty("tip", true));
    }
}

/// <summary>Cocoa pods on the lowest three blocks of a jungle trunk.</summary>
internal sealed class CocoaDecorator(JsonElement json) : TreeDecorator
{
    private readonly float probability = json.GetProperty("probability").GetSingle();

    public override void Place(TreeDecoration context)
    {
        var random = context.Random;

        if (random.NextFloat() >= this.probability)
        {
            return;
        }

        var logs = context.Logs;

        if (logs.Count == 0)
        {
            return;
        }

        var lowestY = logs[0].Y;

        foreach (var log in logs)
        {
            if (log.Y - lowestY > 2)
            {
                continue;
            }

            foreach (var facing in FeatureHelpers.Horizontal)
            {
                if (random.NextFloat() > 0.25f)
                {
                    continue;
                }

                var opposite = Vector.Zero.Offset(facing.Opposite());
                var position = log + (opposite.X, 0, opposite.Z);

                if (!context.IsAir(position))
                {
                    continue;
                }

                var age = random.NextInt(3);
                var cocoa = BlocksRegistry.Get("minecraft:cocoa")
                    .WithProperty("age", age)
                    .WithProperty("facing", FeatureHelpers.FaceName(facing));

                context.SetBlock(position, cocoa);
            }
        }
    }
}

/// <summary>A dormant creaking heart replacing a log surrounded by logs on all six sides.</summary>
internal sealed class CreakingHeartDecorator(JsonElement json) : TreeDecorator
{
    private readonly float probability = json.GetProperty("probability").GetSingle();

    public override void Place(TreeDecoration context)
    {
        var random = context.Random;

        if (context.Logs.Count == 0 || random.NextFloat() >= this.probability)
        {
            return;
        }

        var logs = context.Logs.ToList();

        TreeDecoration.Shuffle(logs, random);

        foreach (var log in logs)
        {
            var enclosed = FeatureHelpers.Directions.All(face => context.Tags.IsLog(context.Level.GetBlock(log.Offset(face))));

            if (!enclosed)
            {
                continue;
            }

            var heart = BlocksRegistry.Get("minecraft:creaking_heart")
                .WithProperty("creaking_heart_state", "dormant")
                .WithProperty("natural", true);

            context.SetBlock(log, heart);
            return;
        }
    }
}
