using Obsidian.API.Loot;
using Obsidian.Entities;
using System.Text.Json;

namespace Obsidian.Progress;

/// <summary>
/// Vanilla's <c>lightning_strike</c> and <c>channeled_lightning</c> advancement triggers, and the parts of 26.3's entity
/// predicates their criteria use: the entity's type, its distance from the player and, for the bolt, how many blocks it
/// set on fire and what it struck.
/// </summary>
/// <remarks>
/// A condition this doesn't understand matches nothing, so a criterion is never granted for less than it asks.
/// </remarks>
internal static class LightningProgress
{
    private const string StrikeTrigger = "lightning_strike";
    private const string ChanneledTrigger = "channeled_lightning";

    /// <summary>
    /// Fires <paramref name="player"/>'s <c>lightning_strike</c> trigger for <paramref name="bolt"/> going, with the
    /// living things around it that it didn't strike: a criterion's <c>lightning</c> condition tests the bolt, and its
    /// <c>bystander</c> condition any one of the bystanders.
    /// </summary>
    internal static ValueTask FireStrikeAsync(PlayerBase player, LightningBolt bolt, IReadOnlyList<IEntity> bystanders) =>
        player.FireProgressAsync(
            StrikeTrigger,
            conditions => MatchesStrike(player, conditions, bolt, bystanders));

    /// <summary>
    /// Fires the <c>channeled_lightning</c> trigger of the player who called a bolt down, for the entities it struck this
    /// tick: each of a criterion's <c>victims</c> conditions must match one of them.
    /// </summary>
    internal static ValueTask FireChanneledAsync(PlayerBase player, IReadOnlyList<IEntity> victims) =>
        player.FireProgressAsync(
            ChanneledTrigger,
            conditions => MatchesChanneled(player, conditions, victims));

    private static bool MatchesStrike(
        PlayerBase player,
        JsonElement conditions,
        LightningBolt bolt,
        IReadOnlyList<IEntity> bystanders)
    {
        foreach (var condition in conditions.EnumerateObject())
        {
            var matched = condition.Name switch
            {
                "player" => Matches(condition.Value, player, player),
                "lightning" => Matches(condition.Value, player, bolt),
                "bystander" => bystanders.Any(entity => Matches(condition.Value, player, entity)),
                _ => false
            };
            if (!matched)
                return false;
        }

        return true;
    }

    private static bool MatchesChanneled(PlayerBase player, JsonElement conditions, IReadOnlyList<IEntity> victims)
    {
        foreach (var condition in conditions.EnumerateObject())
        {
            var matched = condition.Name switch
            {
                "player" => Matches(condition.Value, player, player),
                "victims" => condition.Value.ValueKind == JsonValueKind.Array
                    && condition.Value.EnumerateArray()
                        .All(victim => victims.Any(entity => Matches(victim, player, entity))),
                _ => false
            };
            if (!matched)
                return false;
        }

        return true;
    }

    // A loot condition about the entity, as advancements hold them: entity_properties of "this", and the conditions
    // that combine others. A list of them must all hold.
    private static bool Matches(JsonElement condition, PlayerBase player, IEntity entity)
    {
        if (condition.ValueKind == JsonValueKind.Array)
            return condition.EnumerateArray().All(term => Matches(term, player, entity));

        if (condition.ValueKind != JsonValueKind.Object)
            return false;

        var type = condition.TryGetProperty("type", out var named) || condition.TryGetProperty("condition", out named)
            ? AdvancementsRegistry.Qualify(named.GetString()!)
            : null;

        return type switch
        {
            "minecraft:entity_properties" => condition.TryGetProperty("entity", out var target)
                && target.GetString() == "this"
                && condition.TryGetProperty("predicate", out var predicate)
                && MatchesEntity(predicate, player, entity),
            "minecraft:all_of" => condition.GetProperty("terms").EnumerateArray()
                .All(term => Matches(term, player, entity)),
            "minecraft:any_of" => condition.GetProperty("terms").EnumerateArray()
                .Any(term => Matches(term, player, entity)),
            "minecraft:inverted" => !Matches(condition.GetProperty("term"), player, entity),
            _ => false
        };
    }

    // An entity predicate in 26.3's form, measured from the player as vanilla's trigger contexts are.
    private static bool MatchesEntity(JsonElement predicate, PlayerBase player, IEntity entity)
    {
        foreach (var property in predicate.EnumerateObject())
        {
            var matched = AdvancementsRegistry.Qualify(property.Name) switch
            {
                "minecraft:entity_type" => MatchesType(property.Value, entity),
                "minecraft:distance" => MatchesDistance(property.Value, player.Position, entity.Position),
                "minecraft:type_specific/lightning" => entity is LightningBolt bolt
                    && MatchesBolt(property.Value, player, bolt),
                _ => false
            };
            if (!matched)
                return false;
        }

        return true;
    }

    private static bool MatchesType(JsonElement types, IEntity entity) =>
        entity is Entity concrete
        && ProgressConditions.EntityIdentifier(entity) is string identifier
        && LootTableJson.MatchesRegistrySet(types, "entity_type", identifier, concrete.TypeId);

    // Vanilla's LightningBoltPredicate: how many blocks it set on fire, and that one of the living things it struck
    // matches.
    private static bool MatchesBolt(JsonElement predicate, PlayerBase player, LightningBolt bolt)
    {
        foreach (var property in predicate.EnumerateObject())
        {
            var matched = property.Name switch
            {
                "blocks_set_on_fire" => LootTableJson.MatchesRange(bolt.BlocksSetOnFire, property.Value),
                "entity_struck" => bolt.HitEntities.Any(entity => MatchesEntity(property.Value, player, entity)),
                _ => false
            };
            if (!matched)
                return false;
        }

        return true;
    }

    // Vanilla's DistancePredicate: each axis apart, across, and in all, each within its bounds.
    private static bool MatchesDistance(JsonElement predicate, VectorD from, VectorD to)
    {
        var dx = from.X - to.X;
        var dy = from.Y - to.Y;
        var dz = from.Z - to.Z;
        foreach (var property in predicate.EnumerateObject())
        {
            var distance = property.Name switch
            {
                "x" => Math.Abs(dx),
                "y" => Math.Abs(dy),
                "z" => Math.Abs(dz),
                "horizontal" => Math.Sqrt(dx * dx + dz * dz),
                "absolute" => Math.Sqrt(dx * dx + dy * dy + dz * dz),
                _ => double.NaN
            };
            if (!WithinBounds(distance, property.Value))
                return false;
        }

        return true;
    }

    private static bool WithinBounds(double value, JsonElement bounds)
    {
        if (double.IsNaN(value))
            return false;

        if (bounds.ValueKind == JsonValueKind.Number)
            return value == bounds.GetDouble();

        var aboveMin = !bounds.TryGetProperty("min", out var min) || value >= min.GetDouble();
        var belowMax = !bounds.TryGetProperty("max", out var max) || value <= max.GetDouble();
        return aboveMin && belowMax;
    }
}
