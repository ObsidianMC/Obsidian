#nullable enable

using Obsidian.API;
using Obsidian.API.Advancements;
using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Nbt;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using ServerPlayer = Obsidian.Entities.Player;

namespace Obsidian.Tests;

/// <summary>Replays the food and harvest capture through the packet dispatcher, comparing every recorded field.</summary>
[Collection(nameof(PluginTagFiles))]
public sealed class VanillaFoodHarvest(ITestOutputHelper output)
{
    // How far from the target the compared area reaches, as in the capture.
    private const int Reach = 2;

    // Setup and clearing, like the capture's: no neighbor or shape updates and nothing placed reacting.
    private const BlockUpdateFlags Quiet = BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace;

    private static readonly Vector target = new(0, 64, 0);

    // Every deviation is pinned to one exact case and field, and must disappear when parity improves.
    private static readonly Dictionary<(string Case, string Field), string> knownDifferences = new()
    {
        [("hive_bottle_creative_full_false", "hand")] = "minecraft:glass_bottle x2",
        [("hive_bottle_creative_full_false", "inventory")] =
            "0=minecraft:glass_bottle x2, 1=minecraft:honey_bottle x1",
        [("hive_bottle_creative_full_true", "hand")] = "minecraft:glass_bottle x2",
        [("hive_bottle_creative_full_true", "inventory")] = "0=minecraft:glass_bottle x2, "
            + string.Join(", ", Enumerable.Range(1, 35).Select(slot => $"{slot}=minecraft:stone x64"))
    };

    private sealed record Harness(RealServerContext Context, ServerPlayer User, ServerPlayer Bystander);

    [Fact]
    public async Task ReplayFoodAndHarvestReference()
    {
        using var stream = typeof(VanillaFoodHarvest).Assembly
            .GetManifestResourceStream("Obsidian.Tests.Assets.food_and_harvest.json")!;
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        Assert.Equal(ServerConstants.ProtocolDescription, root.GetProperty("version").GetString());

        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        var harness = await StartAsync(root);
        await using var context = harness.Context;

        var previous = AdvancementsRegistry.Current.Values.ToArray();
        try
        {
            await FixtureCases.AssertAllAsync(cases, Name, async use =>
            {
                var differences = await WithCriteriaAsync(use, previous, () => ReplayAsync(harness, use));
                Assert.True(differences.Count == 0, string.Join("; ", differences));
            });
        }
        finally
        {
            AdvancementsRegistry.Apply(previous);
        }

        output.WriteLine($"Replayed {cases.Length} uses.");
    }

    // The case's criteria, as an advancement of its own while it runs.
    private static async Task<List<string>> WithCriteriaAsync(JsonElement use, Advancement[] previous,
        Func<Task<List<string>>> replay)
    {
        if (!use.TryGetProperty("criteria", out var criteria))
            return await replay();

        using var definition = JsonDocument.Parse("{\"criteria\":" + criteria.GetRawText() + "}");
        var advancement = AdvancementsRegistry.Parse("probe:" + Name(use), definition.RootElement);
        AdvancementsRegistry.Apply(previous.Append(advancement));
        try
        {
            return await replay();
        }
        finally
        {
            AdvancementsRegistry.Apply(previous);
        }
    }

    // One world with full sky light, as the capture's open sky has, the user where the capture's player stood and a
    // bystander above the target.
    private static async Task<Harness> StartAsync(JsonElement root)
    {
        var context = TestServerFactory.CreateServer($"food-harvest-{Guid.NewGuid():N}");
        var world = context.World;

        var chunk = WorldBlockEntities.AddEmptyChunk(world);
        WorldBlockEntities.AddEmptyChunk(world, -1, -1);
        WorldBlockEntities.AddEmptyChunk(world, -1, 0);
        WorldBlockEntities.AddEmptyChunk(world, 0, -1);
        foreach (var corner in new[] { target, new(-1, 64, -1), new(-1, 64, 0), new(0, 64, -1) })
            await world.GetBlockAsync(corner);

        for (var y = target.Y - Reach - 1; y <= target.Y + Reach + 1; y++)
        {
            for (var z = 0; z < 16; z++)
            {
                for (var x = 0; x < 16; x++)
                    chunk.SetLightLevel(x, y, z, LightType.Sky, 15);
            }
        }

        var standing = root.GetProperty("player");
        var userPosition = new VectorD(standing[0].GetDouble(), standing[1].GetDouble(), standing[2].GetDouble());
        var user = TestServerFactory.CreateConnectedPlayer(context.Server, world, "User", userPosition);
        user.Yaw = standing[3].GetSingle();
        user.Pitch = standing[4].GetSingle();

        var bystanderPosition = (VectorD)target + new VectorD(0.5, 3, 0.5);
        var bystander = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Bystander", bystanderPosition);
        bystander.GameMode = GameMode.Spectator;
        foreach (var player in new[] { user, bystander })
        {
            world.Players.TryAdd(player.Uuid, player);
            player.LoadedChunks.Add(0);
        }

        return new Harness(context, user, bystander);
    }

