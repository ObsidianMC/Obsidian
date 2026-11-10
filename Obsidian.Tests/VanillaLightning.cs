using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.ChunkData;
using Obsidian.Entities;
using Obsidian.Entities.Factories;
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
/// Replays <c>lightning_bolts.json</c>: vanilla's lightning bolts, recorded tick by tick on a dedicated server by
/// <c>Java/LightningFixtures.java</c> (<c>CaptureFixtures=lightning</c>), and the natural strikes of its thunderstorm
/// chunk tick.
/// </summary>
public sealed class VanillaLightning(ITestOutputHelper output)
{
    // The cases run this far apart along x, each in a chunk of its own, around the middle of that chunk.
    private const int Spacing = 32;

    // The watched area around a case's origin, as the probe's.
    private const int Reach = 6;
    private const int Below = 2;
    private const int Above = 8;

    // Quiet writes, as the probe's setup: no neighbour or shape updates and no on-place.
    private const BlockUpdateFlags Quiet = BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace;

    private static readonly VectorD ListenerOffset = new(5.5, 10, 0.5);

    private static readonly string[] Advancements =
    [
        "minecraft:adventure/lightning_rod_with_villager_no_fire",
        "minecraft:adventure/very_very_frightening"
    ];

    private static readonly JsonElement fixture = LoadFixture();

    // A powered rod's two lamps, on its side and beside the block it stands on, lit and then out again.
    private static readonly string[] RodLamps =
    [
        LampFlags(1,
            "0,0,0 minecraft:lightning_rod[powered=true] flags 3",
            "1,0,0 minecraft:redstone_lamp[lit=true] flags 2",
            "0,-1,1 minecraft:redstone_lamp[lit=true] flags 2"),
        LampFlags(13,
            "1,0,0 minecraft:redstone_lamp flags 2",
            "0,-1,1 minecraft:redstone_lamp flags 2")
    ];

    /// <summary>
    /// Expected differences by case, each failing the replay once it no longer occurs (see
    /// <c>docs/known-gaps/lightning.md</c>).
    /// </summary>
    private static readonly Dictionary<string, string[]> KnownDifferences = new()
    {
        ["rod_powers_and_sets_fire"] = RodLamps,
        ["rod_fire_spread_off"] = RodLamps,
        ["rod_with_villager_and_no_fire"] = RodLamps,
        ["visual_only_bolt_on_rod"] = RodLamps,
        ["rod_sideways_powers_its_wall"] =
        [
            LampFlags(1,
                "-1,0,-1 minecraft:fire flags 3",
                "0,0,0 minecraft:lightning_rod[facing=east,powered=true] flags 3",
                "-1,0,1 minecraft:redstone_lamp[lit=true] flags 2"),
            LampFlags(13, "-1,0,1 minecraft:redstone_lamp flags 2")
        ],

        // Obsidian's light reads open sky at the bottom of an empty column, where vanilla's doesn't, so a storm
        // strikes the bottom of the world there.
        ["natural_strike_into_the_void"] =
        [
            "added: vanilla [], Obsidian [minecraft:lightning_bolt -5.500,-128.000,-6.500 fire 0]"
        ]
    };

    /// <summary>
    /// Every case in one world, each at its own chunk: a strike summons a bolt with the case's seeds and runs the level's
    /// ticks, comparing each tick's block writes (with their flags), changed blocks, entities, sounds, level events and
    /// game events, then the advancements granted; a natural case runs the thunderstorm chunk tick once and compares the
    /// entities it added and the draws it made.
    /// </summary>
    [Fact(Timeout = TestServerFactory.TestTimeout * 4)]
    public async Task ReplayLightningReference()
    {
        Assert.Equal(ServerConstants.ProtocolDescription, fixture.GetProperty("version").GetString());

        var cases = fixture.GetProperty("cases").EnumerateArray()
            .Select((probe, index) => (Probe: probe, Origin: new Vector(index * Spacing + 8, 64, 8)))
            .ToArray();
        await using var replay = await LightningWorld.CreateAsync(cases.Length);

        await FixtureCases.AssertAllAsync(
            cases,
            item => Name(item.Probe),
            item => Replay(replay, item.Probe, item.Origin));

        output.WriteLine($"Replayed {cases.Length} lightning cases.");
    }

