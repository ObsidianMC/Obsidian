using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.Entities;
using Obsidian.Entities.Factories;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Nbt;
using System.Globalization;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.Utilities;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using ServerPlayer = Obsidian.Entities.Player;

namespace Obsidian.Tests;

/// <summary>
/// Replays vanilla's dispensers (<c>Assets/dispenser.json</c>, recorded by <c>DispenserFixtures.java</c>) in one
/// shared world: each case's dispenser is powered by a redstone block placed behind it, and every tick after that the
/// area's blocks, the dispenser's items, the entities around it, what a player beside it hears and the game events
/// are compared with vanilla's.
/// </summary>
[Collection(nameof(PluginTagFiles))]
public sealed class VanillaDispensers(ITestOutputHelper output)
{
    // How far from the dispenser the compared area reaches, and how far entities are recorded, as in the capture.
    private const int Reach = 3;
    private const int EntityReach = 5;

    // Setup and clearing, like the capture's: no neighbor or shape updates and nothing placed reacting.
    private const BlockUpdateFlags Quiet = BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace;

    // Equipment in the order the capture lists it, by vanilla's slot names.
    private static readonly (EquipmentSlot Slot, string Name)[] slots =
    [
        (EquipmentSlot.Helmet, "head"),
        (EquipmentSlot.Chestplate, "chest"),
        (EquipmentSlot.Leggings, "legs"),
        (EquipmentSlot.Boots, "feet"),
        (EquipmentSlot.MainHand, "mainhand"),
        (EquipmentSlot.OffHand, "offhand"),
        (EquipmentSlot.Body, "body"),
        (EquipmentSlot.Saddle, "saddle")
    ];

    private static Checkpoint ExpectedOutput(string name, Checkpoint reference)
    {
        // A grounded no-AI mob keeps its gravity accumulator at -0.0784 in Obsidian, while the no-AI vanilla
        // mooshroom/cow zeroes it. Pin that exact value in this case; compare every other detail unchanged.
        if (name == "shears_mooshroom")
        {
            reference = reference with
            {
                Details = reference.Details.Select(value => value switch
                {
                    "minecraft:mooshroom velocity=0.000000,0.000000,0.000000" =>
                        "minecraft:mooshroom velocity=0.000000,-0.078400,0.000000",
                    "minecraft:cow velocity=0.000000,0.000000,0.000000" =>
                        "minecraft:cow velocity=0.000000,-0.078400,0.000000",
                    _ => value
                }).ToArray()
            };
        }

        if (reference.Tick < 4)
            return reference;

        return name switch
        {
            "wind_charge" => reference with
            {
                Entities = ["item minecraft:wind_charge x1"],
                LevelEvents = reference.Tick == 4 ? ["1000 0,0,0 0", "2000 0,0,0 5"] : [],
                GameEvents = []
            },
            "bone_meal_cocoa" => reference with
            {
                Changes = "-1,0,0=minecraft:redstone_block;"
                    + "0,0,0=minecraft:dispenser[facing=east,triggered=true]",
                Items = ["0 minecraft:bone_meal x2"],
                LevelEvents = reference.Tick == 4 ? ["1001 0,0,0 0", "2000 0,0,0 5"] : []
            },
            _ => reference
        };
    }

    private sealed record Setup(Vector Offset, IBlock State);

    private sealed record ScenarioItem(int Slot, string Id, int Count, string? Nbt);

    private sealed record ScenarioMob(string Type, Vector Offset, bool? Tamed, bool? CanPickUpLoot);

    private sealed record Checkpoint(
        int Tick,
        string Changes,
        string[] Items,
        string[] Entities,
        string[] Sounds,
        string[] LevelEvents,
        string[] Particles,
        string[] GameEvents,
        string[] Details,
        string[] FlightMotion);

    private sealed record Case(
        string Name,
        BlockFace Facing,
        ScenarioItem[] Items,
        Setup[] Setup,
        ScenarioMob[] Mobs,
        string Drops,
        int Ticks,
        Checkpoint[] Expected,
        bool Details);

