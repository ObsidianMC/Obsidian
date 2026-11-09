using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
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
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using ServerPlayer = Obsidian.Entities.Player;

namespace Obsidian.Tests;

/// <summary>
/// Replays vanilla's block use (<c>Assets/block_use.json</c>, recorded by <c>BlockUseFixtures.java</c>) through the
/// use-item-on packet players send, in one shared world: the result, the blocks, the hand, the entities, what a
/// player beside the block hears, the game events, the statistics and the menu, and what closing the menu does.
/// </summary>
[Collection(nameof(PluginTagFiles))]
public sealed class VanillaBlockUse(ITestOutputHelper output)
{
    // How far from the target the compared area reaches, as in the capture.
    private const int Reach = 2;

    // Setup and clearing, like the capture's: no neighbor or shape updates and nothing placed reacting.
    private const BlockUpdateFlags Quiet = BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace;

    // Where the replay's target is: inside one chunk with room around it, unlike the capture's (0, 64, 0).
    private static readonly Vector target = new(8, 64, 8);

    // Mobs don't make their ambient sounds in Obsidian (known-gaps/mob-ambient-sounds.md), and vanilla's spawn eggs
    // play one as the mob appears: those sounds aren't compared.
    private static readonly HashSet<string> withoutAmbientSounds = ["spawn_egg_on_stone"];

    private sealed record Setup(Vector Offset, IBlock State, string? Text);

    private sealed record Effects(
        string Changes,
        string[] Sounds,
        string[] LevelEvents,
        string[] BlockEvents,
        string[] GameEvents);

    private sealed record Outcome(
        string Result,
        string Hand,
        string[] Entities,
        Effects Effects,
        string[] Stats,
        string? Menu,
        Effects? Closed);

    private sealed record Case(
        string Name,
        Setup[] Setup,
        Vector Clicked,
        BlockFace Face,
        string? Item,
        int Count,
        string? Offhand,
        GameMode GameMode,
        bool Sneaking,
        Outcome Expected,
        JsonElement Reference);

    private sealed record Harness(RealServerContext Context, ServerPlayer User, ServerPlayer Bystander);

    [Fact]
    public async Task ReplayBlockUseReference()
    {
        using var stream = typeof(VanillaBlockUse).Assembly
            .GetManifestResourceStream("Obsidian.Tests.Assets.block_use.json")!;
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        Assert.Equal(ServerConstants.ProtocolDescription, root.GetProperty("version").GetString());

        var cases = root.GetProperty("cases").EnumerateArray().Select(ReadCase).ToArray();
        var harness = await StartAsync(root);
        await using var context = harness.Context;

        var previous = AdvancementsRegistry.Current.Values.ToArray();
        try
        {
            await FixtureCases.AssertAllAsync(cases, use => use.Name, async use =>
            {
                if (use.Reference.TryGetProperty("criteria", out var criteria))
                {
                    using var definition = JsonDocument.Parse("{\"criteria\":" + criteria.GetRawText() + "}");
                    var advancement = AdvancementsRegistry.Parse("probe:" + use.Name, definition.RootElement);
                    AdvancementsRegistry.Apply(previous.Append(advancement));
                }

                try
                {
                    await ReplayAsync(harness, use);
                }
                finally
                {
                    if (use.Reference.TryGetProperty("criteria", out _))
                        AdvancementsRegistry.Apply(previous);
                }
            });
        }
        finally
        {
            AdvancementsRegistry.Apply(previous);
        }
        output.WriteLine($"Replayed {cases.Length} block uses.");
    }

    // One world, with the user standing where the capture's player stood and a bystander above the target.
    private static async Task<Harness> StartAsync(JsonElement root)
    {
        var context = TestServerFactory.CreateServer($"block-use-{Guid.NewGuid():N}");
        var world = context.World;
        var chunk = WorldBlockEntities.AddEmptyChunk(world);
        await world.GetBlockAsync(target);

        // The capture's world is air under open sky, so its sky light is full everywhere (a daylight detector reads
        // it).
        for (var y = target.Y - Reach - 1; y <= target.Y + Reach + 1; y++)
        {
            for (var z = 0; z < 16; z++)
            {
                for (var x = 0; x < 16; x++)
                    chunk.SetLightLevel(x, y, z, LightType.Sky, 15);
            }
        }

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

        // Both have the target's chunk, so level events reach them.
        user.LoadedChunks.Add(0);
        bystander.LoadedChunks.Add(0);

        return new Harness(context, user, bystander);
    }