    private static async Task Replay(LightningWorld replay, JsonElement probe, Vector origin)
    {
        var differences = IsNatural(probe)
            ? await ReplayNaturalAsync(replay, probe, origin)
            : await ReplayStrikeAsync(replay, probe, origin);

        BlockProbes.ExpectKnown(differences, KnownDifferences.GetValueOrDefault(Name(probe)) ?? []);
        if (differences.Count > 0)
            throw new Xunit.Sdk.XunitException(string.Join("\n", differences));
    }

    private static async Task<List<string>> ReplayStrikeAsync(LightningWorld replay, JsonElement probe, Vector origin)
    {
        var level = replay.Level;
        ApplyRules(level, probe);
        Setup(level, probe, origin);
        SpawnEntities(level, probe, origin);
        var listener = replay.Listen(origin + ListenerOffset, Name(probe));

        // The case's chunk ticks as the probe's forced chunk does: spectators simulate none.
        using var ticket = level.AddChunkTicket(origin.X >> 4, origin.Z >> 4);

        // Recorded from here: the writes the level stores, the game events it emits and what the listener hears.
        var writes = new List<string>();
        var gameEvents = new List<string>();
        level.Fluids.BlockStored = (position, block, flags) =>
        {
            if (InArea(position, origin))
                writes.Add($"{Offset(position, origin)} {StateText(block)} flags {(int)flags}");
        };
        level.GameEventQueued = vibration => gameEvents.Add(GameEvent(vibration, origin));

        level.GrowthRandom = new LegacyRandomSource(probe.GetProperty("seed").GetInt64());
        var bolt = new LightningBolt(new LegacyRandomSource(probe.GetProperty("boltSeed").GetInt64()))
        {
            Level = level,
            EntityId = Server.GetNextEntityId(),
            Position = (VectorD)origin + BlockProbes.Point(probe.GetProperty("bolt")),
            VisualOnly = probe.TryGetProperty("visualOnly", out var visual) && visual.GetBoolean(),
            Cause = probe.TryGetProperty("cause", out var cause) && cause.GetBoolean() ? listener.Player : null
        };
        level.SpawnEntity(bolt);

        var differences = new List<string>();
        var blocks = Snapshot(level, origin);
        var tick = 0;
        foreach (var checkpoint in probe.GetProperty("checkpoints").EnumerateArray())
        {
            tick++;
            var recordWeathering = probe.TryGetProperty("recordWeathering", out var record) && record.GetBoolean();
            await level.DoWorldTickAsync();

            var actual = new Dictionary<string, List<string>>
            {
                ["writes"] = [.. writes],
                ["changes"] = Changes(level, origin, blocks),
                ["entities"] = Entities(level, origin),
                ["sounds"] = Sounds(level, listener.Client, origin),
                ["levelEvents"] = LevelEvents(listener.Client, origin),
                ["gameEvents"] = [.. gameEvents]
            };
            if (recordWeathering)
            {
                var deadlines = EntitiesInChunk(level, origin).OfType<CopperGolem>()
                    .Select(golem => EntityNbt.Save(golem)!.GetLong("next_weather_age"))
                    .Select(deadline => deadline < 0 ? deadline : deadline - level.LevelData.GameTime)
                    .ToArray();
                var expected = checkpoint.GetProperty("weatherDeadlines").EnumerateArray()
                    .Select(deadline => deadline.GetInt64()).ToArray();
                BlockProbes.Expect(differences, $"tick {tick} weatherDeadlines",
                    string.Join(",", expected), string.Join(",", deadlines));
                BlockProbes.Expect(differences, $"tick {tick} next", checkpoint.GetProperty("next").GetInt32(),
                    level.GrowthRandom.NextInt());
            }
            writes.Clear();
            gameEvents.Clear();
            listener.Client.ClearPackets();

            foreach (var (field, obsidian) in actual)
                Compare(differences, $"tick {tick} {field}", Strings(checkpoint.GetProperty(field)), obsidian);
        }

        // The level's tick matched the triggers; their grants run in the server's background work.
        await replay.DrainGrantsAsync();
        var granted = Advancements.Where(listener.Player.ProgressAdvancements.IsDone).ToList();
        Compare(differences, "advancements", Strings(probe.GetProperty("advancements")), granted);

        level.Fluids.BlockStored = null;
        level.GameEventQueued = null;
        await replay.ClearAsync(origin);
        return differences;
    }

