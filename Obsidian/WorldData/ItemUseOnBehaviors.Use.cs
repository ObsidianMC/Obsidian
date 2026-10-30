using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Features.Tree;
using System.Collections.Frozen;
using System.Text.Json;

namespace Obsidian.WorldData;

internal static partial class ItemUseOnBehaviors
{
    private static readonly FrozenDictionary<string, LootTable> loot = LoadLoot();

    private static FrozenDictionary<string, LootTable> LoadLoot()
    {
        var tables = new Dictionary<string, LootTable>();
        foreach (var (identifier, definition) in Load("block_transformer_loot.json"))
        {
            var json = definition.GetRawText();
            tables.Add(identifier, LootTableJson.Parse(json, identifier));
        }
        return tables.ToFrozenDictionary();
    }

    public static async ValueTask<bool> UseOnAsync(
        AbstractLevel level,
        IPlayer player,
        ItemStack stack,
        int slot,
        Vector position,
        BlockFace face)
    {
        var identifier = Component(stack, DataComponentType.BlockTransformer, "block_transformer");
        if (identifier is null || HasBlockingIntent(player, slot))
            return false;

        Transformation? transform;
        lock (level.Fluids.TickLock)
        {
            transform = Evaluate(identifier, level.BlockUpdates.GetBlock, position, face);
            if (transform is null)
                return false;

            var state = transform.State;
            var updateShape = !transform.Data.TryGetProperty("update_from_neighbors", out var update)
                || update.GetBoolean();
            if (updateShape)
            {
                var view = new LiveShapeView(level);
                state = ShapeUpdater.UpdateFromNeighbourShapes(view, state, position);
            }

            var partner = CopperChestPartner(level, position, transform);
            DropLoot(level, player, stack, position, face, transform);
            level.BlockUpdates.SetBlock(position, state);
            if (partner is PartnerState other)
            {
                level.BlockUpdates.SetBlock(other.Position, other.State);
                level.EmitGameEvent(MobGameEvent.BlockChange, other.Position, player, affectedBlock: other.State);
                SendParticle(level, player, other.Position, transform);
            }

            level.EmitGameEvent(MobGameEvent.BlockChange, position, player, affectedBlock: state);
            SendEffects(level, player, position, transform);
        }

        await player.DamageInventoryItemAsync(slot, transform.Damage, expected: stack);
        return true;
    }

    private sealed record PartnerState(Vector Position, IBlock State);

    // CopperChestBlock.updateShape changes the connected half's material too; preserve both halves' own properties.
    private static PartnerState? CopperChestPartner(AbstractLevel level, Vector position, Transformation transform)
    {
        if (!transform.Data.TryGetProperty("transform_type", out var type) || type.GetString() != "copper_chest")
            return null;

        var original = level.BlockUpdates.GetBlock(position);
        var half = original.GetProperty("type");
        if (half is not ("left" or "right"))
            return null;

        var facing = FeatureHelpers.ParseFace(original.GetProperty("facing"));
        var direction = half == "left" ? facing.ClockWise() : facing.CounterClockWise();
        var partnerPosition = position + direction.ToVector();
        var partner = level.BlockUpdates.GetBlock(partnerPosition);
        if (partner.RegistryId != original.RegistryId)
            return null;

        var replacement = CopyProperties(transform.State, partner);
        return new PartnerState(partnerPosition, replacement);
    }

    private static bool HasBlockingIntent(IPlayer player, int slot)
    {
        if (slot == 45 || player.Sneaking)
            return false;

        var offhand = player.Inventory.GetItem(45);
        return offhand is { Count: > 0 }
            && !offhand.RemoveComponents.Contains(DataComponentType.BlocksAttacks)
            && (offhand.ContainsKey(DataComponentType.BlocksAttacks)
                || CombatItems.Component(offhand, "blocks_attacks").ValueKind != JsonValueKind.Undefined);
    }

    private static void DropLoot(
        AbstractLevel level,
        IPlayer player,
        ItemStack stack,
        Vector position,
        BlockFace face,
        Transformation transform)
    {
        if (transform.Loot is not string identifier || !level.LevelData.GetBooleanRule("block_drops"))
            return;

        var table = PluginLootTables.Resolve(identifier) ?? loot.GetValueOrDefault(identifier);
        if (table is null)
            return;

        var random = BlockLoot.SequencesFor(level).Get(table.RandomSequence ?? identifier);
        var context = new LootContext
        {
            Random = random,
            Tool = stack,
            ThisEntity = player,
            BlockState = level.BlockUpdates.GetBlock(position),
            Origin = (VectorD)position + new VectorD(0.5, 0.5, 0.5),
            ResolveTable = PluginLootTables.Resolve
        };

        lock (random)
        {
            foreach (var drop in table.GetRandomItems(context))
                level.PopTransformerResource(position, face, drop);
        }
    }

    private static void SendEffects(AbstractLevel level, IPlayer player, Vector position, Transformation transform)
    {
        var sound = new SoundPacket
        {
            SoundLocation = transform.Sound,
            SoundPosition = new SoundPosition(position.X + 0.5, position.Y + 0.5, position.Z + 0.5),
            Category = SoundCategory.Blocks,
            Volume = 1,
            Pitch = 1,
            Seed = Random.Shared.NextInt64()
        };
        level.PacketBroadcaster.BroadcastToLevelInRange(level, position, sound, player.EntityId);
        SendParticle(level, player, position, transform);
    }

    private static void SendParticle(AbstractLevel level, IPlayer player, Vector position, Transformation transform)
    {
        if (transform.Data.TryGetProperty("particle", out var particle))
        {
            var eventId = particle.GetString() == "scrape" ? 3005 : 3004;
            var packet = new LevelEventPacket(eventId, position, 0);
            level.PacketBroadcaster.BroadcastToLevelInRange(level, position, packet, player.EntityId);
        }
    }
}
