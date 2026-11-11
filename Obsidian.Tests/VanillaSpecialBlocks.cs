using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.Utilities;
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

/// <summary>
/// Replays vanilla's special blocks (<c>Assets/special_blocks.json</c>, recorded by <c>SpecialBlockFixtures.java</c>)
/// in one shared world: ender chests and shulker boxes opening or refusing to, the dragon egg teleporting when used
/// and attacked, and, tick by tick, shulker lids pushing entities, conduits, and End gateways. Each case compares
/// everything the capture recorded, from the start (see <see cref="ReplayUseAsync"/> and
/// <see cref="ReplayTicksAsync"/>).
/// </summary>
[Collection(nameof(PluginTagFiles))]
public sealed partial class VanillaSpecialBlocks(ITestOutputHelper output)
{
    // How far from the target the compared area reaches, as in the capture.
    private const int Reach = 2;

    // Setup and clearing, like the capture's: no neighbor or shape updates and nothing placed reacting.
    private const BlockUpdateFlags Quiet = BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace;

    private static readonly Vector target = new(0, 64, 0);

    // Known differences, by case and field (a tick case's fields by tick): what Obsidian records instead of vanilla's
    // value. Each fails once Obsidian matches vanilla or changes, so it can be removed (known-gaps/special-blocks.md).
    private static readonly Dictionary<(string Case, string Field), string> knownDifferences = KnownDifferences();

    private static Dictionary<(string Case, string Field), string> KnownDifferences()
    {
        // A creative player breaking the egg sends its break sound where vanilla sends level event 2001
        // (known-gaps/player-break-effects.md).
        var known = new Dictionary<(string Case, string Field), string>
        {
            [("dragon_egg_attack_creative", "sounds")] = "minecraft:block.stone.break",
            [("dragon_egg_attack_creative", "sound details")] = "minecraft:block.stone.break 0,0,0 1.0",
            [("dragon_egg_attack_creative", "level events")] = ""
        };

        // TNT bouncing on the open lid lands without the hit_ground game event, which no landing entity emits.
        foreach (var tick in new[] { 4, 7, 10, 12 })
            known[("shulker_box_up_lifts_falling_tnt", $"t{tick} gameEvents")] = "";

        // The observer the lid's shape updates pulse writes its state with every update (3), where vanilla's writes
        // it for clients only (2) and then updates the block in front.
        var pulses = new[] { (3, true), (5, false), (12, true), (14, false), (24, true), (26, false) };
        foreach (var (tick, powered) in pulses)
        {
            var state = powered ? "minecraft:observer[facing=west,powered=true]" : "minecraft:observer[facing=west]";
            known[("shulker_box_observer_sees_lid", $"t{tick} writes")] = $"1,0,0={state} flags 3";
        }

        return known;
    }

    private sealed record Harness(RealServerContext Context, ServerPlayer User, ServerPlayer Bystander,
        List<string> GameEvents);

    [Fact]
    public async Task ReplaySpecialBlocksReference()
    {
        using var stream = typeof(VanillaSpecialBlocks).Assembly
            .GetManifestResourceStream("Obsidian.Tests.Assets.special_blocks.json")!;
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        Assert.Equal(ServerConstants.ProtocolDescription, root.GetProperty("version").GetString());

        // conduit_full_frame_ignores_dry_zombie is left out: its zombie now and then takes 1 damage at tick 2 that
        // vanilla's didn't, so the case fails from run to run.
        var cases = root.GetProperty("cases").EnumerateArray()
            .Where(special => Name(special) != "conduit_full_frame_ignores_dry_zombie")
            .ToArray();
        var harness = await StartAsync(root);
        await using var context = harness.Context;

        await FixtureCases.AssertAllAsync(cases, Name, async special =>
        {
            var differences = special.TryGetProperty("ticks", out _)
                ? await ReplayTicksAsync(harness, root, special)
                : await ReplayUseAsync(harness, root, special);
            Assert.True(differences.Count == 0, string.Join("; ", differences));
        });

        output.WriteLine($"Replayed {cases.Length} special blocks.");
    }