    private static async Task<List<string>> ReplayAsync(Harness harness, JsonElement use)
    {
        var world = harness.Context.World;
        var user = harness.User;

        // The case's blocks in a cleared area, and the user's hands, mode and sneaking as the case has them.
        await ClearAsync(world);
        foreach (var setup in use.GetProperty("setup").EnumerateArray())
        {
            var state = VanillaSulfurBlocks.ParseState(setup.GetProperty("state").GetString()!);
            var position = target + Position(setup.GetProperty("offset"));
            world.BlockUpdates.SetBlock(position, state, Quiet);
            if (OptionalString(setup, "nbt") is string snbt)
            {
                var blockEntity = Assert.IsAssignableFrom<BlockEntity>(world.GetResidentBlockEntity(position));
                blockEntity.Load(SnbtReader.ReadCompound(snbt));
            }
        }

        Prepare(user, use);
        var seed = use.TryGetProperty("randomSeed", out var randomSeed) ? randomSeed.GetInt64() : 1234;
        world.GrowthRandom = new LegacyRandomSource(seed);
        foreach (var table in new[] { "harvest/sweet_berry_bush", "harvest/cave_vine", "harvest/beehive", "carve/pumpkin" })
            BlockLoot.SequencesFor(world).Get("minecraft:" + table).SetSeed(seed);

        if (use.TryGetProperty("nearbyBee", out _))
        {
            var bee = new Bee
            {
                Level = world,
                EntityId = Server.GetNextEntityId(),
                Position = (VectorD)target + new VectorD(1.5, 0, 0.5)
            };
            world.SpawnEntity(bee);
        }
        var before = Snapshot(world);
        var statistics = user.ProgressStatistics.Save();
        ((TestClient)user.Client).ClearPackets();
        ((TestClient)harness.Bystander.Client).ClearPackets();
        world.TakeGameEvents();

        // The use, the block updates it caused and the tick's block events, with every write observed.
        var writes = new List<string>();
        var destroyed = new List<string>();
        world.Fluids.BlockStored = (position, block, flags) => writes.Add(Write(position, block, flags));
        world.BlockDestroyed = (position, block, drops) => destroyed.Add(Destroyed(position, block, drops));
        UseResult? result;
        try
        {
            result = await Packet(use).UseAsync(harness.Context.Server, user);
            await world.RunQueuedEntityActionsAsync();
            await world.BlockNotifications.DrainAsync();
            await world.ProcessBlockEventsAsync();
        }
        finally
        {
            world.Fluids.BlockStored = null;
            world.BlockDestroyed = null;
        }

        var actual = Observe(harness, use, result, before, statistics, writes, destroyed);
        return Compare(Name(use), Expected(use), actual);
    }

    private static UseItemOnPacket Packet(JsonElement use)
    {
        var cursor = use.TryGetProperty("cursor", out var point)
            ? new VectorF(point[0].GetSingle(), point[1].GetSingle(), point[2].GetSingle())
            : new VectorF(0.5f, 0.5f, 0.5f);
        var face = Enum.Parse<BlockFace>(use.GetProperty("face").GetString()!, ignoreCase: true);
        var packet = BlockUpdates.UseOn(target + Position(use.GetProperty("clicked")), face, cursor);
        if (UsesOffhand(use))
        {
            var hand = typeof(UseItemOnPacket).GetProperty(nameof(UseItemOnPacket.Hand))!;
            hand.SetValue(packet, InteractionHand.OffHand);
        }

        return packet;
    }

