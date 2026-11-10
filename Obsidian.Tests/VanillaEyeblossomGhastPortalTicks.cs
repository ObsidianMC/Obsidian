using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.ChunkData;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.Utilities;
using Obsidian.WorldData;
using Xunit;
using Xunit.Abstractions;

namespace Obsidian.Tests;

/// <summary>
/// Replays <c>eyeblossom_ghast_portal_ticks.json</c>: vanilla's eyeblossoms, potted eyeblossoms, dried ghasts and
/// nether portals ticking, from <c>Java/EyeblossomGhastPortalFixtures.java</c>
/// (<c>CaptureFixtures=eyeblossom_ghast_portal</c>).
/// </summary>
public sealed class VanillaEyeblossomGhastPortalTicks(ITestOutputHelper output)
{
    // Where the listener stands: this far from the case's origin, within range of its sounds and particles, or, for a
    // case with no player near, more than 128 blocks from the centre of the chunk the cases use.
    private static readonly VectorD nearListener = new(4, 6, 4);

    private static readonly VectorD farListener = new(4, 70, 300);

    private static readonly JsonElement fixture = LoadFixture();

    // Obsidian asks whether a zombified piglin may stand on the floor below a portal with its spawner's own floor rule
    // (a solid floor that isn't a liquid), where vanilla asks the floor's isValidSpawn: its top face must be sturdy,
    // its light under 14, and glass never allows it. In these cases vanilla draws once and spawns nothing; Obsidian
    // spawns the piglin vanilla spawns over obsidian with the same seed, which FloorReference records, with its
    // draws and sound. Each must fail once the rule is vanilla's
    // (docs/known-gaps/eyeblossoms-dried-ghasts-and-portal-piglins.md).
    private static readonly HashSet<string> spawnFloorDifferences =
        ["portal_over_glowstone", "portal_over_glass", "portal_over_bottom_slab"];

    private const string FloorReference = "portal_spawns_on_normal";

    /// <summary>
    /// Each case runs through Obsidian's dispatch (the owner the generator binds the block's class to, or a live
    /// block change) in a fresh world holding only the case's blocks, with the case's seed as the level's random. The
    /// draws, the writes with their flags, the blocks, the scheduled ticks, the level events, sounds, particles and
    /// game events, the entities added and the blocks destroyed must be vanilla's.
    /// </summary>
    [Fact]
    public async Task ReplayEyeblossomGhastPortalReference()
    {
        Assert.Equal(ServerConstants.ProtocolDescription, fixture.GetProperty("version").GetString());

        var timer = Stopwatch.StartNew();
        var cases = fixture.GetProperty("cases").EnumerateArray().ToArray();
        await FixtureCases.AssertAllAsync(cases, Name, ReplayAsync);

        output.WriteLine($"Replayed {cases.Length} eyeblossom, dried ghast and portal cases in "
            + $"{timer.Elapsed.TotalSeconds:F2}s.");
    }

    private static string Name(JsonElement probe) => probe.GetProperty("name").GetString()!;

    private static async Task ReplayAsync(JsonElement probe)
    {
        await using var context = TestServerFactory.CreateServer($"eyeblossom-ghast-portal-{Guid.NewGuid():N}");
        var world = context.World;
        if (probe.TryGetProperty("dimension", out var dimension))
            world.Initialize(CodecRegistry.Dimensions.All[dimension.GetString()!]);

        Configure(probe, world);
        await AddEmptyChunksAsync(world);

        // A player with the nine chunks, who gets the packets sent there (queued ones go to the server's players).
        var near = !probe.TryGetProperty("playerNear", out var playerNear) || playerNear.GetBoolean();
        var listener = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Listener",
            near ? (VectorD)Position(probe.GetProperty("origin")) + nearListener : farListener);
        world.Players.TryAdd(listener.Uuid, listener);
        context.Server.OnlinePlayers.TryAdd(listener.Uuid, listener);
        foreach (var chunk in Chunks(world))
            listener.LoadedChunks.Add(NumericsHelper.IntsToLong(chunk.X, chunk.Z));

        var client = Assert.IsType<TestClient>(listener.Client);