    // One world with full sky light, as the capture's open sky has, the user where the capture's player stood and a
    // bystander above the target.
    private static async Task<Harness> StartAsync(JsonElement root)
    {
        var context = TestServerFactory.CreateServer($"special-blocks-{Guid.NewGuid():N}");
        var world = context.World;
        world.LevelData.GameRules["random_tick_speed"] = "0";
        world.LevelData.GameRules["spawn_mobs"] = "false";
        world.LevelData.GameRules["advance_weather"] = "false";

        for (var x = -2; x <= 1; x++)
        {
            for (var z = -2; z <= 1; z++)
                WorldBlockEntities.AddEmptyChunk(world, x, z);
        }

        for (var x = -2; x <= 1; x++)
        {
            for (var z = -2; z <= 1; z++)
            {
                var chunk = await world.GetChunkAsync(x, z, scheduleGeneration: false);
                LightSky(chunk!);
            }
        }

        var standing = root.GetProperty("player");
        var userPosition = new VectorD(standing[0].GetDouble(), standing[1].GetDouble(), standing[2].GetDouble());
        var user = TestServerFactory.CreateConnectedPlayer(context.Server, world, "User", userPosition);
        var bystanderPosition = (VectorD)target + new VectorD(0.5, 3, 0.5);
        var bystander = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Bystander", bystanderPosition);
        foreach (var player in new[] { user, bystander })
        {
            world.Players.TryAdd(player.Uuid, player);
            for (var x = -2; x <= 1; x++)
            {
                for (var z = -2; z <= 1; z++)
                    player.LoadedChunks.Add(NumericsHelper.IntsToLong(x, z));
            }
        }

        // The level dispatches game events later in its tick, so they're seen as they're emitted.
        var harness = new Harness(context, user, bystander, []);
        world.GameEventQueued = gameEvent => harness.GameEvents.Add(GameEvent(gameEvent, user));
        return harness;
    }

    private static void LightSky(IChunk chunk)
    {
        if (chunk is not Chunk resident)
            return;

        for (var y = target.Y - Reach - 1; y <= target.Y + Reach + 1; y++)
        {
            for (var z = 0; z < 16; z++)
            {
                for (var x = 0; x < 16; x++)
                    resident.SetLightLevel(x, y, z, LightType.Sky, 15);
            }
        }
    }

    // A use, or an attack, compared like a block_use case with ordered writes, effects and the user's own effects.
    private static async Task<List<string>> ReplayUseAsync(Harness harness, JsonElement root, JsonElement use)
    {
        var world = harness.Context.World;
        var user = harness.User;

        await SetUpAsync(harness, use);
        Prepare(user, root, use);
        var before = Snapshot(world);
        var statistics = user.ProgressStatistics.Save();
        Forget(harness);
        if (use.TryGetProperty("randomSeed", out var seed))
            world.GrowthRandom = new LegacyRandomSource(seed.GetInt64());

        // The use or attack, the block updates it caused and the tick's block events, with every write observed.
        var writes = new List<string>();
        var destroyed = new List<string>();
        world.Fluids.BlockStored = (position, block, flags) => writes.Add(Write(position, block, flags));
        world.BlockDestroyed = (position, block, drops) => destroyed.Add(Destroyed(position, block, drops));
        UseResult? result = null;
        var attack = use.TryGetProperty("attack", out var attacking) && attacking.GetBoolean();
        try
        {
            if (attack)
                await AttackAsync(harness, use);
            else
                result = await UsePacket(use).UseAsync(harness.Context.Server, user);

            await world.RunQueuedEntityActionsAsync();
            await world.BlockNotifications.DrainAsync();
            await world.ProcessBlockEventsAsync();
        }
        finally
        {
            world.Fluids.BlockStored = null;
            world.BlockDestroyed = null;
        }

        var actual = ObserveUse(harness, use, result, attack, before, statistics, writes, destroyed);
        var differences = Compare(Name(use), ExpectedUse(use, attack), actual);
        differences.AddRange(await CloseAsync(harness, use));
        return differences;
    }

    // A survival player starting to hit the clicked block, through the player action packet.
    private static async Task AttackAsync(Harness harness, JsonElement use)
    {
        var face = Enum.Parse<BlockFace>(use.GetProperty("face").GetString()!, ignoreCase: true);
        var packet = new PlayerActionPacket();
        var type = typeof(PlayerActionPacket);
        type.GetProperty(nameof(PlayerActionPacket.Status))!.SetValue(packet, PlayerActionStatus.StartedDigging);
        type.GetProperty(nameof(PlayerActionPacket.Position))!.SetValue(packet,
            target + Position(use.GetProperty("clicked")));
        type.GetProperty(nameof(PlayerActionPacket.Face))!.SetValue(packet, face);
        await packet.HandleAsync(harness.Context.Server, harness.User);
    }