    private static async Task ReplayAsync(Harness harness, Case use)
    {
        var world = harness.Context.World;
        var user = harness.User;

        // The case's blocks in a cleared area, and the user as the case has them.
        await ClearAsync(world);
        foreach (var setup in use.Setup)
        {
            world.BlockUpdates.SetBlock(target + setup.Offset, setup.State, Quiet);
            if (setup.Text is string text
                && world.GetResidentBlockEntity(target + setup.Offset) is DataBlockEntity sign)
            {
                foreach (var side in new[] { "front_text", "back_text" })
                {
                    var messages = new NbtList(NbtTagType.String, "messages")
                    {
                        new NbtTag<string>("", text),
                        new NbtTag<string>("", ""),
                        new NbtTag<string>("", ""),
                        new NbtTag<string>("", "")
                    };
                    sign.Set(new NbtCompound(side) { messages });
                }
            }
        }

        Prepare(user, use);
        if (use.Reference.TryGetProperty("checkAttack", out _))
            ChargeAttack(user);

        var before = Snapshot(world);
        var statistics = user.ProgressStatistics.Save();
        Forget(harness);

        // The use, the block updates it caused and the tick's block events.
        var offhand = OptionalString(use.Reference, "handUsed") == "offhand";
        var packet = BlockUpdates.UseOn(target + use.Clicked, use.Face, new VectorF(0.5f, 0.5f, 0.5f));
        if (offhand)
            typeof(UseItemOnPacket).GetProperty(nameof(UseItemOnPacket.Hand))!.SetValue(packet, InteractionHand.OffHand);

        var result = await packet.UseAsync(harness.Context.Server, user);
        await world.RunQueuedEntityActionsAsync();
        await world.BlockNotifications.DrainAsync();
        await world.ProcessBlockEventsAsync();

        var effects = Record(harness, before);
        var menu = Menu(user);
        var swings = Swings(harness);
        var outcome = new Outcome(
            Name(result),
            Text(offhand ? user.GetOffHandItem() : user.GetHeldItem()),
            Entities(world),
            effects,
            Awarded(statistics, user.ProgressStatistics.Save()),
            menu,
            await CloseAsync(harness));

        var expected = withoutAmbientSounds.Contains(use.Name) ? WithoutAmbientSounds(use.Expected) : use.Expected;
        var differences = Compare(expected, outcome).ToList();
        differences.AddRange(CompareSwings(use.Expected.Result, swings));
        if (use.Reference.TryGetProperty("criteria", out var criteria))
        {
            var awarded = criteria.EnumerateObject()
                .Where(criterion => user.Advancements.HasCriterion("probe:" + use.Name, criterion.Name))
                .Select(criterion => criterion.Name)
                .ToArray();
            differences.AddRange(CompareSets("advancement criteria",
                Strings(use.Reference.GetProperty("awardedCriteria")), awarded));
        }

        if (use.Reference.TryGetProperty("checkAttack", out _))
        {
            Assert.Equal(use.Reference.GetProperty("attackCharge").GetSingle(), user.AttackCharge);
            world.LevelData.GameTime++;
            ChargeAttack(user);
            Assert.False(user.SwingForUse(InteractionHand.MainHand, user.GetHeldItem(), includeSelf: true));
            Assert.Equal(use.Reference.GetProperty("blockedAttackCharge").GetSingle(), user.AttackCharge);
            world.LevelData.GameTime += 6;
        }

        if (use.Reference.TryGetProperty("checkSign", out _))
        {
            var sign = (DataBlockEntity)world.GetResidentBlockEntity(target + use.Clicked)!;
            var data = sign.SnapshotData();
            if (!data.TryGetTag<NbtCompound>("front_text", out var text))
                text = new NbtCompound("front_text");
            var color = text.TryGetTag<NbtTag<string>>("color", out var dye) ? dye.Value : "black";
            var glowing = text.TryGetBool("has_glowing_text", out var glow) && glow;
            var waxed = data.TryGetBool("is_waxed", out var wax) && wax;
            var editor = ((TestClient)user.Client).SentPackets.OfType<OpenSignEditorPacket>().LastOrDefault();
            var edited = editor?.Slot.ToString().ToLowerInvariant();
            if (color != use.Reference.GetProperty("signColor").GetString()
                || glowing != use.Reference.GetProperty("signGlowing").GetBoolean()
                || waxed != use.Reference.GetProperty("signWaxed").GetBoolean()
                || edited != OptionalString(use.Reference, "editor"))
            {
                differences.Add($"sign: color={color}, glowing={glowing}, waxed={waxed}, editor={edited}");
            }
        }
        Assert.True(differences.Count == 0, string.Join("; ", differences));

        if (use.Reference.TryGetProperty("checkCloseSources", out _))
            await CheckCloseSourcesAsync(harness, use);
    }

