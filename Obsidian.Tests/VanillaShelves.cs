using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.Net.Packets.Play.Serverbound;
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
/// Replays vanilla's shelves (<c>Assets/shelves.json</c>, recorded by <c>ShelfFixtures.java</c>) in one shared world: a
/// player's use through the use-item-on packet, a block set beside shelves with every update, one hopper transfer
/// and a player breaking a shelf. Each case compares, from the start: the result and the swings it decides, the hand
/// and inventory, the blocks, the ordered writes with every flag bit, destroyed blocks, the entities, what a player
/// above the shelf and the user's own client were sent, the game events, the statistics, every block entity's saved
/// data and a comparator's output.
/// </summary>
[Collection(nameof(PluginTagFiles))]
public sealed class VanillaShelves(ITestOutputHelper output)
{
    // How far from the target the compared area reaches, as in the capture.
    private const int Reach = 2;

    // Setup and clearing, like the capture's: no neighbor or shape updates and nothing placed reacting.
    private const BlockUpdateFlags Quiet = BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace;

    private static readonly Vector target = new(0, 64, 0);

    private static readonly CaptureText text = new(target);

    // Known differences, by case and field: what Obsidian records instead of vanilla's value. Each fails once Obsidian
    // matches vanilla or changes, so it can be removed. A player breaking a block sends the players around its break
    // sound where vanilla sends level event 2001, whose clients play it, and its block_destroy game event follows the
    // removal's own effects (known-gaps/player-break-effects.md, as VanillaBlockUse pins them).
    private static readonly Dictionary<(string Case, string Field), string> knownDifferences = new()
    {
        [("break_drops_items", "sounds")] = "minecraft:block.shelf.break",
        [("break_drops_items", "sound details")] = "minecraft:block.shelf.break 0,0,0 1.0",
        [("break_drops_items", "level events")] = "",
        [("break_with_comparator_behind", "sounds")] = "minecraft:block.shelf.break",
        [("break_with_comparator_behind", "sound details")] = "minecraft:block.shelf.break 0,0,0 1.0",
        [("break_with_comparator_behind", "level events")] = "",
        [("break_middle_of_chain", "sounds")] =
            "minecraft:block.shelf.deactivate, minecraft:block.shelf.deactivate, minecraft:block.shelf.break",
        [("break_middle_of_chain", "sound details")] =
            "minecraft:block.shelf.deactivate -1,0,0 1.0, minecraft:block.shelf.deactivate 1,0,0 1.0, "
            + "minecraft:block.shelf.break 0,0,0 1.0",
        [("break_middle_of_chain", "level events")] = "",
        [("break_middle_of_chain", "game events")] =
            "minecraft:block_deactivate -1,0,0 none, minecraft:block_deactivate 1,0,0 none, "
            + "minecraft:block_destroy 0,0,0 player"
    };

    private sealed record Harness(RealServerContext Context, ServerPlayer User, ServerPlayer Bystander);

    [Fact]
    public async Task ReplayShelvesReference()
    {
        using var stream = typeof(VanillaShelves).Assembly
            .GetManifestResourceStream("Obsidian.Tests.Assets.shelves.json")!;
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        Assert.Equal(ServerConstants.ProtocolDescription, root.GetProperty("version").GetString());

        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        var harness = await StartAsync(root);
        await using var context = harness.Context;

        await FixtureCases.AssertAllAsync(cases, Name, async shelf =>
        {
            var differences = await ReplayAsync(harness, shelf);
            Assert.True(differences.Count == 0, string.Join("; ", differences));
        });

        output.WriteLine($"Replayed {cases.Length} shelf cases.");
    }

    // One world with full sky light, as the capture's open sky has, the user where the capture's player stood and a
    // bystander above the target. The chunks around the target are loaded, so comparators beside it tick.
    private static async Task<Harness> StartAsync(JsonElement root)
    {
        var context = TestServerFactory.CreateServer($"shelves-{Guid.NewGuid():N}");
        var world = context.World;

        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
            {
                var chunk = WorldBlockEntities.AddEmptyChunk(world, chunkX, chunkZ);
                await world.GetChunkAsync(chunkX, chunkZ, scheduleGeneration: false);
                LightSky(chunk);
            }
        }

