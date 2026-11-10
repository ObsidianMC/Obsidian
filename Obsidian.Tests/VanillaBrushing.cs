using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Utilities;
using Obsidian.ChunkData;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using ServerPlayer = Obsidian.Entities.Player;

namespace Obsidian.Tests;

/// <summary>
/// Replays vanilla's brushing (<c>Assets/brushing.json</c>, recorded by <c>BrushingFixtures.java</c>): the click
/// through the use-item-on packet, then each tick as the probe ran it (the game time, the block ticks, the area's
/// entities, then the player), comparing every recorded tick: the writes with their flags, what a player standing by
/// is sent (sounds, level events, particles, entity events, block entity data), the game events, statistics,
/// advancement criteria, the used hand, whether the player is still using it, the block entity's saved data and the
/// entities that appear or go.
/// </summary>
/// <remarks>
/// The click's result is compared through what it decides (the statistic, the use starting). Item entities' yaw and
/// the sounds' seeds come from randoms Obsidian doesn't share and aren't recorded.
/// </remarks>
[Collection(nameof(PluginTagFiles))]
public sealed class VanillaBrushing(ITestOutputHelper output)
{
    // How far from the target the area that is cleared and compared reaches, as in the capture.
    private const int Reach = 4;

    private const BlockUpdateFlags Quiet = BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace;

    private static readonly Vector target = new(8, 64, 8);

    private static readonly System.Reflection.MethodInfo UseTick = typeof(PlayerBase).GetMethod(
        "TickWeaponUseAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

    // Ticks a case's leftovers get to run out on cleared air before the next case starts.
    private const int DrainTicks = 6;

    // Game events vanilla sends that Obsidian knowingly doesn't, by case and tick: item entities and falling blocks
    // don't emit hit_ground when they land (known-gaps/brushing-and-armor-stands.md). Each fails once it is sent.
    private static readonly (string Case, int Tick, string GameEvent)[] missingGameEvents =
    [
        ("sand_top_completes_with_loot", 101, "minecraft:hit_ground 0,1,0 minecraft:item"),
        ("falls_after_a_stroke", 19, "minecraft:hit_ground 0,-3,0 minecraft:falling_block")
    ];

    private sealed record Harness(RealServerContext Context, ServerPlayer User, ServerPlayer Bystander, VectorD Shift);

    [Fact]
    public async Task ReplayBrushingReference()
    {
        using var stream = typeof(VanillaBrushing).Assembly
            .GetManifestResourceStream("Obsidian.Tests.Assets.brushing.json")!;
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        Assert.Equal(ServerConstants.ProtocolDescription, root.GetProperty("version").GetString());

        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        var harness = await StartAsync(root);
        await using var context = harness.Context;

        var previous = AdvancementsRegistry.Current.Values.ToArray();
        try
        {
            await FixtureCases.AssertAllAsync(cases, use => use.GetProperty("name").GetString()!, async use =>
            {
                RegisterCriteria(previous, use);
                var actual = await ReplayAsync(harness, root, use);
                var name = use.GetProperty("name").GetString()!;
                var differences = Compare(Pinned(name, use.GetProperty("timeline"), actual), actual).ToList();
                Assert.True(differences.Count == 0, string.Join("; ", differences));
            });
        }
        finally
        {
            AdvancementsRegistry.Apply(previous);
        }

        output.WriteLine($"Replayed {cases.Length} brushing cases.");
    }

    private static async Task<Harness> StartAsync(JsonElement root)
    {
        var context = TestServerFactory.CreateServer($"brushing-{Guid.NewGuid():N}");
        var world = context.World;
        // Scheduled block ticks run only in a chunk whose neighbours are loaded too.
        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
            {
                var empty = new Chunk(chunkX, chunkZ);
                empty.SetChunkStatus(ChunkGenStage.full);
                world.LoadRegionByChunk(chunkX, chunkZ).SetChunk(empty);
                await world.GetChunkAsync(chunkX, chunkZ, scheduleGeneration: false);
            }
        }

        var chunk = (Chunk)world.GetLoadedChunk(0, 0)!;
        await world.GetBlockAsync(target);
        for (var y = target.Y - Reach - 1; y <= target.Y + Reach + 1; y++)
        {
            for (var z = 0; z < 16; z++)
            {
                for (var x = 0; x < 16; x++)
                    chunk.SetLightLevel(x, y, z, LightType.Sky, 15);
            }
        }

        var shift = (VectorD)(target - Position(root.GetProperty("target")));
        var user = TestServerFactory.CreateConnectedPlayer(context.Server, world, "User", (VectorD)target);
        var bystanderPosition = (VectorD)target + new VectorD(0.5, 3, 0.5);
        var bystander = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Bystander", bystanderPosition);
        world.Players.TryAdd(user.Uuid, user);
        world.Players.TryAdd(bystander.Uuid, bystander);
        user.LoadedChunks.Add(0);
        bystander.LoadedChunks.Add(0);

        return new Harness(context, user, bystander, shift);
    }

