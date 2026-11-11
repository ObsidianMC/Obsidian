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
using Obsidian.Commands;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.Utilities;
using Obsidian.WorldData;
using Obsidian.WorldData.TrialSpawners;
using Xunit;
using Xunit.Abstractions;

namespace Obsidian.Tests;

/// <summary>
/// Replays <c>trial_spawners.json</c>: vanilla's trial spawners recorded tick by tick on a dedicated server by
/// <c>Java/TrialSpawnerFixtures.java</c> (<c>CaptureFixtures=trial_spawners</c>), and loads and saves the trial
/// spawners and vaults of a trial chamber vanilla placed.
/// </summary>
public sealed class VanillaTrialSpawners(ITestOutputHelper output)
{
    // The cases run this far apart along x, each in a chunk of its own, around the middle of that chunk.
    private const int Spacing = 32;

    // The watched area around a case's origin, as the probe's.
    private const int Reach = 8;
    private const int Below = 2;
    private const int Above = 6;

    // How far a mob that leaves is moved down, as the probe moves it.
    private const int LeaveDepth = 60;

    // Quiet writes, as the probe's setup: no neighbour or shape updates and no on-place.
    private const BlockUpdateFlags Quiet = BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace;

    private static readonly VectorD ListenerOffset = new(0.5, 10, 0.5);

    // What the probe's saved data leaves out: the id and position, which the chunk saves, and keepPacked, which only
    // chunks write.
    private static readonly string[] HeaderFields = ["id", "x", "y", "z", "keepPacked"];

    private static readonly JsonElement fixture = LoadFixture();

    /// <summary>
    /// Expected differences by case, each failing the replay once it no longer occurs (see
    /// <c>docs/known-gaps/trial-spawners.md</c>).
    /// </summary>
    private static readonly Dictionary<string, string[]> KnownDifferences = new();

    /// <summary>
    /// Every trial case in one world, each at its own chunk: the spawner is placed with the case's data, the players
    /// stand where the case puts them, and the level ticks, comparing after each tick the block writes (with their
    /// flags), changed blocks, the spawner's saved data, the data clients got for it, the entities, sounds, level
    /// events, particles and game events, and the level random's state.
    /// </summary>
    [Fact]
    public async Task ReplayTrialSpawnerReference()
    {
        Assert.Equal(ServerConstants.ProtocolDescription, fixture.GetProperty("version").GetString());

        var cases = fixture.GetProperty("cases").EnumerateArray()
            .Select((probe, index) => (Probe: probe, Origin: new Vector(index * Spacing + 8, 64, 8)))
            .Where(item => !IsChamber(item.Probe))
            .ToArray();
        await using var replay = await TrialWorld.CreateAsync(fixture.GetProperty("cases").GetArrayLength());

        await FixtureCases.AssertAllAsync(
            cases,
            item => Name(item.Probe),
            item => Replay(replay, item.Probe, item.Origin));

        output.WriteLine($"Replayed {cases.Length} trial spawner cases.");
    }

    /// <summary>
    /// Obsidian-only risk: a trial chamber's trial spawners and vaults, as vanilla placed them and after they ticked,
    /// and each trial case's spawner at its end, load as their typed block entities and save exactly what vanilla
    /// saved. Each ticked spawner's spawn data is one its registry config, as Obsidian reads it, can pick.
    /// </summary>
    [Fact]
    public void TrialChamberDataRoundTrips()
    {
        var samples = new List<(string Name, string Id, string Saved)>();
        foreach (var probe in fixture.GetProperty("cases").EnumerateArray())
        {
            if (!IsChamber(probe))
            {
                samples.Add((Name(probe), "minecraft:trial_spawner", probe.GetProperty("finalSaved").GetString()!));
                continue;
            }

            foreach (var entry in probe.GetProperty("blockEntities").EnumerateArray())
            {
                var at = entry.GetProperty("pos").GetString()!;
                var id = entry.GetProperty("id").GetString()!;
                samples.Add(($"{id} at {at} as placed", id, entry.GetProperty("fresh").GetString()!));
                samples.Add(($"{id} at {at} ticked", id, entry.GetProperty("ticked").GetString()!));
            }
        }

        FixtureCases.AssertAll(samples, sample => sample.Name, sample => AssertRoundTrips(sample.Id, sample.Saved));

        output.WriteLine($"Round-tripped {samples.Count} saved trial spawners and vaults.");
    }