    private static async Task<List<string>> ReplayNaturalAsync(LightningWorld replay, JsonElement probe, Vector origin)
    {
        var level = replay.Level;
        ApplyRules(level, probe);
        Setup(level, probe, origin);
        SpawnEntities(level, probe, origin);
        LightColumns(level, origin);
        var listener = replay.Listen(origin + ListenerOffset, Name(probe));
        listener.Player.GameMode = GameMode.Spectator;

        var chunk = (Chunk)level.GetLoadedChunk(origin.X >> 4, origin.Z >> 4)!;
        chunk.InhabitedTime = 0;
        if (probe.TryGetProperty("inhabitedChunks", out var inhabitedChunks))
        {
            foreach (var entry in inhabitedChunks.EnumerateArray())
            {
                var offset = entry.GetProperty("offset");
                var chunkOrigin = origin + new Vector(offset[0].GetInt32() * 16, 0, offset[1].GetInt32() * 16);
                var target = (Chunk)level.GetLoadedChunk(chunkOrigin.X >> 4, chunkOrigin.Z >> 4)!;
                target.InhabitedTime = entry.GetProperty("time").GetInt64();
                LightColumns(level, chunkOrigin);
            }
        }
        var existing = NaturalEntities(level, probe, origin).Select(entity => entity.EntityId).ToHashSet();
        level.LevelData.Raining = !probe.TryGetProperty("raining", out var raining) || raining.GetBoolean();
        level.LevelData.Thundering = !probe.TryGetProperty("thundering", out var thundering) || thundering.GetBoolean();
        level.GrowthRandom = new LegacyRandomSource(probe.GetProperty("seed").GetInt64());
        level.RandomTickValue = probe.GetProperty("randValue").GetInt32();

        if (level.StormStrikes)
            level.TickThunder(chunk);

        var added = NaturalEntities(level, probe, origin)
            .Where(entity => !existing.Contains(entity.EntityId))
            .Select(entity => Describe(entity, origin) + (entity is LightningBolt { VisualOnly: true } ? " visual" : ""))
            .ToList();

        var differences = new List<string>();
        Compare(differences, "added", Strings(probe.GetProperty("added")), added);
        BlockProbes.Expect(differences, "randValueAfter", probe.GetProperty("randValueAfter").GetInt32(),
            level.RandomTickValue);
        BlockProbes.Expect(differences, "next", probe.GetProperty("next").GetInt32(), level.GrowthRandom.NextInt());

        level.LevelData.Raining = false;
        level.LevelData.Thundering = false;
        foreach (var entity in NaturalEntities(level, probe, origin).ToList())
            await entity.RemoveAsync();
        foreach (var placement in probe.GetProperty("setup").EnumerateArray())
        {
            var from = origin + BlockProbes.Position(placement.GetProperty("pos"));
            var to = placement.TryGetProperty("to", out var end) ? origin + BlockProbes.Position(end) : from;
            foreach (var position in Box(from, to))
                level.BlockUpdates.SetBlock(position, BlocksRegistry.Air, Quiet);
        }
        await replay.ClearAsync(origin);
        return differences;
    }

    private static void ApplyRules(AbstractLevel level, JsonElement probe)
    {
        var difficulty = probe.TryGetProperty("difficulty", out var named) ? named.GetString()! : "normal";
        level.LevelData.Difficulty = Enum.Parse<Difficulty>(difficulty, ignoreCase: true);

        var rules = level.LevelData.GameRules;
        rules["fire_spread_radius_around_player"] = probe.TryGetProperty("fireSpread", out var radius)
            ? radius.GetInt32().ToString(CultureInfo.InvariantCulture)
            : "-1";
        rules["spawn_mobs"] = probe.TryGetProperty("spawnMobs", out var spawn) && spawn.GetBoolean() ? "true" : "false";
        rules["mob_drops"] = probe.TryGetProperty("mobDrops", out var drops) && !drops.GetBoolean() ? "false" : "true";
    }

    // The case's blocks, set without updates: single positions, or boxes from pos to to.
    private static void Setup(AbstractLevel level, JsonElement probe, Vector origin)
    {
        foreach (var placement in probe.GetProperty("setup").EnumerateArray())
        {
            var state = BlockProbes.ParseState(placement.GetProperty("state").GetString()!);
            var from = origin + BlockProbes.Position(placement.GetProperty("pos"));
            var to = placement.TryGetProperty("to", out var end) ? origin + BlockProbes.Position(end) : from;
            foreach (var position in Box(from, to))
                level.BlockUpdates.SetBlock(position, state, Quiet);
        }
    }

