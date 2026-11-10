using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using ServerPlayer = Obsidian.Entities.Player;

namespace Obsidian.Tests.Utilities;

/// <summary>
/// What Obsidian did, written the way <c>BlockUseFixtures.java</c> and the probes built on it record vanilla, with
/// positions relative to the capture's target: block states, item stacks, ordered writes, sounds, events, statistics
/// and saved data.
/// </summary>
internal sealed class CaptureText(Vector target)
{
    /// <summary>The position relative to the target, as "dx,dy,dz".</summary>
    public string Offset(Vector position)
    {
        var offset = position - target;
        return $"{offset.X},{offset.Y},{offset.Z}";
    }

    /// <summary>A recorded write: "dx,dy,dz=state flags n".</summary>
    public string Write(Vector position, IBlock block, BlockUpdateFlags flags) =>
        $"{this.Offset(position)}={State(block)} flags {(int)flags}";

    /// <summary>A recorded destruction: "dx,dy,dz=state drops true|false".</summary>
    public string Destroyed(Vector position, IBlock block, bool drops) =>
        $"{this.Offset(position)}={State(block)} drops {drops}";

    /// <summary>The fixture's recorded writes, in the form of <see cref="Write"/>.</summary>
    public IEnumerable<string> RecordedWrites(JsonElement writes) => writes.EnumerateArray().Select(write => this.Write(
        target + Position(write.GetProperty("offset")),
        VanillaSulfurBlocks.ParseState(write.GetProperty("state").GetString()!),
        (BlockUpdateFlags)write.GetProperty("flags").GetInt32()));

    /// <summary>The fixture's recorded destructions, in the form of <see cref="Destroyed"/>.</summary>
    public IEnumerable<string> RecordedDestroyed(JsonElement destroyed) => destroyed.EnumerateArray().Select(
        block => this.Destroyed(
            target + Position(block.GetProperty("offset")),
            VanillaSulfurBlocks.ParseState(block.GetProperty("state").GetString()!),
            block.GetProperty("drops").GetBoolean()));

    /// <summary>A level event: "type dx,dy,dz data".</summary>
    public string LevelEvent(LevelEventPacket levelEvent) =>
        $"{levelEvent.Type} {this.Offset(levelEvent.Position)} {levelEvent.Data}";

    /// <summary>A block event: "block action parameter dx,dy,dz".</summary>
    public string BlockEvent(BlockEventPacket blockEvent)
    {
        var block = BlocksRegistry.GetDefault(blockEvent.BlockType).UnlocalizedName;
        return $"{block} {blockEvent.ActionId} {blockEvent.ActionParam} {this.Offset(blockEvent.Position)}";
    }

    /// <summary>A sound with the block it plays in and its volume. Sound positions travel in eighths of a block.</summary>
    public string SoundDetail(SoundPacket sound)
    {
        var position = new Vector(
            (int)Math.Floor(sound.SoundPosition.X / 8.0),
            (int)Math.Floor(sound.SoundPosition.Y / 8.0),
            (int)Math.Floor(sound.SoundPosition.Z / 8.0));
        var volume = sound.Volume.ToString("0.0####", CultureInfo.InvariantCulture);
        return $"{sound.SoundLocation} {this.Offset(position)} {volume}";
    }

    /// <summary>A game event: "name dx,dy,dz source", the user as "player".</summary>
    public string GameEvent(MobVibration gameEvent, ServerPlayer user)
    {
        var position = (Vector)gameEvent.Position.Floor();
        var source = gameEvent.Source switch
        {
            null => "none",
            ServerPlayer player when ReferenceEquals(player, user) => "player",
            Entity entity => EntityNbt.TypeId(entity),
            _ => "unknown"
        };

        return $"{gameEvent.Kind.Identifier()} {this.Offset(position)} {source}";
    }

    /// <summary>
    /// A sound, level event or block event the user's own client was sent, as "kind detail"; null for other packets.
    /// </summary>
    public string? UserEffect(IClientboundPacket packet) => packet switch
    {
        SoundPacket sound => "sound " + sound.SoundLocation,
        LevelEventPacket levelEvent => "level_event " + this.LevelEvent(levelEvent),
        BlockEventPacket blockEvent => "block_event " + this.BlockEvent(blockEvent),
        _ => null
    };

    /// <summary>
    /// The saved data of the block entity at <paramref name="position"/>, without its position, as canonical SNBT;
    /// null without one.
    /// </summary>
    public static string? SavedData(WorldData.World world, Vector position)
    {
        if (world.GetResidentBlockEntity(position) is not IBlockEntity blockEntity
            || BlockEntityNbt.Save(blockEntity) is not NbtCompound saved)
        {
            return null;
        }

        foreach (var field in new[] { "x", "y", "z", "keepPacked" })
            saved.Remove(field);

        return Obsidian.Commands.Snbt.Write(saved);
    }

    /// <summary>Vanilla's SNBT as Obsidian writes the same NBT, so that only names, types and values are compared.</summary>
    public static string CanonicalNbt(string snbt) => Obsidian.Commands.Snbt.Write(SnbtReader.ReadCompound(snbt));

    /// <summary>A particle packet: "minecraft:type count".</summary>
    public static string Particle(LevelParticlesPacket particle) =>
        $"minecraft:{particle.Data.ParticleType.ToString().ToSnakeCase()} {particle.ParticleCount}";

    /// <summary>An item stack as "id xcount", with its damage when it has any, or "empty".</summary>
    public static string Stack(ItemStack? stack)
    {
        if (stack is not { Count: > 0 } || stack.Holder.Id == ItemsRegistry.Air.Id)
            return "empty";

        var text = $"{stack.Holder.UnlocalizedName} x{stack.Count}";
        return stack.Damage > 0 ? $"{text} damage={stack.Damage}" : text;
    }

    /// <summary>Like <see cref="Stack"/>, followed by the components the stack changes as canonical SNBT.</summary>
    public static string DetailedStack(ItemStack? stack)
    {
        var text = Stack(stack);
        if (text == "empty" || !stack!.ToNbt().TryGetTag<NbtCompound>("components", out var components)
            || components.Count == 0)
        {
            return text;
        }

        return $"{text} {Obsidian.Commands.Snbt.Write(components)}";
    }

    /// <summary>A recorded detailed stack or "item stack" entity, with its components as canonical SNBT.</summary>
    public static string CanonicalStack(string recorded)
    {
        var components = recorded.IndexOf(" {", StringComparison.Ordinal);
        if (components < 0)
            return recorded;

        return $"{recorded[..components]} {CanonicalNbt(recorded[(components + 1)..])}";
    }

    /// <summary>The block and the properties it doesn't share with its default state, sorted by name.</summary>
    public static string State(IBlock state)
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

    /// <summary>Each statistic awarded between two saves of a player's statistics, as "type value amount".</summary>
    public static IEnumerable<string> Awarded(JsonObject before, JsonObject after)
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

    /// <summary>A recorded offset, [dx, dy, dz].</summary>
    public static Vector Position(JsonElement values) =>
        new(values[0].GetInt32(), values[1].GetInt32(), values[2].GetInt32());

    /// <summary>A recorded list of strings.</summary>
    public static string[] Strings(JsonElement values) =>
        [.. values.EnumerateArray().Select(value => value.GetString()!)];

    /// <summary>A list kept in its order.</summary>
    public static string List(IEnumerable<string> values) => string.Join(", ", values);

    /// <summary>A set, sorted, since neither side fixes its order.</summary>
    public static string Set(IEnumerable<string> values) => List(values.Order(StringComparer.Ordinal));
}
