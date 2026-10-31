using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.API.World;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.Fluids;
using Obsidian.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Obsidian.Tests;

public sealed class VanillaSulfurBlocks(ITestOutputHelper output)
{
    private static readonly JsonElement fixture = LoadFixture();
    private static readonly JsonElement probes = LoadFixture("sulfur_fixtures");

    [Fact]
    public async Task ReplayCapturedProbeFixture()
    {
        var failures = new List<string>();
        var cases = 0;
        foreach (var probe in probes.GetProperty("cases").EnumerateArray())
        {
            var name = probe.GetProperty("name").GetString()!;
            var kind = probe.GetProperty("kind").GetString();
            if (kind is "arrow_saved_state" or "arrow_release")
            {
                foreach (var type in new[] { EntityType.Arrow, EntityType.SpectralArrow, EntityType.Trident })
                {
                    cases++;
                    await CollectFailureAsync(failures, $"{name}/{type}", () => kind == "arrow_release"
                        ? ProjectilePersistence.ReplayVanillaReleaseTransition(probe, type)
                        : ProjectilePersistence.VanillaBlockStateSurvivesRoundTripAndReleases(probe, type));
                }
            }
            else
            {
                cases++;
                await CollectFailureAsync(failures, $"{name}/notifications", () => ReplayCapturedProbe(probe, false));
                if (kind == "shape_update")
                {
                    cases++;
                    await CollectFailureAsync(failures, $"{name}/fluids", () => ReplayCapturedProbe(probe, true));
                }
            }
        }

        output.WriteLine($"Replayed {cases} probe cases.");
        Assert.True(cases > 0 && failures.Count == 0, $"{cases} probe cases:\n{string.Join("\n", failures)}");
    }