    // The case's mobs (without AI, as the probe's) and items, in the fixture's order.
    private static void SpawnEntities(AbstractLevel level, JsonElement probe, Vector origin)
    {
        if (!probe.TryGetProperty("entities", out var entities))
            return;

        foreach (var spawn in entities.EnumerateArray())
            level.SpawnEntity(CreateEntity(level, spawn, (VectorD)origin + BlockProbes.Point(spawn.GetProperty("pos"))));
    }

    private static Entity CreateEntity(AbstractLevel level, JsonElement spawn, VectorD position)
    {
        var type = spawn.GetProperty("type").GetString()!;
        if (type == "minecraft:item")
        {
            var item = ItemsRegistry.Get(spawn.GetProperty("item").GetString()!);
            return new ItemEntity
            {
                Level = level,
                EntityId = Server.GetNextEntityId(),
                Position = position,
                Item = new ItemStack(item)
            };
        }

        Assert.True(EntityNbt.TryParseType(type, out var entityType), type);
        var entity = EntitySpawner.Create(entityType, level);
        entity.Type = entityType;
        entity.EntityId = Server.GetNextEntityId();
        entity.Position = position;

        // Standing on the case's floor, as the probe's mobs are, so that none lands on its first tick.
        entity.MovementFlags = MovementFlags.OnGround;
        if (entity is Mob mob)
            mob.MobBitMask |= MobBitmask.NoAi;
        if (entity is Mooshroom mooshroom && spawn.TryGetProperty("variant", out var variant))
            mooshroom.Brown = variant.GetString() == "brown";
        if (entity is CopperGolem golem && spawn.TryGetProperty("weatherState", out var weather))
            golem.Weather = Array.IndexOf(["unaffected", "exposed", "weathered", "oxidized"], weather.GetString());

        return entity;
    }

    // Sky light for a natural case's chunk, which the probe's light engine worked out: full above the highest block of
    // each column, none at and below it. Only living things under the sky and the strike's column read it.
    private static void LightColumns(AbstractLevel level, Vector origin)
    {
        var chunk = level.GetLoadedChunk(origin.X >> 4, origin.Z >> 4)!;
        for (var x = 0; x < 16; x++)
        {
            for (var z = 0; z < 16; z++)
            {
                var sky = 15;
                for (var y = level.MinY + level.Height - 1; y >= level.MinY; y--)
                {
                    if (!chunk.GetBlock(x, y, z).IsAir)
                        sky = 0;

                    chunk.SetLightLevel(x, y, z, LightType.Sky, sky);
                }
            }
        }
    }

    private static List<string> Changes(AbstractLevel level, Vector origin, Dictionary<Vector, IBlock> blocks)
    {
        var changes = new List<string>();
        foreach (var position in Area(origin))
        {
            var current = level.BlockUpdates.GetBlock(position);
            if (current.GetHashCode() == blocks[position].GetHashCode())
                continue;

            changes.Add($"{Offset(position, origin)}={StateText(current)}");
            blocks[position] = current;
        }

        return changes;
    }

    private static Dictionary<Vector, IBlock> Snapshot(AbstractLevel level, Vector origin) =>
        Area(origin).ToDictionary(position => position, level.BlockUpdates.GetBlock);

    // The watched area in the probe's order: y, then z, then x ascending.
    private static IEnumerable<Vector> Area(Vector origin)
    {
        for (var y = -Below; y <= Above; y++)
        {
            for (var z = -Reach; z <= Reach; z++)
            {
                for (var x = -Reach; x <= Reach; x++)
                    yield return origin + new Vector(x, y, z);
            }
        }
    }

    private static bool InArea(Vector position, Vector origin)
    {
        var offset = position - origin;
        return Math.Abs(offset.X) <= Reach && Math.Abs(offset.Z) <= Reach && offset.Y >= -Below && offset.Y <= Above;
    }

    private static IEnumerable<Vector> Box(Vector from, Vector to)
    {
        for (var y = from.Y; y <= to.Y; y++)
        {
            for (var z = from.Z; z <= to.Z; z++)
            {
                for (var x = from.X; x <= to.X; x++)
                    yield return new Vector(x, y, z);
            }
        }
    }

