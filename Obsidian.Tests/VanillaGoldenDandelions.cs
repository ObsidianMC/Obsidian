using Obsidian.API;
using Obsidian.API.Inventory;
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
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using ServerPlayer = Obsidian.Entities.Player;

namespace Obsidian.Tests;

/// <summary>
/// Replays vanilla's golden dandelions locking young mobs' age (<c>Assets/golden_dandelions.json</c>, recorded by
/// <c>GoldenDandelionFixtures.java</c>): a player's interaction through the interact packet, and the mob's ticks after
/// it. An interaction compares the hand, what a bystander heard, the age particles it was sent, the game events, the
/// statistics and the mob's saved age fields; ticks compare the particles and the mob, since a mob's ambient sounds and
/// steps are its own chance.
/// </summary>
[Collection(nameof(PluginTagFiles))]
public sealed class VanillaGoldenDandelions(ITestOutputHelper output)
{
    private static readonly Vector target = new(0, 64, 0);

    private static readonly CaptureText text = new(target);

    // The particles about a mob's age, as the capture keeps them.
    private static readonly HashSet<string> ageParticles =
        ["minecraft:pause_mob_growth", "minecraft:reset_mob_growth", "minecraft:happy_villager"];

    // The saved fields about a mob's age, as the capture records them.
    private static readonly string[] ageFields = ["Age", "ForcedAge", "AgeLocked", "PersistenceRequired"];

    // Known differences, by case, step and field: what Obsidian records instead of vanilla's value. Each fails once
    // Obsidian matches vanilla or changes, so it can be removed. None is about the age lock: a horse eating plays no
    // sound or eat event, a zombie horse doesn't anger at an interaction, and a baby villager neither refuses one with
    // its sound nor stays despawnable (known-gaps/26-3-blocks.md).
    private static readonly Dictionary<(string Case, int Step, string Field), string> knownDifferences = new()
    {
        [("horse_locked_not_fed", 0, "sounds")] = "",
        [("horse_locked_not_fed", 0, "game events")] = "minecraft:entity_interact 0,0,0 player",
        [("zombie_horse_cannot", 0, "mob")] = "minecraft:zombie_horse Age=-24000 ForcedAge=0 AgeLocked=0b "
            + "PersistenceRequired=0b",
        [("zombie_horse_cannot", 0, "sounds")] = "",
        [("zombie_horse_cannot", 0, "game events")] = "",
        [("villager_cannot", 0, "mob")] = "minecraft:villager Age=-24000 ForcedAge=0 AgeLocked=0b "
            + "PersistenceRequired=1b",
        [("villager_cannot", 0, "sounds")] = ""
    };

    private sealed record Harness(RealServerContext Context, ServerPlayer User, ServerPlayer Bystander);

    [Fact]
    public async Task ReplayGoldenDandelionReference()
    {
        using var stream = typeof(VanillaGoldenDandelions).Assembly
            .GetManifestResourceStream("Obsidian.Tests.Assets.golden_dandelions.json")!;
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        Assert.Equal(ServerConstants.ProtocolDescription, root.GetProperty("version").GetString());

        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        var harness = await StartAsync(root);
        await using var context = harness.Context;

        await FixtureCases.AssertAllAsync(cases, Name, async dandelion =>
        {
            var differences = await ReplayAsync(harness, dandelion);
            Assert.True(differences.Count == 0, string.Join("; ", differences));
        });

        output.WriteLine($"Replayed {cases.Length} golden dandelion cases.");
    }

    // One world with a stone floor around the target, the user where the capture's player stood and a bystander above.
    private static async Task<Harness> StartAsync(JsonElement root)
    {
        var context = TestServerFactory.CreateServer($"golden-dandelions-{Guid.NewGuid():N}");
        var world = context.World;
        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
            {
                WorldBlockEntities.AddEmptyChunk(world, chunkX, chunkZ);
                await world.GetChunkAsync(chunkX, chunkZ, scheduleGeneration: false);
            }
        }

        for (var x = -2; x <= 2; x++)
        {
            for (var z = -2; z <= 2; z++)
                world.BlockUpdates.SetBlock(target + new Vector(x, -1, z), BlocksRegistry.Stone);
        }

        var standing = root.GetProperty("player");
        var userPosition = new VectorD(standing[0].GetDouble(), standing[1].GetDouble(), standing[2].GetDouble());
        var user = TestServerFactory.CreateConnectedPlayer(context.Server, world, "User", userPosition);
        user.Yaw = standing[3].GetSingle();
        user.Pitch = standing[4].GetSingle();