    private static async Task ReplayCapturedProbe(JsonElement probe, bool fluidPath)
    {
        Assert.Equal(ServerConstants.ProtocolDescription, probes.GetProperty("version").GetString());
        await using var context = TestServerFactory.CreateServer($"sulfur-probe-{Guid.NewGuid():N}");
        var world = context.World;
        world.LevelData.GameRules["spawn_mobs"] = "false";
        world.LevelData.GameRules["random_tick_speed"] = "0";
        await BlockUpdates.AddChunksAsync(world);
        foreach (var placement in probe.GetProperty("setup").EnumerateArray())
        {
            world.BlockUpdates.SetBlock(Position(placement.GetProperty("pos")),
                ParseState(placement.GetProperty("state").GetString()!),
                BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
        }

        var origin = Position(probe.GetProperty("origin"));
        if (probe.GetProperty("kind").GetString() == "shape_update")
        {
            var neighbor = Position(probe.GetProperty("neighbor"));
            if (fluidPath)
            {
                var fluidLevel = new FluidLevel(world.Fluids);
                var direction = Enum.Parse<BlockFace>(probe.GetProperty("direction").GetString()!, ignoreCase: true);
                fluidLevel.ExecuteShapeUpdate(direction, origin, world.BlockUpdates.GetBlock(neighbor),
                    BlockUpdateFlags.All, 512, VanillaShapes.All);
            }
            else
            {
                world.BlockNotifications.Enqueue(new BlockChange(neighbor, BlocksRegistry.Get("minecraft:stone"),
                    world.BlockUpdates.GetBlock(neighbor)), BlockUpdateFlags.SkipOnPlace);
                await world.BlockNotifications.DrainAsync();
            }

            Assert.True(ParseState(probe.GetProperty("updated").GetString()!)
                .IsSameState(world.BlockUpdates.GetBlock(origin)));
            var chunk = await world.GetChunkAsync(origin.X >> 4, origin.Z >> 4);
            var ticks = Assert.IsType<Chunk>(chunk).FluidTicks.Pack().Where(tick => tick.Position == origin).ToArray();
            var expected = probe.GetProperty("fluidTicks").EnumerateArray().ToArray();
            Assert.Equal(expected.Length, ticks.Length);
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.Equal(Position(expected[i].GetProperty("pos")), ticks[i].Position);
                Assert.Equal(expected[i].GetProperty("delay").GetInt32(), ticks[i].Delay);
                Assert.Equal(expected[i].GetProperty("type").GetString(),
                    $"minecraft:{ticks[i].Type.ToString().ToLowerInvariant()}");
            }

            return;
        }

        if (probe.GetProperty("kind").GetString() == "landing")
        {
            world.LevelData.GameRules["entity_drops"] = probe.GetProperty("entityDrops").GetBoolean().ToString();
            var listener = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Listener",
                new VectorD(8, 80, 8));
            world.Players.TryAdd(listener.Uuid, listener);
            var client = Assert.IsType<TestClient>(listener.Client);
            var spawn = (VectorD)origin + new VectorD(0.5, 3, 0.5);
            var falling = new FallingBlock(spawn)
            {
                Level = world,
                Block = ParseState("minecraft:sulfur_spike[vertical_direction=down]"),
                Type = EntityType.FallingBlock,
                DropItem = probe.GetProperty("dropItem").GetBoolean(),
                CancelDrop = probe.GetProperty("cancelDrop").GetBoolean(),
                Silent = probe.GetProperty("silent").GetBoolean()
            };
            var saved = new NbtCompound();
            falling.WriteNbt(saved);
            var restored = new FallingBlock(spawn)
            {
                Level = world,
                Block = falling.Block,
                Type = EntityType.FallingBlock,
                EntityId = Obsidian.Server.GetNextEntityId()
            };
            restored.ReadNbt(saved);
            Assert.Equal(falling.DropItem, restored.DropItem);
            Assert.Equal(falling.CancelDrop, restored.CancelDrop);
            Assert.Equal(falling.Silent, restored.Silent);
            Assert.True(world.TryAddEntity(restored));
            for (var tick = 0; tick < 60; tick++)
            {
                await world.DoWorldTickAsync();
            }

            var remaining = world.GetNonPlayerEntitiesInRange((VectorD)origin, 10).ToArray();
            Assert.Equal(probe.GetProperty("removed").GetBoolean(), !remaining.Contains(restored));
            Assert.Equal(probe.GetProperty("items").GetInt32(), remaining.OfType<ItemEntity>()
                .Where(item => !item.Removed && item.Item.Holder.Id == ItemIds.SulfurSpike)
                .Sum(item => item.Item.Count));
            var effects = client.SentPackets.OfType<LevelEventPacket>().Where(packet => packet.Type == 1052).ToArray();
            var events = probe.GetProperty("events").EnumerateArray().ToArray();
            Assert.Equal(events.Length, effects.Length);
            for (var i = 0; i < events.Length; i++)
            {
                Assert.Equal(events[i].GetProperty("type").GetInt32(), effects[i].Type);
                Assert.Equal(Position(events[i].GetProperty("pos")), effects[i].Position);
                Assert.Equal(events[i].GetProperty("data").GetInt32(), effects[i].Data);
            }

            return;
        }

        if (probe.GetProperty("kind").GetString() == "placement")
        {
            var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Placer");
            player.Yaw = 180;
            player.Pitch = probe.GetProperty("look").GetString() == "down" ? 90 : -90;
            player.Sneaking = probe.GetProperty("secondary").GetBoolean();
            var block = ParseState(probe.GetProperty("block").GetString()!);
            var face = probe.GetProperty("face").GetString() == "up" ? BlockFace.Up : BlockFace.Down;
            var behavior = PlacementBehaviors.For(block);
            var placed = behavior is null
                ? VanillaPlacement.GetState(world.BlockUpdates, block, origin, player, face,
                    VectorD.Zero, replacesClicked: true)
                : behavior.GetStateForPlacement(new BlockPlaceContext
                {
                    Level = world.BlockUpdates,
                    Player = player,
                    Hand = InteractionHand.MainHand,
                    Item = null!,
                    Block = block,
                    Position = origin,
                    ClickedPosition = origin,
                    ClickedFace = face,
                    ClickedAt = VectorD.Zero,
                    ReplacesClicked = true,
                    HorizontalDirection = BlockFace.North,
                    NearestLookDirection = probe.GetProperty("look").GetString() == "down"
                        ? BlockFace.Down : BlockFace.Up
                });
            var expected = probe.GetProperty("placed");
            if (expected.ValueKind == JsonValueKind.Null)
            {
                Assert.Null(placed);
            }
            else
            {
                Assert.NotNull(placed);
                Assert.True(ParseState(expected.GetString()!).IsSameState(placed), expected.GetString());
            }

            return;
        }