    private static void ChargeAttack(ServerPlayer player) =>
        typeof(PlayerBase).GetField("attackTicks", System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance)!.SetValue(player, 30);

    private static async Task CheckCloseSourcesAsync(Harness harness, Case use)
    {
        var world = harness.Context.World;
        var other = harness.Bystander;
        other.GameMode = GameMode.Survival;

        async Task OpenAsync(ServerPlayer player)
        {
            await BlockMenus.OpenAsync(player, world.BlockUpdates.GetBlock(target), target);
            await world.ProcessBlockEventsAsync();
        }

        async Task CloseAsync(ServerPlayer player)
        {
            await harness.Context.EventDispatcher.ExecuteEventAsync(new ContainerClosedEventArgs(
                player, harness.Context.Server, player.OpenedContainer!, player.CurrentContainerId));
        }

        await OpenAsync(harness.User);
        await OpenAsync(other);
        world.TakeGameEvents();
        await CloseAsync(harness.User);
        await CloseAsync(other);
        await world.ProcessBlockEventsAsync();
        var explicitEvents = world.TakeGameEvents().Select(e => GameEvent(e, harness.User)).ToArray();
        Assert.Equal(Strings(use.Reference.GetProperty("explicitCloseEvents")), explicitEvents);

        await OpenAsync(harness.User);
        world.TakeGameEvents();
        harness.User.Health = 0;
        world.LevelData.GameTime += AbstractLevel.ContainerLidRecheckDelay;
        await world.ProcessBlockEventsAsync();
        var periodicEvents = world.TakeGameEvents().Select(e => GameEvent(e, harness.User)).ToArray();
        Assert.Equal(Strings(use.Reference.GetProperty("periodicCloseEvents")), periodicEvents);
        harness.User.Health = 20;
        await CloseAsync(harness.User);
    }

    private static void Prepare(ServerPlayer user, Case use)
    {
        user.GameMode = use.GameMode;
        user.Sneaking = use.Sneaking;

        var held = use.Item is null ? null : new ItemStack(ItemsRegistry.Get(use.Item), use.Count);
        var offhand = use.Offhand is null ? null : new ItemStack(ItemsRegistry.Get(use.Offhand), 2);
        user.Inventory.SetItem(user.CurrentHeldItemSlot, held);
        user.Inventory.SetItem(45, offhand);
    }

    // Packets, game events and swings from earlier steps aren't this step's.
    private static void Forget(Harness harness)
    {
        ((TestClient)harness.User.Client).ClearPackets();
        ((TestClient)harness.Bystander.Client).ClearPackets();
        harness.Context.World.TakeGameEvents();
    }

    // Closes the menu the use opened, as the client does, and records what closing did.
    private static async Task<Effects?> CloseAsync(Harness harness)
    {
        var user = harness.User;
        if (user.OpenedContainer is null)
            return null;

        var world = harness.Context.World;
        var before = Snapshot(world);
        Forget(harness);

        var closed = new ContainerClosedEventArgs(
            user,
            harness.Context.Server,
            user.OpenedContainer,
            user.CurrentContainerId);
        await harness.Context.EventDispatcher.ExecuteEventAsync(closed);
        await world.ProcessBlockEventsAsync();

        return Record(harness, before);
    }

    // What changed in the area, what the bystander heard, and the game events, the way the capture writes them.
    private static Effects Record(Harness harness, IBlock[] before)
    {
        var heard = ((TestClient)harness.Bystander.Client).SentPackets.ToArray();
        var sounds = heard.OfType<SoundPacket>().Select(sound => sound.SoundLocation).ToArray();
        var levelEvents = heard.OfType<LevelEventPacket>()
            .Select(levelEvent => $"{levelEvent.Type} {Offset(levelEvent.Position)} {levelEvent.Data}")
            .ToArray();
        var blockEvents = heard.OfType<BlockEventPacket>()
            .Select(blockEvent => BlockEvent(blockEvent))
            .ToArray();
        var gameEvents = harness.Context.World.TakeGameEvents()
            .Select(gameEvent => GameEvent(gameEvent, harness.User))
            .ToArray();

        return new Effects(Changes(harness.Context.World, before), sounds, levelEvents, blockEvents, gameEvents);
    }

