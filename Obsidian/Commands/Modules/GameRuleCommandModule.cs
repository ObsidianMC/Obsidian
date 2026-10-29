using Obsidian.API.Commands;
using Obsidian.Commands.Framework;
using System.Collections.Frozen;
using System.Globalization;

namespace Obsidian.Commands.Modules;

/// <summary>
/// Vanilla's <c>/gamerule</c>: queries or sets a game rule of the level the command runs in.
/// </summary>
[RequirePermissionLevel(2)]
public sealed class GameRuleCommandModule : CommandModuleBase
{
    /// <summary>
    /// Vanilla's game rules by their names since 1.21.11, with their defaults: <see langword="bool"/> or
    /// <see langword="int"/>.
    /// </summary>
    internal static readonly FrozenDictionary<string, object> Rules = new Dictionary<string, object>
    {
        ["advance_time"] = true,
        ["advance_weather"] = true,
        ["allow_entering_nether_using_portals"] = true,
        ["block_drops"] = true,
        ["block_explosion_drop_decay"] = true,
        ["command_block_output"] = true,
        ["command_blocks_work"] = true,
        ["drowning_damage"] = true,
        ["elytra_movement_check"] = true,
        ["ender_pearls_vanish_on_death"] = true,
        ["entity_drops"] = true,
        ["fall_damage"] = true,
        ["fire_damage"] = true,
        ["fire_spread_radius_around_player"] = 128,
        ["forgive_dead_players"] = true,
        ["freeze_damage"] = true,
        ["global_sound_events"] = true,
        ["immediate_respawn"] = false,
        ["keep_inventory"] = false,
        ["lava_source_conversion"] = false,
        ["limited_crafting"] = false,
        ["locator_bar"] = true,
        ["log_admin_commands"] = true,
        ["max_block_modifications"] = WorldCommandModule.MaxBlocks,
        ["max_command_forks"] = 65536,
        ["max_command_sequence_length"] = 65536,
        ["max_entity_cramming"] = 24,
        ["max_minecart_speed"] = 8,
        ["max_snow_accumulation_height"] = 1,
        ["mob_drops"] = true,
        ["mob_explosion_drop_decay"] = true,
        ["mob_griefing"] = true,
        ["natural_health_regeneration"] = true,
        ["player_movement_check"] = true,
        ["players_nether_portal_creative_delay"] = 0,
        ["players_nether_portal_default_delay"] = 80,
        ["players_sleeping_percentage"] = 100,
        ["projectiles_can_break_blocks"] = true,
        ["pvp"] = true,
        ["raids"] = true,
        ["random_tick_speed"] = 3,
        ["reduced_debug_info"] = false,
        ["respawn_radius"] = 10,
        ["send_command_feedback"] = true,
        ["show_advancement_messages"] = true,
        ["show_death_messages"] = true,
        ["spawn_mobs"] = true,
        ["spawn_monsters"] = true,
        ["spawn_patrols"] = true,
        ["spawn_phantoms"] = true,
        ["spawn_wandering_traders"] = true,
        ["spawn_wardens"] = true,
        ["spawner_blocks_work"] = true,
        ["spectators_generate_chunks"] = true,
        ["spread_vines"] = true,
        ["tnt_explodes"] = true,
        ["tnt_explosion_drop_decay"] = false,
        ["universal_anger"] = false,
        ["water_source_conversion"] = true
    }.ToFrozenDictionary(StringComparer.Ordinal);

    [Command("gamerule")]
    [CommandInfo("Sets or queries a game rule.", "/gamerule <rule> [<value>]")]
    public async Task GameRuleAsync([GameRuleSuggestions] string rule)
    {
        if (!TryFind(rule, out var name, out var defaultValue))
        {
            await this.CommandContext.FailAsync($"Unknown game rule: {rule}");
            return;
        }

        var data = CommandFeedback.LevelOf(this.CommandContext).LevelData;
        var value = defaultValue is bool flag
            ? (data.GetBooleanRule(name, flag) ? "true" : "false")
            : data.GetIntegerRule(name, (int)defaultValue).ToString(CultureInfo.InvariantCulture);

        await this.CommandContext.SuccessAsync($"Gamerule {name} is currently set to: {value}");
    }

    [CommandOverload]
    public async Task GameRuleAsync([GameRuleSuggestions] string rule, [Suggestions("true", "false")] string value)
    {
        if (!TryFind(rule, out var name, out _))
        {
            await this.CommandContext.FailAsync($"Unknown game rule: {rule}");
            return;
        }

        if (!WorldData.GameRuleSynchronization.TryNormalize(name, value, out _, out var normalized))
        {
            await this.CommandContext.FailAsync($"Invalid value '{value}' for game rule {name}");
            return;
        }

        await WorldData.GameRuleSynchronization.SetAsync(this.Server, name, normalized);

        await this.CommandContext.SuccessAsync($"Gamerule {name} is now set to: {normalized}");
    }

    private static bool TryFind(string rule, out string name, out object defaultValue)
    {
        name = rule.StartsWith("minecraft:", StringComparison.Ordinal) ? rule[10..] : rule;
        return Rules.TryGetValue(name, out defaultValue!);
    }

    /// <summary>Suggests the names of vanilla's game rules.</summary>
    private sealed class GameRuleSuggestionsAttribute : BaseSuggestionProviderAttribute
    {
        public override ValueTask<IEnumerable<CommandSuggestion>> GetSuggestionsAsync(CommandContext context) =>
            ValueTask.FromResult(Rules.Keys.Order(StringComparer.Ordinal).Select(name => new CommandSuggestion(name)));
    }
}
