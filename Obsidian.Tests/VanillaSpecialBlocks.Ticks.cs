using Obsidian.API;
using Obsidian.API.Utilities;
using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Entities.Factories;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using ServerPlayer = Obsidian.Entities.Player;

namespace Obsidian.Tests;

public sealed partial class VanillaSpecialBlocks
{
    // How far around the target a tick case's entities are recorded and cleared, as in the capture.
    private const int EntityReach = 16;

    // The fields a capture's checkpoint holds only when the tick did something; it records the entities of every tick
    // it keeps.
    private static readonly string[] eventFields =
        ["writes", "sounds", "levelEvents", "blockEvents", "particles", "gameEvents", "effects", "blockEntity", "user"];

    /// <summary>
    /// Runs the case's whole level ticks after its optional use, comparing every tick from the first: a tick the
    /// capture kept must match it field by field, entities included, and a tick it left out must have done nothing it
    /// records.
    /// </summary>
    private static async Task<List<string>> ReplayTicksAsync(Harness harness, JsonElement root, JsonElement special)
    {
        var world = harness.Context.World;
        var user = harness.User;
        var name = Name(special);

        await SetUpAsync(harness, special);
        Prepare(user, root, special);
        await FreshPortalsAsync(user);
        if (special.TryGetProperty("gameTime", out var gameTime))
            world.LevelData.GameTime = gameTime.GetInt64();

        if (special.TryGetProperty("entities", out var entities))
        {
            foreach (var input in entities.EnumerateArray())
                Spawn(world, input);

            await world.RunQueuedEntityActionsAsync();
        }

        Forget(harness);
        var differences = new List<string>();
        if (special.TryGetProperty("use", out var use))
        {
            var result = await UsePacket(use).UseAsync(harness.Context.Server, user);
            var expected = special.GetProperty("useResult").GetString()!;
            if (Name(result) != expected)
                differences.Add($"use: expected [{expected}], got [{Name(result)}]");
        }

        var close = special.TryGetProperty("close", out var closing) ? closing.GetInt32() : -1;
        var expectedTicks = special.GetProperty("checkpoints").EnumerateArray()
            .ToDictionary(checkpoint => checkpoint.GetProperty("tick").GetInt32());
        var writes = new List<string>();
        world.Fluids.BlockStored = (position, block, flags) => writes.Add(Write(position, block, flags));
        var previousData = BlockEntityData(world);
        var previousUser = UserPosition(user);
        try
        {
            for (var tick = 1; tick <= special.GetProperty("ticks").GetInt32(); tick++)
            {
                if (tick == close)
                    await CloseMenuAsync(harness);

                await world.DoWorldTickAsync();

                var actual = Checkpoint(harness, writes, ref previousData, ref previousUser);
                differences.AddRange(CompareTick(name, tick, expectedTicks.GetValueOrDefault(tick), actual));
                writes.Clear();
                Forget(harness);
            }
        }
        finally
        {
            world.Fluids.BlockStored = null;
            if (user.OpenedContainer is not null)
                await CloseMenuAsync(harness);

            await world.ProcessBlockEventsAsync();
        }

        return differences;
    }

    // A tick the capture kept compares all its fields; one it left out must have none of the event fields.
    private static IEnumerable<string> CompareTick(string name, int tick, JsonElement expected,
        Dictionary<string, string> actual)
    {
        var kept = expected.ValueKind == JsonValueKind.Object;
        var fields = kept ? [.. eventFields, "entities"] : eventFields;
        foreach (var field in fields)
        {
            var value = kept ? ExpectedField(expected, field) : "";
            var observed = actual.GetValueOrDefault(field) ?? "";
            var key = (name, $"t{tick} {field}");
            if (knownDifferences.TryGetValue(key, out var known))
            {
                if (observed != known || value == known)
                {
                    yield return $"t{tick} {field}: the known difference changed; vanilla [{value}], "
                        + $"got [{observed}]";
                }
            }
            else if (observed != value)
            {
                yield return $"t{tick} {field}: expected [{value}], got [{observed}]";
            }
        }
    }