        // The case's blocks, without updates, as vanilla's block view holds them, and the ticks already pending.
        foreach (var placement in probe.GetProperty("setup").EnumerateArray())
        {
            var state = VanillaSulfurBlocks.ParseState(placement.GetProperty("state").GetString()!);
            world.BlockUpdates.SetBlock(Position(placement.GetProperty("pos")), state,
                BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
        }

        foreach (var tick in Pending(probe))
        {
            var block = BlocksRegistry.Get(tick.GetProperty("type").GetString()!);
            var delay = tick.GetProperty("delay").GetInt32();
            world.BlockUpdates.ScheduleTick(Position(tick.GetProperty("pos")), block, delay);
        }

        var seed = probe.TryGetProperty("seed", out var given) ? given.GetInt64() : 0;
        var random = new RecordingRandom(seed);
        world.GrowthRandom = random;

        var observed = Observe(probe, world);
        var differences = observed.Differences;
        var origin = Position(probe.GetProperty("origin"));
        var call = probe.GetProperty("call").GetString();
        switch (call)
        {
            case "randomTick":
                RandomTick(differences, probe, world, origin, random);
                break;
            case "tick":
                ScheduledTick(differences, world, origin, random);
                break;
            case "set":
                var changed = VanillaSulfurBlocks.ParseState(probe.GetProperty("state").GetString()!);
                world.BlockUpdates.SetBlock(origin, changed);
                break;
            default:
                throw new InvalidOperationException($"Unknown call {call}");
        }

        // The game events the routine emitted, before a world tick would dispatch them.
        var gameEvents = world.TakeGameEvents().Select(GameEvent).ToList();

        // Keep observing through deferred destruction, including the level random used by item spawning.
        if (probe.GetProperty("destroyed").GetArrayLength() > 0)
            await world.DoWorldTickAsync();

        world.Fluids.BlockStored = null;
        world.BlockDestroyed = null;

        // What a pinned floor difference spawns is checked against the reference case's spawn.
        var spawn = SpawnReference(probe);

        CompareWrites(differences, probe, observed.Writes);
        ExpectSequence(differences, "destroyed", Destroyed(probe), observed.Destroyed);
        CompareDraws(differences, spawn, random.Draws);
        CompareBlocks(differences, probe.GetProperty("after"), world);
        CompareUnchangedBlocks(differences, probe.GetProperty("after"), world);
        CompareTicks(differences, probe, world);
        CompareEvents(differences, probe, client.SentPackets.OfType<LevelEventPacket>().ToList());
        CompareSounds(differences, spawn, client.SentPackets.ToList(), world);
        CompareParticles(differences, probe, client.SentPackets.OfType<LevelParticlesPacket>().ToList());
        CompareGameEvents(differences, probe, gameEvents);
        CompareEntities(differences, spawn, world);

        if (differences.Count > 0)
            throw new Xunit.Sdk.XunitException(string.Join("\n", differences));
    }

    // The case whose draws, sounds and entities a case expects: its own, or for a pinned floor difference, those of
    // the case where vanilla spawns over obsidian with the same seed. The pinned case's own capture must still be a
    // single missed draw, so a changed capture can't hide behind the pin.
    private static JsonElement SpawnReference(JsonElement probe)
    {
        if (!spawnFloorDifferences.Contains(Name(probe)))
            return probe;

        Assert.Single(probe.GetProperty("draws").EnumerateArray());
        Assert.Empty(probe.GetProperty("entities").EnumerateArray());
        Assert.Empty(probe.GetProperty("sounds").EnumerateArray());

        var reference = fixture.GetProperty("cases").EnumerateArray().Single(other => Name(other) == FloorReference);
        Assert.Equal(probe.GetProperty("seed").GetInt64(), reference.GetProperty("seed").GetInt64());
        Assert.Single(reference.GetProperty("entities").EnumerateArray());
        return reference;
    }

