using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Nbt;

namespace Obsidian.WorldData.Features;

/// <summary>
/// The obsidian pillars around the main end island, some caged in iron bars, each topped with an end crystal on bedrock
/// in fire, like vanilla's SpikeFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:end_spike")]
public sealed class EndSpikeFeature : ConfiguredFeatureBase
{
    private const int SpikeCount = 10;
    private const int SpikeDistance = 42;

    public override string Type => "minecraft:end_spike";

    /// <summary>
    /// Whether the crystals can't be destroyed (used by the dragon fight respawn).
    /// </summary>
    public bool CrystalInvulnerable { get; init; }

    /// <summary>
    /// Explicit spikes; when empty, the spikes are derived from the world seed like vanilla.
    /// </summary>
    public required ImmutableArray<EndSpike> Spikes { get; init; }

    /// <summary>
    /// Where the crystals' beams point, if anywhere.
    /// </summary>
    public Vector? CrystalBeamTarget { get; init; }

    private static IBlock Obsidian => field ??= BlocksRegistry.Get(Material.Obsidian);

    private static IBlock Air => field ??= BlocksRegistry.Get(Material.Air);

    private static IBlock Bedrock => field ??= BlocksRegistry.Get(Material.Bedrock);

    private static IBlock Fire => field ??= BlocksRegistry.Get(Material.Fire);

    private static IBlock IronBars => field ??= BlocksRegistry.Get(Material.IronBars);

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin))
            return false;

        ReadOnlySpan<EndSpike> spikes = this.Spikes.Length > 0 ? this.Spikes.AsSpan() : GetSpikesForLevel(level.Seed);
        foreach (var spike in spikes)
        {
            if (origin.X >> 4 == spike.CenterX >> 4 && origin.Z >> 4 == spike.CenterZ >> 4)
                this.PlaceSpike(level, random, spike);
        }

        return true;
    }

    /// <summary>
    /// Vanilla <c>SpikeFeature.getSpikesForLevel</c>: ten spikes in a circle with heights shuffled by the world seed.
    /// </summary>
    internal static EndSpike[] GetSpikesForLevel(long seed)
    {
        var key = new LegacyRandomSource(seed).NextLong() & 65535L;
        var order = FeatureHelpers.ShuffledCopy(Enumerable.Range(0, SpikeCount), new LegacyRandomSource(key));
        var spikes = new EndSpike[SpikeCount];

        for (var i = 0; i < SpikeCount; i++)
        {
            var angle = 2.0 * (-Math.PI + Math.PI / 10 * i);
            var size = order[i];
            spikes[i] = new EndSpike
            {
                CenterX = Mth.Floor(SpikeDistance * Math.Cos(angle)),
                CenterZ = Mth.Floor(SpikeDistance * Math.Sin(angle)),
                Radius = 2 + size / 3,
                Height = 76 + size * 3,
                Guarded = size == 1 || size == 2
            };
        }

        return spikes;
    }

    private void PlaceSpike(IWorldGenLevel level, IRandomSource random, EndSpike spike)
    {
        var radius = spike.Radius;
        var min = new Vector(spike.CenterX - radius, level.MinY, spike.CenterZ - radius);
        var max = new Vector(spike.CenterX + radius, spike.Height + 10, spike.CenterZ + radius);

        foreach (var position in FeatureHelpers.BetweenClosed(min, max))
        {
            double dx = position.X - spike.CenterX;
            double dz = position.Z - spike.CenterZ;
            if (dx * dx + dz * dz <= radius * radius + 1 && position.Y < spike.Height)
                level.SetBlock(position, Obsidian);
            else if (position.Y > 65)
                level.SetBlock(position, Air);
        }

        if (spike.Guarded)
        {
            for (var dx = -2; dx <= 2; dx++)
            {
                for (var dz = -2; dz <= 2; dz++)
                {
                    for (var dy = 0; dy <= 3; dy++)
                    {
                        var edgeX = Math.Abs(dx) == 2;
                        var edgeZ = Math.Abs(dz) == 2;
                        var top = dy == 3;
                        if (!edgeX && !edgeZ && !top)
                            continue;

                        var alongX = dx == -2 || dx == 2 || top;
                        var alongZ = dz == -2 || dz == 2 || top;
                        var bars = IronBars
                            .WithProperty("north", alongX && dz != -2)
                            .WithProperty("south", alongX && dz != 2)
                            .WithProperty("west", alongZ && dx != -2)
                            .WithProperty("east", alongZ && dx != 2);
                        level.SetBlock(new Vector(spike.CenterX + dx, spike.Height + dy, spike.CenterZ + dz), bars);
                    }
                }
            }
        }

        // The crystal stands on bedrock in a fire block; its yaw is the only random draw.
        var crystal = new Vector(spike.CenterX, spike.Height + 1, spike.CenterZ);
        var data = new NbtCompound();
        if (this.CrystalBeamTarget is not null)
            data.Add(new NbtArray<int>("beam_target", [this.CrystalBeamTarget.Value.X, this.CrystalBeamTarget.Value.Y, this.CrystalBeamTarget.Value.Z]));
        if (this.CrystalInvulnerable)
            data.Add(new NbtTag<bool>("Invulnerable", true));

        level.AddEntity(new GeneratedEntity("minecraft:end_crystal", new VectorD(crystal.X + 0.5, crystal.Y, crystal.Z + 0.5),
            random.NextFloat() * 360.0f) { Data = data });
        level.SetBlock(crystal + Vector.Down, Bedrock);
        level.SetBlock(crystal, Fire);
    }
}

/// <summary>
/// One end spike, like vanilla's <c>SpikeFeature.EndSpike</c> (JSON keys <c>centerX</c>, <c>centerZ</c>, <c>radius</c>,
/// <c>height</c>, <c>guarded</c>).
/// </summary>
public sealed class EndSpike
{
    public int CenterX { get; init; }

    public int CenterZ { get; init; }

    public int Radius { get; init; }

    public int Height { get; init; }

    /// <summary>
    /// Whether the top is caged in iron bars.
    /// </summary>
    public bool Guarded { get; init; }
}