    private static void RegisterCriteria(IEnumerable<API.Advancements.Advancement> previous, JsonElement use)
    {
        AdvancementsRegistry.Apply(previous);
        if (!use.TryGetProperty("criteria", out var criteria))
            return;

        var name = use.GetProperty("name").GetString()!;
        using var definition = JsonDocument.Parse("{\"criteria\":" + criteria.GetRawText() + "}");
        var advancement = AdvancementsRegistry.Parse("probe:" + name, definition.RootElement);
        AdvancementsRegistry.Apply(previous.Append(advancement));
    }

    // The case from a cleared area, tick by tick as the probe ran it, keeping the ticks the probe would keep.
    private static async Task<List<JsonObject>> ReplayAsync(Harness harness, JsonElement root, JsonElement use)
    {
        var world = harness.Context.World;
        var user = harness.User;
        await ResetAsync(harness);
        Setup(world, use);
        Prepare(harness, root, use);

        // The new block entity's first data goes out before the case starts, as in the capture.
        await world.TickBlockEntitiesAsync();

        var writes = new List<string>();
        world.Fluids.BlockStored = (position, block, flags) =>
            writes.Add($"{Offset(position)} {Text(block)} {(int)flags}");

        var timeline = new List<JsonObject>();
        var known = KnownEntities(world);
        var saved = SavedData(world);
        var hand = Hand(use);
        var name = use.GetProperty("name").GetString()!;
        var criteria = Criteria(use);
        string? lastHand = null;
        var lastUsing = false;
        try
        {
            var ticks = use.GetProperty("ticks").GetInt32();
            for (var tick = 0; tick <= ticks; tick++)
            {
                Forget(harness);
                writes.Clear();
                var statistics = user.ProgressStatistics.Save();
                var doneBefore = Done(user, name, criteria);

                string? result = null;
                foreach (var action in Actions(use, tick))
                    result = await ActAsync(harness, action) ?? result;

                if (tick == 0)
                    result = await ClickAsync(harness, use, hand);
                else
                    await TickAsync(harness);

                var record = Record(harness, tick, writes, statistics, doneBefore, name, criteria, hand);
                if (result is not null)
                    record["result"] = result;

                var data = SavedData(world);
                if (!NbtJson.Same(data, saved))
                    record["blockEntity"] = data?.DeepClone();

                saved = data;
                if (ClientData(harness.Bystander) is JsonNode client)
                    record["clientData"] = client;

                record["entities"] = new JsonArray(EntityChanges(world, harness, known).Select(Node).ToArray());

                var handText = record["hand"]!.GetValue<string>();
                var stillUsing = record["using"]!.GetValue<bool>();
                var changed = handText != lastHand || stillUsing != lastUsing || tick == 0;
                lastHand = handText;
                lastUsing = stillUsing;
                if (changed || Meaningful(record))
                    timeline.Add(record);
            }
        }
        finally
        {
            world.Fluids.BlockStored = null;
            user.StopUsingItem();
        }

        return timeline;
    }

    // Clears the last case: its use, its blocks, its entities, and its pending ticks, which run out on air.
    private static async Task ResetAsync(Harness harness)
    {
        var world = harness.Context.World;
        harness.User.StopUsingItem();
        for (var y = -Reach - 1; y <= Reach + 1; y++)
        {
            for (var z = -Reach - 1; z <= Reach + 1; z++)
            {
                for (var x = -Reach - 1; x <= Reach + 1; x++)
                    world.BlockUpdates.SetBlock(target + new Vector(x, y, z), BlocksRegistry.Air, Quiet);
            }
        }

        for (var drain = 0; drain < DrainTicks; drain++)
        {
            world.LevelData.GameTime++;
            world.Fluids.Tick();
        }

        await world.RunQueuedEntityActionsAsync();
        foreach (var entity in world.GetNonPlayerEntitiesInRange((VectorD)target, Reach + 6).ToArray())
            await world.DestroyEntityAsync(entity);

        await world.TickBlockEntitiesAsync();
    }