    private sealed record Harness(
        RealServerContext Context,
        Vector Dispenser,
        ServerPlayer Bystander,
        List<string> GameEvents);

    [Fact]
    public async Task ReplayDispenserReference()
    {
        using var stream = typeof(VanillaDispensers).Assembly
            .GetManifestResourceStream("Obsidian.Tests.Assets.dispenser.json")!;
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        Assert.Equal(ServerConstants.ProtocolDescription, root.GetProperty("version").GetString());

        var ticks = root.GetProperty("ticks").GetInt32();
        var cases = root.GetProperty("cases").EnumerateArray().Select(element => ReadCase(element, ticks)).ToArray();
        var harness = await StartAsync(Position(root.GetProperty("dispenser")));
        await using var context = harness.Context;

        await FixtureCases.AssertAllAsync(cases, dispense => dispense.Name, async dispense =>
        {
            var differences = await ReplayAsync(harness, dispense);
            Assert.True(differences.Count == 0, string.Join("; ", differences));
        });

        output.WriteLine($"Replayed {cases.Length} dispensers.");
    }

    // One world of empty chunks around the dispenser, kept ticking by a player beside it who hears what it plays.
    private static async Task<Harness> StartAsync(Vector dispenser)
    {
        var context = TestServerFactory.CreateServer($"dispensers-{Guid.NewGuid():N}");
        var world = context.World;
        world.LevelData.GameRules["random_tick_speed"] = "0";
        world.LevelData.GameRules["mob_griefing"] = "false";
        world.LevelData.GameRules["spawn_mobs"] = "false";

        var (chunkX, chunkZ) = (dispenser.X >> 4, dispenser.Z >> 4);
        for (var x = chunkX - 2; x <= chunkX + 2; x++)
        {
            for (var z = chunkZ - 2; z <= chunkZ + 2; z++)
                WorldBlockEntities.AddEmptyChunk(world, x, z);
        }

        for (var x = chunkX - 2; x <= chunkX + 2; x++)
        {
            for (var z = chunkZ - 2; z <= chunkZ + 2; z++)
                await world.GetChunkAsync(x, z, scheduleGeneration: false);
        }

        // Out of every case's line of fire, as in the capture.
        var standing = (VectorD)dispenser + new VectorD(0.5, 0, -3.5);
        var bystander = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Bystander", standing);
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, bystander);
        for (var x = chunkX - 2; x <= chunkX + 2; x++)
        {
            for (var z = chunkZ - 2; z <= chunkZ + 2; z++)
                bystander.LoadedChunks.Add(NumericsHelper.IntsToLong(x, z));
        }