    private static void Prepare(ServerPlayer user, JsonElement use)
    {
        var mode = use.TryGetProperty("gameMode", out var gameMode) ? gameMode.GetString()! : "survival";
        user.GameMode = Enum.Parse<GameMode>(mode, ignoreCase: true);
        user.FoodLevel = use.TryGetProperty("food", out var food) ? food.GetInt32() : 20;
        user.FoodSaturationLevel = use.TryGetProperty("saturation", out var saturation) ? saturation.GetSingle() : 5;
        user.Sneaking = use.TryGetProperty("sneaking", out var sneaking) && sneaking.GetBoolean();
        for (var slot = 9; slot <= 45; slot++)
            user.Inventory.SetItem(slot, null);

        var count = use.TryGetProperty("count", out var held) ? held.GetInt32() : 2;
        user.Inventory.SetItem(user.CurrentHeldItemSlot, Stack(OptionalString(use, "item"), count));
        var offhandCount = use.TryGetProperty("offhandCount", out var offhand) ? offhand.GetInt32() : 2;
        user.Inventory.SetItem(45, Stack(OptionalString(use, "offhand"), offhandCount));
        if (use.TryGetProperty("damage", out var damage))
        {
            var stack = UsesOffhand(use) ? user.GetOffHandItem() : user.GetHeldItem();
            stack![DataComponentType.Damage] = ComponentBuilder.Damage with { Value = damage.GetInt32() };
        }
        if (use.TryGetProperty("fullInventory", out var full) && full.GetBoolean())
        {
            for (var slot = 9; slot <= 44; slot++)
            {
                if (slot != user.CurrentHeldItemSlot)
                    user.Inventory.SetItem(slot, new ItemStack(ItemsRegistry.Stone, 64));
            }
        }
    }

    private static ItemStack? Stack(string? item, int count) =>
        item is null ? null : new ItemStack(ItemsRegistry.Get(item), count);

    // What the capture records, read from the reference, field by field in the form Observe gives them.
    private static Dictionary<string, string> Expected(JsonElement use)
    {
        var result = use.GetProperty("result").GetString()!;
        var expected = new Dictionary<string, string>
        {
            ["food"] = use.GetProperty("foodAfter").GetInt32().ToString(CultureInfo.InvariantCulture),
            ["saturation"] = use.GetProperty("saturationAfter").GetSingle().ToString(CultureInfo.InvariantCulture),
            ["inventory"] = List(Strings(use.GetProperty("inventory"))),
            ["smokey"] = use.GetProperty("smokey").GetBoolean().ToString(),
            ["bees"] = Set(Strings(use.GetProperty("bees"))),
            ["result"] = result,
            ["swings"] = Swings(result is "success" or "success_server", result == "success_server"),
            ["hand"] = use.GetProperty("hand").GetString()!,
            ["changes"] = use.GetProperty("changes").GetString()!,
            ["entities"] = Set(Strings(use.GetProperty("entities"))),
            ["stats"] = Set(Strings(use.GetProperty("stats"))),
            ["sounds"] = List(Strings(use.GetProperty("sounds"))),
            ["sound details"] = List(Strings(use.GetProperty("soundDetails"))),
            ["sound packets"] = List(use.GetProperty("soundPackets").EnumerateArray().Select(SoundFields)),
            ["level events"] = List(Strings(use.GetProperty("levelEvents"))),
            ["block events"] = List(Strings(use.GetProperty("blockEvents"))),
            ["particles"] = List(Strings(use.GetProperty("particles"))),
            ["game events"] = List(Strings(use.GetProperty("gameEvents"))),
            ["user effects"] = List(Strings(use.GetProperty("userEffects"))),
            ["writes"] = List(use.GetProperty("writes").EnumerateArray().Select(write => Write(
                target + Position(write.GetProperty("offset")),
                VanillaSulfurBlocks.ParseState(write.GetProperty("state").GetString()!),
                (BlockUpdateFlags)write.GetProperty("flags").GetInt32()))),
            ["destroyed"] = List(use.GetProperty("destroyed").EnumerateArray().Select(block => Destroyed(
                target + Position(block.GetProperty("offset")),
                VanillaSulfurBlocks.ParseState(block.GetProperty("state").GetString()!),
                block.GetProperty("drops").GetBoolean())))
        };

        if (use.TryGetProperty("awardedCriteria", out var criteria))
            expected["criteria"] = Set(Strings(criteria));

        if (use.TryGetProperty("dropPositions", out var positions))
            expected["drop positions"] = Set(Strings(positions));

        return expected;
    }