        if (probe.GetProperty("kind").GetString() == "impact")
        {
            Assert.Equal(probe.GetProperty("damage").GetSingle(),
                FallingBlock.StalactiteImpactDamage(probe.GetProperty("distance").GetDouble(),
                    probe.GetProperty("size").GetSingle()));
            return;
        }

        if (probe.GetProperty("kind").GetString() == "projectile")
        {
            Assert.Equal(probe.GetProperty("broken").GetBoolean(),
                SulfurSpikeBehavior.BreaksFromTrident(probe.GetProperty("speed").GetDouble(),
                    probe.GetProperty("allowed").GetBoolean()));
            return;
        }

        if (probe.GetProperty("kind").GetString() == "growth")
        {
            var level = new GrowthView(world);
            var random = new LegacyRandomSource(probe.GetProperty("seed").GetInt64());
            SulfurSpikeBehavior.GrowOnRandomTick(level, origin, level.GetBlock(origin), random);
            var writes = probe.GetProperty("writes").EnumerateArray().ToArray();
            Assert.Equal(writes.Length, level.Writes.Count);
            for (var i = 0; i < writes.Length; i++)
            {
                Assert.Equal(Position(writes[i].GetProperty("pos")), level.Writes[i].Position);
                Assert.True(ParseState(writes[i].GetProperty("state").GetString()!)
                    .IsSameState(level.Writes[i].State));
            }

            Assert.Equal(probe.GetProperty("nextRandom").GetInt32(), random.NextInt());
            return;
        }

        var expectedSource = probe.GetProperty("source");
        var source = SulfurGas.FindSource(world.BlockUpdates, origin);
        if (expectedSource.ValueKind == JsonValueKind.Null)
        {
            Assert.Null(source);
            return;
        }

        Assert.Equal(Position(expectedSource), source);
        var depth = source!.Value.Y - origin.Y - 1;
        Assert.Equal(probe.GetProperty("forceHeight").GetInt32(),
            SulfurGas.ForceHeight(world.BlockUpdates, origin, depth));
        var eyes = probe.GetProperty("eyes").EnumerateArray().ToArray();
        for (var i = 0; i < eyes.Length; i++)
        {
            var eye = eyes[i];
            Assert.Equal(probe.GetProperty("reaches")[i].GetBoolean(),
                SulfurGas.Reaches(world, source.Value,
                    new VectorD(eye[0].GetDouble(), eye[1].GetDouble(), eye[2].GetDouble())));
        }

