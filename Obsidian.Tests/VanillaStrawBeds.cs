using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.Registries.Codecs;
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

/// <summary>
/// Replays vanilla's straw beds and beds over time (<c>Assets/straw_bed_sleep.json</c>, recorded by
/// <c>StrawBedFixtures.java</c>): a player using one, sleeping as the level and the player tick, waking (by leaving
/// it, as the night passes and as the morning comes) and respawning. Each step compares the blocks, the ordered writes
/// with every flag bit, destroyed blocks, the entities, what a spectator above the bed and the user were sent, the
/// game events, the statistics, the user's messages, whether it sleeps, where it stands and its respawn point.
/// </summary>
[Collection(nameof(PluginTagFiles))]
public sealed class VanillaStrawBeds(ITestOutputHelper output)
{
    private const int Reach = 2;

    private const BlockUpdateFlags Quiet = BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace;

    private static readonly Vector target = new(0, 64, 0);

    private static readonly CaptureText text = new(target);

    // Known differences, by case, step and field: what Obsidian records instead of vanilla's value. Each fails once
    // Obsidian matches vanilla or changes, so it can be removed (known-gaps/26-3-blocks.md). A player lying down on a
    // bed falls the last eighth of a block onto it, which bounces in vanilla's own physics; Obsidian leaves players'
    // movement to their clients.
    private static readonly Dictionary<(string Case, int Step, string Field), string> knownDifferences = new()
    {
        [("bed_stays_when_night_passes", 1, "game events")] = ""
    };

    private sealed record Harness(RealServerContext Context, ServerPlayer User, ServerPlayer Bystander,
        JsonElement Standing);

    [Fact]
    public async Task ReplayStrawBedSleepReference()
    {
        using var stream = typeof(VanillaStrawBeds).Assembly
            .GetManifestResourceStream("Obsidian.Tests.Assets.straw_bed_sleep.json")!;
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        Assert.Equal(ServerConstants.ProtocolDescription, root.GetProperty("version").GetString());

        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        var harnesses = new Dictionary<string, Harness>();
        try
        {
            await FixtureCases.AssertAllAsync(cases, Name, async bed =>
            {
                var dimension = OptionalString(bed, "dimension") ?? "minecraft:overworld";
                if (!harnesses.TryGetValue(dimension, out var harness))
                {
                    harness = await StartAsync(root, dimension);
                    harnesses[dimension] = harness;
                }

                var differences = await ReplayAsync(harness, bed);
                Assert.True(differences.Count == 0, string.Join("; ", differences));
            });
        }
        finally
        {
            foreach (var harness in harnesses.Values)
                await harness.Context.DisposeAsync();
        }

        output.WriteLine($"Replayed {cases.Length} bed cases.");
    }

    // One world of the dimension, the user where the capture's player stood and a spectator above the target, who
    // doesn't count towards the sleepers needed.
    private static async Task<Harness> StartAsync(JsonElement root, string dimension)
    {
        var context = TestServerFactory.CreateServer($"straw-beds-{Guid.NewGuid():N}");
        var world = context.World;
        if (dimension != "minecraft:overworld")
            world.Initialize(CodecRegistry.Dimensions.All[dimension]);

        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
            {
                WorldBlockEntities.AddEmptyChunk(world, chunkX, chunkZ);
                await world.GetChunkAsync(chunkX, chunkZ, scheduleGeneration: false);
            }
        }

        var standing = root.GetProperty("player");
        var userPosition = new VectorD(standing[0].GetDouble(), standing[1].GetDouble(), standing[2].GetDouble());
        var user = TestServerFactory.CreateConnectedPlayer(context.Server, world, "User", userPosition);

        var bystanderPosition = (VectorD)target + new VectorD(0.5, 3, 0.5);
        var bystander = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Bystander", bystanderPosition);
        bystander.GameMode = GameMode.Spectator;
        foreach (var player in new[] { user, bystander })
        {
            world.Players.TryAdd(player.Uuid, player);
            player.LoadedChunks.Add(0);
        }