    // The case's difficulty, rules and time of day. Random ticks are off, so only the case's routine runs.
    private static void Configure(JsonElement probe, WorldData.World world)
    {
        world.LevelData.GameRules["random_tick_speed"] = "0";
        world.LevelData.GameRules["spawn_mobs"] = Flag(probe, "spawnMobs") ? "true" : "false";
        world.LevelData.GameRules["spawn_monsters"] = Flag(probe, "spawnMonsters") ? "true" : "false";

        var difficulty = probe.TryGetProperty("difficulty", out var given) ? given.GetString()! : "normal";
        world.LevelData.Difficulty = Enum.Parse<Difficulty>(difficulty, ignoreCase: true);

        var dayTime = probe.TryGetProperty("dayTime", out var time) ? time.GetInt64() : 0;
        foreach (var clock in world.LevelData.WorldClocks.States.Values)
            clock.TotalTicks = dayTime;

        // The local date vanilla saw: the case's own, or the day the fixture was first captured.
        var date = probe.TryGetProperty("date", out var day) ? day : fixture.GetProperty("capturedOn");
        world.TimeProvider = new FixedDate(DateOnly.Parse(date.GetString()!, CultureInfo.InvariantCulture));
    }

    /// <summary>A clock that reads noon of <paramref name="date"/>, in a local time zone of UTC.</summary>
    private sealed class FixedDate(DateOnly date) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => new(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
    }

    private static bool Flag(JsonElement probe, string name) =>
        !probe.TryGetProperty(name, out var value) || value.GetBoolean();

    private static IEnumerable<JsonElement> Pending(JsonElement probe) =>
        probe.TryGetProperty("pending", out var pending) ? pending.EnumerateArray() : [];

    private sealed record Observed(List<string> Differences, List<Write> Writes, List<string> Destroyed);

    // Hears the level's writes and destroyed blocks while the routine runs.
    private static Observed Observe(JsonElement probe, WorldData.World world)
    {
        var observed = new Observed([], [], []);
        var expectedPositions = probe.GetProperty("after").EnumerateArray()
            .Select(placement => Position(placement.GetProperty("pos")))
            .ToHashSet();

        world.Fluids.BlockStored = (position, block, flags) =>
        {
            observed.Writes.Add(new Write(position, block, flags));
            if (!expectedPositions.Contains(position))
            {
                observed.Differences.Add(
                    $"Unexpected write outside the case: {position} -> {block}, flags {(int)flags}");
            }
        };
        world.BlockDestroyed = (position, block, drops) =>
            observed.Destroyed.Add(Destroyed(position, block, drops));

        return observed;
    }

    // AbstractLevel.TickChunkBlocks: only a randomly ticking state ticks, through its generated owner.
    private static void RandomTick(
        List<string> differences,
        JsonElement probe,
        WorldData.World world,
        Vector origin,
        RecordingRandom random)
    {
        var block = world.BlockUpdates.GetBlock(origin);
        var ticks = BlockPhysics.IsRandomlyTicking(block.GetHashCode());
        Expect(differences, "ticks", probe.GetProperty("ticks").GetBoolean(), ticks);
        if (!ticks)
            return;

        if (Growth.VanillaRandomTick(block) is not IRandomTickBehavior behavior)
        {
            differences.Add($"{block.UnlocalizedName} has no random tick owner");
            return;
        }

        behavior.RandomTick(world, origin, block, random);
    }

    // LevelBlockUpdates.RunScheduledTick's dispatch, with the case's random: the owner of the block's tick.
    private static void ScheduledTick(
        List<string> differences,
        WorldData.World world,
        Vector origin,
        RecordingRandom random)
    {
        var block = world.BlockUpdates.GetBlock(origin);
        if (BlocksRegistry.GetTickBehavior(block.RegistryId) is not IScheduledTickBehavior behavior)
        {
            differences.Add($"{block.UnlocalizedName} has no scheduled tick owner");
            return;
        }

        behavior.Tick(world.BlockUpdates, origin, block, random);
    }

    private static void CompareDraws(List<string> differences, JsonElement probe, List<Draw> actual)
    {
        var draws = probe.GetProperty("draws").EnumerateArray().Select(Draw.Parse);
        ExpectSequence(differences, "draws", draws.Select(Text), actual.Select(Text));
    }

    private static string Text(Draw draw) => draw.ToString();

    private static void CompareBlocks(List<string> differences, JsonElement expected, WorldData.World world)
    {
        foreach (var placement in expected.EnumerateArray())
        {
            var position = Position(placement.GetProperty("pos"));
            var state = placement.GetProperty("state").GetString()!;
            var actual = world.BlockUpdates.GetBlock(position);
            if (!VanillaSulfurBlocks.ParseState(state).IsSameState(actual))
                differences.Add($"{position}: vanilla {state}, Obsidian {actual}");
        }
    }

