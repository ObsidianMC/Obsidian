using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
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
/// Replays vanilla's armor stand use (<c>Assets/armor_stand_use.json</c>, recorded by <c>ArmorStandFixtures.java</c>)
/// through the interact packet a player sends, in one shared world: the hand, the stand's equipment and saved fields,
/// what a player standing by is sent (sounds, swings, equipment), the swings the user is sent, the game events, the
/// statistics and the advancement criteria.
/// </summary>
/// <remarks>
/// The interaction result itself isn't compared: Obsidian returns none, and what it decides (the swing, the trigger,
/// the changes) is.
/// </remarks>
[Collection(nameof(PluginTagFiles))]
public sealed class VanillaArmorStandUse(ITestOutputHelper output)
{
    // Where the replay's stand stands, on stone, inside one chunk.
    private static readonly Vector target = new(8, 64, 8);

    private sealed record Harness(RealServerContext Context, ServerPlayer User, ServerPlayer Bystander);

    private sealed record Outcome(
        string Hand,
        string[] Equipment,
        JsonNode Saved,
        string[] Sounds,
        string[] Swings,
        string[] EquipmentPackets,
        string[] GameEvents,
        string[] Stats,
        string[] Criteria);

    [Fact]
    public async Task ReplayArmorStandUseReference()
    {
        using var stream = typeof(VanillaArmorStandUse).Assembly
            .GetManifestResourceStream("Obsidian.Tests.Assets.armor_stand_use.json")!;
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
                var name = use.GetProperty("name").GetString()!;
                using var definition = JsonDocument.Parse(
                    "{\"criteria\":" + use.GetProperty("criteria").GetRawText() + "}");
                var advancement = AdvancementsRegistry.Parse("probe:" + name, definition.RootElement);
                AdvancementsRegistry.Apply(previous.Append(advancement));

                var actual = await ReplayAsync(harness, use);
                var differences = Compare(Expected(use), actual).ToList();
                Assert.True(differences.Count == 0, string.Join("; ", differences));
            });
        }
        finally
        {
            AdvancementsRegistry.Apply(previous);
        }

        output.WriteLine($"Replayed {cases.Length} armor stand uses.");
    }

    private static async Task<Harness> StartAsync(JsonElement root)
    {
        var context = TestServerFactory.CreateServer($"armor-stand-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        await world.GetBlockAsync(target);
        world.BlockUpdates.SetBlock(target + Vector.Down, BlocksRegistry.Stone,
            BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);

        var standing = root.GetProperty("player");
        var shift = (VectorD)(target - Position(root.GetProperty("target")));
        var userPosition = new VectorD(standing[0].GetDouble(), standing[1].GetDouble(), standing[2].GetDouble());
        var user = TestServerFactory.CreateConnectedPlayer(context.Server, world, "User", userPosition + shift);
        user.Yaw = standing[3].GetSingle();
        user.Pitch = standing[4].GetSingle();

        var bystanderPosition = (VectorD)target + new VectorD(0.5, 3, 0.5);
        var bystander = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Bystander", bystanderPosition);
        world.Players.TryAdd(user.Uuid, user);
        world.Players.TryAdd(bystander.Uuid, bystander);
        user.LoadedChunks.Add(0);
        bystander.LoadedChunks.Add(0);

        return new Harness(context, user, bystander);
    }

    // The case's stand and hand, then the click as the packet brings it, then the level's queued actions.
    private static async Task<Outcome> ReplayAsync(Harness harness, JsonElement use)
    {
        var world = harness.Context.World;
        var user = harness.User;
        foreach (var entity in world.GetNonPlayerEntitiesInRange((VectorD)target, 6).ToArray())
            await world.DestroyEntityAsync(entity);

        var stand = Stand(world, use);
        Prepare(user, use);
        await world.RunQueuedEntityActionsAsync();

        ((TestClient)user.Client).ClearPackets();
        ((TestClient)harness.Bystander.Client).ClearPackets();
        world.TakeGameEvents();
        world.LevelData.GameTime += 20;
        var statistics = user.ProgressStatistics.Save();

        var hand = Hand(use);
        var clicked = use.GetProperty("clicked");
        var packet = new InteractPacket();
        Set(packet, nameof(InteractPacket.EntityId), stand.EntityId);
        Set(packet, nameof(InteractPacket.Hand), hand);
        Set(packet, nameof(InteractPacket.Location),
            new VectorF(clicked[0].GetSingle(), clicked[1].GetSingle(), clicked[2].GetSingle()));
        await packet.HandleAsync(harness.Context.Server, user);
        await world.RunQueuedEntityActionsAsync();

        var held = hand == InteractionHand.OffHand ? user.GetOffHandItem() : user.GetHeldItem();
        var name = use.GetProperty("name").GetString()!;
        var criteria = use.GetProperty("criteria").EnumerateObject()
            .Where(criterion => user.Advancements.HasCriterion("probe:" + name, criterion.Name))
            .Select(criterion => criterion.Name)
            .ToArray();

        return new Outcome(
            Text(held),
            Equipment(stand),
            Saved(stand),
            Sounds(harness.Bystander),
            Swings(harness),
            EquipmentPackets(harness.Bystander),
            world.TakeGameEvents().Select(gameEvent => GameEvent(gameEvent, user)).ToArray(),
            Awarded(statistics, user.ProgressStatistics.Save()),
            criteria);
    }

    private static ArmorStand Stand(WorldData.World world, JsonElement use)
    {
        var stand = new ArmorStand
        {
            Level = world,
            EntityId = Server.GetNextEntityId(),
            Position = (VectorD)target + new VectorD(0.5, 0, 0.5),
            Type = EntityType.ArmorStand,
            ShowArms = Flag(use, "showArms"),
            Small = Flag(use, "small"),
            Marker = Flag(use, "marker"),
            NoBasePlate = Flag(use, "noBasePlate"),
            DisabledSlots = use.TryGetProperty("disabledSlots", out var disabled) ? disabled.GetInt32() : 0
        };

        if (use.TryGetProperty("wearing", out var wearing))
        {
            foreach (var worn in wearing.EnumerateObject())
                stand.SetEquipment(Slot(worn.Name), new ItemStack(ItemsRegistry.Get(worn.Value.GetString()!)));
        }

        Assert.True(world.TryAddEntity(stand));
        return stand;
    }

    private static void Prepare(ServerPlayer user, JsonElement use)
    {
        var mode = use.TryGetProperty("gameMode", out var gameMode) ? gameMode.GetString()! : "survival";
        user.GameMode = Enum.Parse<GameMode>(mode, ignoreCase: true);
        user.Sneaking = false;
        user.Inventory.SetItem(user.CurrentHeldItemSlot, null);
        user.Inventory.SetItem(45, null);

        if (!use.TryGetProperty("item", out var item))
            return;

        var count = use.TryGetProperty("count", out var amount) ? amount.GetInt32() : 1;
        var held = new ItemStack(ItemsRegistry.Get(item.GetString()!), count);
        if (use.TryGetProperty("itemName", out var name))
        {
            held[DataComponentType.CustomName] = ComponentBuilder.CustomName with
            {
                Value = new ChatMessage { Text = name.GetString() }
            };
        }

        var slot = Hand(use) == InteractionHand.OffHand ? 45 : user.CurrentHeldItemSlot;
        user.Inventory.SetItem(slot, held);
    }

    private static Outcome Expected(JsonElement use) => new(
        use.GetProperty("hand").GetString()!,
        Strings(use.GetProperty("equipment")),
        JsonNode.Parse(use.GetProperty("saved").GetRawText())!,
        Strings(use.GetProperty("sounds")),
        Strings(use.GetProperty("swings")),
        Strings(use.GetProperty("equipmentPackets")),
        Strings(use.GetProperty("gameEvents")),
        Strings(use.GetProperty("stats")),
        Strings(use.GetProperty("completedCriteria")));

    private static IEnumerable<string> Compare(Outcome expected, Outcome actual)
    {
        if (expected.Hand != actual.Hand)
            yield return $"hand: expected '{expected.Hand}', got '{actual.Hand}'";

        if (!NbtJson.Same(expected.Saved, actual.Saved))
        {
            yield return $"saved: expected {NbtJson.Canonical(expected.Saved)}, "
                + $"got {NbtJson.Canonical(actual.Saved)}";
        }

        var lists = new (string Name, string[] Expected, string[] Actual)[]
        {
            ("equipment", expected.Equipment, actual.Equipment),
            ("sounds", expected.Sounds, actual.Sounds),
            ("swings", expected.Swings, actual.Swings),
            ("equipment packets", expected.EquipmentPackets, actual.EquipmentPackets),
            ("game events", expected.GameEvents, actual.GameEvents),
            ("stats", expected.Stats, actual.Stats),
            ("criteria", expected.Criteria, actual.Criteria)
        };

        foreach (var (name, wanted, got) in lists)
        {
            if (!wanted.SequenceEqual(got))
                yield return $"{name}: expected [{string.Join(", ", wanted)}], got [{string.Join(", ", got)}]";
        }
    }

    // Each filled slot in vanilla's slot order, as "slot=item".
    private static string[] Equipment(ArmorStand stand)
    {
        EquipmentSlot[] order =
        [
            EquipmentSlot.MainHand, EquipmentSlot.OffHand, EquipmentSlot.Boots, EquipmentSlot.Leggings,
            EquipmentSlot.Chestplate, EquipmentSlot.Helmet
        ];

        return order
            .Where(slot => !stand.GetEquipment(slot).IsAir)
            .Select(slot => $"{SlotName(slot)}={Text(stand.GetEquipment(slot))}")
            .ToArray();
    }

    // The saved fields the capture keeps, as JSON the way vanilla's NbtOps turns them into it.
    private static JsonNode Saved(ArmorStand stand)
    {
        var tag = new NbtCompound();
        stand.WriteNbt(tag);

        var kept = new JsonObject();
        foreach (var name in new[] { "equipment", "DisabledSlots", "ShowArms", "Small", "NoBasePlate", "Marker",
            "Pose", "Invisible" })
        {
            if (tag.TryGetTag(name, out var value))
                kept[name] = NbtJson.ToJson(value);
        }

        return kept;
    }

    private static string[] Sounds(ServerPlayer bystander) =>
        ((TestClient)bystander.Client).SentPackets.OfType<SoundPacket>()
            .Select(sound => NbtJson.Sound(sound, target))
            .ToArray();

    private static string[] Swings(Harness harness)
    {
        var bystander = ((TestClient)harness.Bystander.Client).SentPackets.OfType<SwingAnimationPacket>()
            .Where(swing => swing.EntityId == harness.User.EntityId)
            .Select(swing => "bystander " + swing.Hand.ToString().ToSnakeCase());
        var user = ((TestClient)harness.User.Client).SentPackets.OfType<SwingAnimationPacket>()
            .Where(swing => swing.EntityId == harness.User.EntityId)
            .Select(swing => "user " + swing.Hand.ToString().ToSnakeCase());

        return [.. bystander, .. user];
    }

    private static string[] EquipmentPackets(ServerPlayer bystander) =>
        ((TestClient)bystander.Client).SentPackets.OfType<SetEquipmentPacket>()
            .SelectMany(packet => packet.Equipment)
            .Select(equipment => $"{SlotName(equipment.Slot)}={Text(equipment.Item)}")
            .ToArray();

    private static string GameEvent(MobVibration gameEvent, ServerPlayer user)
    {
        var position = (Vector)gameEvent.Position.Floor() - target;
        var source = gameEvent.Source switch
        {
            null => "none",
            ServerPlayer player when ReferenceEquals(player, user) => "player",
            Entity entity => EntityNbt.TypeId(entity),
            _ => "unknown"
        };

        return $"{gameEvent.Kind.Identifier()} {position.X},{position.Y},{position.Z} {source}";
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

    // An item stack as "<id> x<count>", with its custom name when it has one, or "empty".
    private static string Text(ItemStack? stack)
    {
        if (stack is not { Count: > 0 } || stack.IsAir)
            return "empty";

        var text = $"{stack.Holder.UnlocalizedName} x{stack.Count}";
        var name = stack.GetComponent<SimpleDataComponent<ChatMessage>>(DataComponentType.CustomName);
        return name?.Value?.Text is string named ? $"{text} named={named}" : text;
    }

    private static string SlotName(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.MainHand => "mainhand",
        EquipmentSlot.OffHand => "offhand",
        EquipmentSlot.Boots => "feet",
        EquipmentSlot.Leggings => "legs",
        EquipmentSlot.Chestplate => "chest",
        EquipmentSlot.Helmet => "head",
        _ => slot.ToString().ToLowerInvariant()
    };

    private static EquipmentSlot Slot(string name) => name switch
    {
        "mainhand" => EquipmentSlot.MainHand,
        "offhand" => EquipmentSlot.OffHand,
        "feet" => EquipmentSlot.Boots,
        "legs" => EquipmentSlot.Leggings,
        "chest" => EquipmentSlot.Chestplate,
        "head" => EquipmentSlot.Helmet,
        _ => throw new ArgumentException(name)
    };

    private static InteractionHand Hand(JsonElement use) =>
        use.TryGetProperty("handUsed", out var hand) && hand.GetString() == "offhand"
            ? InteractionHand.OffHand
            : InteractionHand.MainHand;

    private static bool Flag(JsonElement use, string name) =>
        use.TryGetProperty(name, out var value) && value.GetBoolean();

    private static void Set(object target, string property, object value) =>
        target.GetType().GetProperty(property)!.SetValue(target, value);

    private static string[] Strings(JsonElement values) =>
        [.. values.EnumerateArray().Select(value => value.GetString()!)];

    private static Vector Position(JsonElement values) =>
        new(values[0].GetInt32(), values[1].GetInt32(), values[2].GetInt32());
}
