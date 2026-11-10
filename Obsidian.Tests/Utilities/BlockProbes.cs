using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Configuration;
using Obsidian.API.Registries;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.ChunkData;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Utilities;
using Obsidian.WorldData;
using Obsidian.WorldData.Fluids;
using Obsidian.WorldData.Generators;
using Xunit;

namespace Obsidian.Tests.Utilities;

/// <summary>
/// Replays the cases of <c>HatchingFixtures.java</c>, which runs a block's own routines against a block view and
/// records what the level was asked to do: the draws, the blocks after, the ticks scheduled, the sounds, the level
/// events and the entities added.
/// </summary>
/// <remarks>
/// The cases share one server: each is moved <see cref="CaseSpacing"/> blocks along x from the last, so that what one
/// left behind (blocks, ticks, entities) is out of the next one's reach. A listener stands at each case's origin while
/// it runs and has every chunk, so it hears the case's sounds and sees its level events.
/// </remarks>
internal sealed class BlockProbes : IAsyncDisposable
{
    /// <summary>How far apart along x the cases run.</summary>
    public const int CaseSpacing = 4;

    // The chunks along x the cases reach, with one more on each side, so that neighbours are always loaded.
    private const int Chunks = 8;

    private readonly RealServerContext context;
    private readonly Dictionary<string, Listener> listeners = [];

    private BlockProbes(RealServerContext context)
    {
        this.context = context;
    }

    public WorldData.World World => this.context.World;

    /// <summary>
    /// A server whose overworld and the other <paramref name="dimensions"/> have empty, complete chunks where the
    /// cases run, random ticks and mob spawning off.
    /// </summary>
    public static async Task<BlockProbes> CreateAsync(string name, params string[] dimensions)
    {
        var context = TestServerFactory.CreateServer($"{name}-{Guid.NewGuid():N}");
        var probes = new BlockProbes(context);
        var world = context.World;
        world.LevelData.GameRules["random_tick_speed"] = "0";
        world.LevelData.GameRules["spawn_mobs"] = "false";

        await probes.AddLevelAsync(world);
        foreach (var dimension in dimensions)
            await probes.AddLevelAsync(probes.AddDimension(dimension));

        return probes;
    }

    /// <summary>The level of the dimension a case names, the overworld by default.</summary>
    public AbstractLevel Level(JsonElement probe)
    {
        var name = probe.TryGetProperty("dimension", out var dimension)
            ? dimension.GetString()!
            : "minecraft:overworld";

        return this.Level(name);
    }

    /// <summary>The level of the dimension <paramref name="name"/>.</summary>
    public AbstractLevel Level(string name) => this.listeners[name].Level;

    /// <summary>
    /// Moves the level's listener to <paramref name="origin"/>, forgets what it heard and starts recording the blocks
    /// the level stores, with their flags, in order.
    /// </summary>
    public void Listen(AbstractLevel level, Vector origin)
    {
        var listener = this.listeners[level.DimensionName];
        listener.Player.Position = (VectorD)origin + new VectorD(0.5, 0, 0.5);
        listener.Client.ClearPackets();
        listener.Writes.Clear();
        level.Fluids.BlockStored = (position, block, flags) => listener.Writes.Add(new Write(position, block, flags));
    }

    /// <summary>
    /// Removes from <paramref name="differences"/> the ones <paramref name="expected"/> lists for the case, which are
    /// known and explained where they're listed, and adds a difference for each listed one that didn't occur, so that
    /// a gap closing fails the replay until its entry is removed.
    /// </summary>
    public static void ExpectKnown(List<string> differences, IReadOnlyList<string> expected)
    {
        foreach (var difference in expected)
        {
            if (!differences.Remove(difference))
                differences.Add($"expected difference no longer occurs: {difference}");
        }
    }