    // What closing the menu the use opened did, compared with the capture's "closed".
    private static async Task<List<string>> CloseAsync(Harness harness, JsonElement use)
    {
        var world = harness.Context.World;
        var user = harness.User;
        var opened = user.OpenedContainer is not null;
        var expected = use.TryGetProperty("closed", out var closed) && closed.ValueKind == JsonValueKind.Object;
        if (opened != expected)
        {
            // Closed all the same, so the next case starts without it.
            await CloseMenuAsync(harness);
            await world.ProcessBlockEventsAsync();
            return [$"closed: expected a menu to close {expected}, got {opened}"];
        }

        if (!opened)
            return [];

        var before = Snapshot(world);
        Forget(harness);
        var closing = new API.Events.ContainerClosedEventArgs(
            user,
            harness.Context.Server,
            user.OpenedContainer!,
            user.CurrentContainerId);
        await harness.Context.EventDispatcher.ExecuteEventAsync(closing);
        await world.ProcessBlockEventsAsync();

        var heard = ((TestClient)harness.Bystander.Client).SentPackets.ToArray();
        var actual = new Dictionary<string, string>
        {
            ["closed changes"] = Changes(world, before),
            ["closed sounds"] = List(heard.OfType<SoundPacket>().Select(sound => sound.SoundLocation)),
            ["closed level events"] = List(heard.OfType<LevelEventPacket>().Select(LevelEvent)),
            ["closed block events"] = List(heard.OfType<BlockEventPacket>().Select(BlockEvent)),
            ["closed game events"] = List(harness.GameEvents)
        };
        var expectedClosed = new Dictionary<string, string>
        {
            ["closed changes"] = closed.GetProperty("changes").GetString()!,
            ["closed sounds"] = List(Strings(closed.GetProperty("sounds"))),
            ["closed level events"] = List(Strings(closed.GetProperty("levelEvents"))),
            ["closed block events"] = List(Strings(closed.GetProperty("blockEvents"))),
            ["closed game events"] = List(Strings(closed.GetProperty("gameEvents")))
        };

        return Compare(Name(use), expectedClosed, actual);
    }

    private static UseItemOnPacket UsePacket(JsonElement use)
    {
        var face = Enum.Parse<BlockFace>(use.GetProperty("face").GetString()!, ignoreCase: true);
        var packet = BlockUpdates.UseOn(target + Position(use.GetProperty("clicked")), face,
            new VectorF(0.5f, 0.5f, 0.5f));
        if (OptionalString(use, "handUsed") == "offhand")
        {
            var hand = typeof(UseItemOnPacket).GetProperty(nameof(UseItemOnPacket.Hand))!;
            hand.SetValue(packet, InteractionHand.OffHand);
        }

        return packet;
    }

    // The case's blocks, their block entity data and its cats, in a cleared area without other entities.
    private static async Task SetUpAsync(Harness harness, JsonElement special)
    {
        var world = harness.Context.World;
        await ClearAsync(world);
        foreach (var setup in special.GetProperty("setup").EnumerateArray())
        {
            var state = VanillaSulfurBlocks.ParseState(setup.GetProperty("state").GetString()!);
            var position = target + Position(setup.GetProperty("offset"));
            world.BlockUpdates.SetBlock(position, state, Quiet);
            if (OptionalString(setup, "nbt") is string snbt)
            {
                var blockEntity = (API.BlockEntities.BlockEntity)world.GetResidentBlockEntity(position)!;
                blockEntity.Load(SnbtReader.ReadCompound(snbt));
            }
        }

        if (special.TryGetProperty("cats", out var cats))
        {
            foreach (var input in cats.EnumerateArray())
            {
                var at = target + Position(input.GetProperty("offset"));
                var cat = new Cat
                {
                    EntityId = Server.GetNextEntityId(),
                    Level = world,
                    Position = (VectorD)at + new VectorD(0.5, 0, 0.5),
                    IsSitting = input.GetProperty("sitting").GetBoolean()
                };
                world.SpawnEntity(cat);
            }
        }

        await world.RunQueuedEntityActionsAsync();
    }

