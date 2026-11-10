using Obsidian.API;
using Obsidian.API.Advancements;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Loot;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.Utilities;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using ServerPlayer = Obsidian.Entities.Player;

namespace Obsidian.Tests;

/// <summary>
/// Replays vanilla's projectile block hits (<c>Assets/projectile_block_hits.json</c>, recorded by
/// <c>ProjectileHitFixtures.java</c>): each case's projectile is spawned already moving at a block, and the level
/// ticks the case's ticks. After each tick the blocks that changed, the signal east of the block, the entities, what a
/// bystander hears, the game events, the ordered block writes with their flags, the blocks destroyed, and the owner's
/// criteria and statistics are compared with vanilla's, from the first tick.
/// </summary>
[Collection(nameof(PluginTagFiles))]
public sealed class VanillaProjectileHits(ITestOutputHelper output)
{
    // How far from the target the compared area reaches, and the entities are looked for, as in the capture.
    private const int Reach = 2;

    // Ticks run after each case's area is cleared, as in the capture, so that its pending ticks find air.
    private const int DrainTicks = 30;

    // Setup and clearing, like the capture's: no neighbor or shape updates and nothing placed reacting.
    private const BlockUpdateFlags Quiet = BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace;

    // The fields of a tick's record, in the capture's order.
    private static readonly string[] fields =
    [
        "changes", "signal", "entities", "sounds", "levelEvents", "blockEvents", "gameEvents", "writes", "destroyed",
        "criteria", "stats"
    ];

    // Vanilla's coordinates: offset shapes (pointed dripstone) depend on position.
    private static readonly Vector target = new(0, 64, 0);

    /// <summary>
    /// Differences from vanilla known by exact case and field, with the reason
    /// (<c>docs/known-gaps/projectile-block-hits.md</c>). Each must still differ, so a pin fails once its
    /// difference goes.
    /// </summary>
    private static readonly Dictionary<(string Case, string Field), string> pinned = new()
    {
        [("splash_potion_target", "levelEvents")] = PotionColour,
        [("splash_potion_chorus_flower", "levelEvents")] = PotionColour,
        [("experience_bottle_target", "gameEvents")] = "experience orbs don't emit hit_ground as they land",
        [("shulker_bullet_target", "gameEvents")] =
            "the bullet stops at the block, where vanilla's moves into it first, so entity_damage is a block higher",
        [("fishing_bobber_target", "gameEvents")] = BobberRehits,
        [("fishing_bobber_target", "stats")] = BobberRehits,
        [("flaming_arrow_waterlogged_campfire", "sounds")] = "water doesn't put out a burning arrow",
        [("channeling_trident_lightning_rod", "changes")] = Lightning,
        [("channeling_trident_lightning_rod", "signal")] = Lightning,
        [("channeling_trident_lightning_rod", "levelEvents")] = Lightning,
        [("channeling_trident_lightning_rod", "gameEvents")] = Lightning,
        [("channeling_trident_lightning_rod", "writes")] = Lightning,
        [("arrow_oak_button", "sounds")] = ButtonContact,
        [("arrow_oak_button", "gameEvents")] = ButtonContact,
        [("spectral_arrow_oak_button", "sounds")] = ButtonContact,
        [("spectral_arrow_oak_button", "gameEvents")] = ButtonContact,
        [("trident_oak_button", "sounds")] = ButtonContact,
        [("trident_oak_button", "gameEvents")] = ButtonContact,
    };

    private const string PotionColour = "the splash's particles have a fixed colour, not the potion's";

    private const string BobberRehits =
        "vanilla's grounded bobber hits the block again on ticks 2 and 4; Obsidian's rests on it";

    private const string Lightning = "the summoned lightning bolt doesn't strike: no rod power, sparks or event";

    private const string ButtonContact =
        "the button is pressed in the contact pass after the arrow's tick, not as the arrow moves, before its hit";

    private sealed record Case(string Name, JsonElement Input, JsonElement[] Ticks);

    private sealed record Harness(RealServerContext Context, ServerPlayer Owner, ServerPlayer Bystander);

    /// <summary>
    /// What the level reported while a case's ticks ran: its game events, block writes and destructions.
    /// </summary>
    private sealed class Observed
    {
        public List<string> GameEvents { get; } = [];

        public List<string> Writes { get; } = [];

        public List<string> Destroyed { get; } = [];

        public void Clear()
        {
            this.GameEvents.Clear();
            this.Writes.Clear();
            this.Destroyed.Clear();
        }
    }