    private static string ExpectedField(JsonElement checkpoint, string field)
    {
        if (!checkpoint.TryGetProperty(field, out var value))
            return "";

        return value.ValueKind == JsonValueKind.Array ? List(Strings(value)) : value.GetString()!;
    }

    // What the tick did, in the capture's forms.
    private static Dictionary<string, string> Checkpoint(Harness harness, List<string> writes,
        ref string previousData, ref string previousUser)
    {
        var world = harness.Context.World;
        var user = harness.User;
        var heard = ((TestClient)harness.Bystander.Client).SentPackets.ToArray();
        var received = ((TestClient)user.Client).SentPackets.ToArray();

        var checkpoint = new Dictionary<string, string>
        {
            ["writes"] = List(writes),
            ["sounds"] = List(heard.Select(TickSound).OfType<string>()),
            ["levelEvents"] = List(heard.OfType<LevelEventPacket>().Select(LevelEvent)),
            ["blockEvents"] = List(heard.OfType<BlockEventPacket>().Select(BlockEvent)),
            ["particles"] = List(heard.OfType<LevelParticlesPacket>().Select(Particle)),
            ["gameEvents"] = List(harness.GameEvents),
            ["effects"] = List(received.OfType<UpdateMobEffectPacket>()
                .Where(effect => effect.EntityId == user.EntityId)
                .Select(Effect)),
            ["entities"] = List(TickEntities(world).Order(StringComparer.Ordinal))
        };

        var data = BlockEntityData(world);
        if (data != previousData)
        {
            checkpoint["blockEntity"] = data;
            previousData = data;
        }

        var position = UserPosition(user);
        if (position != previousUser)
        {
            checkpoint["user"] = position;
            previousUser = position;
        }

        return checkpoint;
    }

    private static string? TickSound(IClientboundPacket packet) => packet switch
    {
        SoundPacket sound => $"{sound.SoundLocation} {Offset(SoundBlock(sound))}",
        SoundEntityPacket sound => $"{sound.SoundLocation} entity",
        _ => null
    };

    // An effect the user was given, as "id amplifier duration" and its flags.
    private static string Effect(UpdateMobEffectPacket effect)
    {
        var id = "minecraft:" + ((PotionEffect)(effect.EffectId + 1)).ToString().ToSnakeCase();
        var text = $"{id} {effect.Amplifier} {effect.Duration}";
        if (effect.Flags.HasFlag(EntityEffectFlags.IsAmbient))
            text += " ambient";

        if (effect.Flags.HasFlag(EntityEffectFlags.ShowParticles))
            text += " visible";

        if (effect.Flags.HasFlag(EntityEffectFlags.ShowIcon))
            text += " icon";

        return text;
    }

    // The entities within reach of the target other than players: "type x y z" relative to it, to a thousandth of a
    // block, with a living entity's health.
    private static IEnumerable<string> TickEntities(WorldData.World world)
    {
        var center = (VectorD)target + new VectorD(0.5, 0.5, 0.5);
        foreach (var entity in world.GetEntitiesInRange(center, EntityReach * 2).OfType<Entity>())
        {
            if (entity is ServerPlayer)
                continue;

            var offset = entity.Position - (VectorD)target;
            if (Math.Abs(offset.X - 0.5) > EntityReach + 0.5
                || Math.Abs(offset.Y - 0.5) > EntityReach + 0.5
                || Math.Abs(offset.Z - 0.5) > EntityReach + 0.5)
            {
                continue;
            }

            var type = entity is ItemEntity item
                ? "item " + item.Item.Holder.UnlocalizedName
                : EntityNbt.TypeId(entity);
            var text = $"{type} {Relative(offset.X)} {Relative(offset.Y)} {Relative(offset.Z)}";
            if (entity is Living living)
                text += " health " + living.Health.ToString("0.0", CultureInfo.InvariantCulture);

            yield return text;
        }
    }

    private static string UserPosition(ServerPlayer user)
    {
        var offset = user.Position - (VectorD)target;
        return $"{Relative(offset.X)} {Relative(offset.Y)} {Relative(offset.Z)}";
    }