    private static void Setup(WorldData.World world, JsonElement use)
    {
        foreach (var placement in use.GetProperty("setup").EnumerateArray())
        {
            var state = VanillaSulfurBlocks.ParseState(placement.GetProperty("state").GetString()!);
            world.BlockUpdates.SetBlock(target + Position(placement.GetProperty("offset")), state, Quiet);
        }

        if (world.GetResidentBlockEntity(target) is not BrushableBlockEntity brushable)
            return;

        if (use.TryGetProperty("lootTable", out var table))
        {
            var seed = use.TryGetProperty("seed", out var value) ? value.GetInt64() : 0;
            brushable.SetLootTable(table.GetString()!, seed);
        }

        if (use.TryGetProperty("storedItem", out var stored))
        {
            var item = new ItemStack(ItemsRegistry.Get(stored.GetString()!));
            brushable.Load(new NbtCompound { item.ToNbt("item") });
        }
    }

    private static void Prepare(Harness harness, JsonElement root, JsonElement use)
    {
        var user = harness.User;
        var standing = use.TryGetProperty("player", out var own) ? own : root.GetProperty("player");
        var mode = use.TryGetProperty("gameMode", out var gameMode) ? gameMode.GetString()! : "survival";
        user.GameMode = Enum.Parse<GameMode>(mode, ignoreCase: true);
        user.Sneaking = false;
        user.Position = new VectorD(standing[0].GetDouble(), standing[1].GetDouble(), standing[2].GetDouble())
            + harness.Shift;
        user.Yaw = standing[3].GetSingle();
        user.Pitch = standing[4].GetSingle();
        user.CurrentHeldItemSlot = 0;
        for (var slot = 0; slot < 46; slot++)
            user.Inventory.SetItem(slot, null);

        var item = use.TryGetProperty("item", out var id) ? id.GetString()! : "minecraft:brush";
        var count = use.TryGetProperty("count", out var amount) ? amount.GetInt32() : 1;
        var held = new ItemStack(ItemsRegistry.Get(item), count);
        if (use.TryGetProperty("damage", out var damage))
            held[DataComponentType.Damage] =
                API.Inventory.DataComponents.ComponentBuilder.Damage with { Value = damage.GetInt32() };

        user.Inventory.SetItem(HandSlot(user, Hand(use)), held);
    }

    private static Task<string> ClickAsync(Harness harness, JsonElement use, InteractionHand hand)
    {
        var clicked = use.TryGetProperty("clicked", out var offset) ? Position(offset) : Vector.Zero;
        var face = Enum.Parse<BlockFace>(use.GetProperty("face").GetString()!, ignoreCase: true);
        return UseOnAsync(harness, target + clicked, face, hand);
    }

    // The click as its packet brings it, and its result as the probe names it.
    private static async Task<string> UseOnAsync(Harness harness, Vector clicked, BlockFace face,
        InteractionHand hand)
    {
        var cursor = new VectorF(0.5f, 0.5f, 0.5f) + (VectorF)face.ToVector() * 0.5f;
        var packet = BlockUpdates.UseOn(clicked, face, cursor);
        typeof(UseItemOnPacket).GetProperty(nameof(UseItemOnPacket.Hand))!.SetValue(packet, hand);
        var result = await packet.UseAsync(harness.Context.Server, harness.User);
        await harness.Context.World.RunQueuedEntityActionsAsync();
        return result switch
        {
            UseResult.Success => "success",
            UseResult.SuccessServer => "success_server",
            UseResult.Consume => "consume",
            UseResult.Fail => "fail",
            _ => "pass"
        };
    }

    // One tick as the probe ran it: the game time, the due block ticks, the area's entities, then the player, and
    // the level's end-of-tick block entity work.
    private static async Task TickAsync(Harness harness)
    {
        var world = harness.Context.World;
        world.LevelData.GameTime++;
        world.Fluids.Tick();
        await world.RunQueuedEntityActionsAsync();
        foreach (var entity in world.GetNonPlayerEntitiesInRange((VectorD)target, Reach + 6).ToArray())
            await entity.TickAsync();

        // Only the player's held use, as the probe ticks only LivingEntity.updatingUsingItem.
        await (ValueTask)UseTick.Invoke(harness.User, [])!;
        await world.RunQueuedEntityActionsAsync();
        await world.TickBlockEntitiesAsync();
    }