    [Fact]
    public async Task ReplayProjectileHitReference()
    {
        using var stream = typeof(VanillaProjectileHits).Assembly
            .GetManifestResourceStream("Obsidian.Tests.Assets.projectile_block_hits.json")!;
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        Assert.Equal(ServerConstants.ProtocolDescription, root.GetProperty("version").GetString());

        var cases = root.GetProperty("cases").EnumerateArray().Select(ReadCase).ToArray();
        var harness = await StartAsync(root);
        await using var context = harness.Context;

        var previous = AdvancementsRegistry.Current.Values.ToArray();
        try
        {
            await FixtureCases.AssertAllAsync(cases, hit => hit.Name, hit => ReplayAsync(harness, hit, previous));
        }
        finally
        {
            AdvancementsRegistry.Apply(previous);
        }

        output.WriteLine($"Replayed {cases.Length} projectile hits.");
    }

    // One world of empty chunks under open sky, the owner and a bystander standing where the capture's stood, with
    // vanilla's fixed rules: easy, and mob griefing off.
    private static async Task<Harness> StartAsync(JsonElement root)
    {
        var context = TestServerFactory.CreateServer($"projectile-hits-{Guid.NewGuid():N}");
        var world = context.World;

        // The area's chunks and a ring around them, all tracked, since a chunk's block ticks run only once its
        // neighbors are loaded and complete.
        for (var chunkX = -2; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -2; chunkZ <= 1; chunkZ++)
            {
                var chunk = WorldBlockEntities.AddEmptyChunk(world, chunkX, chunkZ);
                FillSkyLight(chunk);
                world.Fluids.Track(chunk);
                await world.GetBlockAsync(new Vector(chunkX * 16, 64, chunkZ * 16));
            }
        }

        world.LevelData.Difficulty = Difficulty.Easy;
        world.LevelData.GameRules["mob_griefing"] = "false";