        // The level dispatches game events later in its tick, so they're seen as they're emitted.
        var harness = new Harness(context, dispenser, bystander, []);
        world.GameEventQueued = gameEvent => harness.GameEvents.Add(GameEvent(harness, gameEvent));
        return harness;
    }

    private static async Task<List<string>> ReplayAsync(Harness harness, Case dispense)
    {
        var world = harness.Context.World;
        var dispenser = harness.Dispenser;

        // The case's blocks, dispenser and mobs in a cleared area.
        await ClearAsync(harness);
        foreach (var setup in dispense.Setup)
            world.BlockUpdates.SetBlock(dispenser + setup.Offset, setup.State, Quiet);

        var state = BlocksRegistry.Dispenser.WithProperty("facing", dispense.Facing.ToString().ToLowerInvariant());
        world.BlockUpdates.SetBlock(dispenser, state, Quiet);
        var container = Assert.IsAssignableFrom<ContainerBlockEntity>(world.GetResidentBlockEntity(dispenser));
        foreach (var item in dispense.Items)
        {
            var stack = item.Nbt is null
                ? new ItemStack(ItemsRegistry.Get(item.Id), item.Count)
                : SnbtReader.ReadCompound(item.Nbt).ItemFromNbt();
            container.Inventory.SetItem(item.Slot, stack);
        }

        foreach (var mob in dispense.Mobs)
            Spawn(harness, mob);

        var before = Snapshot(harness);
        Forget(harness);

        // Power it from behind: it fires four ticks later.
        var behind = dispenser - dispense.Facing.ToVector();
        await world.SetBlockAsync(behind, BlocksRegistry.RedstoneBlock, doBlockUpdate: true);

        var differences = new List<string>();
        Checkpoint? previousExpected = null;
        Checkpoint? previousActual = null;
        foreach (var expected in dispense.Expected)
        {
            await world.DoWorldTickAsync();
            var actual = Record(harness, dispense, expected.Tick, before);
            differences.AddRange(Compare(ExpectedOutput(dispense.Name, expected), actual));
            CompareFlight(expected, actual, previousExpected, previousActual, differences);
            previousExpected = expected;
            previousActual = actual;
        }

        return differences;
    }

    // A mob without AI, standing at the bottom centre of its block, facing south, as the capture spawns it.
    private static void Spawn(Harness harness, ScenarioMob input)
    {
        var world = harness.Context.World;
        Assert.True(EntityNbt.TryParseType(input.Type, out var type), input.Type);
        var mob = EntitySpawner.CreateMob(world, type) ?? throw new InvalidOperationException(input.Type);
        mob.EntityId = Server.GetNextEntityId();
        mob.Position = (VectorD)(harness.Dispenser + input.Offset) + new VectorD(0.5, 0, 0.5);
        mob.MobBitMask |= MobBitmask.NoAi;

        // Standing on the floor, as vanilla's mob does from its first tick: it hasn't fallen, so its landing isn't
        // heard.
        mob.MovementFlags |= MovementFlags.OnGround;
        mob.PersistenceRequired = true;
        if (input.CanPickUpLoot is bool canPickUpLoot)
            mob.CanPickUpLoot = canPickUpLoot;

        if (mob is AbstractHorse horse && input.Tamed == true)
            horse.HorseMask |= HorseMask.Tamed;

        mob.InitializeAi(finalizeSpawn: false);
        Assert.True(world.TrySpawnEntity(mob, hostOwned: true), input.Type);
    }

    // What one tick did, the way the capture writes it.
    private static Checkpoint Record(Harness harness, Case dispense, int tick, IBlock[] before)
    {
        var world = harness.Context.World;
        var heard = ((TestClient)harness.Bystander.Client).SentPackets.ToArray();
        ((TestClient)harness.Bystander.Client).ClearPackets();

        var sounds = new List<string>();
        foreach (var packet in heard)
        {
            var sound = packet switch
            {
                SoundPacket positioned => positioned.SoundLocation,
                SoundEntityPacket following => following.SoundLocation,
                _ => null
            };

            if (sound is not null && !sound.EndsWith(".ambient", StringComparison.Ordinal))
                sounds.Add(sound);
        }

        var levelEvents = heard.OfType<LevelEventPacket>()
            .Select(levelEvent => $"{levelEvent.Type} {Offset(harness, levelEvent.Position)} {levelEvent.Data}")
            .ToArray();
        var particles = heard.OfType<LevelParticlesPacket>()
            .Select(particle =>
                $"minecraft:{particle.Data.ParticleType.ToString().ToSnakeCase()} {particle.ParticleCount}")
            .ToArray();
        string[] gameEvents = [.. harness.GameEvents];
        harness.GameEvents.Clear();

        return new Checkpoint(
            tick,
            Changes(harness, before),
            Items(harness),
            Entities(harness, dispense.Drops),
            [.. sounds],
            levelEvents,
            particles,
            gameEvents,
            dispense.Details ? Details(harness, heard) : [],
            dispense.Details ? FlightMotion(harness) : []);
    }

    private static string[] FlightMotion(Harness harness)
    {
        var center = (VectorD)harness.Dispenser + new VectorD(0.5);
        return harness.Context.World.GetEntitiesInRange(center, EntityReach * 2)
            .OfType<Entity>()
            .Where(entity => entity is Arrow or Egg or ThrownExperienceBottle or MobProjectile or FireworkRocket)
            .Where(entity => InArea(harness, entity.CurrentBox))
            .Select(entity => FormattableString.Invariant(
                $"{EntityNbt.TypeId(entity)} {entity.Motion.X:R} {entity.Motion.Y:R} {entity.Motion.Z:R}"))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    // Initial flight velocity varies with each projectile's private spread random. Later ticks must follow the
    // same transform as the capture: horizontal scaling (drag), then vertical acceleration. This compares velocity
    // without requiring Obsidian to draw the same shot. A stopped projectile must have the captured zero velocity.
    private static void CompareFlight(Checkpoint expected, Checkpoint actual, Checkpoint? previousExpected,
        Checkpoint? previousActual, List<string> differences)
    {
        var reference = expected.FlightMotion.Select(ParseMotion).ToArray();
        var observed = actual.FlightMotion.Select(ParseMotion).ToArray();
        if (!reference.Select(value => value.Type).SequenceEqual(observed.Select(value => value.Type)))
        {
            differences.Add($"tick {expected.Tick} flight motion: projectile types differ");
            return;
        }

        var oldReference = previousExpected?.FlightMotion.Select(ParseMotion).ToArray() ?? [];
        var oldObserved = previousActual?.FlightMotion.Select(ParseMotion).ToArray() ?? [];
        for (var index = 0; index < reference.Length; index++)
        {
            var (type, motion) = reference[index];
            var actualMotion = observed[index].Motion;
            if (!double.IsFinite(actualMotion.MagnitudeSquared()))
            {
                differences.Add($"tick {expected.Tick} {type} has non-finite velocity");
                continue;
            }

            VectorD? predicted = motion == VectorD.Zero ? VectorD.Zero : null;
            if (predicted is null && index < oldReference.Length && index < oldObserved.Length
                && oldReference[index].Type == type && oldObserved[index].Type == type
                && Math.Abs(oldReference[index].Motion.X) > 1e-12)
            {
                var before = oldReference[index].Motion;
                var scale = motion.X / before.X;
                var gravity = before.Y * scale - motion.Y;
                predicted = oldObserved[index].Motion * scale - new VectorD(0, gravity, 0);
            }

            if (predicted is null)
            {
                // Two independent triangular spreads can differ by twice their configured radius per axis.
                var tolerance = type switch
                {
                    "minecraft:firework_rocket" => 0.018,
                    "minecraft:splash_potion" => 0.143,
                    _ => 0.228
                };
                var delta = actualMotion - motion;
                if (Math.Abs(delta.X) > tolerance || Math.Abs(delta.Y) > tolerance || Math.Abs(delta.Z) > tolerance)
                    differences.Add($"tick {expected.Tick} {type} launch velocity exceeds vanilla spread");
            }

            if (predicted is VectorD target && (actualMotion - target).Magnitude > 1e-6)
            {
                differences.Add($"tick {expected.Tick} {type} velocity: "
                    + $"expected {VectorText(target)}, got {VectorText(actualMotion)}");
            }
        }
    }

    private static (string Type, VectorD Motion) ParseMotion(string value)
    {
        var parts = value.Split(' ');
        var motion = new VectorD(
            double.Parse(parts[1], CultureInfo.InvariantCulture),
            double.Parse(parts[2], CultureInfo.InvariantCulture),
            double.Parse(parts[3], CultureInfo.InvariantCulture));
        return (parts[0], motion);
    }

    private static string[] Details(Harness harness, IClientboundPacket[] heard)
    {
        var world = harness.Context.World;
        var result = new List<string>();
        if (world.GetResidentBlockEntity(harness.Dispenser) is ContainerBlockEntity dispenser)
        {
            for (var slot = 0; slot < dispenser.Inventory.Size; slot++)
            {
                if (dispenser.Inventory.GetItem(slot) is { IsAir: false, Count: > 0 } stack)
                    result.Add($"slot {slot} {DetailedStack(stack)}");
            }
        }

        var drops = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var center = (VectorD)harness.Dispenser + new VectorD(0.5);
        foreach (var entity in world.GetEntitiesInRange(center, EntityReach * 2).OfType<Entity>())
        {
            if (entity is ServerPlayer || !InArea(harness, entity.CurrentBox))
                continue;

            var type = EntityNbt.TypeId(entity);
            var item = entity switch
            {
                Arrow arrow => arrow.PickupItem,
                Egg egg => egg.Item,
                ThrownExperienceBottle bottle => bottle.Item,
                MobProjectile potion when potion.Type == EntityType.SplashPotion => potion.SplashItem,
                FireworkRocket rocket => rocket.Item,

                _ => null
            };
            if (item is not null)
            {
                var pickup = entity is Arrow arrow ? $" pickup={arrow.Pickup.ToString().ToLowerInvariant()}" : "";
                result.Add($"{type}{pickup} item={DetailedStack(item)}");
            }

            if (entity is ItemEntity dropped)
            {
                var single = DetailedStack(new ItemStack(dropped.Item, 1));
                drops[single] = drops.GetValueOrDefault(single) + dropped.Item.Count;
            }

            if (entity is FireworkRocket firework)
            {
                result.Add($"{type} angled={firework.ShotAtAngle.ToString().ToLowerInvariant()}");
                var fireworks = firework.Item?.GetComponent<API.Inventory.DataComponents.FireworksDataComponent>(
                    DataComponentType.Fireworks);
                result.Add($"{type} flight={fireworks?.FlightDuration ?? 0}");
            }

            if (entity is PrimedTnt)
                result.Add($"{type} velocityY={Number(entity.Motion.Y)}");
            else if (entity is Mob)
                result.Add($"{type} velocity={VectorText(entity.Motion)}");

            if (entity is Mob mob)
            {
                foreach (var (slot, name) in slots)
                {
                    var equipment = mob.GetEquipment(slot);
                    if (!equipment.IsAir && equipment.Count > 0)
                        result.Add($"{type} {name}={DetailedStack(equipment)}");
                }
            }
        }

        foreach (var (item, count) in drops)
            result.Add($"drop {item} total={count}");

        foreach (var position in Area(harness))
        {
            var block = world.BlockUpdates.GetBlock(position);
            if (!block.UnlocalizedName.EndsWith("shulker_box", StringComparison.Ordinal)
                || world.GetResidentBlockEntity(position) is not ContainerBlockEntity box)
            {
                continue;
            }

            for (var slot = 0; slot < box.Inventory.Size; slot++)
            {
                if (box.Inventory.GetItem(slot) is { IsAir: false, Count: > 0 } item)
                    result.Add($"box {Offset(harness, position)} slot {slot} {DetailedStack(item)}");
            }
        }

        foreach (var packet in heard)
        {
            if (packet is SoundPacket sound)
            {
                if (sound.SoundLocation.EndsWith(".ambient", StringComparison.Ordinal))
                    continue;

                var projectileHit = sound.SoundLocation == "minecraft:entity.arrow.hit";
                var flight = projectileHit || sound.SoundLocation == "minecraft:entity.firework_rocket.launch";
                var relative = new VectorD(sound.SoundPosition.X / 8.0, sound.SoundPosition.Y / 8.0,
                    sound.SoundPosition.Z / 8.0) - (VectorD)harness.Dispenser;
                var position = flight ? "flight" : VectorText(relative);
                var pitch = projectileHit ? "random" : Number(sound.Pitch);
                result.Add($"sound {sound.SoundLocation} {SoundCategoryName(sound.Category)} {position}"
                    + $" volume={Number(sound.Volume)} pitch={pitch}");
            }
            else if (packet is SoundEntityPacket following)
            {
                if (!following.SoundLocation.EndsWith(".ambient", StringComparison.Ordinal))
                {
                    result.Add($"entitySound {following.SoundLocation} {SoundCategoryName(following.Category)}"
                        + $" volume={Number(following.Volume)} pitch={Number(following.Pitch)}");
                }
            }
            else if (packet is LevelParticlesPacket particle)
            {
                var position = VectorText(particle.Position - (VectorD)harness.Dispenser);
                result.Add($"particle minecraft:{particle.Data.ParticleType.ToString().ToSnakeCase()} {position}"
                    + $" count={particle.ParticleCount}");
            }
        }

        return [.. result.Order(StringComparer.Ordinal)];
    }

    private static string SoundCategoryName(SoundCategory category) =>
        category == SoundCategory.Blocks ? "block" : category.ToString().ToSnakeCase();

    private static string Number(double value) => (value == 0 ? 0 : value).ToString("F6", CultureInfo.InvariantCulture);

    private static string VectorText(VectorD value) => $"{Number(value.X)},{Number(value.Y)},{Number(value.Z)}";

    private static string DetailedStack(ItemStack stack)
    {
        var text = Text(stack);
        if (stack.ToNbt().TryGetTag<NbtCompound>("components", out var components) && components.Count > 0)
            return $"{text} {Obsidian.Commands.Snbt.Write(components)}";

        return text;
    }

    private static string CanonicalDetail(string value)
    {
        var components = value.IndexOf(" {", StringComparison.Ordinal);
        return components < 0 ? value : value[..(components + 1)]
            + Obsidian.Commands.Snbt.Write(SnbtReader.ReadCompound(value[(components + 1)..]));
    }

    private static IEnumerable<string> Compare(Checkpoint expected, Checkpoint actual)
    {
        var tick = $"tick {expected.Tick}";
        if (expected.Changes != actual.Changes)
            yield return $"{tick} changes: expected '{expected.Changes}', got '{actual.Changes}'";

        var lists = new (string Name, string[] Expected, string[] Actual)[]
        {
            ("items", expected.Items, actual.Items),
            ("entities", expected.Entities, actual.Entities),
            ("sounds", expected.Sounds, actual.Sounds),
            ("level events", expected.LevelEvents, actual.LevelEvents),
            ("particles", expected.Particles, actual.Particles),
            ("game events", expected.GameEvents, actual.GameEvents),
            ("details", expected.Details, actual.Details)
        };

        foreach (var (name, expectedValues, actualValues) in lists)
        {
            if (expectedValues.SequenceEqual(actualValues))
                continue;

            var wanted = string.Join(", ", expectedValues);
            var got = string.Join(", ", actualValues);
            yield return $"{tick} {name}: expected [{wanted}], got [{got}]";
        }
    }

    // The dispenser's items as "<slot> <id> x<count>", with their damage when they have any.
    private static string[] Items(Harness harness)
    {
        var world = harness.Context.World;
        if (world.GetResidentBlockEntity(harness.Dispenser) is not ContainerBlockEntity container)
            return [];

        var items = new List<string>();
        for (var slot = 0; slot < container.Inventory.Size; slot++)
        {
            if (container.Inventory.GetItem(slot) is { Count: > 0 } stack && !stack.IsAir)
                items.Add($"{slot} {Text(stack)}");
        }

        return [.. items];
    }

    // The entities around the dispenser other than players, sorted: dropped items summed by item, since item entities
    // merge, others by type with their fuse, equipment and whether they can be sheared.
    private static string[] Entities(Harness harness, string drops)
    {
        var world = harness.Context.World;
        var center = (VectorD)harness.Dispenser + new VectorD(0.5, 0.5, 0.5);
        var found = new List<string>();
        var totals = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var entity in world.GetEntitiesInRange(center, EntityReach * 2).OfType<Entity>())
        {
            if (entity is ServerPlayer || !InArea(harness, entity.CurrentBox))
                continue;

            if (entity is ItemEntity item)
            {
                var id = item.Item.Holder.UnlocalizedName;
                totals[id] = totals.GetValueOrDefault(id) + item.Item.Count;
            }
            else
            {
                found.Add(Describe(entity));
            }
        }

        foreach (var (id, total) in totals)
        {
            var text = drops switch
            {
                "ids" => $"item {id}",
                "none" => "item",
                _ => $"item {id} x{total}"
            };

            if (!found.Contains(text))
                found.Add(text);
        }

        return [.. found.Order(StringComparer.Ordinal)];
    }

    private static string Describe(Entity entity)
    {
        var text = EntityNbt.TypeId(entity);
        if (entity is PrimedTnt tnt)
            text += $" fuse={tnt.Fuse}";

        if (entity is Mob mob)
        {
            foreach (var (slot, name) in slots)
            {
                var stack = mob.GetEquipment(slot);
                if (!stack.IsAir && stack.Count > 0)
                    text += $" {name}={stack.Holder.UnlocalizedName}";
            }
        }

        // Vanilla's Shearable.readyForShearing.
        bool? shearable = entity switch
        {
            Sheep sheep => sheep.Alive && !sheep.Sheared && !sheep.IsBaby,
            SnowGolem golem => golem.Alive && golem.Pumpkin,
            Bogged bogged => bogged.Alive && !bogged.Sheared,
            Mooshroom mooshroom => mooshroom.Alive && !mooshroom.IsBaby,
            _ => null
        };

        if (shearable is bool ready)
            text += $" shearable={(ready ? "true" : "false")}";

        return text;
    }

    // Like the capture's query: entities whose boxes reach into the blocks within the entity reach of the dispenser.
    private static bool InArea(Harness harness, BoundingBox box)
    {
        var min = (VectorD)harness.Dispenser - new VectorD(EntityReach, EntityReach, EntityReach);
        var max = (VectorD)harness.Dispenser + new VectorD(EntityReach + 1, EntityReach + 1, EntityReach + 1);

        return box.Min.X < max.X && box.Max.X > min.X
            && box.Min.Y < max.Y && box.Max.Y > min.Y
            && box.Min.Z < max.Z && box.Max.Z > min.Z;
    }

    private static string GameEvent(Harness harness, MobVibration gameEvent)
    {
        var position = (Vector)gameEvent.Position.Floor();
        var source = gameEvent.Source is Entity entity ? EntityNbt.TypeId(entity) : "none";

        return $"{gameEvent.Kind.Identifier()} {Offset(harness, position)} {source}";
    }

    // Packets and game events from earlier steps aren't the case's.
    private static void Forget(Harness harness)
    {
        ((TestClient)harness.Bystander.Client).ClearPackets();
        harness.GameEvents.Clear();
    }

    private static async Task ClearAsync(Harness harness)
    {
        var world = harness.Context.World;
        for (var y = -Reach - 1; y <= Reach + 1; y++)
        {
            for (var z = -Reach - 1; z <= Reach + 1; z++)
            {
                for (var x = -Reach - 1; x <= Reach + 1; x++)
                    world.BlockUpdates.SetBlock(harness.Dispenser + new Vector(x, y, z), BlocksRegistry.Air, Quiet);
            }
        }

        // Clearing can drop what the last case's blocks held, which goes too, as in the capture.
        await world.RunQueuedEntityActionsAsync();
        var center = (VectorD)harness.Dispenser + new VectorD(0.5, 0.5, 0.5);
        foreach (var entity in world.GetEntitiesInRange(center, EntityReach * 3).ToArray())
        {
            if (entity is not ServerPlayer)
                await world.DestroyEntityAsync(entity);
        }
    }

    // An item stack as "<id> x<count>", with its damage when it has any.
    private static string Text(ItemStack stack)
    {
        var text = $"{stack.Holder.UnlocalizedName} x{stack.Count}";
        return stack.Damage > 0 ? $"{text} damage={stack.Damage}" : text;
    }

    private static IBlock[] Snapshot(Harness harness) =>
        Area(harness).Select(harness.Context.World.BlockUpdates.GetBlock).ToArray();

    // Each block of the area that changed, as dx,dy,dz=state relative to the dispenser.
    private static string Changes(Harness harness, IBlock[] before)
    {
        var world = harness.Context.World;
        var changed = new List<string>();
        var index = 0;
        foreach (var position in Area(harness))
        {
            var state = world.BlockUpdates.GetBlock(position);
            if (!state.IsSameState(before[index]))
                changed.Add($"{Offset(harness, position)}={Text(state)}");

            index++;
        }

        return string.Join(';', changed);
    }

    // The compared area in the capture's order: y, then z, then x ascending.
    private static IEnumerable<Vector> Area(Harness harness)
    {
        for (var y = -Reach; y <= Reach; y++)
        {
            for (var z = -Reach; z <= Reach; z++)
            {
                for (var x = -Reach; x <= Reach; x++)
                    yield return harness.Dispenser + new Vector(x, y, z);
            }
        }
    }

    private static string Offset(Harness harness, Vector position)
    {
        var offset = position - harness.Dispenser;
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

    private static Case ReadCase(JsonElement input, int defaultTicks)
    {
        var items = input.TryGetProperty("items", out var itemsElement)
            ? itemsElement.EnumerateArray()
                .Select(item => new ScenarioItem(
                    item.GetProperty("slot").GetInt32(),
                    item.GetProperty("id").GetString()!,
                    item.GetProperty("count").GetInt32(),
                    item.TryGetProperty("nbt", out var nbt) ? nbt.GetString() : null))
                .ToArray()
            : [];
        var setup = input.TryGetProperty("setup", out var setupElement)
            ? setupElement.EnumerateArray()
                .Select(block => new Setup(
                    Position(block.GetProperty("offset")),
                    VanillaSulfurBlocks.ParseState(block.GetProperty("state").GetString()!)))
                .ToArray()
            : [];
        var mobs = input.TryGetProperty("entities", out var mobsElement)
            ? mobsElement.EnumerateArray().Select(ReadMob).ToArray()
            : [];
        var drops = input.TryGetProperty("drops", out var dropsElement) ? dropsElement.GetString()! : "counts";
        var ticks = input.TryGetProperty("ticks", out var ticksElement) ? ticksElement.GetInt32() : defaultTicks;
        var checkpoints = input.GetProperty("checkpoints").EnumerateArray().Select(ReadCheckpoint).ToArray();
        Assert.Equal(ticks, checkpoints.Length);

        return new Case(
            input.GetProperty("name").GetString()!,
            Enum.Parse<BlockFace>(input.GetProperty("facing").GetString()!, ignoreCase: true),
            items,
            setup,
            mobs,
            drops,
            ticks,
            checkpoints,
            input.TryGetProperty("details", out var details) && details.GetBoolean());
    }

    private static ScenarioMob ReadMob(JsonElement mob) => new(
        mob.GetProperty("type").GetString()!,
        Position(mob.GetProperty("offset")),
        mob.TryGetProperty("tamed", out var tamed) ? tamed.GetBoolean() : null,
        mob.TryGetProperty("canPickUpLoot", out var loot) ? loot.GetBoolean() : null);

    private static Checkpoint ReadCheckpoint(JsonElement checkpoint) => new(
        checkpoint.GetProperty("tick").GetInt32(),
        checkpoint.TryGetProperty("changes", out var changes) ? changes.GetString()! : "",
        Strings(checkpoint, "items"),
        Strings(checkpoint, "entities"),
        Strings(checkpoint, "sounds"),
        Strings(checkpoint, "levelEvents"),
        Strings(checkpoint, "particles"),
        Strings(checkpoint, "gameEvents"),
        Strings(checkpoint, "details").Select(CanonicalDetail).Order(StringComparer.Ordinal).ToArray(),
        Strings(checkpoint, "flightMotion"));

    private static string[] Strings(JsonElement element, string name) =>
        element.TryGetProperty(name, out var values)
            ? [.. values.EnumerateArray().Select(value => value.GetString()!)]
            : [];

    private static Vector Position(JsonElement values) =>
        new(values[0].GetInt32(), values[1].GetInt32(), values[2].GetInt32());
}