    // What Obsidian did, in the capture's forms.
    private static Dictionary<string, string> Observe(Harness harness, JsonElement use, UseResult? result,
        IBlock[] before, JsonObject statistics, List<string> writes, List<string> destroyed)
    {
        var world = harness.Context.World;
        var user = harness.User;
        var heard = ((TestClient)harness.Bystander.Client).SentPackets.ToArray();
        var received = ((TestClient)user.Client).SentPackets.ToArray();
        var hand = UsesOffhand(use) ? user.GetOffHandItem() : user.GetHeldItem();

        var observed = new Dictionary<string, string>
        {
            ["food"] = user.FoodLevel.ToString(CultureInfo.InvariantCulture),
            ["saturation"] = user.FoodSaturationLevel.ToString(CultureInfo.InvariantCulture),
            ["inventory"] = List(Inventory(user)),
            ["smokey"] = CampfireSmoke.Reaches(world, target).ToString(),
            ["bees"] = Set(Bees(world, user)),
            ["result"] = Name(result),
            ["swings"] = Swings(heard.OfType<SwingAnimationPacket>().Any(),
                received.OfType<SwingAnimationPacket>().Any()),
            ["hand"] = Text(hand),
            ["changes"] = Changes(world, before),
            ["entities"] = Set(Entities(world)),
            ["stats"] = Set(Awarded(statistics, user.ProgressStatistics.Save())),
            ["sounds"] = List(heard.OfType<SoundPacket>().Select(sound => sound.SoundLocation)),
            ["sound details"] = List(heard.OfType<SoundPacket>().Select(SoundDetail)),
            ["sound packets"] = List(heard.OfType<SoundPacket>().Select(sound => SoundFields(
                sound.SoundLocation, sound.SoundPosition.X, sound.SoundPosition.Y, sound.SoundPosition.Z,
                sound.Volume, sound.Pitch, sound.Category.ToString().ToLowerInvariant()))),
            ["level events"] = List(heard.OfType<LevelEventPacket>().Select(LevelEvent)),
            ["block events"] = List(heard.OfType<BlockEventPacket>().Select(BlockEvent)),
            ["particles"] = List(heard.OfType<LevelParticlesPacket>().Select(Particle)),
            ["game events"] = List(world.TakeGameEvents().Select(gameEvent => GameEvent(gameEvent, user))),
            ["user effects"] = List(received.Select(UserEffect).OfType<string>()),
            ["writes"] = List(writes),
            ["destroyed"] = List(destroyed)
        };

        if (use.TryGetProperty("criteria", out var criteria))
        {
            var awarded = criteria.EnumerateObject()
                .Select(criterion => criterion.Name)
                .Where(name => user.Advancements.HasCriterion("probe:" + Name(use), name));
            observed["criteria"] = Set(awarded);
        }

        if (use.TryGetProperty("dropPositions", out _))
            observed["drop positions"] = Set(DropPositions(world));

        return observed;
    }

    private static IEnumerable<string> Inventory(ServerPlayer user)
    {
        for (var slot = 0; slot < 41; slot++)
        {
            var mapped = slot switch
            {
                < 9 => slot + 36,
                < 36 => slot,
                40 => 45,
                _ => 44 - slot
            };
            if (user.Inventory.GetItem(mapped) is ItemStack { Count: > 0 } item)
                yield return $"{slot}={Text(item)}";
        }
    }