    // The view starts empty outside setup, and after contains all setup positions. Check every loaded cell, so writes
    // that skip the observed path can't escape.
    private static void CompareUnchangedBlocks(List<string> differences, JsonElement expected, WorldData.World world)
    {
        var positions = expected.EnumerateArray()
            .Select(placement => Position(placement.GetProperty("pos")))
            .ToHashSet();
        foreach (var chunk in Chunks(world))
        {
            foreach (var section in chunk.Sections)
            {
                if (section.IsEmpty)
                    continue;

                for (var y = 0; y < 16; y++)
                {
                    for (var z = 0; z < 16; z++)
                    {
                        for (var x = 0; x < 16; x++)
                        {
                            var position = new Vector(chunk.X * 16 + x, section.YBase!.Value * 16 + y,
                                chunk.Z * 16 + z);
                            var block = section.GetBlock(x, y, z);
                            if (!positions.Contains(position) && !block.IsAir)
                                differences.Add($"Unexpected block outside the case at {position}: {block}");
                        }
                    }
                }
            }
        }
    }

    // Every block and fluid tick pending in the nine chunks, against the case's pending ticks and those vanilla
    // scheduled. Only the first request for a position and type is pending.
    private static void CompareTicks(List<string> differences, JsonElement probe, WorldData.World world)
    {
        var vanilla = Pending(probe)
            .Concat(probe.GetProperty("scheduled").EnumerateArray())
            .DistinctBy(tick => (Position(tick.GetProperty("pos")), tick.GetProperty("type").GetString()))
            .Select(tick => Tick(Position(tick.GetProperty("pos")), tick.GetProperty("type").GetString()!,
                tick.GetProperty("delay").GetInt32()))
            .ToList();

        var obsidian = new List<string>();
        foreach (var chunk in Chunks(world))
        {
            obsidian.AddRange(chunk.BlockTicks.Pack().Select(tick => Tick(tick.Position, tick.Block, tick.Delay)));
            obsidian.AddRange(chunk.FluidTicks.Pack()
                .Select(tick => Tick(tick.Position, FluidName(tick.Type), tick.Delay)));
        }

        obsidian.Sort(StringComparer.Ordinal);
        vanilla.Sort(StringComparer.Ordinal);
        ExpectSequence(differences, "ticks", vanilla, obsidian);
    }

    private static string Tick(Vector position, string type, int delay) => $"{position} {type} in {delay}";

    private static void CompareEvents(List<string> differences, JsonElement probe, List<LevelEventPacket> packets)
    {
        var vanilla = probe.GetProperty("events").EnumerateArray()
            .Select(recorded => $"{recorded.GetProperty("type").GetInt32()} at "
                + $"{Position(recorded.GetProperty("pos"))} ({recorded.GetProperty("data").GetInt32()})");
        var obsidian = packets.Select(packet => $"{packet.Type} at {packet.Position} ({packet.Data})");
        ExpectSequence(differences, "events", vanilla, obsidian);
    }

    // The sounds clients got, in order: a positional sound where the packet puts it (eighths of a block), or the
    // entity type an entity sound follows. A null pitch is vanilla's mob voice, drawn from the mob's own random: it
    // must lie in the voice's range, (-0.2, 0.2) around 1, or 1.5 for a baby.
    private static void CompareSounds(
        List<string> differences,
        JsonElement probe,
        List<IClientboundPacket> packets,
        WorldData.World world)
    {
        var babyVoice = probe.GetProperty("entities").EnumerateArray()
            .Any(entity => entity.TryGetProperty("baby", out var baby) && baby.GetBoolean());
        var voices = probe.GetProperty("sounds").EnumerateArray()
            .Select(sound => sound.GetProperty("pitch").ValueKind == JsonValueKind.Null)
            .ToList();
        var vanilla = probe.GetProperty("sounds").EnumerateArray().Select(sound =>
        {
            var pitch = sound.GetProperty("pitch");
            var pitchText = pitch.ValueKind == JsonValueKind.Null ? "voice" : Number(pitch.GetSingle());
            var where = sound.TryGetProperty("entity", out var entity)
                ? $"from {entity.GetString()}"
                : $"at {Eighths(sound.GetProperty("pos"))}";
            return $"{sound.GetProperty("sound").GetString()} {sound.GetProperty("source").GetString()} {where} "
                + $"volume {Number(sound.GetProperty("volume").GetSingle())} pitch {pitchText}";
        });

        var obsidian = new List<string>();
        foreach (var packet in packets)
        {
            switch (packet)
            {
                case SoundPacket sound:
                    var position = sound.SoundPosition;
                    var positionalPitch = Pitch(sound.Pitch, IsVoice(voices, obsidian.Count), babyVoice);
                    obsidian.Add($"{sound.SoundLocation} {CategoryName(sound.Category)} at "
                        + $"{position.X},{position.Y},{position.Z} volume {Number(sound.Volume)} "
                        + $"pitch {positionalPitch}");
                    break;
                case SoundEntityPacket sound:
                    var source = world.GetNonPlayerEntitiesInRange(VectorD.Zero, float.MaxValue)
                        .FirstOrDefault(entity => entity.EntityId == sound.EntityId);
                    var entityPitch = Pitch(sound.Pitch, IsVoice(voices, obsidian.Count), babyVoice);
                    obsidian.Add($"{sound.SoundLocation} {CategoryName(sound.Category)} from "
                        + $"{EntityName(source?.Type)} volume {Number(sound.Volume)} "
                        + $"pitch {entityPitch}");
                    break;
            }
        }

        ExpectSequence(differences, "sounds", vanilla, obsidian);
    }