    private static void AssertRoundTrips(string id, string saved)
    {
        var vanilla = SnbtReader.ReadCompound(saved);
        var loaded = BlockEntityNbt.Load(vanilla, id, Vector.Zero);
        if (id == "minecraft:vault")
            Assert.IsType<VaultBlockEntity>(loaded);
        else
            Assert.IsType<TrialSpawnerBlockEntity>(loaded);

        Assert.Equal(Snbt.Write(vanilla), Snbt.Write(Saved(loaded)));

        // A config with nothing to pick leaves empty spawn data.
        if (loaded is TrialSpawnerBlockEntity spawner
            && !spawner.Data.NormalConfig.SpawnPotentials.IsEmpty
            && vanilla.TryGetTag<NbtCompound>("spawn_data", out var picked))
        {
            var candidates = spawner.Data.NormalConfig.SpawnPotentials.Entries
                .Select(entry => Snbt.Write(entry.Value))
                .ToList();
            Assert.Contains(Snbt.Write(picked), candidates);
        }
    }

    // What the chunk would save of the block entity, without what the probe's saved data leaves out.
    private static NbtCompound Saved(IBlockEntity blockEntity)
    {
        var saved = BlockEntityNbt.Save(blockEntity)!;
        foreach (var field in HeaderFields)
            saved.Remove(field);

        return saved;
    }

    private static async Task Replay(TrialWorld replay, JsonElement probe, Vector origin)
    {
        var level = replay.Level;
        ApplyRules(level, probe);
        Setup(level, probe, origin);
        var spawner = PlaceSpawner(level, probe, origin);
        var players = replay.AddPlayers(probe, origin);
        var listener = replay.Listen(origin + ListenerOffset, Name(probe));

        // The case's chunk ticks as the probe's forced chunk does.
        using var ticket = level.AddChunkTicket(origin.X >> 4, origin.Z >> 4);

        // Recorded from here: the writes the level stores, the game events it emits and what the listener gets.
        var writes = new List<string>();
        var gameEvents = new List<string>();
        level.Fluids.BlockStored = (position, block, flags) =>
        {
            if (InArea(position, origin))
                writes.Add($"{Offset(position, origin)} {StateText(block)} flags {(int)flags}");
        };
        level.GameEventQueued = vibration => gameEvents.Add(GameEvent(vibration, origin));

        // The level's tick moves the time on, as vanilla's does, before its block entities tick.
        level.LevelData.GameTime = probe.GetProperty("startTime").GetInt64();
        var random = new LegacyRandomSource(probe.GetProperty("seed").GetInt64());
        level.GrowthRandom = random;

        var differences = new List<string>();
        var mobs = new MobLog();
        var phased = new HashSet<ItemEntity>();
        var blocks = Snapshot(level, origin);
        var checkpoints = probe.GetProperty("checkpoints").EnumerateArray().ToArray();

        for (var tick = 1; tick <= checkpoints.Length; tick++)
        {
            var checkpoint = checkpoints[tick - 1];
            await ActAsync(probe, mobs, tick);
            await level.DoWorldTickAsync();
            mobs.Observe(level, origin, tick);
            TakeItemPhases(level, origin, checkpoint.GetProperty("itemPhases"), phased);

            var actual = new Dictionary<string, List<string>>
            {
                ["writes"] = [.. writes],
                ["changes"] = Changes(level, origin, blocks),
                ["entities"] = Entities(level, origin, mobs),
                ["sounds"] = Sounds(level, listener.Client, origin),
                ["levelEvents"] = LevelEvents(listener.Client, origin),
                ["particles"] = Particles(listener.Client, origin),
                ["gameEvents"] = [.. gameEvents]
            };
            foreach (var (field, obsidian) in actual)
                Compare(differences, $"tick {tick} {field}", Strings(checkpoint.GetProperty(field)), obsidian);

            var saved = Canonical(checkpoint.GetProperty("saved").GetString()!);
            var ours = NormalizedSave(spawner, mobs);
            ExpectKept(differences, $"tick {tick}", probe, ours);
            BlockProbes.Expect(differences, $"tick {tick} saved", saved, Snbt.Write(ours));
            BlockProbes.Expect(differences, $"tick {tick} random", checkpoint.GetProperty("random").GetInt64(),
                random.State);

            var vanillaSent = Strings(checkpoint.GetProperty("client")).Select(Canonical).ToList();
            Compare(differences, $"tick {tick} client", vanillaSent, ClientData(listener.Client, origin));

            writes.Clear();
            gameEvents.Clear();
            listener.Client.ClearPackets();
        }

        BlockProbes.ExpectKnown(differences, KnownDifferences.GetValueOrDefault(Name(probe)) ?? []);

        level.Fluids.BlockStored = null;
        level.GameEventQueued = null;
        replay.RemovePlayers(players);
        await replay.ClearAsync(origin);

        if (differences.Count > 0)
            throw new Xunit.Sdk.XunitException(string.Join("\n", differences));
    }