    private static IEnumerable<string> Bees(WorldData.World world, ServerPlayer user)
    {
        if (world.GetResidentBlockEntity(target) is DataBlockEntity hive)
        {
            var occupants = hive.SnapshotTag<NbtList>("bees")?.Count ?? 0;
            yield return $"occupants={occupants}";
        }

        foreach (var bee in world.GetEntitiesInRange((VectorD)target, 10).OfType<Bee>())
        {
            var targeted = ReferenceEquals(bee.AttackTarget, user).ToString().ToLowerInvariant();
            var angry = (bee.AngerTicks > 0).ToString().ToLowerInvariant();
            yield return $"bee target={targeted} angry={angry}";
        }
    }

    private static IEnumerable<string> DropPositions(WorldData.World world)
    {
        foreach (var item in world.GetEntitiesInRange((VectorD)target, 5).OfType<ItemEntity>())
        {
            var offset = item.Position - (VectorD)target;
            yield return Text(item.Item) + " at " + Coordinate(offset.X) + "," + Coordinate(offset.Y)
                + "," + Coordinate(offset.Z);
        }
    }

    private static string Coordinate(double value)
    {
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        return value == Math.Truncate(value) ? text + ".0" : text;
    }

    private static List<string> Compare(string name, Dictionary<string, string> expected,
        Dictionary<string, string> actual)
    {
        var differences = new List<string>();
        foreach (var (field, value) in expected)
        {
            if (knownDifferences.TryGetValue((name, field), out var known))
            {
                if (actual[field] != known || value == known)
                    differences.Add($"{field}: the known difference changed; vanilla [{value}], got [{actual[field]}]");
            }
            else if (actual[field] != value)
            {
                differences.Add($"{field}: expected [{value}], got [{actual[field]}]");
            }
        }

        return differences;
    }

    // Who was shown a swing: vanilla swings for the players around after a success, and for the user too when its
    // client didn't predict it.
    private static string Swings(bool bystander, bool user) => $"bystander={bystander}, user={user}";

    private static string? UserEffect(IClientboundPacket packet) => packet switch
    {
        SoundPacket sound => "sound " + sound.SoundLocation,
        LevelEventPacket levelEvent => "level_event " + LevelEvent(levelEvent),
        BlockEventPacket blockEvent => "block_event " + BlockEvent(blockEvent),
        _ => null
    };

    private static string LevelEvent(LevelEventPacket levelEvent) =>
        $"{levelEvent.Type} {Offset(levelEvent.Position)} {levelEvent.Data}";

    private static string BlockEvent(BlockEventPacket blockEvent)
    {
        var block = BlocksRegistry.GetDefault(blockEvent.BlockType).UnlocalizedName;
        return $"{block} {blockEvent.ActionId} {blockEvent.ActionParam} {Offset(blockEvent.Position)}";
    }

    // The block a sound plays in and its volume. Sound positions travel in eighths of a block.
    private static string SoundDetail(SoundPacket sound)
    {
        var position = new Vector(
            (int)Math.Floor(sound.SoundPosition.X / 8.0),
            (int)Math.Floor(sound.SoundPosition.Y / 8.0),
            (int)Math.Floor(sound.SoundPosition.Z / 8.0));
        var volume = sound.Volume.ToString("0.0####", CultureInfo.InvariantCulture);
        return $"{sound.SoundLocation} {Offset(position)} {volume}";
    }

    private static string SoundFields(JsonElement sound) => SoundFields(
        sound.GetProperty("id").GetString()!, sound.GetProperty("x").GetInt32(),
        sound.GetProperty("y").GetInt32(), sound.GetProperty("z").GetInt32(),
        sound.GetProperty("volume").GetSingle(), sound.GetProperty("pitch").GetSingle(),
        sound.GetProperty("category").GetString()!);

    private static string SoundFields(string id, int x, int y, int z, float volume, float pitch, string category) =>
        $"{id} {x},{y},{z} {volume.ToString(CultureInfo.InvariantCulture)} "
        + $"{pitch.ToString(CultureInfo.InvariantCulture)} {category}";

    private static string Particle(LevelParticlesPacket particle) =>
        $"minecraft:{particle.Data.ParticleType.ToString().ToSnakeCase()} {particle.ParticleCount}";