    // An action between ticks; a click returns its result.
    private static async Task<string?> ActAsync(Harness harness, JsonElement action)
    {
        var user = harness.User;
        var world = harness.Context.World;
        switch (action.GetProperty("kind").GetString())
        {
            case "look":
                user.Yaw = action.GetProperty("rotation")[0].GetSingle();
                user.Pitch = action.GetProperty("rotation")[1].GetSingle();
                break;
            case "move":
                var to = action.GetProperty("position");
                user.Position = new VectorD(to[0].GetDouble(), to[1].GetDouble(), to[2].GetDouble()) + harness.Shift;
                break;
            case "release":
                await user.ReleaseUsingItemAsync();
                break;
            case "set":
                var state = VanillaSulfurBlocks.ParseState(action.GetProperty("state").GetString()!);
                world.BlockUpdates.SetBlock(target + Position(action.GetProperty("offset")), state);
                break;
            case "select":
                // As SetCarriedItemPacket does: another slot ends the main hand's use.
                user.CurrentHeldItemSlot = (short)action.GetProperty("slot").GetInt32();
                user.StopUsingItem();
                break;
            case "click":
                var face = Enum.Parse<BlockFace>(action.GetProperty("face").GetString()!, ignoreCase: true);
                return await UseOnAsync(harness, target, face, InteractionHand.MainHand);
            default:
                throw new ArgumentException(action.GetRawText());
        }

        return null;
    }

    private static JsonObject Record(Harness harness, int tick, List<string> writes, JsonObject statistics,
        HashSet<string> doneBefore, string name, string[] criteria, InteractionHand hand)
    {
        var user = harness.User;
        var heard = ((TestClient)harness.Bystander.Client).SentPackets.ToArray();
        var own = ((TestClient)user.Client).SentPackets.ToArray();
        var sounds = heard.OfType<SoundPacket>().Select(sound => NbtJson.Sound(sound, target));
        var levelEvents = heard.OfType<LevelEventPacket>()
            .Select(levelEvent => $"{levelEvent.Type} {Offset(levelEvent.Position)} {levelEvent.Data}");
        var particles = heard.OfType<LevelParticlesPacket>().Select(particle => particle.GetType().Name);
        var entityEvents = heard.OfType<EntityEventPacket>()
            .Where(entityEvent => entityEvent.EntityId == user.EntityId)
            .Select(entityEvent => $"bystander {entityEvent.Event}")
            .Concat(own.OfType<EntityEventPacket>()
                .Where(entityEvent => entityEvent.EntityId == user.EntityId)
                .Select(entityEvent => $"user {entityEvent.Event}"));
        var gameEvents = harness.Context.World.TakeGameEvents().Select(gameEvent => GameEvent(gameEvent, user));
        var completed = Done(user, name, criteria).Where(criterion => !doneBefore.Contains(criterion))
            .Order(StringComparer.Ordinal);
        var held = hand == InteractionHand.OffHand ? user.GetOffHandItem() : user.GetHeldItem();

        return new JsonObject
        {
            ["tick"] = tick,
            ["writes"] = Array(writes),
            ["sounds"] = Array(sounds),
            ["levelEvents"] = Array(levelEvents),
            ["particles"] = Array(particles),
            ["entityEvents"] = Array(entityEvents),
            ["gameEvents"] = Array(gameEvents),
            ["stats"] = Array(Awarded(statistics, user.ProgressStatistics.Save())),
            ["criteria"] = Array(completed),
            ["hand"] = Text(held),
            ["using"] = user.LivingBitMask.HasFlag(LivingBitMask.HandActive)
        };
    }

    private static bool Meaningful(JsonObject record)
    {
        if (record.ContainsKey("blockEntity") || record.ContainsKey("clientData") || record.ContainsKey("result"))
            return true;

        string[] lists = ["writes", "sounds", "levelEvents", "particles", "entityEvents", "gameEvents", "stats",
            "criteria", "entities"];
        return lists.Any(list => record[list]!.AsArray().Count > 0);
    }