    // Every entity in the area but players, in the order they were added.
    private static List<string> Entities(AbstractLevel level, Vector origin)
    {
        var min = (VectorD)(origin + new Vector(-Reach, -Below, -Reach));
        var max = (VectorD)(origin + new Vector(Reach + 1, Above + 1, Reach + 1));
        return level.GetNonPlayerEntitiesInRange((VectorD)origin, 32)
            .OfType<Entity>()
            .Where(entity => Overlaps(entity, min, max))
            .OrderBy(entity => entity.EntityId)
            .Select(entity => Describe(entity, origin))
            .ToList();
    }

    private static IEnumerable<Entity> EntitiesInChunk(AbstractLevel level, Vector origin) =>
        level.GetNonPlayerEntitiesInRange((VectorD)origin, 400)
            .OfType<Entity>()
            .Where(entity => (int)Math.Floor(entity.Position.X) >> 4 == origin.X >> 4
                && (int)Math.Floor(entity.Position.Z) >> 4 == origin.Z >> 4)
            .OrderBy(entity => entity.EntityId);

    private static IEnumerable<Entity> NaturalEntities(AbstractLevel level, JsonElement probe, Vector origin)
    {
        var entities = EntitiesInChunk(level, origin);
        if (probe.TryGetProperty("inhabitedChunks", out var chunks))
        {
            foreach (var entry in chunks.EnumerateArray())
            {
                var offset = entry.GetProperty("offset");
                var chunkOrigin = origin + new Vector(offset[0].GetInt32() * 16, 0, offset[1].GetInt32() * 16);
                entities = entities.Concat(EntitiesInChunk(level, chunkOrigin));
            }
        }
        return entities.Distinct().OrderBy(entity => entity.EntityId);
    }

    private static bool Overlaps(Entity entity, VectorD min, VectorD max)
    {
        var box = entity.CurrentBox;
        return box.Min.X < max.X && box.Max.X > min.X
            && box.Min.Y < max.Y && box.Max.Y > min.Y
            && box.Min.Z < max.Z && box.Max.Z > min.Z;
    }

    // An entity as the probe describes it: type, offset, remaining fire ticks, health, then what lightning changes.
    private static string Describe(Entity entity, Vector origin)
    {
        var at = entity.Position - (VectorD)origin;
        var fire = entity is Living living ? living.FireTicks : 0;
        var text = $"{EntityNbt.TypeId(entity)} {Number(at.X, 3)},{Number(at.Y, 3)},{Number(at.Z, 3)} fire {fire}";
        if (entity is Living alive)
            text += $" health {Number(Math.Max(0, alive.Health), 2)}";
        if (entity is Mob { PersistenceRequired: true })
            text += " persistent";
        if (entity is Creeper { Powered: true })
            text += " powered";
        if (entity is Mooshroom mooshroom)
            text += mooshroom.Brown ? " variant brown" : " variant red";
        if (entity is CopperGolem golem)
            text += " weather " + new[] { "unaffected", "exposed", "weathered", "oxidized" }[golem.Weather];
        if (entity is ItemEntity item)
            text += " item " + Stack(item.Item);
        if (entity is Mob mob and not Witch && mob.GetEquipment(EquipmentSlot.MainHand) is { Count: > 0 } held)
            text += " holding " + Held(mob, held);
        if (entity is SkeletonHorse { SkeletonTrap: true })
            text += " trap";

        return text;
    }

    // The sounds the listener heard, at the entity's position for those that follow one. Mobs' ambient sounds and a
    // witch's are left out, as the probe leaves them out (their own randoms decide them).
    private static List<string> Sounds(AbstractLevel level, TestClient client, Vector origin)
    {
        var sounds = new List<string>();
        foreach (var packet in client.SentPackets)
        {
            var (id, position, volume) = packet switch
            {
                SoundPacket sound => (sound.SoundLocation, SoundPosition(sound), sound.Volume),
                SoundEntityPacket sound => (sound.SoundLocation, EntityPosition(level, sound.EntityId), sound.Volume),
                _ => (null, default(VectorD?), 0f)
            };
            if (id is null || id.EndsWith(".ambient", StringComparison.Ordinal) || id.Contains(":entity.witch."))
                continue;

            var at = position is VectorD point ? point - (VectorD)origin : new VectorD(double.NaN, 0, 0);
            sounds.Add($"{id} {Number(at.X, 3)},{Number(at.Y, 3)},{Number(at.Z, 3)} volume {Number(volume, 2)}");
        }

        return sounds;
    }