    // Whether vanilla's sound at the same place in the sequence is a mob's voice, whose pitch it doesn't record.
    private static bool IsVoice(List<bool> voices, int index) => index < voices.Count && voices[index];

    // Obsidian's pitch as the comparison reads it: where vanilla's sound is a mob's voice, any pitch in the voice's
    // range reads as the voice; otherwise the pitch must be vanilla's exactly.
    private static string Pitch(float pitch, bool voice, bool baby)
    {
        var centre = baby ? 1.5f : 1f;
        if (voice && pitch > centre - 0.2f && pitch < centre + 0.2f)
            return "voice";

        return Number(pitch);
    }

    private static string Eighths(JsonElement position) =>
        $"{(int)(position[0].GetDouble() * 8)},{(int)(position[1].GetDouble() * 8)},"
        + $"{(int)(position[2].GetDouble() * 8)}";

    private static string CategoryName(SoundCategory category) => category switch
    {
        SoundCategory.Blocks => "block",
        SoundCategory.Records => "record",
        SoundCategory.Players => "player",
        _ => category.ToString().ToLowerInvariant()
    };

    private static void CompareParticles(
        List<string> differences,
        JsonElement probe,
        List<LevelParticlesPacket> packets)
    {
        var vanilla = probe.GetProperty("particles").EnumerateArray().Select(particle =>
        {
            var type = particle.GetProperty("type").GetString()!;
            var trail = particle.TryGetProperty("target", out var target)
                ? $" to {Vec(target)} colour {particle.GetProperty("color").GetInt32()} for "
                    + $"{particle.GetProperty("duration").GetInt32()}"
                : "";
            return $"{type} at {Vec(particle.GetProperty("pos"))} x{particle.GetProperty("count").GetInt32()} "
                + $"spread {Vec(particle.GetProperty("dist"))} speed {Vec(particle.GetProperty("speed"))} "
                + $"limiter {particle.GetProperty("overrideLimiter").GetBoolean()} "
                + $"always {particle.GetProperty("alwaysShow").GetBoolean()}{trail}";
        });

        var obsidian = packets.Select(packet =>
        {
            var type = $"minecraft:{Snake(packet.Data.ParticleType.ToString())}";
            var trail = packet.Data is TrailParticle data
                ? $" to {Vec(data.Target)} colour {data.Color} for {data.Duration}"
                : "";
            var speed = packet.Speed ?? new VectorF(packet.MaxSpeed, packet.MaxSpeed, packet.MaxSpeed);
            return $"{type} at {Vec(packet.Position)} x{packet.ParticleCount} "
                + $"spread {Vec(packet.Offset)} speed {Vec(speed)} "
                + $"limiter {packet.OverrideLimiter} always {packet.AlwaysShow}{trail}";
        });

        ExpectSequence(differences, "particles", vanilla, obsidian);
    }