    /// <summary>
    /// Sets each of the case's <c>setup</c> blocks, moved by <paramref name="offset"/>, without updates, as the probe's
    /// block view holds them.
    /// </summary>
    public static void Setup(AbstractLevel level, JsonElement probe, Vector offset)
    {
        foreach (var placement in probe.GetProperty("setup").EnumerateArray())
        {
            var state = ParseState(placement.GetProperty("state").GetString()!);
            var position = Position(placement.GetProperty("pos")) + offset;
            level.BlockUpdates.SetBlock(position, state, BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
        }
    }

    /// <summary>
    /// Compares what Obsidian did in the case's area with what vanilla recorded, adding each difference to
    /// <paramref name="differences"/>.
    /// </summary>
    public void Compare(
        List<string> differences,
        JsonElement probe,
        AbstractLevel level,
        Vector offset,
        RecordingRandom random)
    {
        CompareDraws(differences, probe.GetProperty("draws"), random.Draws);
        this.CompareWrites(differences, probe.GetProperty("writes"), level, offset);
        CompareBlocks(differences, probe.GetProperty("after"), level, offset);
        CompareTicks(differences, probe.GetProperty("scheduled"), level, offset);
        this.CompareSounds(differences, probe.GetProperty("sounds"), level, offset);
        this.CompareLevelEvents(differences, probe.GetProperty("levelEvents"), level, offset);
    }

    /// <summary>A block state as the probes write it: its id, then its properties in brackets.</summary>
    public static IBlock ParseState(string text)
    {
        var bracket = text.IndexOf('[');
        if (bracket < 0)
            return BlockStateProperties.GetState(text, new Dictionary<string, string>());

        var properties = text[(bracket + 1)..^1].Split(',')
            .Select(property => property.Split('='))
            .ToDictionary(pair => pair[0], pair => pair[1]);

        return BlockStateProperties.GetState(text[..bracket], properties);
    }

    public static Vector Position(JsonElement array) =>
        new(array[0].GetInt32(), array[1].GetInt32(), array[2].GetInt32());

    public static VectorD Point(JsonElement array) =>
        new(array[0].GetDouble(), array[1].GetDouble(), array[2].GetDouble());

    public static void Expect<T>(List<string> differences, string what, T vanilla, T obsidian)
    {
        if (!EqualityComparer<T>.Default.Equals(vanilla, obsidian))
            differences.Add($"{what}: vanilla {vanilla}, Obsidian {obsidian}");
    }

    public static string Format(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    public async ValueTask DisposeAsync() => await this.context.DisposeAsync();

    private static void CompareDraws(List<string> differences, JsonElement expected, List<Draw> actual)
    {
        var draws = expected.EnumerateArray().Select(Draw.Parse).ToList();
        if (!draws.SequenceEqual(actual))
            differences.Add($"draws: vanilla [{string.Join(", ", draws)}], Obsidian [{string.Join(", ", actual)}]");
    }

    // The blocks the level stored since the case began, in order, with every flag, against vanilla's setBlock calls.
    private void CompareWrites(List<string> differences, JsonElement expected, AbstractLevel level, Vector offset)
    {
        var vanilla = expected.EnumerateArray()
            .Select(write => Write.Describe(
                Position(write.GetProperty("pos")) + offset,
                ParseState(write.GetProperty("state").GetString()!),
                write.GetProperty("flags").GetInt32()))
            .ToList();

        var obsidian = this.listeners[level.DimensionName].Writes
            .Select(write => Write.Describe(write.Position, write.Block, (int)write.Flags))
            .ToList();

        if (!vanilla.SequenceEqual(obsidian))
            differences.Add($"writes: vanilla [{string.Join(", ", vanilla)}], Obsidian [{string.Join(", ", obsidian)}]");
    }

    private static void CompareBlocks(List<string> differences, JsonElement expected, AbstractLevel level, Vector offset)
    {
        foreach (var placement in expected.EnumerateArray())
        {
            var position = Position(placement.GetProperty("pos")) + offset;
            var state = placement.GetProperty("state").GetString()!;
            var actual = level.BlockUpdates.GetBlock(position);
            if (!ParseState(state).IsSameState(actual))
                differences.Add($"{position}: vanilla {state}, Obsidian {actual}");
        }
    }

    // The block and fluid ticks pending within reach of the case, against those vanilla scheduled.
    private static void CompareTicks(List<string> differences, JsonElement expected, AbstractLevel level, Vector offset)
    {
        var vanilla = expected.EnumerateArray()
            .Select(tick => $"{Position(tick.GetProperty("pos")) + offset} {tick.GetProperty("type").GetString()} "
                + $"in {tick.GetProperty("delay").GetInt32()}")
            .Order()
            .ToList();

        var chunk = (Chunk)level.GetLoadedChunk(offset.X >> 4, offset.Z >> 4)!;
        var blockTicks = chunk.BlockTicks.Pack()
            .Where(tick => IsNear(tick.Position, offset))
            .Select(tick => $"{tick.Position} {tick.Block} in {tick.Delay}");
        var fluidTicks = chunk.FluidTicks.Pack()
            .Where(tick => IsNear(tick.Position, offset))
            .Select(tick => $"{tick.Position} {tick.Type.Name()} in {tick.Delay}");
        var obsidian = blockTicks.Concat(fluidTicks).Order().ToList();

        if (!vanilla.SequenceEqual(obsidian))
            differences.Add($"ticks: vanilla [{string.Join(", ", vanilla)}], Obsidian [{string.Join(", ", obsidian)}]");
    }

    private void CompareSounds(List<string> differences, JsonElement expected, AbstractLevel level, Vector offset)
    {
        var vanilla = expected.EnumerateArray()
            .Select(sound => Sound(
                sound.GetProperty("sound").GetString()!,
                Point(sound.GetProperty("pos")) + (VectorD)offset,
                sound.GetProperty("volume").GetSingle(),
                sound.GetProperty("pitch").GetSingle()))
            .ToList();

        var obsidian = this.listeners[level.DimensionName].Client.SentPackets
            .OfType<SoundPacket>()
            .Select(sound => Sound(
                sound.SoundLocation,
                new VectorD(sound.SoundPosition.X / 8d, sound.SoundPosition.Y / 8d, sound.SoundPosition.Z / 8d),
                sound.Volume,
                sound.Pitch))
            .ToList();

        if (!vanilla.SequenceEqual(obsidian))
            differences.Add($"sounds: vanilla [{string.Join(", ", vanilla)}], Obsidian [{string.Join(", ", obsidian)}]");
    }

    // A sound as both sides can tell it: its id, where (to the precision the packet carries), its volume and pitch.
    private static string Sound(string id, VectorD position, float volume, float pitch)
    {
        var x = Math.Floor(position.X * 8) / 8;
        var y = Math.Floor(position.Y * 8) / 8;
        var z = Math.Floor(position.Z * 8) / 8;

        return $"{id} at ({x}, {y}, {z}) volume {Format(volume)} pitch {Format(pitch)}";
    }

    private void CompareLevelEvents(List<string> differences, JsonElement expected, AbstractLevel level, Vector offset)
    {
        var vanilla = expected.EnumerateArray()
            .Select(levelEvent => $"{levelEvent.GetProperty("type").GetInt32()} at "
                + $"{Position(levelEvent.GetProperty("pos")) + offset} data {levelEvent.GetProperty("data").GetInt32()}")
            .ToList();

        var obsidian = this.listeners[level.DimensionName].Client.SentPackets
            .OfType<LevelEventPacket>()
            .Select(levelEvent => $"{levelEvent.Type} at {levelEvent.Position} data {levelEvent.Data}")
            .ToList();

        if (!vanilla.SequenceEqual(obsidian))
        {
            differences.Add(
                $"level events: vanilla [{string.Join(", ", vanilla)}], Obsidian [{string.Join(", ", obsidian)}]");
        }
    }

    private static bool IsNear(Vector position, Vector origin) =>
        Math.Abs(position.X - origin.X) < CaseSpacing / 2 + 1
        && Math.Abs(position.Z - origin.Z) <= CaseSpacing;

    private WorldData.Dimension AddDimension(string identifier)
    {
        var world = this.context.World;
        var dimension = new WorldData.Dimension(
            NullLogger<WorldData.Dimension>.Instance,
            this.context.Broadcaster,
            new TestOptionsMonitor<ServerConfiguration>(world.Configuration),
            this.context.EventDispatcher,
            new EmptyWorldGenerator(),
            identifier,
            world);
        dimension.Initialize(CodecRegistry.Dimensions.All[identifier]);
        world.RegisterDimension(dimension);

        return dimension;
    }

    // Empty, complete chunks where the cases run, and a listener that has them all.
    private async Task AddLevelAsync(AbstractLevel level)
    {
        for (var chunkX = -1; chunkX < Chunks; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
            {
                var chunk = new Chunk(chunkX, chunkZ, level.MinY, level.Height, ChunkGenStage.full);
                level.LoadRegionByChunk(chunkX, chunkZ).SetChunk(chunk);
            }
        }

        var player = TestServerFactory.CreateConnectedPlayer(this.context.Server, this.context.World,
            $"Listener{this.listeners.Count}");
        player.Level = level;
        for (var chunkX = -1; chunkX < Chunks; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
            {
                await level.GetChunkAsync(chunkX, chunkZ, scheduleGeneration: false);
                player.LoadedChunks.Add(NumericsHelper.IntsToLong(chunkX, chunkZ));
            }
        }

        level.Players.TryAdd(player.Uuid, player);
        this.listeners[level.DimensionName] = new Listener(level, player, Assert.IsType<TestClient>(player.Client));
    }

    private sealed record Listener(AbstractLevel Level, Entities.Player Player, TestClient Client)
    {
        public List<Write> Writes { get; } = [];
    }

    // A block the level stored, with its flags.
    private sealed record Write(Vector Position, IBlock Block, BlockUpdateFlags Flags)
    {
        // The state by its id and its block's name, which both sides share.
        public static string Describe(Vector position, IBlock block, int flags) =>
            $"{position} {block.UnlocalizedName}#{block.GetHashCode()} flags {flags}";
    }
}

/// <summary>A draw as the probes record it: the call, its bound for <c>nextInt(bound)</c>, and its result.</summary>
internal sealed record Draw(string Call, int? Bound, string Value)
{
    public static Draw Parse(JsonElement draw)
    {
        var call = draw.GetProperty("call").GetString()!;
        int? bound = draw.TryGetProperty("bound", out var given) ? given.GetInt32() : null;
        var value = draw.GetProperty("value");
        var text = call switch
        {
            "nextBoolean" => value.GetBoolean().ToString(),
            "nextFloat" => BlockProbes.Format(value.GetSingle()),
            "nextDouble" => value.GetDouble().ToString("R", CultureInfo.InvariantCulture),
            _ => value.GetInt64().ToString(CultureInfo.InvariantCulture)
        };

        return new Draw(call, bound, text);
    }

    public override string ToString() => Bound is int bound ? $"{Call}({bound})={Value}" : $"{Call}={Value}";
}

/// <summary>A legacy random that records each draw the way the probes do.</summary>
internal sealed class RecordingRandom(long seed) : IRandomSource
{
    private readonly LegacyRandomSource source = new(seed);

    public List<Draw> Draws { get; } = [];

    public IRandomSource Fork() => throw new NotSupportedException();

    public IPositionalRandomFactory ForkPositional() => throw new NotSupportedException();

    public void SetSeed(long seed) => throw new NotSupportedException();

    public int NextInt() => this.Record("nextInt", null, this.source.NextInt());

    public int NextInt(int bound) => this.Record("nextInt", bound, this.source.NextInt(bound));

    // Vanilla's RandomSource.nextInt(origin, bound): one nextInt of the range's width.
    public int NextInt(int origin, int bound) => origin + this.NextInt(bound - origin);

    public int NextIntBetweenInclusive(int min, int max) => this.NextInt(max - min + 1) + min;

    public long NextLong()
    {
        var value = this.source.NextLong();
        this.Draws.Add(new Draw("nextLong", null, value.ToString(CultureInfo.InvariantCulture)));
        return value;
    }

    public bool NextBoolean()
    {
        var value = this.source.NextBoolean();
        this.Draws.Add(new Draw("nextBoolean", null, value.ToString()));
        return value;
    }

    public float NextFloat()
    {
        var value = this.source.NextFloat();
        this.Draws.Add(new Draw("nextFloat", null, BlockProbes.Format(value)));
        return value;
    }

    public double NextDouble()
    {
        var value = this.source.NextDouble();
        this.Draws.Add(new Draw("nextDouble", null, value.ToString("R", CultureInfo.InvariantCulture)));
        return value;
    }

    public double NextGaussian() => throw new NotSupportedException();

    public double Triangle(double mode, double deviation) => throw new NotSupportedException();

    public float Triangle(float mode, float deviation) => throw new NotSupportedException();

    public void ConsumeCount(int count) => throw new NotSupportedException();

    private int Record(string call, int? bound, int value)
    {
        this.Draws.Add(new Draw(call, bound, value.ToString(CultureInfo.InvariantCulture)));
        return value;
    }
}