    private static VectorD SoundPosition(SoundPacket sound) =>
        new(sound.SoundPosition.X / 8d, sound.SoundPosition.Y / 8d, sound.SoundPosition.Z / 8d);

    private static VectorD? EntityPosition(AbstractLevel level, int entityId) =>
        level.GetNonPlayerEntitiesInRange(level.LevelData.SpawnPosition, float.MaxValue)
            .FirstOrDefault(entity => entity.EntityId == entityId)?.Position;

    private static List<string> LevelEvents(TestClient client, Vector origin) => client.SentPackets
        .OfType<LevelEventPacket>()
        .Select(levelEvent => $"{levelEvent.Type} {Offset(levelEvent.Position, origin)} {levelEvent.Data}")
        .ToList();

    // A game event as the probe's listener records it: its name, the block it's in and its source. A witch's are left
    // out, as the probe leaves them out.
    private static string GameEvent(MobVibration vibration, Vector origin)
    {
        var source = vibration.Source is Entity entity ? EntityNbt.TypeId(entity) : "none";
        var block = (Vector)vibration.Position.Floor();
        return $"{vibration.Kind.Identifier()} {Offset(block, origin)} {source}";
    }

    // Only the game events vanilla's listener could hear and the probe keeps: within 16 blocks, and not a witch's.
    private static List<string> Strings(JsonElement array) =>
        array.EnumerateArray().Select(value => value.GetString()!).ToList();

    private static void Compare(List<string> differences, string what, List<string> vanilla, List<string> obsidian)
    {
        if (obsidian.Count > 0 && what.EndsWith("gameEvents", StringComparison.Ordinal))
            obsidian = obsidian.Where(text => !text.EndsWith(" minecraft:witch", StringComparison.Ordinal)).ToList();

        if (!vanilla.SequenceEqual(obsidian))
            differences.Add($"{what}: vanilla [{string.Join("; ", vanilla)}], Obsidian [{string.Join("; ", obsidian)}]");
    }

    // A state as the probe writes it: the block, then the properties that differ from its default, by name.
    private static string StateText(IBlock state)
    {
        var defaults = BlocksRegistry.GetDefault(state.RegistryId).GetProperties();
        var properties = state.GetProperties()
            .Where(property => defaults.GetValueOrDefault(property.Key) != property.Value)
            .OrderBy(property => property.Key, StringComparer.Ordinal)
            .Select(property => $"{property.Key}={property.Value}")
            .ToList();

        return properties.Count == 0 ? state.UnlocalizedName : $"{state.UnlocalizedName}[{string.Join(",", properties)}]";
    }

    // The difference a tick's writes show where the redstone engine stores a lamp's change with SkipOnPlace (512) as
    // well as vanilla's flag 2.
    private static string LampFlags(int tick, params string[] vanillaWrites)
    {
        var obsidianWrites = vanillaWrites
            .Select(write => write.Contains("redstone_lamp", StringComparison.Ordinal)
                ? write.Replace(" flags 2", " flags 514", StringComparison.Ordinal)
                : write);

        return $"tick {tick} writes: vanilla [{string.Join("; ", vanillaWrites)}], "
            + $"Obsidian [{string.Join("; ", obsidianWrites)}]";
    }

    // A zombified piglin's golden spear or sword, which the probe doesn't tell apart (the pig's own random picks it).
    private static string Held(Mob mob, ItemStack held)
    {
        var golden = held.Holder.Id is ItemIds.GoldenSword or ItemIds.GoldenSpear;
        return mob is ZombifiedPiglin && golden ? "golden sword or spear" : Stack(held);
    }

    private static string Stack(ItemStack stack) => $"{stack.Holder.UnlocalizedName} x{stack.Count}";

    private static string Offset(Vector position, Vector origin)
    {
        var offset = position - origin;
        return $"{offset.X},{offset.Y},{offset.Z}";
    }

    private static string Number(double value, int decimals) =>
        value.ToString("F" + decimals, CultureInfo.InvariantCulture);

    private static bool IsNatural(JsonElement probe) =>
        probe.TryGetProperty("kind", out var kind) && kind.GetString() == "natural";

