using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData;

/// <summary>
/// What open and closed eyeblossoms share, planted or potted: which state the time of day asks for, and the trail
/// particle and sounds of a change.
/// </summary>
internal static class Eyeblossoms
{
    // Vanilla's EyeblossomBlock.Type particle colours, as RGB.
    private const int OpenColor = 16545810;

    private const int ClosedColor = 6250335;

    /// <summary>Whether <paramref name="block"/> is an open eyeblossom, planted or potted.</summary>
    public static bool IsOpen(IBlock block) =>
        block.Is(BlockIds.OpenEyeblossom) || block.Is(BlockIds.PottedOpenEyeblossom);

    /// <summary>The sound of a change to <paramref name="open"/>: the long one on a random tick.</summary>
    public static string Sound(bool open, bool randomTick) => (open, randomTick) switch
    {
        (true, true) => "minecraft:block.eyeblossom.open_long",
        (true, false) => "minecraft:block.eyeblossom.open",
        (false, true) => "minecraft:block.eyeblossom.close_long",
        (false, false) => "minecraft:block.eyeblossom.close"
    };

    /// <summary>
    /// Vanilla's <c>EyeblossomBlock.Type.spawnTransformParticle</c>: a trail from the block's centre towards a point
    /// above it, in the colour of the state it changed to.
    /// </summary>
    /// <remarks>
    /// The four draws, and their order (the lifetime, then the velocity's x, y and z), are vanilla's: they are part of
    /// the level's random sequence.
    /// </remarks>
    public static void SpawnTransformParticle(AbstractLevel level, Vector position, bool open, IRandomSource random)
    {
        var start = (VectorD)position + 0.5;
        var lifetime = 0.5 + random.NextDouble();
        var x = random.NextDouble() - 0.5;
        var y = random.NextDouble() + 1.0;
        var z = random.NextDouble() - 0.5;
        var target = start + new VectorD(x * lifetime, y * lifetime, z * lifetime);
        var particle = new TrailParticle(target, open ? OpenColor : ClosedColor, (int)(20.0 * lifetime));

        level.SpawnParticles(particle, start);
    }
}

/// <summary>
/// Vanilla's <c>EyeblossomBlock</c> <c>randomTick</c> and <c>tick</c>: an eyeblossom that the time of day (the
/// <c>eyeblossom_open</c> attribute) wants the other way turns, and schedules every eyeblossom of its old state within
/// 3 blocks across and 2 up or down to follow 5 to 10 ticks for each block of distance later, so a field turns in a
/// wave. A random tick plays the long sound, a scheduled tick the short one.
/// </summary>
internal sealed class EyeblossomBehavior : IRandomTickBehavior, IScheduledTickBehavior
{
    // Vanilla's EYEBLOSSOM_XZ_RANGE and EYEBLOSSOM_Y_RANGE.
    private const int Across = 3;

    private const int Up = 2;

    public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
    {
        if (!TryTurn(level, position, block, random))
            return;

        var sound = Eyeblossoms.Sound(!Eyeblossoms.IsOpen(block), randomTick: true);
        LevelSounds.PlayAtBlock(level, position, sound);
    }