    private static string Relative(double value)
    {
        var text = value.ToString("0.000", CultureInfo.InvariantCulture);
        return text == "-0.000" ? "0.000" : text;
    }

    // The client data of the block entity at the target, keys sorted, an entity's UUID written as its type.
    private static string BlockEntityData(WorldData.World world)
    {
        if (world.GetResidentBlockEntity(target) is not API.BlockEntities.BlockEntity blockEntity)
            return "";

        var data = blockEntity.GetUpdateData() ?? new NbtCompound();
        var parts = data
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={NbtText(world, pair.Value)}");
        return string.Join(' ', parts);
    }

    // A tag written the way vanilla's toString writes it.
    private static string NbtText(WorldData.World world, INbtTag tag) => tag switch
    {
        NbtTag<long> value => $"{value.Value}L",
        NbtTag<bool> value => value.Value ? "1b" : "0b",
        NbtTag<byte> value => $"{(sbyte)value.Value}b",
        NbtTag<sbyte> value => $"{value.Value}b",
        NbtTag<short> value => $"{value.Value}s",
        NbtTag<int> value => value.Value.ToString(CultureInfo.InvariantCulture),
        NbtTag<float> value => value.Value.ToString(CultureInfo.InvariantCulture) + "f",
        NbtTag<double> value => value.Value.ToString(CultureInfo.InvariantCulture) + "d",
        NbtTag<string> value => $"\"{value.Value}\"",
        NbtArray<int> { Count: 4 } uuid => EntityOf(world, uuid.GetArray()),
        NbtArray<int> array => $"[I;{string.Join(',', array.GetArray())}]",
        NbtCompound compound => "{" + string.Join(',', compound
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}:{NbtText(world, pair.Value)}")) + "}",
        _ => tag.ToString() ?? ""
    };

    // The type of the entity a UUID's four ints name, or "unknown".
    private static string EntityOf(WorldData.World world, int[] values)
    {
        var uuid = EntityNbt.UuidFromInts(values);
        var entity = world.GetEntitiesInRange((VectorD)target, EntityReach * 4)
            .OfType<Entity>()
            .FirstOrDefault(entity => entity.Uuid == uuid);
        return entity is null ? "unknown" : EntityNbt.TypeId(entity);
    }

    // A mob without AI unless the case gives it one, standing where the case says, facing south.
    private static void Spawn(WorldData.World world, JsonElement input)
    {
        Assert.True(EntityNbt.TryParseType(input.GetProperty("type").GetString()!, out var type));
        var entity = EntitySpawner.Create(type, world);
        var offset = input.GetProperty("offset");
        entity.EntityId = Server.GetNextEntityId();
        entity.Level = world;
        entity.Position = (VectorD)target
            + new VectorD(offset[0].GetDouble(), offset[1].GetDouble(), offset[2].GetDouble());
        entity.NoGravity = input.TryGetProperty("noGravity", out var noGravity) && noGravity.GetBoolean();
        entity.Silent = input.TryGetProperty("silent", out var silent) && silent.GetBoolean();
        if (entity is Mob mob)
        {
            var ai = input.TryGetProperty("ai", out var withAi) && withAi.GetBoolean();
            if (!ai)
                mob.MobBitMask |= MobBitmask.NoAi;

            mob.PersistenceRequired = true;
        }

        world.SpawnEntity(entity);
    }

    // The user as a player who just joined, as the capture's is: no portal trip or cooldown left from the last case.
    private static async Task FreshPortalsAsync(ServerPlayer user)
    {
        await user.CancelPortalTravelAsync();
        var cooldown = typeof(PlayerBase).GetField("portalCooldown",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        cooldown.SetValue(user, 0);
    }

    private static async Task CloseMenuAsync(Harness harness)
    {
        var user = harness.User;
        if (user.OpenedContainer is null)
            return;

        var closing = new ContainerClosedEventArgs(
            user,
            harness.Context.Server,
            user.OpenedContainer,
            user.CurrentContainerId);
        await harness.Context.EventDispatcher.ExecuteEventAsync(closing);
    }
}
