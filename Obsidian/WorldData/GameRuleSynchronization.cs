using Obsidian.Commands.Modules;
using Obsidian.Net.Packets.Play.Clientbound;
using System.Globalization;

namespace Obsidian.WorldData;

internal static class GameRuleSynchronization
{
    internal static bool TryNormalize(string identifier, string value, out string name, out string normalized)
    {
        name = identifier.StartsWith("minecraft:", StringComparison.Ordinal) ? identifier[10..] : identifier;
        normalized = value;
        if (!GameRuleCommandModule.Rules.TryGetValue(name, out var defaultValue))
            return false;

        // GameRule.deserialize uses Brigadier and requires the entire input to be consumed.
        if (defaultValue is bool)
            return value is "true" or "false";

        var min = name switch
        {
            "fire_spread_radius_around_player" => -1,
            "max_block_modifications" or "max_minecart_speed" => 1,
            _ => 0
        };
        var max = name switch
        {
            "max_snow_accumulation_height" => 8,
            "max_minecart_speed" => 1000,
            _ => int.MaxValue
        };
        if (value.StartsWith('+') || !int.TryParse(value, NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var number) || number < min || number > max)
            return false;

        normalized = number.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    internal static GameRuleValuesPacket Snapshot(LevelData data) => new()
    {
        Values = GameRuleCommandModule.Rules.OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .ToDictionary(entry => $"minecraft:{entry.Key}", entry => entry.Value is bool flag
                ? data.GetBooleanRule(entry.Key, flag) ? "true" : "false"
                : data.GetIntegerRule(entry.Key, (int)entry.Value).ToString(CultureInfo.InvariantCulture))
    };

    internal static async ValueTask<bool> SetAsync(IServer server, string identifier, string value)
    {
        if (!TryNormalize(identifier, value, out var name, out var normalized))
            return false;

        // Dimensions share their parent world's rule storage. Keep the existing command's server-wide scope.
        foreach (var world in server.WorldManager.GetAvailableWorlds())
        {
            world.LevelData.GameRules.Remove($"minecraft:{name}");
            world.LevelData.GameRules[name] = normalized;
        }

        var packet = new GameRuleValuesPacket { Values = new Dictionary<string, string> { [$"minecraft:{name}"] = normalized } };
        foreach (var player in server.OnlinePlayers.Values.OfType<IClientPlayer>())
            await player.QueuePacketAsync(packet);
        return true;
    }
}