    private static void CompareGameEvents(List<string> differences, JsonElement probe, List<string> actual)
    {
        var vanilla = probe.GetProperty("gameEvents").EnumerateArray().Select(recorded =>
        {
            var state = recorded.GetProperty("state");
            var affected = state.ValueKind == JsonValueKind.Null
                ? null
                : VanillaSulfurBlocks.ParseState(state.GetString()!).StateId().ToString(CultureInfo.InvariantCulture);
            var position = recorded.GetProperty("pos");
            return GameEvent(recorded.GetProperty("type").GetString()!,
                new VectorD(position[0].GetDouble(), position[1].GetDouble(), position[2].GetDouble()), affected);
        });

        ExpectSequence(differences, "game events", vanilla, actual);
    }

    private static string GameEvent(string type, VectorD position, string? affected) =>
        $"{type} at {Vec(position)} state {affected ?? "none"}";

    private static string GameEvent(MobVibration vibration) => GameEvent(
        $"minecraft:{Snake(vibration.Kind.ToString())}",
        vibration.Position,
        vibration.AffectedBlock?.StateId().ToString(CultureInfo.InvariantCulture));

    // Every captured entity field, and no entity vanilla didn't add. Rotations are compared as clients get them, in
    // 256ths of a turn. A head yaw vanilla records as null is the Math.random() radians a jockey chicken keeps from its
    // constructor (0 to 2pi degrees, so 0 to 4 256ths); Obsidian's must lie there too.
    private static void CompareEntities(List<string> differences, JsonElement probe, WorldData.World world)
    {
        var recorded = probe.GetProperty("entities").EnumerateArray().ToList();
        var randomHeads = recorded
            .Where(entity => entity.GetProperty("headYaw").ValueKind == JsonValueKind.Null)
            .Select(entity => entity.GetProperty("type").GetString()!)
            .ToHashSet();

        // In no particular order: the order the level lists its entities isn't vanilla's add order.
        var vanilla = recorded.Select(entity => Describe(entity)).Order(StringComparer.Ordinal);
        var obsidian = world.GetNonPlayerEntitiesInRange(VectorD.Zero, float.MaxValue)
            .OfType<Entity>()
            .Select(entity => Describe(entity, randomHeads.Contains(EntityName(entity.Type))))
            .Order(StringComparer.Ordinal);

        ExpectSequence(differences, "entities", vanilla, obsidian);
    }

    private static string Describe(JsonElement entity)
    {
        var position = entity.GetProperty("pos");
        var headYaw = entity.GetProperty("headYaw");
        var head = headYaw.ValueKind == JsonValueKind.Null ? "random" : Turn(headYaw.GetSingle()).ToString();
        var chicken = entity.TryGetProperty("variant", out var variant)
            ? $" variant {variant.GetString()} sound {entity.GetProperty("soundVariant").GetString()} "
                + $"jockey {entity.GetProperty("jockey").GetBoolean()}"
            : "";
        var age = entity.TryGetProperty("age", out var given) ? $" age {given.GetInt32()}" : "";
        var mob = "";
        if (entity.TryGetProperty("baby", out var baby))
        {
            var enchantments = entity.TryGetProperty("enchantments", out var list)
                ? list.EnumerateArray().Select(enchantment => enchantment.GetString()!)
                : [];
            var helmet = entity.TryGetProperty("head", out var worn) ? worn.GetString()! : "none";
            mob = Mob(baby.GetBoolean(), entity.GetProperty("mainHand").GetString()!,
                entity.GetProperty("enchanted").GetBoolean(), enchantments, helmet,
                entity.GetProperty("leftHanded").GetBoolean(), entity.GetProperty("canPickUpLoot").GetBoolean(),
                entity.GetProperty("vehicle").GetString() ?? "none");
        }

        return $"{entity.GetProperty("type").GetString()} at "
            + $"{Vec(new VectorD(position[0].GetDouble(), position[1].GetDouble(), position[2].GetDouble()))} "
            + $"yaw {Turn(entity.GetProperty("yaw").GetSingle())} head {head} "
            + $"pitch {Turn(entity.GetProperty("pitch").GetSingle())} "
            + $"cooldown {entity.GetProperty("portalCooldown").GetInt32()}{chicken}{age}{mob}";
    }