    private static string GameEvent(MobVibration gameEvent, ServerPlayer user)
    {
        var position = (Vector)gameEvent.Position.Floor();
        var source = gameEvent.Source switch
        {
            null => "none",
            ServerPlayer player when ReferenceEquals(player, user) => "player",
            Entity entity => EntityNbt.TypeId(entity),
            _ => "unknown"
        };

        return $"{gameEvent.Kind.Identifier()} {Offset(position)} {source}";
    }

    private static string Name(UseResult? result) => result switch
    {
        UseResult.Success => "success",
        UseResult.SuccessServer => "success_server",
        UseResult.Consume => "consume",
        UseResult.Fail => "fail",
        _ => "pass"
    };

    // Each statistic the use awarded, as "type value amount".
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

    // The entities in the area other than players: items as "item <id> x<count>", others by type.
    private static IEnumerable<string> Entities(WorldData.World world)
    {
        var center = (VectorD)target + new VectorD(0.5, 0.5, 0.5);
        var min = (VectorD)target - new VectorD(Reach + 1, Reach + 1, Reach + 1);
        var max = (VectorD)target + new VectorD(Reach + 2, Reach + 2, Reach + 2);
        return world.GetEntitiesInRange(center, Reach + 3)
            .OfType<Entity>()
            .Where(entity => entity is not ServerPlayer)
            .Where(entity => entity.Position.X >= min.X && entity.Position.X <= max.X
                && entity.Position.Y >= min.Y && entity.Position.Y <= max.Y
                && entity.Position.Z >= min.Z && entity.Position.Z <= max.Z)
            .Select(entity => entity is ItemEntity item ? "item " + Text(item.Item) : EntityNbt.TypeId(entity));
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

        for (var depth = 4; depth <= 7; depth++)
            world.BlockUpdates.SetBlock(target + new Vector(0, -depth, 0), BlocksRegistry.Air, Quiet);

        await world.RunQueuedEntityActionsAsync();
        foreach (var entity in world.GetEntitiesInRange((VectorD)target, Reach + 4).ToArray())
        {
            if (entity is not ServerPlayer)
                await world.DestroyEntityAsync(entity);
        }
    }

    private static IBlock[] Snapshot(WorldData.World world) => Area().Select(world.BlockUpdates.GetBlock).ToArray();

    // Each block of the area that changed, as dx,dy,dz=state relative to the target, the way the capture writes them.
    private static string Changes(WorldData.World world, IBlock[] before)
    {
        var changed = Area()
            .Select((position, index) => (position, index))
            .Where(entry => !world.BlockUpdates.GetBlock(entry.position).IsSameState(before[entry.index]))
            .Select(entry => $"{Offset(entry.position)}={Text(world.BlockUpdates.GetBlock(entry.position))}");

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

    // An item stack as "<id> x<count>", with its damage when it has any, or "empty".
    private static string Text(ItemStack? stack)
    {
        if (stack is not { Count: > 0 } || stack.Holder.Id == ItemsRegistry.Air.Id)
            return "empty";

        var text = $"{stack.Holder.UnlocalizedName} x{stack.Count}";
        return stack.Damage > 0 ? $"{text} damage={stack.Damage}" : text;
    }

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

    private static string Write(Vector position, IBlock block, BlockUpdateFlags flags) =>
        $"{Offset(position)}={Text(block)} flags {(int)flags}";

    private static string Destroyed(Vector position, IBlock block, bool drops) =>
        $"{Offset(position)}={Text(block)} drops {drops}";

    private static string Offset(Vector position)
    {
        var offset = position - target;
        return $"{offset.X},{offset.Y},{offset.Z}";
    }

    // Lists keep their order; sets (entities, statistics, criteria) are sorted, since neither side fixes theirs.
    private static string List(IEnumerable<string> values) => string.Join(", ", values);

    private static string Set(IEnumerable<string> values) => List(values.Order(StringComparer.Ordinal));

    private static bool UsesOffhand(JsonElement use) => OptionalString(use, "handUsed") == "offhand";

    private static string Name(JsonElement use) => use.GetProperty("name").GetString()!;

    private static string[] Strings(JsonElement values) =>
        [.. values.EnumerateArray().Select(value => value.GetString()!)];

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static Vector Position(JsonElement values) =>
        new(values[0].GetInt32(), values[1].GetInt32(), values[2].GetInt32());
}