    // The case's difficulty (normal by default), and whether spawner blocks work (they do by default). Natural
    // spawning and mob drops stay off, as in the probe.
    private static void ApplyRules(AbstractLevel level, JsonElement probe)
    {
        var difficulty = probe.TryGetProperty("difficulty", out var named) ? named.GetString()! : "normal";
        level.LevelData.Difficulty = Enum.Parse<Difficulty>(difficulty, ignoreCase: true);

        var rules = level.LevelData.GameRules;
        var spawnersWork = !probe.TryGetProperty("spawnerBlocksWork", out var work) || work.GetBoolean();
        rules["spawner_blocks_work"] = spawnersWork ? "true" : "false";
        rules["spawn_mobs"] = "false";
        rules["mob_drops"] = "false";
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

    // The spawner, set without updates and loaded with the case's data as a structure loads it, allowed to spawn
    // with natural spawning off unless the case tests that rule.
    private static TrialSpawnerBlockEntity PlaceSpawner(AbstractLevel level, JsonElement probe, Vector origin)
    {
        var spec = probe.GetProperty("spawner");
        level.BlockUpdates.SetBlock(origin, BlockProbes.ParseState(spec.GetProperty("state").GetString()!), Quiet);
        var spawner = Assert.IsType<TrialSpawnerBlockEntity>(level.GetResidentBlockEntity(origin));
        BlockEntityNbt.LoadInto(spawner, SnbtReader.ReadCompound(spec.GetProperty("nbt").GetString()!));
        spawner.OverridesSpawnRules = !probe.TryGetProperty("overrideSpawnRules", out var overrides)
            || overrides.GetBoolean();

        // What the setup left to send goes out now, before anyone listens, as the probe sends it.
        level.AttachBlockEntity(spawner);
        level.SendBlockEntityUpdates();
        return spawner;
    }

    // What happens before the tick: each mob the spawner spawned is killed or moved out of its range once it has been
    // around as long as the case says.
    private static async Task ActAsync(JsonElement probe, MobLog mobs, int tick)
    {
        if (!probe.TryGetProperty("mobActions", out var actions))
            return;

        for (var ordinal = 0; ordinal < mobs.Count; ordinal++)
        {
            var (mob, appeared) = mobs[ordinal];
            var action = actions[ordinal % actions.GetArrayLength()];
            if (appeared + action.GetProperty("after").GetInt32() != tick || !mob.Alive)
                continue;

            // Vanilla's kill: damage beyond any health, from nothing.
            var kind = action.GetProperty("action").GetString();
            if (kind == "kill")
            {
                await mob.DamageCombatAsync(null, float.MaxValue, CombatDamageKind.Magic);
                continue;
            }

            if (kind == "discard")
            {
                await mob.RemoveAsync();
                continue;
            }

            mob.NoGravity = true;
            await mob.TeleportAsync(mob.Position - new VectorD(0, LeaveDepth, 0));
        }
    }

    // Gives each item that appeared vanilla's id modulo 4 for the one in its place, which picks the ticks it moves on
    // as it rests: entity ids can't match between servers.
    private static void TakeItemPhases(AbstractLevel level, Vector origin, JsonElement phases,
        HashSet<ItemEntity> phased)
    {
        var items = ItemsInArea(level, origin);
        for (var index = 0; index < items.Count && index < phases.GetArrayLength(); index++)
        {
            if (phased.Add(items[index]))
                items[index].RestPhase = phases[index].GetInt32();
        }
    }

    private static List<ItemEntity> ItemsInArea(AbstractLevel level, Vector origin)
    {
        var min = (VectorD)(origin + new Vector(-Reach, -Below, -Reach));
        var max = (VectorD)(origin + new Vector(Reach + 1, Above + 1, Reach + 1));
        return level.GetNonPlayerEntitiesInRange((VectorD)origin, 32)
            .OfType<ItemEntity>()
            .Where(item => Overlaps(item, min, max))
            .OrderBy(item => item.EntityId)
            .ToList();
    }

    // The fields the case says Obsidian keeps where vanilla drops them (a config naming a registry entry this server
    // doesn't have): they must be saved as the case's data had them, and are left out of the comparison with vanilla.
    private static void ExpectKept(List<string> differences, string what, JsonElement probe, NbtCompound ours)
    {
        if (!probe.TryGetProperty("obsidianKeeps", out var kept))
            return;

        var input = SnbtReader.ReadCompound(probe.GetProperty("spawner").GetProperty("nbt").GetString()!);
        foreach (var field in kept.EnumerateArray().Select(name => name.GetString()!))
        {
            var expected = Snbt.Write(input[field]);
            var actual = ours.TryGetTag(field, out var tag) ? Snbt.Write(tag) : "nothing";
            BlockProbes.Expect(differences, $"{what} kept {field}", expected, actual);
            ours.Remove(field);
        }
    }

    // The spawner's saved data as the probe writes it: its UUID sets in a fixed order, and its mobs by the order they
    // spawned in.
    private static NbtCompound NormalizedSave(TrialSpawnerBlockEntity spawner, MobLog mobs)
    {
        var saved = Saved(spawner);
        if (saved.TryGetTag<NbtList>("registered_players", out var players))
        {
            var sorted = players.OfType<NbtArray<int>>()
                .Select(uuid => EntityNbt.UuidFromInts(uuid.GetArray()))
                .OrderBy(uuid => uuid.ToString(), StringComparer.Ordinal)
                .ToList();
            var list = new NbtList(NbtTagType.IntArray, "registered_players");
            foreach (var uuid in sorted)
                list.Add(new NbtArray<int>(string.Empty, EntityNbt.UuidToInts(uuid)));

            saved.Remove("registered_players");
            saved.Add(list);
        }

        if (saved.TryGetTag<NbtList>("current_mobs", out var current))
        {
            var names = current.OfType<NbtArray<int>>()
                .Select(uuid => mobs.NameOf(EntityNbt.UuidFromInts(uuid.GetArray())))
                .Order(StringComparer.Ordinal)
                .ToList();
            var list = new NbtList(NbtTagType.String, "current_mobs");
            foreach (var name in names)
                list.Add(new NbtTag<string>(string.Empty, name));

            saved.Remove("current_mobs");
            saved.Add(list);
        }

        return saved;
    }

    private static string Canonical(string snbt) => Snbt.Write(SnbtReader.ReadCompound(snbt));

    // The block entity data the listener got for the spawner, in order.
    private static List<string> ClientData(TestClient client, Vector origin) => client.SentPackets
        .OfType<BlockEntityDataPacket>()
        .Where(packet => packet.Position == origin)
        .Select(packet => Snbt.Write(packet.NBTData))
        .ToList();

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
    private static List<string> Entities(AbstractLevel level, Vector origin, MobLog mobs)
    {
        var min = (VectorD)(origin + new Vector(-Reach, -Below, -Reach));
        var max = (VectorD)(origin + new Vector(Reach + 1, Above + 1, Reach + 1));
        return level.GetNonPlayerEntitiesInRange((VectorD)origin, 32)
            .OfType<Entity>()
            .Where(entity => Overlaps(entity, min, max))
            .OrderBy(entity => entity.EntityId)
            .Select(entity => Describe(entity, origin, mobs))
            .ToList();
    }

    private static bool Overlaps(Entity entity, VectorD min, VectorD max)
    {
        var box = entity.CurrentBox;
        return box.Min.X < max.X && box.Max.X > min.X
            && box.Min.Y < max.Y && box.Max.Y > min.Y
            && box.Min.Z < max.Z && box.Max.Z > min.Z;
    }

    // An entity as the probe describes it: type and offset, then a mob's yaw, name and what it holds and wears, its
    // health, whether it must persist, and the item it is.
    private static string Describe(Entity entity, Vector origin, MobLog mobs)
    {
        var at = entity.Position - (VectorD)origin;
        var text = $"{EntityNbt.TypeId(entity)} {Number(at.X, 3)},{Number(at.Y, 3)},{Number(at.Z, 3)}";
        if (entity is Mob mob)
            text += $" yaw {Number(mob.Yaw, 2)} ({mobs.NameOf(mob.Uuid)}){Worn(mob)}";
        if (entity is Living living)
            text += $" health {Number(Math.Max(0, living.Health), 2)}";
        if (entity is Mob { PersistenceRequired: true })
            text += " persistent";
        if (entity is ItemEntity item)
            text += $" item {item.Item.Holder.UnlocalizedName} x{item.Item.Count}";

        return text;
    }

    // What a mob holds and wears, by vanilla's slot names in vanilla's slot order, as the probe lists them.
    private static string Worn(Mob mob)
    {
        (EquipmentSlot Slot, string Name)[] slots =
        [
            (EquipmentSlot.MainHand, "mainhand"),
            (EquipmentSlot.OffHand, "offhand"),
            (EquipmentSlot.Boots, "feet"),
            (EquipmentSlot.Leggings, "legs"),
            (EquipmentSlot.Chestplate, "chest"),
            (EquipmentSlot.Helmet, "head"),
            (EquipmentSlot.Body, "body"),
            (EquipmentSlot.Saddle, "saddle")
        ];

        var text = "";
        foreach (var (slot, name) in slots)
        {
            if (mob.GetEquipment(slot) is { IsAir: false, Count: > 0 } worn)
                text += $" {name} {worn.Holder.UnlocalizedName} x{worn.Count}";
        }

        return text;
    }

    // The sounds the listener heard, at the entity's position for those that follow one. Mobs' ambient sounds are left
    // out, as the probe leaves them out (their own randoms decide them).
    private static List<string> Sounds(AbstractLevel level, TestClient client, Vector origin)
    {
        var sounds = new List<string>();
        foreach (var packet in client.SentPackets)
        {
            var (id, position, volume) = packet switch
            {
                SoundPacket sound => (sound.SoundLocation, SoundPosition(sound), sound.Volume),
                SoundEntityPacket sound => (sound.SoundLocation, Quantized(EntityPosition(level, sound.EntityId)),
                    sound.Volume),
                _ => (null, default(VectorD?), 0f)
            };
            if (id is null || id.EndsWith(".ambient", StringComparison.Ordinal))
                continue;

            var at = position is VectorD point ? point - (VectorD)origin : new VectorD(double.NaN, 0, 0);
            sounds.Add($"{id} {Number(at.X, 3)},{Number(at.Y, 3)},{Number(at.Z, 3)} volume {Number(volume, 2)}");
        }

        return sounds;
    }

    // Where a sound following an entity is heard, to the eighth of a block vanilla's positioned sound carries (cast
    // towards zero), which is how vanilla sends a mob's sounds.
    private static VectorD? Quantized(VectorD? position) => position is VectorD point
        ? new VectorD((int)(point.X * 8) / 8d, (int)(point.Y * 8) / 8d, (int)(point.Z * 8) / 8d)
        : null;

    private static VectorD SoundPosition(SoundPacket sound) =>
        new(sound.SoundPosition.X / 8d, sound.SoundPosition.Y / 8d, sound.SoundPosition.Z / 8d);

    private static VectorD? EntityPosition(AbstractLevel level, int entityId) =>
        level.GetNonPlayerEntitiesInRange(level.LevelData.SpawnPosition, float.MaxValue)
            .FirstOrDefault(entity => entity.EntityId == entityId)?.Position;

    private static List<string> LevelEvents(TestClient client, Vector origin) => client.SentPackets
        .OfType<LevelEventPacket>()
        .Select(levelEvent => $"{levelEvent.Type} {Offset(levelEvent.Position, origin)} {levelEvent.Data}")
        .ToList();

    // Vanilla sends a trial spawner no particles itself (its level events are drawn as particles by clients), so any
    // Obsidian sends are named by their count and place.
    private static List<string> Particles(TestClient client, Vector origin) => client.SentPackets
        .OfType<LevelParticlesPacket>()
        .Select(particle => particle.Position - (VectorD)origin)
        .Select(at => $"particles at {Number(at.X, 3)},{Number(at.Y, 3)},{Number(at.Z, 3)}")
        .ToList();

    // A game event as the probe's listener records it: its name, the block it's in and its source.
    private static string GameEvent(MobVibration vibration, Vector origin)
    {
        var source = vibration.Source is Entity entity ? EntityNbt.TypeId(entity) : "none";
        var block = (Vector)vibration.Position.Floor();
        return $"{vibration.Kind.Identifier()} {Offset(block, origin)} {source}";
    }

    private static List<string> Strings(JsonElement array) =>
        array.EnumerateArray().Select(value => value.GetString()!).ToList();

    private static void Compare(List<string> differences, string what, List<string> vanilla, List<string> obsidian)
    {
        if (vanilla.SequenceEqual(obsidian))
            return;

        var expected = string.Join("; ", vanilla);
        var actual = string.Join("; ", obsidian);
        differences.Add($"{what}: vanilla [{expected}], Obsidian [{actual}]");
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

        if (properties.Count == 0)
            return state.UnlocalizedName;

        return $"{state.UnlocalizedName}[{string.Join(",", properties)}]";
    }

    private static string Offset(Vector position, Vector origin)
    {
        var offset = position - origin;
        return $"{offset.X},{offset.Y},{offset.Z}";
    }

    private static string Number(double value, int decimals) =>
        value.ToString("F" + decimals, CultureInfo.InvariantCulture);

    private static bool IsChamber(JsonElement probe) =>
        probe.TryGetProperty("kind", out var kind) && kind.GetString() == "chamber";

    private static string Name(JsonElement probe) => probe.GetProperty("name").GetString()!;

    private static JsonElement LoadFixture()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Obsidian.Tests.Assets.trial_spawners.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// The mobs a case's spawner spawned, in the order they appeared, with the tick each appeared at: the probe names
    /// mobs by this order, as their UUIDs come from their own randoms.
    /// </summary>
    private sealed class MobLog
    {
        private readonly List<(Mob Mob, int Appeared)> mobs = [];

        public int Count => this.mobs.Count;

        public (Mob Mob, int Appeared) this[int ordinal] => this.mobs[ordinal];

        /// <summary>Adds the mobs that appeared in the case's chunk this tick, in the order they were added.</summary>
        public void Observe(AbstractLevel level, Vector origin, int tick)
        {
            var found = level.GetNonPlayerEntitiesInRange((VectorD)origin, 400)
                .OfType<Mob>()
                .Where(mob => (int)Math.Floor(mob.Position.X) >> 4 == origin.X >> 4
                    && (int)Math.Floor(mob.Position.Z) >> 4 == origin.Z >> 4)
                .OrderBy(mob => mob.EntityId);
            foreach (var mob in found)
            {
                if (!this.mobs.Any(known => ReferenceEquals(known.Mob, mob)))
                    this.mobs.Add((mob, tick));
            }
        }

        public string NameOf(Guid uuid)
        {
            var ordinal = this.mobs.FindIndex(known => known.Mob.Uuid == uuid);
            return ordinal < 0 ? "unknown" : $"mob {ordinal + 1}";
        }
    }

    /// <summary>
    /// One server whose overworld has empty, complete plains chunks where the cases run, random ticks off and time
    /// still, with the case's players and a spectator listener.
    /// </summary>
    private sealed class TrialWorld : IAsyncDisposable
    {
        private readonly RealServerContext context;
        private readonly int caseCount;
        private Listener current;

        private TrialWorld(RealServerContext context, int caseCount)
        {
            this.context = context;
            this.caseCount = caseCount;
        }

        public AbstractLevel Level => this.context.World;

        public static async Task<TrialWorld> CreateAsync(int caseCount)
        {
            var context = TestServerFactory.CreateServer($"trial-spawners-{Guid.NewGuid():N}");
            var world = context.World;
            world.LevelData.GameRules["random_tick_speed"] = "0";
            world.LevelData.GameRules["advance_time"] = "false";
            world.LevelData.GameRules["advance_weather"] = "false";
            world.LevelData.Raining = false;
            world.LevelData.Thundering = false;

            Assert.True(CodecRegistry.TryGetBiome("minecraft:plains", out var plains));
            var replay = new TrialWorld(context, caseCount);
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

            // Loaded and ticking, so their block entities tick.
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

        /// <summary>The case's players, with the probe's UUIDs, standing where the case puts them.</summary>
        public List<Entities.Player> AddPlayers(JsonElement probe, Vector origin)
        {
            var players = new List<Entities.Player>();
            if (!probe.TryGetProperty("players", out var specs))
                return players;

            foreach (var spec in specs.EnumerateArray())
            {
                var position = (VectorD)origin + BlockProbes.Point(spec.GetProperty("pos"));
                var uuid = Guid.Parse(spec.GetProperty("uuid").GetString()!);
                var player = this.Join(spec.GetProperty("name").GetString()!, position, uuid);
                player.GameMode = spec.TryGetProperty("gameMode", out var mode)
                    ? Enum.Parse<GameMode>(mode.GetString()!, ignoreCase: true)
                    : GameMode.Survival;
                players.Add(player);
            }

            return players;
        }

        public void RemovePlayers(List<Entities.Player> players)
        {
            foreach (var player in players)
                this.Level.Players.TryRemove(player.Uuid, out _);
        }

        /// <summary>
        /// A new spectator at <paramref name="position"/> that has every chunk, in place of the last.
        /// </summary>
        public Listener Listen(VectorD position, string name)
        {
            if (this.current is Listener previous)
                this.Level.Players.TryRemove(previous.Player.Uuid, out _);

            var player = this.Join(name.Length > 16 ? name[..16] : name, position, Guid.NewGuid());
            player.GameMode = GameMode.Spectator;
            var client = Assert.IsType<TestClient>(player.Client);
            client.ClearPackets();
            this.current = new Listener(player, client);
            return this.current;
        }

        private Entities.Player Join(string name, VectorD position, Guid uuid)
        {
            var player = TestServerFactory.CreateConnectedPlayer(this.context.Server, this.context.World, name,
                position, uuid);
            player.Level = this.Level;
            foreach (var (chunkX, chunkZ) in this.Chunks())
                player.LoadedChunks.Add(NumericsHelper.IntsToLong(chunkX, chunkZ));

            this.Level.Players.TryAdd(player.Uuid, player);
            return player;
        }

        /// <summary>
        /// Clears a case's area and its entities without updates, so that nothing it left draws from the level's
        /// random in the cases after it.
        /// </summary>
        public async Task ClearAsync(Vector origin)
        {
            foreach (var position in Area(origin))
                this.Level.BlockUpdates.SetBlock(position, BlocksRegistry.Air, Quiet);

            var entities = this.Level.GetNonPlayerEntitiesInRange((VectorD)origin, 400)
                .OfType<Entity>()
                .Where(entity => (int)Math.Floor(entity.Position.X) >> 4 == origin.X >> 4
                    && (int)Math.Floor(entity.Position.Z) >> 4 == origin.Z >> 4)
                .ToList();
            foreach (var entity in entities)
                await entity.RemoveAsync();
        }

        public async ValueTask DisposeAsync() => await this.context.DisposeAsync();
    }

    private sealed record Listener(Entities.Player Player, TestClient Client);
}