    private static string Describe(Entity entity, bool randomHead)
    {
        var headYaw = entity is Mob looking ? looking.LookControl.HeadYaw.Value : entity.Yaw.Value;
        var head = randomHead && headYaw <= Turn(MathF.Tau) ? "random" : headYaw.ToString();
        var chicken = entity is Chicken bird
            ? $" variant {ChickenVariants[bird.Variant]} sound {ChickenSounds[bird.SoundVariant]} "
                + $"jockey {bird.IsChickenJockey}"
            : "";
        var age = entity is AgeableMob ageable ? $" age {ageable.Age}" : "";
        var mob = "";
        if (entity is Mob living)
        {
            var hand = living.GetEquipment(EquipmentSlot.MainHand);
            var helmet = living.GetEquipment(EquipmentSlot.Helmet);
            var enchantments = (hand.GetComponent<SimpleDataComponent<ImmutableArray<Enchantment>>>(
                DataComponentType.Enchantments)?.Value ?? [])
                .Select(enchantment => $"{EnchantmentName(enchantment.Id)} {enchantment.Level}")
                .ToList();
            mob = Mob(living is IAgeable { IsBaby: true }, ItemName(hand), enchantments.Count > 0, enchantments,
                helmet.IsAir ? "none" : ItemName(helmet), living.MobBitMask.HasFlag(MobBitmask.LeftHanded),
                living.CanPickUpLoot, entity.Vehicle is null ? "none" : EntityName(entity.Vehicle.Type));
        }

        return $"{EntityName(entity.Type)} at {Vec(entity.Position)} yaw {entity.Yaw.Value} head {head} "
            + $"pitch {entity.Pitch.Value} cooldown {entity.PortalCooldownTicks}{chicken}{age}{mob}";
    }

    // A mob's fields; the enchantments sorted, as a stack's enchantments have no order that matters.
    private static string Mob(
        bool baby,
        string hand,
        bool enchanted,
        IEnumerable<string> enchantments,
        string helmet,
        bool left,
        bool loot,
        string vehicle)
    {
        var sorted = string.Join(", ", enchantments.Order(StringComparer.Ordinal));
        return $" baby {baby} hand {hand} enchanted {enchanted} [{sorted}] helmet {helmet} left {left} loot {loot} "
            + $"vehicle {vehicle}";
    }

    private static string EnchantmentName(int id) =>
        EnchantmentsRegistry.All.First(enchantment => enchantment.Id == id).Identifier;

    private static string ItemName(ItemStack stack) => stack.IsAir ? "minecraft:air" : stack.Holder.UnlocalizedName;

    // Vanilla's chicken variants and sound variants in registry order, which Obsidian's indices follow.
    private static readonly string[] ChickenVariants = ["minecraft:cold", "minecraft:temperate", "minecraft:warm"];

    private static readonly string[] ChickenSounds = ["minecraft:classic", "minecraft:picky"];

    private static int Turn(float degrees) => ((Angle)degrees).Value;

    private static string EntityName(EntityType? type) =>
        type is EntityType known ? $"minecraft:{Snake(known.ToString())}" : "none";

    private static string Snake(string pascal) =>
        string.Concat(pascal.Select((character, index) => index > 0 && char.IsUpper(character)
            ? $"_{char.ToLowerInvariant(character)}"
            : char.ToLowerInvariant(character).ToString()));

    private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Vec(JsonElement values) =>
        Vec(new VectorD(values[0].GetDouble(), values[1].GetDouble(), values[2].GetDouble()));

    private static string Vec(VectorD value) => string.Create(CultureInfo.InvariantCulture,
        $"({value.X:R}, {value.Y:R}, {value.Z:R})");

    private static string Vec(VectorF value) => string.Create(CultureInfo.InvariantCulture,
        $"({(double)value.X:R}, {(double)value.Y:R}, {(double)value.Z:R})");

    private sealed record Write(Vector Position, IBlock Block, BlockUpdateFlags Flags)
    {
        public override string ToString() => $"{this.Position} {this.Block.StateId()} flags {(int)this.Flags}";
    }

    private static void CompareWrites(List<string> differences, JsonElement probe, List<Write> actual)
    {
        var vanilla = probe.GetProperty("writes").EnumerateArray().Select(write => new Write(
            Position(write.GetProperty("pos")),
            VanillaSulfurBlocks.ParseState(write.GetProperty("state").GetString()!),
            (BlockUpdateFlags)write.GetProperty("flags").GetInt32()));

        ExpectSequence(differences, "writes", vanilla.Select(write => write.ToString()),
            actual.Select(write => write.ToString()));
    }