        var owner = AddPlayer(context, "Owner", Offset(root.GetProperty("owner")));
        var bystander = AddPlayer(context, "Bystander", Offset(root.GetProperty("bystander")));
        return new Harness(context, owner, bystander);
    }

    private static ServerPlayer AddPlayer(RealServerContext context, string name, VectorD offset)
    {
        var world = context.World;
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, name, (VectorD)target + offset);
        player.GameMode = GameMode.Survival;
        world.Players.TryAdd(player.Uuid, player);
        for (var chunkX = -2; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -2; chunkZ <= 1; chunkZ++)
                player.LoadedChunks.Add(NumericsHelper.IntsToLong(chunkX, chunkZ));
        }

        return player;
    }

    // The capture's world is air under open sky, so its sky light is full everywhere the cases reach.
    private static void FillSkyLight(Chunk chunk)
    {
        for (var y = target.Y - Reach - 2; y <= target.Y + Reach + 2; y++)
        {
            for (var z = 0; z < 16; z++)
            {
                for (var x = 0; x < 16; x++)
                    chunk.SetLightLevel(x, y, z, LightType.Sky, 15);
            }
        }
    }

    private async Task ReplayAsync(Harness harness, Case hit, Advancement[] previous)
    {
        var world = harness.Context.World;
        await ResetAsync(harness);

        var input = hit.Input;
        ApplyRules(world, input, apply: true);
        try
        {
            world.LevelData.Raining = Bool(input, "thundering");
            world.LevelData.Thundering = Bool(input, "thundering");
            foreach (var setup in input.GetProperty("setup").EnumerateArray())
            {
                var state = VanillaSulfurBlocks.ParseState(setup.GetProperty("state").GetString()!);
                world.BlockUpdates.SetBlock(target + Position(setup.GetProperty("offset")), state, Quiet);
            }

            PrepareOwner(harness.Owner, input);
            RegisterCriteria(hit, previous);
            var projectile = Spawn(world, harness.Owner, input);
            var differences = await RunAsync(harness, hit, projectile);
            Assert.True(differences.Count == 0, string.Join("; ", differences));
        }
        finally
        {
            ApplyRules(world, input, apply: false);
        }
    }

    // Ticks the case and compares each tick's record with vanilla's, leaving out the pinned fields, which must differ.
    private static async Task<List<string>> RunAsync(Harness harness, Case hit, Entity projectile)
    {
        var world = harness.Context.World;
        var observed = new Observed();
        var differences = new List<string>();
        var pinnedDiffer = new HashSet<string>();
        Observe(world, observed, harness.Owner);
        try
        {
            var before = Snapshot(world);
            var done = Criteria(harness.Owner, hit.Input);
            foreach (var expected in hit.Ticks)
            {
                var actual = await TickAsync(harness, hit, projectile, observed, before, done);
                before = Snapshot(world);
                done = Criteria(harness.Owner, hit.Input);
                Compare(hit.Name, expected, actual, differences, pinnedDiffer);
            }
        }
        finally
        {
            Observe(world, null, harness.Owner);
        }

        foreach (var ((name, field), reason) in pinned)
        {
            if (name == hit.Name && !pinnedDiffer.Contains(field))
                differences.Add($"pinned {field} ({reason}) now matches vanilla: remove the pin");
        }

        return differences;
    }

    // One game tick of the world, then its record, the way the capture writes it.
    private static async Task<Dictionary<string, string>> TickAsync(Harness harness, Case hit, Entity projectile,
        Observed observed, IBlock[] before, string[] done)
    {
        var world = harness.Context.World;
        observed.Clear();
        ((TestClient)harness.Bystander.Client).ClearPackets();
        var statistics = harness.Owner.ProgressStatistics.Save();

        await world.DoWorldTickAsync();
        await harness.Context.Server.BackgroundWork.DrainAsync();

        var criteria = Criteria(harness.Owner, hit.Input).Except(done);
        var awarded = Awarded(statistics, harness.Owner.ProgressStatistics.Save());
        var record = new Dictionary<string, string>
        {
            ["changes"] = Changes(world, before),
            ["signal"] = world.GetBestNeighborSignal(target + new Vector(1, 0, 0)).ToString(),
            ["entities"] = Join(Entities(world, projectile)),
            ["writes"] = Join(observed.Writes),
            ["destroyed"] = Join(observed.Destroyed),
            ["gameEvents"] = Join(observed.GameEvents),
            ["criteria"] = Join(criteria),
            ["stats"] = Join(awarded)
        };
        AddHeard(record, harness.Bystander);
        return record;
    }

    // Each field of the tick that differs from vanilla's, unless pinned; a pinned field that differs is noted.
    private static void Compare(string name, JsonElement expected, Dictionary<string, string> actual,
        List<string> differences, HashSet<string> pinnedDiffer)
    {
        var tick = expected.GetProperty("tick").GetInt32();
        foreach (var field in fields)
        {
            var wanted = Expected(expected, field);
            if (wanted == actual[field])
                continue;

            if (pinned.ContainsKey((name, field)))
            {
                pinnedDiffer.Add(field);
                continue;
            }

            differences.Add($"tick {tick} {field}: expected [{wanted}], got [{actual[field]}]");
        }
    }

    // Hooks the level's observations of game events, writes and destructions into observed, or unhooks them.
    private static void Observe(WorldData.World world, Observed? observed, ServerPlayer owner)
    {
        if (observed is null)
        {
            world.GameEventQueued = null;
            world.Fluids.BlockStored = null;
            world.BlockDestroyed = null;
            return;
        }

        world.GameEventQueued = vibration => observed.GameEvents.Add(GameEvent(vibration, owner));
        world.Fluids.BlockStored = (position, block, flags) =>
            observed.Writes.Add($"{Offset(position)}={Text(block)} flags {(int)flags}");
        world.BlockDestroyed = (position, block, drops) =>
            observed.Destroyed.Add($"{Offset(position)}={Text(block)} drops {(drops ? "true" : "false")}");
    }

    // Clears the area and its entities, and lets work the last case scheduled find air, as the capture does.
    private static async Task ResetAsync(Harness harness)
    {
        var world = harness.Context.World;
        await ClearAsync(world);
        world.LevelData.Raining = false;
        world.LevelData.Thundering = false;
        for (var tick = 0; tick < DrainTicks; tick++)
            await world.DoWorldTickAsync();

        await ClearAsync(world);
        await harness.Context.Server.BackgroundWork.DrainAsync();
        ((TestClient)harness.Bystander.Client).ClearPackets();
    }

    private static async Task ClearAsync(WorldData.World world)
    {
        for (var y = -Reach - 1; y <= Reach + 1; y++)
        {
            for (var z = -Reach - 1; z <= Reach + 1; z++)
            {
                for (var x = -Reach - 1; x <= Reach + 1; x++)
                    world.BlockUpdates.SetBlock(target + new Vector(x, y, z), BlocksRegistry.Air, Quiet);
            }
        }

        await world.RunQueuedEntityActionsAsync();
        foreach (var entity in world.GetEntitiesInRange((VectorD)target, Reach + 8).ToArray())
        {
            if (entity is not ServerPlayer)
                await world.DestroyEntityAsync(entity);
        }
    }

    private static void ApplyRules(WorldData.World world, JsonElement input, bool apply)
    {
        if (!input.TryGetProperty("rules", out var rules))
            return;

        foreach (var rule in rules.EnumerateObject())
        {
            var value = rule.Value.GetBoolean();
            var applied = apply ? value : !value;
            world.LevelData.GameRules[rule.Name] = applied ? "true" : "false";
        }
    }

    private static void PrepareOwner(ServerPlayer owner, JsonElement input)
    {
        for (var slot = 9; slot <= 45; slot++)
            owner.Inventory.SetItem(slot, null);

        if (OptionalString(input, "ownerHolds") is string held)
            owner.Inventory.SetItem(owner.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.Get(held), 1));
    }

    private static void RegisterCriteria(Case hit, Advancement[] previous)
    {
        if (!hit.Input.TryGetProperty("criteria", out var criteria))
        {
            AdvancementsRegistry.Apply(previous);
            return;
        }

        using var definition = JsonDocument.Parse("{\"criteria\":" + criteria.GetRawText() + "}");
        var advancement = AdvancementsRegistry.Parse("probe:" + hit.Name, definition.RootElement);
        AdvancementsRegistry.Apply(previous.Append(advancement));
    }

    // The case's criteria the owner has so far.
    private static string[] Criteria(ServerPlayer owner, JsonElement input)
    {
        if (!input.TryGetProperty("criteria", out var criteria))
            return [];

        var name = "probe:" + input.GetProperty("name").GetString();
        return criteria.EnumerateObject()
            .Select(criterion => criterion.Name)
            .Where(criterion => owner.Advancements.HasCriterion(name, criterion))
            .ToArray();
    }

    // The case's projectile, as the capture spawned it: at its start, moving at the target, owned by the owner unless
    // the case says "none".
    private static Entity Spawn(WorldData.World world, ServerPlayer owner, JsonElement input)
    {
        var id = input.GetProperty("projectile").GetString()!;
        var start = (VectorD)target + Offset(input.GetProperty("from"));
        var motion = Offset(input.GetProperty("motion"));
        var shooter = OptionalString(input, "owner") == "none" ? null : owner;

        if (id == "minecraft:fishing_bobber")
            return CastBobber(world, owner, start, motion);

        var projectile = Create(world, input, shooter, start, motion);
        projectile.EntityId = Server.GetNextEntityId();
        projectile.Position = start;
        projectile.Motion = motion;
        if (input.TryGetProperty("noGravity", out var noGravity))
            projectile.NoGravity = noGravity.GetBoolean();

        if (Bool(input, "burning") && projectile is Arrow burning)
        {
            burning.FireSeconds = 5;
            burning.Burning = true;
        }

        Assert.True(world.TrySpawnEntity(projectile, hostOwned: true));
        return projectile;
    }

    private static Entity Create(WorldData.World world, JsonElement input, ServerPlayer? shooter, VectorD start,
        VectorD motion)
    {
        var id = input.GetProperty("projectile").GetString()!;
        var direction = motion / motion.Magnitude;
        return id switch
        {
            "minecraft:arrow" => new Arrow { Level = world, Type = EntityType.Arrow, Owner = shooter },
            "minecraft:spectral_arrow" => new SpectralArrow { Level = world, Owner = shooter },
            "minecraft:trident" => new Trident
            {
                Level = world,
                Type = EntityType.Trident,
                Owner = shooter,
                Weapon = Trident(input)
            },
            "minecraft:egg" => new Egg
            {
                Level = world,
                Owner = shooter,
                HatchRandom = new LegacyRandomSource(input.GetProperty("seed").GetInt64())
            },
            "minecraft:ender_pearl" => new EnderPearl { Level = world, Owner = shooter },
            "minecraft:experience_bottle" => new ThrownExperienceBottle { Level = world, Owner = shooter },
            "minecraft:shulker_bullet" => new ShulkerBullet { Level = world, Owner = shooter },
            "minecraft:wither_skull" => WithAcceleration(new WitherSkull { Level = world, Owner = shooter }, direction),
            "minecraft:dragon_fireball" => WithAcceleration(
                new DragonFireball(world) { OwnerUuid = shooter?.Uuid ?? Guid.Empty }, direction),
            "minecraft:breeze_wind_charge" => WithOwner(new BreezeWindCharge(world), shooter),
            _ => new MobProjectile(shooter!, MobProjectileType(id), start, direction, (float)motion.Magnitude)
        };
    }

    // The thrown trident's item, with the case's enchantments (Channeling is the only one the cases use).
    private static ItemStack Trident(JsonElement input)
    {
        var trident = new ItemStack(ItemsRegistry.Trident);
        if (!input.TryGetProperty("enchantments", out var enchantments))
            return trident;

        var applied = enchantments.EnumerateObject()
            .Select(enchantment => new Enchantment
            {
                Id = enchantment.Name == "minecraft:channeling"
                    ? EnchantmentsRegistry.Channeling.Id
                    : throw new ArgumentException($"No enchantment {enchantment.Name} in the replay."),
                Level = enchantment.Value.GetInt32()
            })
            .ToArray();
        EnchantmentHelper.SetEnchantments(trident, applied);
        return trident;
    }

    private static EntityType MobProjectileType(string id) => id switch
    {
        "minecraft:snowball" => EntityType.Snowball,
        "minecraft:llama_spit" => EntityType.LlamaSpit,
        "minecraft:splash_potion" => EntityType.SplashPotion,
        "minecraft:fireball" => EntityType.Fireball,
        "minecraft:small_fireball" => EntityType.SmallFireball,
        _ => throw new ArgumentException($"No projectile {id}.")
    };

    // The capture's hurting projectiles accelerate by 0.1 along their motion, vanilla's default accelerationPower.
    private static Entity WithAcceleration(Entity projectile, VectorD direction)
    {
        SetField(projectile, "acceleration", direction * 0.1);
        return projectile;
    }

    private static Entity WithOwner(BreezeWindCharge charge, ServerPlayer? shooter)
    {
        SetField(charge, "owner", shooter);
        SetField(charge, "ownerUuid", shooter?.Uuid ?? Guid.Empty);
        return charge;
    }

    private static void SetField(object instance, string name, object? value)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(instance.GetType().Name, name);
        field.SetValue(instance, value);
    }

    // A bobber cast by the owner, which then starts where the case's does.
    private static Entity CastBobber(WorldData.World world, ServerPlayer owner, VectorD start, VectorD motion)
    {
        var hook = FishingHook.Cast(owner, world, 0, 0);
        Assert.NotNull(hook);
        Assert.True(world.TryMoveEntity(hook, hook.Position, start));
        hook.Position = start;
        hook.Motion = motion;
        return hook;
    }

    // What the bystander heard during the tick, the way the capture writes it.
    private static void AddHeard(Dictionary<string, string> record, ServerPlayer bystander)
    {
        var heard = ((TestClient)bystander.Client).SentPackets.ToArray();
        var sounds = heard.OfType<SoundPacket>().Select(sound => sound.SoundLocation);
        var levelEvents = heard.OfType<LevelEventPacket>()
            .Select(levelEvent => $"{levelEvent.Type} {Offset(levelEvent.Position)} {levelEvent.Data}");
        var blockEvents = heard.OfType<BlockEventPacket>()
            .Select(blockEvent =>
            {
                var block = BlocksRegistry.GetDefault(blockEvent.BlockType).UnlocalizedName;
                return $"{block} {blockEvent.ActionId} {blockEvent.ActionParam} {Offset(blockEvent.Position)}";
            });

        record["sounds"] = Join(sounds);
        record["levelEvents"] = Join(levelEvents);
        record["blockEvents"] = Join(blockEvents);
    }

    private static string GameEvent(MobVibration gameEvent, ServerPlayer owner)
    {
        var source = gameEvent.Source switch
        {
            null => "none",
            ServerPlayer player when ReferenceEquals(player, owner) => "player",
            Entity entity => EntityNbt.TypeId(entity),
            _ => "unknown"
        };

        return $"{gameEvent.Kind.Identifier()} {Offset((Vector)gameEvent.Position.Floor())} {source}";
    }

    // The entities whose boxes reach into the area, sorted: items as "item <id> x<count>", experience orbs by type
    // alone, the case's projectile as "projectile <cell>", others as "<type> <cell>". Players are left out: vanilla's
    // aren't entities of the level's sections.
    private static IEnumerable<string> Entities(WorldData.World world, Entity projectile)
    {
        var min = (VectorD)target - new VectorD(Reach + 1);
        var max = (VectorD)target + new VectorD(Reach + 2);
        var found = new List<string>();
        foreach (var entity in world.GetEntitiesInRange((VectorD)target, Reach + 8).OfType<Entity>())
        {
            var box = entity.Dimension.CreateBBFromPosition(entity.Position);
            var inside = box.Max.X > min.X && box.Min.X < max.X
                && box.Max.Y > min.Y && box.Min.Y < max.Y
                && box.Max.Z > min.Z && box.Min.Z < max.Z;
            if (!inside || entity is ServerPlayer)
                continue;

            var cell = Offset((Vector)entity.Position.Floor());
            var text = entity switch
            {
                ItemEntity item => $"item {item.Item.Holder.UnlocalizedName} x{item.Item.Count}",
                ExperienceOrb => "minecraft:experience_orb",
                _ when ReferenceEquals(entity, projectile) => $"projectile {cell}",
                _ => $"{EntityNbt.TypeId(entity)} {cell}"
            };

            if (text != "minecraft:experience_orb" || !found.Contains(text))
                found.Add(text);
        }

        return found.Order(StringComparer.Ordinal);
    }

    // Each statistic awarded, as "type value amount".
    private static IEnumerable<string> Awarded(JsonObject before, JsonObject after)
    {
        foreach (var (type, values) in after["stats"]!.AsObject())
        {
            foreach (var (identifier, value) in values!.AsObject())
            {
                var previous = before["stats"]?[type]?[identifier]?.GetValue<int>() ?? 0;
                var amount = value!.GetValue<int>() - previous;
                if (amount != 0)
                    yield return $"{type} {identifier} {amount}";
            }
        }
    }

    private static IBlock[] Snapshot(WorldData.World world) => Area().Select(world.BlockUpdates.GetBlock).ToArray();

    // Each block of the area that changed since before, as dx,dy,dz=state, the way the capture writes them.
    private static string Changes(WorldData.World world, IBlock[] before)
    {
        var changed = new List<string>();
        var index = 0;
        foreach (var position in Area())
        {
            var state = world.BlockUpdates.GetBlock(position);
            if (!state.IsSameState(before[index]))
                changed.Add($"{Offset(position)}={Text(state)}");

            index++;
        }

        return string.Join(';', changed);
    }

    // The compared area in the capture's order: y, then z, then x ascending.
    private static IEnumerable<Vector> Area()
    {
        for (var y = -Reach; y <= Reach; y++)
        {
            for (var z = -Reach; z <= Reach; z++)
            {
                for (var x = -Reach; x <= Reach; x++)
                    yield return target + new Vector(x, y, z);
            }
        }
    }

    // A tick's recorded field as the replay writes its own: lists joined, a signal of 0 and nothing at all as "".
    private static string Expected(JsonElement tick, string field)
    {
        if (!tick.TryGetProperty(field, out var value))
            return field == "signal" ? "0" : "";

        return value.ValueKind switch
        {
            JsonValueKind.Array => Join(value.EnumerateArray().Select(entry => entry.GetString()!)),
            JsonValueKind.Number => value.GetInt32().ToString(),
            _ => value.GetString()!
        };
    }

    private static string Join(IEnumerable<string> values) => string.Join(" | ", values);

    // The block and the properties it doesn't share with its default state, sorted by name like vanilla's.
    private static string Text(IBlock state)
    {
        var defaults = BlockStateProperties.GetState(state.UnlocalizedName).GetProperties();
        var properties = state.GetProperties()
            .Where(pair => defaults.GetValueOrDefault(pair.Key) != pair.Value)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value}")
            .ToArray();

        if (properties.Length == 0)
            return state.UnlocalizedName;

        return $"{state.UnlocalizedName}[{string.Join(',', properties)}]";
    }

    private static string Offset(Vector position)
    {
        var offset = position - target;
        return $"{offset.X},{offset.Y},{offset.Z}";
    }

    private static Case ReadCase(JsonElement input) => new(
        input.GetProperty("name").GetString()!,
        input,
        [.. input.GetProperty("record").EnumerateArray()]);

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.GetBoolean();

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static Vector Position(JsonElement values) =>
        new(values[0].GetInt32(), values[1].GetInt32(), values[2].GetInt32());

    private static VectorD Offset(JsonElement values) =>
        new(values[0].GetDouble(), values[1].GetDouble(), values[2].GetDouble());
}