    // The user's mode, sneaking, hands and place, as the case has them.
    private static void Prepare(ServerPlayer user, JsonElement root, JsonElement special)
    {
        var mode = special.TryGetProperty("gameMode", out var gameMode) ? gameMode.GetString()! : "survival";
        user.GameMode = Enum.Parse<GameMode>(mode, ignoreCase: true);
        user.Sneaking = special.TryGetProperty("sneaking", out var sneaking) && sneaking.GetBoolean();
        for (var slot = 9; slot <= 45; slot++)
            user.Inventory.SetItem(slot, null);

        var count = special.TryGetProperty("count", out var held) ? held.GetInt32() : 2;
        user.Inventory.SetItem(user.CurrentHeldItemSlot, Stack(OptionalString(special, "item"), count));
        user.Inventory.SetItem(45, Stack(OptionalString(special, "offhand"), 2));

        var standing = special.TryGetProperty("player", out var own) ? own : root.GetProperty("player");
        user.Position = new VectorD(standing[0].GetDouble(), standing[1].GetDouble(), standing[2].GetDouble());
        user.Yaw = standing[3].GetSingle();
        user.Pitch = standing[4].GetSingle();
    }

    private static ItemStack? Stack(string? item, int count) =>
        item is null ? null : new ItemStack(ItemsRegistry.Get(item), count);

    private static void Forget(Harness harness)
    {
        ((TestClient)harness.User.Client).ClearPackets();
        ((TestClient)harness.Bystander.Client).ClearPackets();
        harness.GameEvents.Clear();
        harness.Context.World.TakeGameEvents();
    }

    // What the capture records of a use, field by field in the form ObserveUse gives them.
    private static Dictionary<string, string> ExpectedUse(JsonElement use, bool attack)
    {
        var expected = new Dictionary<string, string>
        {
            ["hand"] = use.GetProperty("hand").GetString()!,
            ["changes"] = use.GetProperty("changes").GetString()!,
            ["entities"] = Set(Strings(use.GetProperty("entities"))),
            ["stats"] = Set(Strings(use.GetProperty("stats"))),
            ["sounds"] = List(Strings(use.GetProperty("sounds"))),
            ["sound details"] = List(Strings(use.GetProperty("soundDetails"))),
            ["level events"] = List(Strings(use.GetProperty("levelEvents"))),
            ["block events"] = List(Strings(use.GetProperty("blockEvents"))),
            ["particles"] = List(Strings(use.GetProperty("particles"))),
            ["game events"] = List(Strings(use.GetProperty("gameEvents"))),
            ["user effects"] = List(Strings(use.GetProperty("userEffects"))),
            ["menu"] = OptionalString(use, "menu") ?? "none",
            ["writes"] = List(use.GetProperty("writes").EnumerateArray().Select(write => Write(
                target + Position(write.GetProperty("offset")),
                VanillaSulfurBlocks.ParseState(write.GetProperty("state").GetString()!),
                (BlockUpdateFlags)write.GetProperty("flags").GetInt32()))),
            ["destroyed"] = List(use.GetProperty("destroyed").EnumerateArray().Select(block => Destroyed(
                target + Position(block.GetProperty("offset")),
                VanillaSulfurBlocks.ParseState(block.GetProperty("state").GetString()!),
                block.GetProperty("drops").GetBoolean())))
        };

        if (!attack)
        {
            var result = use.GetProperty("result").GetString()!;
            expected["result"] = result;
            expected["swings"] = Swings(result is "success" or "success_server", result == "success_server");
        }

        return expected;
    }

    // What Obsidian did, in the capture's forms.
    private static Dictionary<string, string> ObserveUse(Harness harness, JsonElement use, UseResult? result,
        bool attack, IBlock[] before, JsonObject statistics, List<string> writes, List<string> destroyed)
    {
        var world = harness.Context.World;
        var user = harness.User;
        var heard = ((TestClient)harness.Bystander.Client).SentPackets.ToArray();
        var received = ((TestClient)user.Client).SentPackets.ToArray();
        var hand = OptionalString(use, "handUsed") == "offhand" ? user.GetOffHandItem() : user.GetHeldItem();

        var observed = new Dictionary<string, string>
        {
            ["hand"] = Text(hand),
            ["changes"] = Changes(world, before),
            ["entities"] = Set(UseEntities(world)),
            ["stats"] = Set(Awarded(statistics, user.ProgressStatistics.Save())),
            ["sounds"] = List(heard.OfType<SoundPacket>().Select(sound => sound.SoundLocation)),
            ["sound details"] = List(heard.OfType<SoundPacket>().Select(SoundDetail)),
            ["level events"] = List(heard.OfType<LevelEventPacket>().Select(LevelEvent)),
            ["block events"] = List(heard.OfType<BlockEventPacket>().Select(BlockEvent)),
            ["particles"] = List(heard.OfType<LevelParticlesPacket>().Select(Particle)),
            ["game events"] = List(harness.GameEvents),
            ["user effects"] = List(received.Select(UserEffect).OfType<string>()),
            ["menu"] = Menu(received),
            ["writes"] = List(writes),
            ["destroyed"] = List(destroyed)
        };

        if (!attack)
        {
            observed["result"] = Name(result);
            observed["swings"] = Swings(heard.OfType<SwingAnimationPacket>().Any(),
                received.OfType<SwingAnimationPacket>().Any());
        }

        return observed;
    }