    // The captured timeline in the replay's forms, less the game events pinned as missing, dropping a tick left with
    // nothing to show; a pinned event Obsidian does send is reported as a difference.
    private static List<JsonObject> Pinned(string name, JsonElement timeline, List<JsonObject> actual)
    {
        var records = timeline.EnumerateArray().Select(Expected).ToList();
        foreach (var (_, tick, gameEvent) in missingGameEvents.Where(pin => pin.Case == name))
        {
            var sent = actual.Any(record => record["tick"]!.GetValue<int>() == tick
                && record["gameEvents"]!.AsArray().Any(node => node!.GetValue<string>() == gameEvent));
            if (sent)
                throw new Xunit.Sdk.XunitException($"tick {tick}: the pinned missing {gameEvent} is now sent");

            var record = records.Single(found => found["tick"]!.GetValue<int>() == tick);
            var events = record["gameEvents"]!.AsArray();
            events.Remove(events.Single(node => node!.GetValue<string>() == gameEvent));
            var previous = records.LastOrDefault(found => found["tick"]!.GetValue<int>() < tick);
            var same = previous is not null
                && NbtJson.Same(previous["hand"], record["hand"])
                && NbtJson.Same(previous["using"], record["using"]);
            if (same && !Meaningful(record))
                records.Remove(record);
        }

        return records;
    }

    // Compares each tick either side kept, field by field, in the probe's forms.
    private static IEnumerable<string> Compare(List<JsonObject> expected, List<JsonObject> actual)
    {
        var wanted = expected.ToDictionary(record => record["tick"]!.GetValue<int>());
        var got = actual.ToDictionary(record => record["tick"]!.GetValue<int>());
        foreach (var tick in wanted.Keys.Union(got.Keys).Order())
        {
            var expectedRecord = wanted.GetValueOrDefault(tick) ?? new JsonObject();
            var actualRecord = got.GetValueOrDefault(tick) ?? new JsonObject();
            string[] fields = ["result", "writes", "sounds", "levelEvents", "particles", "entityEvents", "gameEvents", "stats",
                "criteria", "hand", "using", "blockEntity", "clientData", "entities"];
            foreach (var field in fields)
            {
                var want = expectedRecord[field];
                var have = actualRecord[field];
                if (!NbtJson.Same(want, have))
                    yield return $"tick {tick} {field}: expected {NbtJson.Canonical(want)}, got {NbtJson.Canonical(have)}";
            }
        }
    }

    // A captured tick in the replay's forms: writes as "x,y,z state flags".
    private static JsonObject Expected(JsonElement record)
    {
        var node = JsonNode.Parse(record.GetRawText())!.AsObject();
        var writes = record.GetProperty("writes").EnumerateArray().Select(write =>
        {
            var pos = write.GetProperty("pos");
            var flags = write.GetProperty("flags");
            var flagText = flags.ValueKind == JsonValueKind.Null ? "null" : flags.GetInt32().ToString();
            return $"{pos[0].GetInt32()},{pos[1].GetInt32()},{pos[2].GetInt32()} "
                + $"{write.GetProperty("state").GetString()} {flagText}";
        });
        node["writes"] = Array(writes);

        // Obsidian adds no components to a block entity that has none, where vanilla saves them empty.
        if (node["blockEntity"] is JsonObject data && data["components"] is JsonObject { Count: 0 })
            data.Remove("components");

        return node;
    }

    // The block entity's saved data, as vanilla's saveWithoutMetadata gives it, or null without one.
    private static JsonNode? SavedData(WorldData.World world)
    {
        if (world.GetResidentBlockEntity(target) is not API.BlockEntities.BlockEntity blockEntity)
            return null;

        var tag = BlockEntityNbt.Save(blockEntity)!;
        foreach (var header in new[] { "id", "x", "y", "z", "keepPacked" })
            tag.Remove(header);

        if (tag.TryGetTag<NbtCompound>("components", out var components) && components.Count == 0)
            tag.Remove("components");

        return NbtJson.ToJson(tag);
    }

    // The block entity data the bystander was sent for the target this tick, if any.
    private static JsonNode? ClientData(ServerPlayer bystander)
    {
        var packet = ((TestClient)bystander.Client).SentPackets.OfType<BlockEntityDataPacket>()
            .LastOrDefault(data => data.Position == target);
        return packet is null ? null : NbtJson.ToJson(packet.NBTData);
    }

    private static Dictionary<int, string> KnownEntities(WorldData.World world) =>
        world.GetNonPlayerEntitiesInRange((VectorD)target, Reach + 6)
            .ToDictionary(entity => entity.EntityId, entity => EntityNbt.TypeId((Entity)entity));