        if (probe.TryGetProperty("entity", out var actor))
        {
            var pos = actor.GetProperty("pos");
            var stand = new ArmorStand
            {
                EntityId = 123,
                Level = world,
                Type = EntityType.ArmorStand,
                Health = 20,
                Motion = new VectorD(0, actor.GetProperty("motionY").GetDouble(), 0)
            };
            await stand.UpdateAsync(new VectorD(pos[0].GetDouble(), pos[1].GetDouble(), pos[2].GetDouble()),
                MovementFlags.None);
            Assert.True(world.TryAddEntity(stand));
            world.LaunchGeyserEntities(origin, source.Value);
            Assert.Equal(probe.GetProperty("launchedMotionY").GetDouble(), stand.Motion.Y);
            Assert.Equal(probe.GetProperty("clampedFallDistance").GetSingle(),
                Entity.GeyserFallDistance(10, actor.GetProperty("motionY").GetDouble()));
            world.ApplySulfurGas(source.Value);
            stand.ActivePotionEffects.TryGetValue((int)PotionEffect.Nausea - 1, out var nausea);
            Assert.Equal(probe.GetProperty("nauseaDuration").GetInt32(), nausea?.CurrentDuration ?? 0);
            if (nausea is not null)
            {
                Assert.Equal(probe.GetProperty("ambient").GetBoolean(), nausea.EffectData.Ambient);
                Assert.Equal(probe.GetProperty("particles").GetBoolean(), nausea.EffectData.ShowParticles);
            }
        }
    }

    [Fact]
    public async Task ReplayCapturedScenarioFixture()
    {
        var failures = new List<string>();
        var cases = 0;
        foreach (var scenario in fixture.GetProperty("scenarios").EnumerateArray())
        {
            cases++;
            var name = scenario.GetProperty("name").GetString()!;
            await CollectFailureAsync(failures, name, () => ReplayCapturedScenario(scenario));
        }

        output.WriteLine($"Replayed {cases} scenario cases.");
        Assert.True(cases > 0 && failures.Count == 0, $"{cases} scenario cases:\n{string.Join("\n", failures)}");
    }

    private static async Task ReplayCapturedScenario(JsonElement scenario)
    {
        Assert.Equal(ServerConstants.ProtocolDescription, fixture.GetProperty("version").GetString());
        await using var context = TestServerFactory.CreateServer($"sulfur-{Guid.NewGuid():N}");
        var world = context.World;
        world.LevelData.GameRules["spawn_mobs"] = "false";
        world.LevelData.GameRules["random_tick_speed"] = "0";
        if (scenario.TryGetProperty("rules", out var rules))
        {
            foreach (var rule in rules.EnumerateObject())
            {
                world.LevelData.GameRules[rule.Name] = rule.Value.GetString()!;
            }
        }
        await BlockUpdates.AddChunksAsync(world);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Observer",
            new VectorD(8, 80, 8));
        world.Players.TryAdd(player.Uuid, player);
        var checkpoints = scenario.GetProperty("checkpoints").EnumerateArray().ToArray();
        Assert.NotEmpty(checkpoints);
        world.LevelData.GameTime = checkpoints[0].GetProperty("gameTime").GetInt64();

        foreach (var placement in scenario.GetProperty("setup").EnumerateArray())
        {
            await PlaceAsync(world, placement);
        }

        var checkpointIndex = 0;
        Compare(world, scenario, checkpoints[checkpointIndex++]);
        for (var tick = 1; tick <= scenario.GetProperty("ticks").GetInt32(); tick++)
        {
            foreach (var action in scenario.GetProperty("actions").EnumerateArray()
                .Where(action => action.GetProperty("tick").GetInt32() == tick))
            {
                if (action.TryGetProperty("reload", out _))
                {
                    foreach (var position in scenario.GetProperty("watch").EnumerateArray().Select(Position))
                    {
                        if (world.GetResidentBlockEntity(position) is BlockEntity entity)
                        {
                            var tag = BlockEntityNbt.Save(entity)!;
                            var reloaded = BlockEntityNbt.Load(tag, entity.Id, position);
                            var chunk = await world.GetChunkAsync(position.X >> 4, position.Z >> 4);
                            chunk!.SetBlockEntity(position.X, position.Y, position.Z, reloaded);
                            world.AttachBlockEntity(reloaded);
                        }
                    }

                    for (var elapsed = 0; elapsed < action.GetProperty("elapsed").GetInt32(); elapsed++)
                    {
                        await AdvanceAsync(world);
                    }
                }
                else if (action.TryGetProperty("countdown", out var countdown))
                {
                    var entity = Assert.IsType<PotentSulfurBlockEntity>(
                        world.GetResidentBlockEntity(Position(action.GetProperty("pos"))));
                    entity.Countdown = countdown.GetInt32();
                }
                else if (action.TryGetProperty("projectile", out var projectile))
                {
                    var type = projectile.GetProperty("type").GetString();
                    Arrow arrow = type switch
                    {
                        "trident" => new Trident { Level = world, Type = EntityType.Trident },
                        "spectral_arrow" => new SpectralArrow { Level = world },
                        _ => new Arrow { Level = world, Type = EntityType.Arrow }
                    };
                    arrow.EntityId = Obsidian.Server.GetNextEntityId();
                    arrow.Position = Location(projectile.GetProperty("pos"));
                    arrow.Motion = Location(projectile.GetProperty("motion"));
                    if (projectile.TryGetProperty("inBlockState", out var state))
                    {
                        arrow.ReadNbt(new NbtCompound
                        {
                            new NbtTag<bool>("inGround", true),
                            Obsidian.Utilities.Converters.JsonNbt.ToNbt("inBlockState", state)
                        });
                    }

                    arrow.NoGravity = !projectile.TryGetProperty("noGravity", out var noGravity)
                        || noGravity.GetBoolean();
                    Assert.True(world.TryAddEntity(arrow));
                }
                else if (action.TryGetProperty("fallingFlags", out var flags))
                {
                    foreach (var falling in world.GetNonPlayerEntitiesInRange(new VectorD(0, 75, 0), 50)
                        .OfType<FallingBlock>())
                    {
                        var tag = new NbtCompound();
                        foreach (var flag in flags.EnumerateObject())
                        {
                            tag.Add(new NbtTag<bool>(flag.Name, flag.Value.GetInt32() != 0));
                        }

                        falling.ReadNbt(tag);
                    }
                }
                else if (action.TryGetProperty("spawnFalling", out var spawn))
                {
                    var falling = new FallingBlock(Location(spawn.GetProperty("pos")))
                    {
                        Level = world,
                        Type = EntityType.FallingBlock,
                        EntityId = Obsidian.Server.GetNextEntityId(),
                        Block = ParseState(spawn.GetProperty("block").GetString()!),
                        Motion = Location(spawn.GetProperty("motion"))
                    };
                    Assert.True(world.TryAddEntity(falling));
                }
                else
                {
                    await PlaceAsync(world, action);
                }
            }

            await AdvanceAsync(world);
            if (checkpointIndex < checkpoints.Length
                && checkpoints[checkpointIndex].GetProperty("tick").GetInt32() == tick)
            {
                Compare(world, scenario, checkpoints[checkpointIndex++]);
            }
        }

        Assert.Equal(checkpoints.Length, checkpointIndex);
    }

    // A focused Obsidian persistence risk: retained data must be copied and countdowns must remain signed ints.
    [Fact]
    public async Task SavedStateOwnsItsDataAndPreservesCountdown()
    {
        var failures = new List<string>();
        var cases = 0;
        foreach (var countdown in new[] { -1, 0, 300 })
        {
            cases++;
            await CollectFailureAsync(failures, $"countdown={countdown}", () =>
            {
                CheckSavedCountdown(countdown);
                return Task.CompletedTask;
            });
        }

        output.WriteLine($"Replayed {cases} countdown cases.");
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    private static void CheckSavedCountdown(int countdown)
    {
        var source = new NbtCompound
        {
            new NbtTag<int>("countdown", countdown),
            new NbtCompound("components") { new NbtTag<string>("test:marker", "kept") }
        };
        var entity = BlockEntityNbt.Create("minecraft:potent_sulfur", new Vector(3, 72, 4), source);
        Assert.IsType<NbtCompound>(source["components"]).Clear();
        var saved = BlockEntityNbt.Save(entity)!;
        Assert.Equal(countdown, saved.GetInt("countdown"));
        Assert.Equal("kept", Assert.IsType<NbtCompound>(saved["components"]).GetString("test:marker"));
        Assert.Equal("minecraft:potent_sulfur", saved.GetString("id"));
        Assert.Equal(3, saved.GetInt("x"));
        var loaded = BlockEntityNbt.Load(saved, entity.Id, entity.BlockPosition);
        Assert.IsType<NbtCompound>(saved["components"]).Clear();
        var again = BlockEntityNbt.Save(loaded)!;
        Assert.Equal(countdown, again.GetInt("countdown"));
        Assert.Equal("kept", Assert.IsType<NbtCompound>(again["components"]).GetString("test:marker"));
        Assert.Null(loaded.GetUpdateData());
    }

    [Fact]
    public async Task GeyserPacketsUseTheBlockRegistryAndStartClientAnimation()
    {
        var failures = new List<string>();
        var cases = new[]
        {
            (Mode: "continuous", Sound: "minecraft:block.potent_sulfur.geyser_continuous_eruption"),
            (Mode: "erupting", Sound: "minecraft:block.potent_sulfur.geyser_eruption")
        };
        foreach (var (mode, sound) in cases)
        {
            await CollectFailureAsync(failures, mode, () => CheckGeyserPackets(mode, sound));
        }

        output.WriteLine($"Replayed {cases.Length} packet cases.");
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    private static async Task CheckGeyserPackets(string mode, string sound)
    {
        await using var context = TestServerFactory.CreateServer($"geyser-packets-{Guid.NewGuid():N}");
        await BlockUpdates.AddChunksAsync(context.World);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Listener",
            new VectorD(0.5, 74, 0.5));
        context.World.Players.TryAdd(player.Uuid, player);
        var client = Assert.IsType<TestClient>(player.Client);
        var position = new Vector(0, 72, 0);
        var state = ParseState($"minecraft:potent_sulfur[potent_sulfur_state={mode}]");
        context.World.BlockUpdates.SetBlock(position, state,
            BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
        context.World.StartGeyser(position, state);
        Assert.Empty(client.SentPackets.OfType<BlockEventPacket>());
        await context.World.ProcessBlockEventsAsync();

        var blockEvent = Assert.Single(client.SentPackets.OfType<BlockEventPacket>());
        Assert.Equal(position, blockEvent.Position);
        Assert.Equal(BlockIds.PotentSulfur, blockEvent.BlockType);
        Assert.Equal(0, blockEvent.ActionId);
        Assert.Equal(0, blockEvent.ActionParam);
        var started = Assert.Single(client.SentPackets.OfType<SoundPacket>());
        Assert.Equal(sound, started.SoundLocation);
        Assert.Equal(SoundCategory.Blocks, started.Category);
    }

    private static async Task AdvanceAsync(World world)
    {
        await world.DoWorldTickAsync();
    }

    internal static async Task CollectFailureAsync(List<string> failures, string name, Func<Task> replay)
    {
        try
        {
            await replay();
        }
        catch (Exception exception)
        {
            failures.Add($"{name}: {exception.Message}");
        }
    }

    private static async Task PlaceAsync(World world, JsonElement action)
    {
        var position = Position(action.GetProperty("pos"));
        await world.SetBlockAsync(position, ParseState(action.GetProperty("state").GetString()!),
            doBlockUpdate: true);
        if (action.TryGetProperty("withoutBlockEntity", out var absent) && absent.GetBoolean())
        {
            // A bare vanilla setblock moving_piston has no block entity carrying a moving block.
            var chunk = await world.GetChunkAsync(position.X >> 4, position.Z >> 4);
            chunk!.RemoveBlockEntity(position.X, position.Y, position.Z);
        }
    }

    private static void Compare(World world, JsonElement scenario, JsonElement checkpoint)
    {
        Assert.Equal(checkpoint.GetProperty("gameTime").GetInt64(), world.LevelData.GameTime);
        var index = 0;
        foreach (var position in scenario.GetProperty("watch").EnumerateArray().Select(Position))
        {
            var expected = ParseState(checkpoint.GetProperty("states")[index].GetString()!);
            var actual = world.BlockUpdates.GetBlock(position);
            Assert.True(expected.IsSameState(actual),
                $"{scenario.GetProperty("name")} tick {checkpoint.GetProperty("tick")} at {position}: "
                + $"expected {checkpoint.GetProperty("states")[index]}, got {actual.UnlocalizedName} "
                + $"{actual.GetProperty("potent_sulfur_state")} {actual.GetProperty("thickness")}; "
                + string.Join("; ", world.GetNonPlayerEntitiesInRange(new VectorD(0, 75, 0), 50)
                    .OfType<Entity>().Select(entity => $"{entity.Type} {entity.Position} {entity.Motion}")));
            var countdown = checkpoint.GetProperty("countdowns")[index];
            if (countdown.ValueKind != JsonValueKind.Null)
            {
                var entity = Assert.IsType<PotentSulfurBlockEntity>(world.GetResidentBlockEntity(position));
                Assert.Equal(countdown.GetInt32(), entity.Countdown);
            }

            index++;
        }

        var entities = world.GetNonPlayerEntitiesInRange(new VectorD(0, 75, 0), 50).ToArray();
        if (checkpoint.TryGetProperty("falling", out var falling))
        {
            Assert.Equal(falling.GetInt32(), entities.OfType<FallingBlock>().Count());
            Assert.Equal(checkpoint.GetProperty("hurtingFalling").GetInt32(),
                entities.OfType<FallingBlock>().Count(entity => entity.StalactiteDamage > 0));
            Assert.Equal(checkpoint.GetProperty("items").GetInt32(), entities.OfType<ItemEntity>()
                .Where(item => !item.Removed && item.Item.Holder.Id ==
                    ItemsRegistry.Get(scenario.TryGetProperty("item", out var itemId)
                        ? itemId.GetString()! : "minecraft:sulfur_spike").Id)
                .Sum(item => item.Item.Count));
        }

        if (checkpoint.TryGetProperty("fallingMotion", out var motion))
        {
            var fallingEntities = entities.OfType<FallingBlock>().ToArray();
            if (motion.ValueKind == JsonValueKind.Null)
            {
                Assert.Empty(fallingEntities);
            }
            else
            {
                var entity = Assert.Single(fallingEntities);
                Assert.Equal(motion[0].GetDouble(), entity.Position.Y, precision: 7);
                Assert.Equal(motion[1].GetDouble(), entity.Motion.Y, precision: 7);
                Assert.Equal(motion[2].GetDouble() != 0, entity.MovementFlags.HasFlag(MovementFlags.OnGround));
            }
        }

        if (checkpoint.TryGetProperty("embedded", out var embedded))
        {
            Assert.Equal(embedded.GetInt32(), entities.OfType<Arrow>().Count(arrow =>
            {
                var data = new NbtCompound();
                arrow.WriteNbt(data);
                return data.TryGetTagValue<bool>("inGround", out var value) && value;
            }));
        }

        if (checkpoint.TryGetProperty("projectileMotion", out var projectileMotion))
        {
            var arrows = entities.OfType<Arrow>().ToArray();
            if (projectileMotion.ValueKind == JsonValueKind.Null)
            {
                Assert.Empty(arrows);
            }
            else
            {
                var arrow = Assert.Single(arrows);
                Assert.Equal(new VectorD(projectileMotion[0].GetDouble(), projectileMotion[1].GetDouble(),
                    projectileMotion[2].GetDouble()), arrow.Position);
                Assert.Equal(new VectorD(projectileMotion[3].GetDouble(), projectileMotion[4].GetDouble(),
                    projectileMotion[5].GetDouble()), arrow.Motion);
                var saved = EntityNbt.Save(arrow)!;
                Assert.Equal((short)projectileMotion[6].GetDouble(), saved.GetShort("life"));
            }
        }
    }

    private static VectorD Location(JsonElement array) =>
        new(array[0].GetDouble(), array[1].GetDouble(), array[2].GetDouble());

    internal static IBlock ParseState(string text)
    {
        var bracket = text.IndexOf('[');
        if (bracket < 0)
            return BlockStateProperties.GetState(text, new Dictionary<string, string>());

        var properties = text[(bracket + 1)..^1].Split(',')
            .Select(property => property.Split('='))
            .ToDictionary(pair => pair[0], pair => pair[1]);
        return BlockStateProperties.GetState(text[..bracket], properties);
    }

    private static Vector Position(JsonElement array) =>
        new(array[0].GetInt32(), array[1].GetInt32(), array[2].GetInt32());

    private static JsonElement LoadFixture(string name = "sulfur_scenarios")
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream($"Obsidian.Tests.Assets.{name}.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }

    // Like the Java probe's block view, writes are retained without neighbor notifications. The live notification
    // path is covered separately by the RCON scenarios above.
    private sealed class GrowthView(World world) : IBlockLevel
    {
        public List<(Vector Position, IBlock State)> Writes { get; } = [];

        public ILevel Level => world;

        public IRandomSource Random => throw new NotSupportedException();

        public IBlock GetBlock(Vector position) => world.BlockUpdates.GetBlock(position);

        public IBlockEntity GetBlockEntity(Vector position) => world.GetResidentBlockEntity(position);

        public bool SetBlock(Vector position, IBlock block, BlockUpdateFlags flags = BlockUpdateFlags.All)
        {
            this.Writes.Add((position, block));
            return world.BlockUpdates.SetBlock(position, block,
                BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
        }

        public bool RemoveBlock(Vector position) => throw new NotSupportedException();

        public void ScheduleTick(Vector position, IBlock block, int delay,
            TickPriority priority = TickPriority.Normal) =>
            throw new NotSupportedException();

        public bool HasScheduledTick(Vector position, IBlock block) => false;

        public void UpdateNeighbors(Vector position, IBlock source) => throw new NotSupportedException();

        public void UpdateAnalogOutput(Vector position) => throw new NotSupportedException();

        public IItemAccess GetItemAccess(Vector position, BlockFace? side) => throw new NotSupportedException();

        public int GetSignal(Vector position, BlockFace direction) => 0;

        public int GetDirectSignal(Vector position, BlockFace direction) => 0;

        public int GetDirectSignalTo(Vector position) => 0;

        public bool HasSignal(Vector position, BlockFace direction) => false;

        public bool HasNeighborSignal(Vector position) => false;

        public int GetBestNeighborSignal(Vector position) => 0;
    }
}