        var standing = root.GetProperty("player");
        var userPosition = new VectorD(standing[0].GetDouble(), standing[1].GetDouble(), standing[2].GetDouble());
        var user = TestServerFactory.CreateConnectedPlayer(context.Server, world, "User", userPosition);
        user.Yaw = standing[3].GetSingle();
        user.Pitch = standing[4].GetSingle();

        var bystanderPosition = (VectorD)target + new VectorD(0.5, 3, 0.5);
        var bystander = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Bystander", bystanderPosition);
        foreach (var player in new[] { user, bystander })
        {
            world.Players.TryAdd(player.Uuid, player);
            player.LoadedChunks.Add(0);
        }

        return new Harness(context, user, bystander);
    }

    private static void LightSky(Chunk chunk)
    {
        for (var y = target.Y - Reach - 1; y <= target.Y + Reach + 1; y++)
        {
            for (var z = 0; z < 16; z++)
            {
                for (var x = 0; x < 16; x++)
                    chunk.SetLightLevel(x, y, z, LightType.Sky, 15);
            }
        }
    }

    private static async Task<List<string>> ReplayAsync(Harness harness, JsonElement shelf)
    {
        var world = harness.Context.World;
        var user = harness.User;

        // The case's blocks and block entity data in a cleared area, and the user's inventory as the case has it.
        await ClearAsync(world);
        SetUp(world, shelf);
        Prepare(user, shelf);

        var before = Snapshot(world);
        var statistics = user.ProgressStatistics.Save();
        ((TestClient)user.Client).ClearPackets();
        ((TestClient)harness.Bystander.Client).ClearPackets();
        world.TakeGameEvents();

        // What the case does, then the block updates it caused and the tick's block events, with every write observed.
        var writes = new List<string>();
        var destroyed = new List<string>();
        world.Fluids.BlockStored = (position, block, flags) => writes.Add(text.Write(position, block, flags));
        world.BlockDestroyed = (position, block, drops) => destroyed.Add(text.Destroyed(position, block, drops));
        UseResult? result;
        try
        {
            result = await ActAsync(harness, shelf);
            await world.RunQueuedEntityActionsAsync();
            await world.BlockNotifications.DrainAsync();
            await world.ProcessBlockEventsAsync();
        }
        finally
        {
            world.Fluids.BlockStored = null;
            world.BlockDestroyed = null;
        }

        var actual = Observe(harness, shelf, result, before, statistics, writes, destroyed);
        if (shelf.TryGetProperty("comparator", out var comparator))
        {
            var output = await ComparatorOutputAsync(world, target + CaptureText.Position(comparator));
            actual["comparator"] = output.ToString();
        }

        return Compare(Name(shelf), Expected(shelf), actual);
    }

    // The case's action: a use, a block set with every update, a hopper's transfer or the user breaking a block.
    private static async Task<UseResult?> ActAsync(Harness harness, JsonElement shelf)
    {
        var world = harness.Context.World;
        switch (Kind(shelf))
        {
            case "use":
                return await Packet(shelf).UseAsync(harness.Context.Server, harness.User);
            case "set":
                var place = shelf.GetProperty("place");
                var position = target + CaptureText.Position(place.GetProperty("offset"));
                var state = VanillaSulfurBlocks.ParseState(place.GetProperty("state").GetString()!);
                await world.SetBlockAsync(position, state, BlockUpdateFlags.All);
                return null;
            case "hopper":
                var at = target + CaptureText.Position(shelf.GetProperty("hopper"));
                var hopper = Assert.IsType<HopperBlockEntity>(world.GetResidentBlockEntity(at));
                await Hoppers.TickAsync(world, hopper);
                return null;
            default:
                var clicked = target + CaptureText.Position(shelf.GetProperty("clicked"));
                await FinishDigging(clicked).HandleAsync(harness.Context.Server, harness.User);
                return null;
        }
    }

    private static void SetUp(WorldData.World world, JsonElement shelf)
    {
        foreach (var setup in shelf.GetProperty("setup").EnumerateArray())
        {
            var position = target + CaptureText.Position(setup.GetProperty("offset"));
            var state = VanillaSulfurBlocks.ParseState(setup.GetProperty("state").GetString()!);
            world.BlockUpdates.SetBlock(position, state, Quiet);
        }

        // Block entity data loads as a chunk loads it, telling no comparator.
        foreach (var setup in shelf.GetProperty("setup").EnumerateArray())
        {
            if (OptionalString(setup, "nbt") is not string snbt)
                continue;

            var position = target + CaptureText.Position(setup.GetProperty("offset"));
            var blockEntity = (BlockEntity)world.GetResidentBlockEntity(position)!;
            blockEntity.Load(SnbtReader.ReadCompound(snbt));
        }
    }

    private static void Prepare(ServerPlayer user, JsonElement shelf)
    {
        var mode = OptionalString(shelf, "gameMode") ?? "survival";
        user.GameMode = Enum.Parse<GameMode>(mode, ignoreCase: true);
        user.Sneaking = shelf.TryGetProperty("sneaking", out var sneaking) && sneaking.GetBoolean();
        for (var slot = 9; slot <= 45; slot++)
            user.Inventory.SetItem(slot, null);

        var hotbarSlot = shelf.TryGetProperty("selected", out var selected) ? selected.GetInt32() : 0;
        user.CurrentHeldItemSlot = (short)hotbarSlot;
        var count = shelf.TryGetProperty("count", out var held) ? held.GetInt32() : 2;
        user.Inventory.SetItem(user.CurrentHeldItemSlot, Stack(OptionalString(shelf, "item"), count));
        user.Inventory.SetItem(45, Stack(OptionalString(shelf, "offhand"), 2));

        if (!shelf.TryGetProperty("hotbar", out var hotbar))
            return;

        foreach (var entry in hotbar.EnumerateArray())
        {
            var slot = entry[0].GetInt32();
            var stack = Stack(entry[1].GetString(), entry[2].GetInt32());
            user.Inventory.SetItem(HotbarSlot + slot, stack);
        }
    }

    // The player inventory's first hotbar slot; vanilla numbers the hotbar from 0.
    private const int HotbarSlot = 36;

    private static ItemStack? Stack(string? item, int count) =>
        item is null ? null : new ItemStack(ItemsRegistry.Get(item), count);

    private static UseItemOnPacket Packet(JsonElement shelf)
    {
        var cursor = shelf.TryGetProperty("cursor", out var point)
            ? new VectorF(point[0].GetSingle(), point[1].GetSingle(), point[2].GetSingle())
            : new VectorF(0.5f, 0.5f, 0.5f);
        var face = Enum.Parse<BlockFace>(shelf.GetProperty("face").GetString()!, ignoreCase: true);
        var packet = BlockUpdates.UseOn(target + CaptureText.Position(shelf.GetProperty("clicked")), face, cursor);
        if (UsesOffhand(shelf))
        {
            var hand = typeof(UseItemOnPacket).GetProperty(nameof(UseItemOnPacket.Hand))!;
            hand.SetValue(packet, InteractionHand.OffHand);
        }

        return packet;
    }

    private static PlayerActionPacket FinishDigging(Vector position)
    {
        using var writer = new Obsidian.Net.NetworkBuffer();
        writer.WriteVarInt((int)PlayerActionStatus.FinishedDigging);
        writer.WritePosition(position);
        writer.WriteByte((byte)BlockFace.North);
        writer.WriteVarInt(1);

        using var reader = new Obsidian.Net.NetworkBuffer(writer.AsSpan(0, writer.Size).ToArray());
        var packet = new PlayerActionPacket();
        packet.Populate(reader);
        return packet;
    }

    // What the capture records, read from the reference, field by field in the form Observe gives them.
    private static Dictionary<string, string> Expected(JsonElement shelf)
    {
        var expected = new Dictionary<string, string>
        {
            ["hand"] = shelf.GetProperty("hand").GetString()!,
            ["changes"] = shelf.GetProperty("changes").GetString()!,
            ["entities"] = CaptureText.Set(CaptureText.Strings(shelf.GetProperty("entities"))
                .Select(entity => entity.StartsWith("item ", StringComparison.Ordinal)
                    ? "item " + CaptureText.CanonicalStack(entity[5..])
                    : entity)),
            ["stats"] = CaptureText.Set(CaptureText.Strings(shelf.GetProperty("stats"))),
            ["sounds"] = CaptureText.List(CaptureText.Strings(shelf.GetProperty("sounds"))),
            ["sound details"] = CaptureText.List(CaptureText.Strings(shelf.GetProperty("soundDetails"))),
            ["level events"] = CaptureText.List(CaptureText.Strings(shelf.GetProperty("levelEvents"))),
            ["block events"] = CaptureText.List(CaptureText.Strings(shelf.GetProperty("blockEvents"))),
            ["particles"] = CaptureText.List(CaptureText.Strings(shelf.GetProperty("particles"))),
            ["game events"] = CaptureText.List(CaptureText.Strings(shelf.GetProperty("gameEvents"))),
            ["user effects"] = CaptureText.List(CaptureText.Strings(shelf.GetProperty("userEffects"))),
            ["writes"] = CaptureText.List(text.RecordedWrites(shelf.GetProperty("writes"))),
            ["destroyed"] = CaptureText.List(text.RecordedDestroyed(shelf.GetProperty("destroyed"))),
            ["block entities"] = CaptureText.List(CaptureText.Strings(shelf.GetProperty("blockEntities"))
                .Select(CanonicalBlockEntity))
        };

        if (shelf.TryGetProperty("result", out var result))
        {
            var name = result.GetString()!;
            expected["result"] = name;
            expected["swings"] = Swings(name is "success" or "success_server", name == "success_server");
        }

        if (shelf.TryGetProperty("inventory", out var inventory))
        {
            var slots = CaptureText.Strings(inventory).Select(CaptureText.CanonicalStack);
            expected["inventory"] = CaptureText.List(slots);
        }

        if (shelf.TryGetProperty("comparatorSignal", out var signal))
            expected["comparator"] = signal.GetInt32().ToString();

        return expected;
    }

    // What Obsidian did, in the capture's forms.
    private static Dictionary<string, string> Observe(Harness harness, JsonElement shelf, UseResult? result,
        IBlock[] before, JsonObject statistics, List<string> writes, List<string> destroyed)
    {
        var world = harness.Context.World;
        var user = harness.User;
        var heard = ((TestClient)harness.Bystander.Client).SentPackets.ToArray();
        var received = ((TestClient)user.Client).SentPackets.ToArray();
        var hand = UsesOffhand(shelf) ? user.GetOffHandItem() : user.GetHeldItem();

        var observed = new Dictionary<string, string>
        {
            ["result"] = Name(result),
            ["swings"] = Swings(heard.OfType<SwingAnimationPacket>().Any(),
                received.OfType<SwingAnimationPacket>().Any()),
            ["hand"] = CaptureText.Stack(hand),
            ["changes"] = Changes(world, before),
            ["entities"] = CaptureText.Set(Entities(world)),
            ["stats"] = CaptureText.Set(CaptureText.Awarded(statistics, user.ProgressStatistics.Save())),
            ["sounds"] = CaptureText.List(heard.OfType<SoundPacket>().Select(sound => sound.SoundLocation)),
            ["sound details"] = CaptureText.List(heard.OfType<SoundPacket>().Select(text.SoundDetail)),
            ["level events"] = CaptureText.List(heard.OfType<LevelEventPacket>().Select(text.LevelEvent)),
            ["block events"] = CaptureText.List(heard.OfType<BlockEventPacket>().Select(text.BlockEvent)),
            ["particles"] = CaptureText.List(heard.OfType<LevelParticlesPacket>().Select(CaptureText.Particle)),
            ["game events"] = CaptureText.List(world.TakeGameEvents()
                .Select(gameEvent => text.GameEvent(gameEvent, user))),
            ["user effects"] = CaptureText.List(received.Select(text.UserEffect).OfType<string>()),
            ["writes"] = CaptureText.List(writes),
            ["destroyed"] = CaptureText.List(destroyed),
            ["block entities"] = CaptureText.List(BlockEntities(world, shelf)),
            ["inventory"] = CaptureText.List(Enumerable.Range(0, 36)
                .Select(slot => CaptureText.DetailedStack(user.Inventory.GetItem(slot < 9 ? HotbarSlot + slot : slot))))
        };

        return observed;
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

    // The saved data of every block entity still where the setup placed one, in its order, as "dx,dy,dz snbt".
    private static IEnumerable<string> BlockEntities(WorldData.World world, JsonElement shelf)
    {
        foreach (var setup in shelf.GetProperty("setup").EnumerateArray())
        {
            var offset = CaptureText.Position(setup.GetProperty("offset"));
            var position = target + offset;
            var state = world.BlockUpdates.GetBlock(position);
            if (!HasBlockEntity(state) && world.GetResidentBlockEntity(position) is null)
                continue;

            var saved = CaptureText.SavedData(world, position) ?? "none";
            yield return $"{offset.X},{offset.Y},{offset.Z} {saved}";
        }
    }

    private static bool HasBlockEntity(IBlock state) =>
        state.UnlocalizedName.EndsWith("_shelf", StringComparison.Ordinal)
        || state.UnlocalizedName is "minecraft:hopper" or "minecraft:comparator";

    // A recorded "dx,dy,dz snbt" with its SNBT as Obsidian writes the same NBT.
    private static string CanonicalBlockEntity(string recorded)
    {
        var space = recorded.IndexOf(' ');
        var data = recorded[(space + 1)..];
        return data == "none" ? recorded : $"{recorded[..space]} {CaptureText.CanonicalNbt(data)}";
    }

    // The comparator's output once the block ticks due within the comparator's delay ran, as the capture runs its
    // pending tick.
    private static async Task<int> ComparatorOutputAsync(WorldData.World world, Vector comparator)
    {
        await world.BlockNotifications.DrainAsync();
        for (var tick = 0; tick < 2; tick++)
        {
            world.Fluids.Tick();
            await world.RunQueuedEntityActionsAsync();
            await world.BlockNotifications.DrainAsync();
        }

        var entity = world.GetResidentBlockEntity(comparator);
        return Assert.IsType<WorldData.Redstone.ComparatorBlockEntity>(entity).OutputSignal;
    }

    // Who was shown a swing: vanilla swings for the players around after a success, and for the user too when its
    // client didn't predict it.
    private static string Swings(bool bystander, bool user) => $"bystander={bystander}, user={user}";

    private static string Name(UseResult? result) => result switch
    {
        UseResult.Success => "success",
        UseResult.SuccessServer => "success_server",
        UseResult.Consume => "consume",
        UseResult.Fail => "fail",
        _ => "pass"
    };

    // The entities in the area other than players: items with their components, others by type.
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
            .Select(entity => entity is ItemEntity item
                ? "item " + CaptureText.DetailedStack(item.Item)
                : EntityNbt.TypeId(entity));
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
            .Select(entry => Changed(world, entry.position));

        return string.Join(';', changed);
    }

    private static string Changed(WorldData.World world, Vector position) =>
        $"{text.Offset(position)}={CaptureText.State(world.BlockUpdates.GetBlock(position))}";

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

    private static string Kind(JsonElement shelf) => OptionalString(shelf, "kind") ?? "use";

    private static bool UsesOffhand(JsonElement shelf) => OptionalString(shelf, "handUsed") == "offhand";

    private static string Name(JsonElement shelf) => shelf.GetProperty("name").GetString()!;

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