    // Entities that appeared, described as the probe does, and those that went, as "removed <type>".
    private static List<string> EntityChanges(WorldData.World world, Harness harness, Dictionary<int, string> known)
    {
        var changes = new List<string>();
        var present = new HashSet<int>();
        foreach (var entity in world.GetNonPlayerEntitiesInRange((VectorD)target, Reach + 6).OfType<Entity>())
        {
            present.Add(entity.EntityId);
            if (known.TryAdd(entity.EntityId, EntityNbt.TypeId(entity)))
                changes.Add(Describe(entity, harness.Shift));
        }

        foreach (var (id, type) in known.ToArray())
        {
            if (present.Contains(id))
                continue;

            known.Remove(id);
            changes.Add("removed " + type);
        }

        return changes;
    }

    private static string Describe(Entity entity, VectorD shift)
    {
        var position = entity.Position - (VectorD)target;
        var at = $" at {NbtJson.Java(position.X)},{NbtJson.Java(position.Y)},{NbtJson.Java(position.Z)}";
        var motion = entity.Motion;
        var moving = $" motion {NbtJson.Java(motion.X)},{NbtJson.Java(motion.Y)},{NbtJson.Java(motion.Z)}";
        return entity switch
        {
            ItemEntity item => "item " + Text(item.Item) + at + moving,
            FallingBlock falling => "falling_block " + Text(falling.Block) + at + moving,
            _ => EntityNbt.TypeId(entity) + at
        };
    }

    private static string[] Criteria(JsonElement use) =>
        use.TryGetProperty("criteria", out var criteria)
            ? criteria.EnumerateObject().Select(criterion => criterion.Name).ToArray()
            : [];

    private static HashSet<string> Done(ServerPlayer user, string name, string[] criteria) =>
        criteria.Where(criterion => user.Advancements.HasCriterion("probe:" + name, criterion)).ToHashSet();

    private static IEnumerable<JsonElement> Actions(JsonElement use, int tick)
    {
        if (!use.TryGetProperty("actions", out var actions))
            return [];

        return actions.EnumerateArray().Where(action => action.GetProperty("tick").GetInt32() == tick);
    }

    private static void Forget(Harness harness)
    {
        ((TestClient)harness.User.Client).ClearPackets();
        ((TestClient)harness.Bystander.Client).ClearPackets();
        harness.Context.World.TakeGameEvents();
    }

    private static string GameEvent(MobVibration gameEvent, ServerPlayer user)
    {
        var source = gameEvent.Source switch
        {
            null => "none",
            ServerPlayer player when ReferenceEquals(player, user) => "player",
            Entity entity => EntityNbt.TypeId(entity),
            _ => "unknown"
        };

        return $"{gameEvent.Kind.Identifier()} {Offset((Vector)gameEvent.Position.Floor())} {source}";
    }

    private static string[] Awarded(JsonObject before, JsonObject after)
    {
        var awarded = new List<string>();
        foreach (var (type, values) in after["stats"]!.AsObject())
        {
            foreach (var (identifier, value) in values!.AsObject())
            {
                var previous = before["stats"]?[type]?[identifier]?.GetValue<int>() ?? 0;
                var amount = value!.GetValue<int>() - previous;
                if (amount != 0)
                    awarded.Add($"{type} {identifier} {amount}");
            }
        }

        return [.. awarded];
    }

    private static int HandSlot(ServerPlayer user, InteractionHand hand) =>
        hand == InteractionHand.OffHand ? 45 : user.CurrentHeldItemSlot;

    private static InteractionHand Hand(JsonElement use) =>
        use.TryGetProperty("handUsed", out var hand) && hand.GetString() == "offhand"
            ? InteractionHand.OffHand
            : InteractionHand.MainHand;

    // An item stack as "<id> x<count>", with its damage when it has any, or "empty".
    private static string Text(ItemStack? stack)
    {
        if (stack is not { Count: > 0 } || stack.IsAir)
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

    private static string Offset(Vector position)
    {
        var offset = position - target;
        return $"{offset.X},{offset.Y},{offset.Z}";
    }

    private static JsonArray Array(IEnumerable<string> values) => new(values.Select(Node).ToArray());

    private static JsonNode? Node(string value) => JsonValue.Create(value);

    private static Vector Position(JsonElement values) =>
        new(values[0].GetInt32(), values[1].GetInt32(), values[2].GetInt32());
}