    private static string BlockEvent(BlockEventPacket blockEvent)
    {
        var block = BlocksRegistry.GetDefault(blockEvent.BlockType).UnlocalizedName;
        return $"{block} {blockEvent.ActionId} {blockEvent.ActionParam} {Offset(blockEvent.Position)}";
    }

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

    // Who was shown a swing: the user's own client and the bystander's.
    private static (bool User, bool Bystander) Swings(Harness harness)
    {
        var user = ((TestClient)harness.User.Client).SentPackets.OfType<SwingAnimationPacket>().Any();
        var bystander = ((TestClient)harness.Bystander.Client).SentPackets.OfType<SwingAnimationPacket>().Any();

        return (user, bystander);
    }

    // Vanilla's handleUseItemOn swings for a successful use: for the players around, and for the user too when
    // their client didn't predict it.
    private static IEnumerable<string> CompareSwings(string result, (bool User, bool Bystander) swings)
    {
        var bystander = result is "success" or "success_server";
        var user = result == "success_server";
        if (swings.Bystander != bystander)
            yield return $"bystander swing: expected {bystander}, got {swings.Bystander}";

        if (swings.User != user)
            yield return $"user swing: expected {user}, got {swings.User}";
    }

    private static Outcome WithoutAmbientSounds(Outcome outcome)
    {
        var sounds = outcome.Effects.Sounds
            .Where(sound => !sound.EndsWith(".ambient", StringComparison.Ordinal))
            .ToArray();

        return outcome with { Effects = outcome.Effects with { Sounds = sounds } };
    }

    private static IEnumerable<string> Compare(Outcome expected, Outcome actual)
    {
        if (expected.Result != actual.Result)
            yield return $"result: expected {expected.Result}, got {actual.Result}";

        if (expected.Hand != actual.Hand)
            yield return $"hand: expected '{expected.Hand}', got '{actual.Hand}'";

        foreach (var difference in CompareSets("entities", expected.Entities, actual.Entities))
            yield return difference;

        foreach (var difference in CompareSets("stats", expected.Stats, actual.Stats))
            yield return difference;

        if (expected.Menu != actual.Menu)
            yield return $"menu: expected {expected.Menu ?? "none"}, got {actual.Menu ?? "none"}";

        foreach (var difference in CompareEffects("", expected.Effects, actual.Effects))
            yield return difference;

        if (expected.Closed is null || actual.Closed is null)
            yield break;

        foreach (var difference in CompareEffects("closed ", expected.Closed, actual.Closed))
            yield return difference;
    }

    private static IEnumerable<string> CompareEffects(string prefix, Effects expected, Effects actual)
    {
        if (expected.Changes != actual.Changes)
            yield return $"{prefix}changes: expected '{expected.Changes}', got '{actual.Changes}'";

        foreach (var difference in CompareLists(prefix + "sounds", expected.Sounds, actual.Sounds))
            yield return difference;

        foreach (var difference in CompareLists(prefix + "level events", expected.LevelEvents, actual.LevelEvents))
            yield return difference;

        foreach (var difference in CompareLists(prefix + "block events", expected.BlockEvents, actual.BlockEvents))
            yield return difference;

        foreach (var difference in CompareLists(prefix + "game events", expected.GameEvents, actual.GameEvents))
            yield return difference;
    }

    private static IEnumerable<string> CompareLists(string name, string[] expected, string[] actual)
    {
        if (!expected.SequenceEqual(actual))
            yield return $"{name}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]";
    }

    // Entities and statistics are compared without their order, which neither side fixes.
    private static IEnumerable<string> CompareSets(string name, string[] expected, string[] actual)
    {
        var sortedExpected = expected.Order(StringComparer.Ordinal).ToArray();
        var sortedActual = actual.Order(StringComparer.Ordinal).ToArray();

        return CompareLists(name, sortedExpected, sortedActual);
    }

    private static string Name(UseResult? result) => result switch
    {
        UseResult.Success => "success",
        UseResult.SuccessServer => "success_server",
        UseResult.Consume => "consume",
        UseResult.Fail => "fail",
        _ => "pass"
    };