    // The menu the user's client was told to open, by vanilla's menu type.
    private static string Menu(IClientboundPacket[] received)
    {
        var screen = received.OfType<OpenScreenPacket>().LastOrDefault();
        if (screen is null)
            return "none";

        return screen.Type switch
        {
            WindowType.ShulkerBox => "minecraft:shulker_box",
            WindowType.Generic9x3 => "minecraft:generic_9x3",
            _ => screen.Type.ToString()
        };
    }

    private static List<string> Compare(string name, Dictionary<string, string> expected,
        Dictionary<string, string> actual)
    {
        var differences = new List<string>();
        foreach (var (field, value) in expected)
        {
            var observed = actual.GetValueOrDefault(field) ?? "";
            if (knownDifferences.TryGetValue((name, field), out var known))
            {
                if (observed != known || value == known)
                    differences.Add($"{field}: the known difference changed; vanilla [{value}], got [{observed}]");
            }
            else if (observed != value)
            {
                differences.Add($"{field}: expected [{value}], got [{observed}]");
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

    // The block a sound plays in. Sound positions travel in eighths of a block.
    private static Vector SoundBlock(SoundPacket sound) => new(
        (int)Math.Floor(sound.SoundPosition.X / 8.0),
        (int)Math.Floor(sound.SoundPosition.Y / 8.0),
        (int)Math.Floor(sound.SoundPosition.Z / 8.0));

    private static string SoundDetail(SoundPacket sound)
    {
        var volume = sound.Volume.ToString("0.0####", CultureInfo.InvariantCulture);
        return $"{sound.SoundLocation} {Offset(SoundBlock(sound))} {volume}";
    }

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

    // The entities in the use's area other than players: items as "item <id> x<count>", others by type.
    private static IEnumerable<string> UseEntities(WorldData.World world)
    {
        var center = (VectorD)target + new VectorD(0.5, 0.5, 0.5);
        var min = (VectorD)target - new VectorD(Reach, Reach, Reach);
        var max = (VectorD)target + new VectorD(Reach + 1, Reach + 1, Reach + 1);
        return world.GetEntitiesInRange(center, Reach + 3)
            .OfType<Entity>()
            .Where(entity => entity is not ServerPlayer)
            .Where(entity => entity.Position.X >= min.X && entity.Position.X <= max.X
                && entity.Position.Y >= min.Y && entity.Position.Y <= max.Y
                && entity.Position.Z >= min.Z && entity.Position.Z <= max.Z)
            .Select(entity => entity is ItemEntity item ? "item " + Text(item.Item) : EntityNbt.TypeId(entity));
    }

    // Blocks within reach of the target, and the entities within 16 of it but the players.
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
        foreach (var entity in world.GetEntitiesInRange((VectorD)target, EntityReach * 2).ToArray())
        {
            if (entity is not ServerPlayer)
                await world.DestroyEntityAsync(entity);
        }

        await world.RunQueuedEntityActionsAsync();
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

    // Lists keep their order; sets (entities, statistics) are sorted, since neither side fixes theirs.
    private static string List(IEnumerable<string> values) => string.Join(", ", values);

    private static string Set(IEnumerable<string> values) => List(values.Order(StringComparer.Ordinal));

    private static string Name(JsonElement special) => special.GetProperty("name").GetString()!;

    private static string[] Strings(JsonElement values) =>
        [.. values.EnumerateArray().Select(value => value.GetString()!)];

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static Vector Position(JsonElement values) =>
        new(values[0].GetInt32(), values[1].GetInt32(), values[2].GetInt32());
}