    private static string Name(JsonElement probe) => probe.GetProperty("name").GetString()!;

    private static JsonElement LoadFixture()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Obsidian.Tests.Assets.lightning_bolts.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// One server whose overworld has empty, complete plains chunks where the cases run, random ticks off and time
    /// still, and a spectator listener for each case that has them all.
    /// </summary>
    private sealed class LightningWorld : IAsyncDisposable
    {
        private readonly RealServerContext context;
        private readonly int caseCount;
        private Listener? current;

        private LightningWorld(RealServerContext context, int caseCount)
        {
            this.context = context;
            this.caseCount = caseCount;
        }

        public AbstractLevel Level => this.context.World;

        public static async Task<LightningWorld> CreateAsync(int caseCount)
        {
            var context = TestServerFactory.CreateServer($"lightning-{Guid.NewGuid():N}");
            var world = context.World;
            world.LevelData.GameRules["random_tick_speed"] = "0";
            world.LevelData.GameRules["advance_time"] = "false";
            world.LevelData.GameRules["advance_weather"] = "false";
            world.LevelData.Raining = false;
            world.LevelData.Thundering = false;

            Assert.True(CodecRegistry.TryGetBiome("minecraft:plains", out var plains));
            var replay = new LightningWorld(context, caseCount);
            foreach (var (chunkX, chunkZ) in replay.Chunks())
            {
                var chunk = new Chunk(chunkX, chunkZ, world.MinY, world.Height, ChunkGenStage.full);
                for (var y = chunk.MinY; y < chunk.MinY + chunk.Height; y += 4)
                {
                    for (var x = 0; x < 16; x += 4)
                    {
                        for (var z = 0; z < 16; z += 4)
                            chunk.SetBiome(x, y, z, plains!);
                    }
                }

                world.LoadRegionByChunk(chunkX, chunkZ).SetChunk(chunk);
            }

            // Loaded and ticking, so their scheduled block ticks run.
            foreach (var (chunkX, chunkZ) in replay.Chunks())
            {
                var chunk = await world.GetChunkAsync(chunkX, chunkZ, scheduleGeneration: false);
                world.Fluids.Track(chunk!);
            }

            return replay;
        }

        // The cases' chunks, with one more on each side so that neighbours are always loaded.
        private IEnumerable<(int X, int Z)> Chunks()
        {
            var last = ((this.caseCount - 1) * Spacing + 8) >> 4;
            for (var chunkX = -1; chunkX <= last + 1; chunkX++)
            {
                for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
                    yield return (chunkX, chunkZ);
            }
        }

        /// <summary>A new spectator at <paramref name="position"/> that has every chunk, in place of the last.</summary>
        public Listener Listen(VectorD position, string name)
        {
            if (this.current is Listener previous)
                this.Level.Players.TryRemove(previous.Player.Uuid, out _);

            var player = TestServerFactory.CreateConnectedPlayer(this.context.Server, this.context.World,
                name.Length > 16 ? name[..16] : name, position);
            player.Level = this.Level;
            player.GameMode = GameMode.Spectator;
            foreach (var (chunkX, chunkZ) in this.Chunks())
                player.LoadedChunks.Add(NumericsHelper.IntsToLong(chunkX, chunkZ));

            this.Level.Players.TryAdd(player.Uuid, player);
            var client = Assert.IsType<TestClient>(player.Client);
            client.ClearPackets();
            this.current = new Listener(player, client);
            return this.current;
        }

        /// <summary>
        /// Clears a case's area and its entities without updates, so that the ticks it left scheduled find air and draw
        /// nothing from the level's random in the cases after it.
        /// </summary>
        public async Task ClearAsync(Vector origin)
        {
            foreach (var position in Area(origin))
                this.Level.BlockUpdates.SetBlock(position, BlocksRegistry.Air, Quiet);

            foreach (var entity in EntitiesInChunk(this.Level, origin).ToList())
                await entity.RemoveAsync();
        }

        /// <summary>Lets the grants a level's tick queued in the server's background work finish.</summary>
        public Task DrainGrantsAsync() => this.context.Server.BackgroundWork.DrainAsync();

        public async ValueTask DisposeAsync() => await this.context.DisposeAsync();
    }

    private sealed record Listener(Entities.Player Player, TestClient Client);
}