    // The menu the user's client was told to open, by its vanilla menu type.
    private static string? Menu(ServerPlayer user)
    {
        var screen = ((TestClient)user.Client).SentPackets.OfType<OpenScreenPacket>().LastOrDefault();
        if (screen is null)
            return null;

        var name = screen.Type switch
        {
            WindowType.CRAFTER3x3 => "crafter_3x3",
            WindowType.Generic9x1 or WindowType.Generic9x2 or WindowType.Generic9x3 or WindowType.Generic9x4
                or WindowType.Generic9x5 or WindowType.Generic9x6 or WindowType.Generic3x3 =>
                "generic_" + screen.Type.ToString()["Generic".Length..],
            _ => screen.Type.ToString().ToSnakeCase()
        };

        return "minecraft:" + name;
    }

    // Each statistic the use awarded, as "type value amount".
    private static string[] Awarded(JsonObject before, JsonObject after)
    {
        var awarded = new List<string>();
        var groups = after["stats"]!.AsObject();
        foreach (var (type, values) in groups)
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

    // The entities in the area other than players: items as "item <id> x<count>", others by type.
    private static string[] Entities(WorldData.World world)
    {
        var center = (VectorD)target + new VectorD(0.5, 0.5, 0.5);
        return world.GetEntitiesInRange(center, Reach + 3)
            .OfType<Entity>()
            .Where(entity => entity is not ServerPlayer && InArea(entity.Position))
            .Select(entity => entity is ItemEntity item ? "item " + Text(item.Item) : EntityNbt.TypeId(entity))
            .ToArray();
    }

    private static bool InArea(VectorD position)
    {
        var min = (VectorD)target - new VectorD(Reach + 1, Reach + 1, Reach + 1);
        var max = (VectorD)target + new VectorD(Reach + 2, Reach + 2, Reach + 2);

        return position.X >= min.X && position.X <= max.X
            && position.Y >= min.Y && position.Y <= max.Y
            && position.Z >= min.Z && position.Z <= max.Z;
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

        // Clearing can drop what the last case's blocks held, which goes too, as in the capture.
        await world.RunQueuedEntityActionsAsync();
        foreach (var entity in world.GetEntitiesInRange((VectorD)target, Reach + 4).ToArray())
        {
            if (entity is not ServerPlayer)
                await world.DestroyEntityAsync(entity);
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

    private static IBlock[] Snapshot(WorldData.World world) =>
        Area().Select(world.BlockUpdates.GetBlock).ToArray();

    // Each block of the area that changed, as dx,dy,dz=state relative to the target, the way the capture writes them.
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

    private static string Offset(Vector position)
    {
        var offset = position - target;
        return $"{offset.X},{offset.Y},{offset.Z}";
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

    private static Case ReadCase(JsonElement use)
    {
        var setup = use.GetProperty("setup").EnumerateArray()
            .Select(block => new Setup(
                Position(block.GetProperty("offset")),
                VanillaSulfurBlocks.ParseState(block.GetProperty("state").GetString()!),
                OptionalString(block, "text")))
            .ToArray();
        var mode = use.TryGetProperty("gameMode", out var gameMode) ? gameMode.GetString()! : "survival";

        return new Case(
            use.GetProperty("name").GetString()!,
            setup,
            Position(use.GetProperty("clicked")),
            Enum.Parse<BlockFace>(use.GetProperty("face").GetString()!, ignoreCase: true),
            OptionalString(use, "item"),
            use.TryGetProperty("count", out var count) ? count.GetInt32() : 2,
            OptionalString(use, "offhand"),
            Enum.Parse<GameMode>(mode, ignoreCase: true),
            use.TryGetProperty("sneaking", out var sneaking) && sneaking.GetBoolean(),
            ReadOutcome(use),
            use);
    }

    private static Outcome ReadOutcome(JsonElement use)
    {
        var closed = use.TryGetProperty("closed", out var closing) ? ReadEffects(closing) : null;

        return new Outcome(
            use.GetProperty("result").GetString()!,
            use.GetProperty("hand").GetString()!,
            Strings(use.GetProperty("entities")),
            ReadEffects(use),
            Strings(use.GetProperty("stats")),
            OptionalString(use, "menu"),
            closed);
    }

    private static Effects ReadEffects(JsonElement effects) => new(
        effects.GetProperty("changes").GetString()!,
        Strings(effects.GetProperty("sounds")),
        Strings(effects.GetProperty("levelEvents")),
        Strings(effects.GetProperty("blockEvents")),
        Strings(effects.GetProperty("gameEvents")));

    private static string[] Strings(JsonElement values) =>
        [.. values.EnumerateArray().Select(value => value.GetString()!)];

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static Vector Position(JsonElement values) =>
        new(values[0].GetInt32(), values[1].GetInt32(), values[2].GetInt32());
}