        return new Harness(context, user, bystander, standing.Clone());
    }

    private static async Task<List<string>> ReplayAsync(Harness harness, JsonElement bed)
    {
        var world = harness.Context.World;
        var user = harness.User;
        await SetUpAsync(harness, bed);

        var differences = new List<string>();
        var steps = bed.GetProperty("steps").EnumerateArray().ToArray();
        var results = bed.GetProperty("results").EnumerateArray().ToArray();
        for (var index = 0; index < steps.Length; index++)
        {
            var before = Snapshot(world);
            var statistics = user.ProgressStatistics.Save();
            ((TestClient)user.Client).ClearPackets();
            ((TestClient)harness.Bystander.Client).ClearPackets();
            world.TakeGameEvents();

            var writes = new List<string>();
            var destroyed = new List<string>();
            world.Fluids.BlockStored = (position, block, flags) => writes.Add(text.Write(position, block, flags));
            world.BlockDestroyed = (position, block, drops) => destroyed.Add(text.Destroyed(position, block, drops));
            var observed = new Dictionary<string, string>();
            try
            {
                await RunAsync(harness, steps[index], observed);
                await world.RunQueuedEntityActionsAsync();
                await world.BlockNotifications.DrainAsync();
                await world.ProcessBlockEventsAsync();
            }
            finally
            {
                world.Fluids.BlockStored = null;
                world.BlockDestroyed = null;
            }

            Observe(harness, before, statistics, writes, destroyed, observed);
            var expected = Expected(steps[index], results[index]);
            differences.AddRange(Compare(Name(bed), index, expected, observed));
            await ConfirmTeleportAsync(user);
        }

        return differences;
    }

    // The case's time of day, a stone floor, its blocks and the user as the case has it: its game mode, standing,
    // respawn point and a rested sleep timer.
    private static async Task SetUpAsync(Harness harness, JsonElement bed)
    {
        var world = harness.Context.World;
        var user = harness.User;
        if (user.Sleeping)
        {
            await user.StopSleepingAsync();
            await ConfirmTeleportAsync(user);
        }

        harness.Bystander.GameMode = GameMode.Spectator;
        await ClearAsync(world);
        SetTime(world, bed.TryGetProperty("time", out var time) ? time.GetInt64() : 18000);
        for (var x = -2; x <= 2; x++)
        {
            for (var z = -2; z <= 2; z++)
                world.BlockUpdates.SetBlock(target + new Vector(x, -1, z), BlocksRegistry.Stone, Quiet);
        }

        foreach (var setup in bed.GetProperty("setup").EnumerateArray())
        {
            var state = VanillaSulfurBlocks.ParseState(setup.GetProperty("state").GetString()!);
            world.BlockUpdates.SetBlock(target + CaptureText.Position(setup.GetProperty("offset")), state, Quiet);
        }

        var mode = OptionalString(bed, "gameMode") ?? "survival";
        user.GameMode = Enum.Parse<GameMode>(mode, ignoreCase: true);
        var standing = bed.TryGetProperty("player", out var at) ? at : harness.Standing;
        user.Position = new VectorD(standing[0].GetDouble(), standing[1].GetDouble(), standing[2].GetDouble());
        user.Yaw = standing[3].GetSingle();
        user.Pitch = standing[4].GetSingle();
        user.SleepTimer = 0;

        user.RespawnPoint = null;
        if (bed.TryGetProperty("respawnPoint", out var point))
        {
            var forced = point.TryGetProperty("forced", out var flag) && flag.GetBoolean();
            var position = target + CaptureText.Position(point.GetProperty("offset"));
            user.RespawnPoint = new RespawnPoint(world.DimensionName, position, 0, forced);
        }
    }

    private static async Task RunAsync(Harness harness, JsonElement step, Dictionary<string, string> observed)
    {
        var world = harness.Context.World;
        var user = harness.User;
        if (step.TryGetProperty("use", out var use))
        {
            var face = Enum.Parse<BlockFace>(use.GetProperty("face").GetString()!, ignoreCase: true);
            var cursor = use.GetProperty("cursor");
            var at = new VectorF(cursor[0].GetSingle(), cursor[1].GetSingle(), cursor[2].GetSingle());
            var packet = BlockUpdates.UseOn(target + CaptureText.Position(use.GetProperty("clicked")), face, at);
            observed["result"] = Name(await packet.UseAsync(harness.Context.Server, user));
        }
        else if (step.TryGetProperty("tick", out var ticks))
        {
            // A server tick: the level's, then the user's, as vanilla's connection ticks its player.
            for (var tick = 0; tick < ticks.GetInt32(); tick++)
            {
                await world.DoWorldTickAsync();
                await user.TickAsync();
            }
        }
        else if (step.TryGetProperty("bystanderMode", out var mode))
        {
            harness.Bystander.GameMode = Enum.Parse<GameMode>(mode.GetString()!, ignoreCase: true);
            await world.UpdateSleepingPlayersAsync();
        }
        else if (step.TryGetProperty("leave", out _))
        {
            if (user.Sleeping)
                await user.StopSleepingAsync(forceful: false);
        }
        else if (step.TryGetProperty("time", out var time))
        {
            SetTime(world, time.GetInt64());
        }
        else
        {
            var (level, _, _) = await user.FindRespawnAsync();
            var missing = ((TestClient)user.Client).SentPackets.OfType<GameEventPacket>()
                .Any(packet => packet.Reason == ChangeGameStateReason.NoRespawnBlockAvailable);
            observed["missing respawn block"] = missing.ToString();
            observed["respawn level"] = level.DimensionName;
        }
    }

    // The user's client accepts where the server put it (lying down, standing up), as a client does before it clicks
    // again.
    private static async Task ConfirmTeleportAsync(ServerPlayer user)
    {
        if (user.HasPendingTeleport)
            await ((TestClient)user.Client).ConfirmTeleportAsync();
    }

    private static void SetTime(WorldData.World world, long time) =>
        world.LevelData.WorldClocks.States[WorldClocks.Overworld].TotalTicks = time;

    // What Obsidian did in the step, in the capture's forms.
    private static void Observe(Harness harness, IBlock[] before, JsonObject statistics, List<string> writes,
        List<string> destroyed, Dictionary<string, string> observed)
    {
        var world = harness.Context.World;
        var user = harness.User;
        var heard = ((TestClient)harness.Bystander.Client).SentPackets.ToArray();
        var received = ((TestClient)user.Client).SentPackets.ToArray();

        observed["changes"] = Changes(world, before);
        observed["writes"] = CaptureText.List(writes);
        observed["destroyed"] = CaptureText.List(destroyed);
        observed["entities"] = CaptureText.Set(Entities(world));
        observed["sounds"] = CaptureText.List(heard.OfType<SoundPacket>().Select(sound => sound.SoundLocation));
        observed["level events"] = CaptureText.List(heard.OfType<LevelEventPacket>().Select(text.LevelEvent));
        observed["block events"] = CaptureText.List(heard.OfType<BlockEventPacket>().Select(text.BlockEvent));
        observed["game events"] = CaptureText.List(world.TakeGameEvents()
            .Select(gameEvent => text.GameEvent(gameEvent, user)));
        observed["stats"] = CaptureText.Set(CaptureText.Awarded(statistics, user.ProgressStatistics.Save())
            .Where(statistic => !statistic.StartsWith("minecraft:custom minecraft:play_time", StringComparison.Ordinal)
                && !statistic.StartsWith("minecraft:custom minecraft:total_world_time", StringComparison.Ordinal)
                && !statistic.StartsWith("minecraft:custom minecraft:time_since", StringComparison.Ordinal)));
        observed["user effects"] = CaptureText.List(received.Select(text.UserEffect).OfType<string>());
        observed["messages"] = CaptureText.List(received.OfType<SystemChatPacket>().Select(Message));
        observed["sleeping"] = user.Sleeping.ToString();
        observed["sleep timer"] = user.SleepTimer.ToString(CultureInfo.InvariantCulture);
        observed["position"] = Relative(user.Position);
        observed["respawn point"] = RespawnPoint(user);
    }

    // What the capture records for a step, in the forms Observe gives them. Where a sleeper lies once it has lain
    // through ticks is its own physics', which vanilla runs and Obsidian leaves to clients; it's compared as it lies
    // down and once it's up. A respawn step's respawn point is the one it started with in vanilla,
    // whose respawn clears it afterwards, where Obsidian's lookup clears it at once, so it isn't compared.
    private static Dictionary<string, string> Expected(JsonElement step, JsonElement result)
    {
        var sleeping = result.GetProperty("sleeping").GetBoolean();
        var expected = new Dictionary<string, string>
        {
            ["changes"] = result.GetProperty("changes").GetString()!,
            ["writes"] = CaptureText.List(text.RecordedWrites(result.GetProperty("writes"))),
            ["destroyed"] = CaptureText.List(text.RecordedDestroyed(result.GetProperty("destroyed"))),
            ["entities"] = CaptureText.Set(CaptureText.Strings(result.GetProperty("entities"))),
            ["sounds"] = CaptureText.List(CaptureText.Strings(result.GetProperty("sounds"))),
            ["level events"] = CaptureText.List(CaptureText.Strings(result.GetProperty("levelEvents"))),
            ["block events"] = CaptureText.List(CaptureText.Strings(result.GetProperty("blockEvents"))),
            ["game events"] = CaptureText.List(CaptureText.Strings(result.GetProperty("gameEvents"))),
            ["stats"] = CaptureText.Set(CaptureText.Strings(result.GetProperty("stats"))),
            ["user effects"] = CaptureText.List(CaptureText.Strings(result.GetProperty("userEffects"))),
            ["messages"] = CaptureText.List(CaptureText.Strings(result.GetProperty("messages"))),
            ["sleeping"] = sleeping.ToString()
        };

        var lyingDown = step.TryGetProperty("use", out _);
        if (!sleeping || lyingDown)
            expected["position"] = result.GetProperty("position").GetString()!;

        if (sleeping)
        {
            var timer = result.GetProperty("sleepTimer").GetInt32();
            expected["sleep timer"] = timer.ToString(CultureInfo.InvariantCulture);
        }

        if (result.TryGetProperty("result", out var use))
            expected["result"] = use.GetString()!;

        if (result.TryGetProperty("missingRespawnBlock", out var missing))
        {
            expected["missing respawn block"] = missing.GetBoolean().ToString();
            expected["respawn level"] = result.GetProperty("respawnLevel").GetString()!;
        }
        else
        {
            var point = result.GetProperty("respawnPoint");
            expected["respawn point"] = point.ValueKind == JsonValueKind.Null ? "none" : point.GetString()!;
        }

        return expected;
    }

    private static List<string> Compare(string name, int step, Dictionary<string, string> expected,
        Dictionary<string, string> actual)
    {
        var differences = new List<string>();
        foreach (var (field, value) in expected)
        {
            var got = actual.GetValueOrDefault(field, "(not observed)");
            if (knownDifferences.TryGetValue((name, step, field), out var known))
            {
                if (got != known || value == known)
                {
                    var changed = $"the known difference changed; vanilla [{value}], got [{got}]";
                    differences.Add($"step {step} {field}: {changed}");
                }
            }
            else if (got != value)
            {
                differences.Add($"step {step} {field}: expected [{value}], got [{got}]");
            }
        }

        return differences;
    }

    // A message as "overlay key" or "chat key", but advancement announcements, as the capture leaves them out.
    private static string Message(SystemChatPacket chat) =>
        (chat.Overlay ? "overlay " : "chat ") + (chat.Message.Translate ?? chat.Message.Text);

    private static string Relative(VectorD position) => string.Create(CultureInfo.InvariantCulture,
        $"{position.X - target.X:0.0000},{position.Y - target.Y:0.0000},{position.Z - target.Z:0.0000}");

    private static string RespawnPoint(ServerPlayer user)
    {
        if (user.RespawnPoint is not RespawnPoint point)
            return "none";

        var offset = text.Offset(point.Position);
        return $"{offset} {point.Dimension}" + (point.Forced ? " forced" : "");
    }

    private static string Name(UseResult? result) => result switch
    {
        UseResult.Success => "success",
        UseResult.SuccessServer => "success_server",
        UseResult.Consume => "consume",
        UseResult.Fail => "fail",
        _ => "pass"
    };

    private static IEnumerable<string> Entities(WorldData.World world)
    {
        var center = (VectorD)target + new VectorD(0.5, 0.5, 0.5);
        return world.GetEntitiesInRange(center, Reach + 3)
            .OfType<Entity>()
            .Where(entity => entity is not ServerPlayer)
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

    private static string Name(JsonElement bed) => bed.GetProperty("name").GetString()!;

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