        var bystanderPosition = (VectorD)target + new VectorD(0.5, 3, 0.5);
        var bystander = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Bystander", bystanderPosition);
        // Online, so that particles sent to chosen players reach them.
        foreach (var player in new[] { user, bystander })
        {
            world.Players.TryAdd(player.Uuid, player);
            context.Server.OnlinePlayers.TryAdd(player.Uuid, player);
            player.LoadedChunks.Add(0);
        }

        return new Harness(context, user, bystander);
    }

    private static async Task<List<string>> ReplayAsync(Harness harness, JsonElement dandelion)
    {
        var world = harness.Context.World;
        var user = harness.User;
        foreach (var entity in world.GetNonPlayerEntitiesInRange((VectorD)target, 6).ToArray())
            await world.DestroyEntityAsync(entity);

        Prepare(user, dandelion);
        var mob = Spawn(world, dandelion);
        await world.RunQueuedEntityActionsAsync();

        var differences = new List<string>();
        var steps = dandelion.GetProperty("steps").EnumerateArray().ToArray();
        var results = dandelion.GetProperty("results").EnumerateArray().ToArray();
        for (var index = 0; index < steps.Length; index++)
        {
            ((TestClient)user.Client).ClearPackets();
            ((TestClient)harness.Bystander.Client).ClearPackets();
            world.TakeGameEvents();
            var statistics = user.ProgressStatistics.Save();

            var interacting = steps[index].TryGetProperty("interact", out _);
            if (interacting)
                await InteractAsync(harness, mob, Hand(dandelion));
            else
                await TickAsync(mob, steps[index].GetProperty("tick").GetInt32());

            var observed = Observe(harness, mob, Hand(dandelion), statistics);
            var expected = Expected(results[index], interacting);
            differences.AddRange(Compare(Name(dandelion), index, expected, observed));
        }

        return differences;
    }

    // The interact packet the user's client sends, and the entity action it queues.
    private static async Task InteractAsync(Harness harness, Mob mob, InteractionHand hand)
    {
        var world = harness.Context.World;
        world.LevelData.GameTime += 20;

        var packet = new InteractPacket();
        Set(packet, nameof(InteractPacket.EntityId), mob.EntityId);
        Set(packet, nameof(InteractPacket.Hand), hand);
        Set(packet, nameof(InteractPacket.Location), new VectorF(0, 0.3f, 0));
        await packet.HandleAsync(harness.Context.Server, harness.User);
        await world.RunQueuedEntityActionsAsync();
    }

    // The mob's own ticks, as the capture runs them.
    private static async Task TickAsync(Mob mob, int ticks)
    {
        for (var tick = 0; tick < ticks; tick++)
        {
            if (!mob.IsRemoved)
                await mob.TickAsync();
        }
    }

    // The case's mob at the centre of the target, young unless the case says otherwise, loaded as a save loads it.
    private static Mob Spawn(WorldData.World world, JsonElement dandelion)
    {
        var tag = new NbtCompound
        {
            new NbtTag<string>("id", dandelion.GetProperty("entity").GetString()!),
            new NbtList(NbtTagType.Double, "Pos")
            {
                new NbtTag<double>("", target.X + 0.5),
                new NbtTag<double>("", target.Y),
                new NbtTag<double>("", target.Z + 0.5)
            }
        };

        var baby = !dandelion.TryGetProperty("baby", out var young) || young.GetBoolean();
        if (dandelion.TryGetProperty("age", out var age))
            tag.Add(new NbtTag<int>("Age", age.GetInt32()));
        else if (baby)
            tag.Add(new NbtTag<int>("Age", AgeableMob.BabyStartAge));

        if (dandelion.TryGetProperty("ageLocked", out var locked))
            tag.Add(new NbtTag<bool>("AgeLocked", locked.GetBoolean()));

        var mob = Assert.IsAssignableFrom<Mob>(EntityNbt.Load(tag, world));
        Assert.True(world.TryAddEntity(mob));
        return mob;
    }

    private static void Prepare(ServerPlayer user, JsonElement dandelion)
    {
        var mode = OptionalString(dandelion, "gameMode") ?? "survival";
        user.GameMode = Enum.Parse<GameMode>(mode, ignoreCase: true);
        for (var slot = 9; slot <= 45; slot++)
            user.Inventory.SetItem(slot, null);

        var count = dandelion.TryGetProperty("count", out var held) ? held.GetInt32() : 2;
        user.Inventory.SetItem(user.CurrentHeldItemSlot, Stack(OptionalString(dandelion, "item"), count));
        user.Inventory.SetItem(45, Stack(OptionalString(dandelion, "offhand"), 2));
    }

    private static ItemStack? Stack(string? item, int count) =>
        item is null ? null : new ItemStack(ItemsRegistry.Get(item), count);

    // What Obsidian did in the step, in the capture's forms.
    private static Dictionary<string, string> Observe(Harness harness, Mob mob, InteractionHand used,
        System.Text.Json.Nodes.JsonObject statistics)
    {
        var world = harness.Context.World;
        var user = harness.User;
        var heard = ((TestClient)harness.Bystander.Client).SentPackets.ToArray();
        var hand = used == InteractionHand.OffHand ? user.GetOffHandItem() : user.GetHeldItem();

        return new Dictionary<string, string>
        {
            ["hand"] = CaptureText.Stack(hand),
            ["sounds"] = CaptureText.List(heard.OfType<SoundPacket>().Select(Sound)),
            ["particles"] = CaptureText.List(heard.OfType<LevelParticlesPacket>()
                .Select(CaptureText.Particle)
                .Where(particle => ageParticles.Contains(particle[..particle.IndexOf(' ')]))),
            ["game events"] = CaptureText.List(world.TakeGameEvents()
                .Select(gameEvent => text.GameEvent(gameEvent, user))),
            ["stats"] = CaptureText.Set(CaptureText.Awarded(statistics, user.ProgressStatistics.Save())),
            ["mob"] = Describe(world, mob)
        };
    }

    // What the capture records for a step: everything for an interaction, the particles and the mob for ticks.
    private static Dictionary<string, string> Expected(JsonElement result, bool interacting)
    {
        var expected = new Dictionary<string, string>
        {
            ["particles"] = CaptureText.List(CaptureText.Strings(result.GetProperty("particles"))),
            ["mob"] = result.GetProperty("mob").GetString()!
        };

        if (!interacting)
            return expected;

        expected["hand"] = result.GetProperty("hand").GetString()!;
        expected["sounds"] = CaptureText.List(CaptureText.Strings(result.GetProperty("sounds")));
        expected["game events"] = CaptureText.List(CaptureText.Strings(result.GetProperty("gameEvents")));
        expected["stats"] = CaptureText.Set(CaptureText.Strings(result.GetProperty("stats")));
        return expected;
    }

    private static List<string> Compare(string name, int step, Dictionary<string, string> expected,
        Dictionary<string, string> actual)
    {
        var differences = new List<string>();
        foreach (var (field, value) in expected)
        {
            var got = actual[field];
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

    // The mob's type and saved age fields as vanilla writes them, or what replaced it when it grew into another type.
    private static string Describe(WorldData.World world, Mob mob)
    {
        Entity current = mob;
        if (mob.IsRemoved)
        {
            var replaced = world.GetNonPlayerEntitiesInRange(mob.Position, 3).OfType<Entity>().FirstOrDefault();
            if (replaced is null)
                return "removed";

            current = replaced;
        }

        var saved = EntityNbt.Save(current)!;
        var description = EntityNbt.TypeId(current);
        foreach (var field in ageFields)
        {
            if (!saved.TryGetTag(field, out var value))
                continue;

            var written = value switch
            {
                NbtTag<bool> flag => flag.Value ? "1b" : "0b",
                NbtTag<byte> flag => $"{flag.Value}b",
                NbtTag<int> number => number.Value.ToString(CultureInfo.InvariantCulture),
                _ => value.ToString()
            };
            description += $" {field}={written}";
        }

        return description;
    }

    // A sound as "id source volume pitch", the source named as vanilla's SoundSource names it.
    private static string Sound(SoundPacket sound)
    {
        var source = sound.Category switch
        {
            SoundCategory.Blocks => "block",
            SoundCategory.Players => "player",
            SoundCategory.Records => "record",
            var category => category.ToString().ToLowerInvariant()
        };

        return string.Create(CultureInfo.InvariantCulture,
            $"{sound.SoundLocation} {source} {sound.Volume:0.0} {sound.Pitch:0.0}");
    }

    private static InteractionHand Hand(JsonElement dandelion) =>
        OptionalString(dandelion, "handUsed") == "offhand" ? InteractionHand.OffHand : InteractionHand.MainHand;

    private static void Set(object target, string property, object value) =>
        target.GetType().GetProperty(property)!.SetValue(target, value);

    private static string Name(JsonElement dandelion) => dandelion.GetProperty("name").GetString()!;

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