    public void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random)
    {
        var concrete = (AbstractLevel)level.Level;
        if (!TryTurn(concrete, position, block, random))
            return;

        var sound = Eyeblossoms.Sound(!Eyeblossoms.IsOpen(block), randomTick: false);
        LevelSounds.PlayAtBlock(concrete, position, sound);
    }

    private static bool TryTurn(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
    {
        var open = Eyeblossoms.IsOpen(block);
        if (level.EyeblossomOpen(position, open) == open)
            return false;

        var turned = BlocksRegistry.GetDefault(open ? BlockIds.ClosedEyeblossom : BlockIds.OpenEyeblossom);
        level.BlockUpdates.SetBlock(position, turned);
        level.EmitGameEvent(MobGameEvent.BlockChange, (VectorD)position + 0.5, affectedBlock: block);
        Eyeblossoms.SpawnTransformParticle(level, position, !open, random);

        var min = position + new Vector(-Across, -Up, -Across);
        var max = position + new Vector(Across, Up, Across);
        foreach (var neighbor in ScanOrder(level, min, max))
        {
            if (!level.BlockUpdates.GetBlock(neighbor).IsSameState(block))
                continue;

            // Vanilla's nextIntBetweenInclusive((int)(distance * 5), (int)(distance * 10)): one draw per eyeblossom,
            // in the scan's order.
            var offset = neighbor - position;
            var distance = Math.Sqrt(offset.X * offset.X + offset.Y * offset.Y + offset.Z * offset.Z);
            var delay = random.NextIntBetweenInclusive((int)(distance * 5.0), (int)(distance * 10.0));
            level.BlockUpdates.ScheduleTick(neighbor, block, delay);
        }

        return true;
    }

    /// <summary>
    /// The positions of the box from <paramref name="min"/> to <paramref name="max"/> in the order vanilla's block
    /// scan (<c>BlockScanUtils.sectionBasedScan</c>) visits them: chunk columns by x then z, each column's sections
    /// from the bottom of the build height up, and each section's blocks by y, then z, then x.
    /// </summary>
    /// <remarks>
    /// The order is vanilla's because each eyeblossom found draws its delay from the level's random in turn.
    /// </remarks>
    private static IEnumerable<Vector> ScanOrder(AbstractLevel level, Vector min, Vector max)
    {
        var minSectionY = Math.Max(level.MinY >> 4, min.Y >> 4);
        var maxSectionY = Math.Min((level.MinY + level.Height - 1) >> 4, max.Y >> 4);
        for (var sectionX = min.X >> 4; sectionX <= max.X >> 4; sectionX++)
        {
            for (var sectionZ = min.Z >> 4; sectionZ <= max.Z >> 4; sectionZ++)
            {
                for (var sectionY = minSectionY; sectionY <= maxSectionY; sectionY++)
                {
                    foreach (var position in SectionOrder(sectionX, sectionY, sectionZ, min, max))
                        yield return position;
                }
            }
        }
    }

    private static IEnumerable<Vector> SectionOrder(int sectionX, int sectionY, int sectionZ, Vector min, Vector max)
    {
        var fromY = Math.Max(sectionY * 16, min.Y);
        var toY = Math.Min(sectionY * 16 + 15, max.Y);
        var fromZ = Math.Max(sectionZ * 16, min.Z);
        var toZ = Math.Min(sectionZ * 16 + 15, max.Z);
        var fromX = Math.Max(sectionX * 16, min.X);
        var toX = Math.Min(sectionX * 16 + 15, max.X);
        for (var y = fromY; y <= toY; y++)
        {
            for (var z = fromZ; z <= toZ; z++)
            {
                for (var x = fromX; x <= toX; x++)
                    yield return new Vector(x, y, z);
            }
        }
    }
}

/// <summary>
/// Vanilla's <c>FlowerPotBlock.randomTick</c>, which only potted eyeblossoms take: a potted eyeblossom the time of
/// day wants the other way turns, with the trail particle and the long sound, but no game event and no neighbours.
/// </summary>
internal sealed class PottedEyeblossomRandomTick : IRandomTickBehavior
{
    public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
    {
        if (!block.Is(BlockIds.PottedOpenEyeblossom) && !block.Is(BlockIds.PottedClosedEyeblossom))
            return;

        var open = Eyeblossoms.IsOpen(block);
        if (level.EyeblossomOpen(position, open) == open)
            return;

        var turned = open ? BlockIds.PottedClosedEyeblossom : BlockIds.PottedOpenEyeblossom;
        level.BlockUpdates.SetBlock(position, BlocksRegistry.GetDefault(turned));
        Eyeblossoms.SpawnTransformParticle(level, position, !open, random);
        LevelSounds.PlayAtBlock(level, position, Eyeblossoms.Sound(!open, randomTick: true));
    }
}

/// <summary>
/// Vanilla's <c>TrailParticleOption</c>: a particle that moves to <see cref="Target"/> over <see cref="Duration"/>
/// ticks, in an RGB <see cref="Color"/>.
/// </summary>
internal sealed class TrailParticle(VectorD target, int color, int duration) : ParticleData
{
    public VectorD Target { get; } = target;

    public int Color { get; } = color;

    public int Duration { get; } = duration;

    public override ParticleType ParticleType => ParticleType.Trail;

    // Vanilla's STREAM_CODEC: the target as three doubles, the colour as an int, the duration as a VarInt.
    public override void Write(INetStreamWriter writer)
    {
        writer.WriteDouble(this.Target.X);
        writer.WriteDouble(this.Target.Y);
        writer.WriteDouble(this.Target.Z);
        writer.WriteInt(this.Color);
        writer.WriteVarInt(this.Duration);
    }
}

internal static partial class RandomTickBehaviors
{
    internal static IRandomTickBehavior EyeblossomBlock { get; } = new EyeblossomBehavior();

    internal static IRandomTickBehavior FlowerPotBlock { get; } = new PottedEyeblossomRandomTick();
}