    private static IEnumerable<string> Destroyed(JsonElement probe) =>
        probe.GetProperty("destroyed").EnumerateArray().Select(block => Destroyed(
            Position(block.GetProperty("pos")),
            VanillaSulfurBlocks.ParseState(block.GetProperty("state").GetString()!),
            block.GetProperty("drops").GetBoolean()));

    private static string Destroyed(Vector position, IBlock state, bool drops) =>
        $"{position} {state.StateId()} drops {drops}";

    private static string FluidName(FluidKind fluid) => fluid switch
    {
        FluidKind.Water => "minecraft:water",
        FluidKind.FlowingWater => "minecraft:flowing_water",
        FluidKind.Lava => "minecraft:lava",
        FluidKind.FlowingLava => "minecraft:flowing_lava",
        _ => fluid.ToString()
    };

    private static void ExpectSequence(
        List<string> differences,
        string what,
        IEnumerable<string> vanilla,
        IEnumerable<string> obsidian)
    {
        var expected = string.Join(", ", vanilla);
        var actual = string.Join(", ", obsidian);
        if (expected != actual)
            differences.Add($"{what}: vanilla [{expected}], Obsidian [{actual}]");
    }

    private static void Expect<T>(List<string> differences, string what, T vanilla, T obsidian)
    {
        if (!EqualityComparer<T>.Default.Equals(vanilla, obsidian))
            differences.Add($"{what}: vanilla {vanilla}, Obsidian {obsidian}");
    }

    private static IEnumerable<Chunk> Chunks(WorldData.World world)
    {
        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
                yield return (Chunk)world.GetLoadedChunk(chunkX, chunkZ)!;
        }
    }

    // The nine chunks around the cases' blocks, empty and complete, so writes and reads reach them.
    private static async Task AddEmptyChunksAsync(WorldData.World world)
    {
        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
            {
                var chunk = new Chunk(chunkX, chunkZ);
                chunk.SetChunkStatus(ChunkGenStage.full);
                world.LoadRegionByChunk(chunkX, chunkZ).SetChunk(chunk);
            }
        }

        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
                await world.GetChunkAsync(chunkX, chunkZ, scheduleGeneration: false);
        }
    }

    private static Vector Position(JsonElement array) =>
        new(array[0].GetInt32(), array[1].GetInt32(), array[2].GetInt32());

    private static JsonElement LoadFixture()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Obsidian.Tests.Assets.eyeblossom_ghast_portal_ticks.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }

    /// <summary>A draw as the probe records it: the call, its bound for <c>nextInt(bound)</c>, its result.</summary>
    private sealed record Draw(string Call, int? Bound, string Value)
    {
        public static Draw Parse(JsonElement draw)
        {
            var call = draw.GetProperty("call").GetString()!;
            int? bound = draw.TryGetProperty("bound", out var given) ? given.GetInt32() : null;
            var value = draw.GetProperty("value");
            var text = call switch
            {
                "nextBoolean" => value.GetBoolean().ToString(),
                "nextFloat" => value.GetSingle().ToString("R", CultureInfo.InvariantCulture),
                "nextDouble" => value.GetDouble().ToString("R", CultureInfo.InvariantCulture),
                _ => value.GetInt64().ToString(CultureInfo.InvariantCulture)
            };

            return new Draw(call, bound, text);
        }

        public override string ToString() => Bound is int bound ? $"{Call}({bound})={Value}" : $"{Call}={Value}";
    }

    /// <summary>A legacy random that records each draw the way the probe does.</summary>
    private sealed class RecordingRandom(long seed) : IRandomSource
    {
        private readonly LegacyRandomSource source = new(seed);

        public List<Draw> Draws { get; } = [];

        public IRandomSource Fork() => throw new NotSupportedException();

        public IPositionalRandomFactory ForkPositional() => throw new NotSupportedException();

        public void SetSeed(long seed) => throw new NotSupportedException();

        public int NextInt() => this.Record("nextInt", null, this.source.NextInt());

        public int NextInt(int bound) => this.Record("nextInt", bound, this.source.NextInt(bound));

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
            this.Draws.Add(new Draw("nextFloat", null, value.ToString("R", CultureInfo.InvariantCulture)));
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
}
