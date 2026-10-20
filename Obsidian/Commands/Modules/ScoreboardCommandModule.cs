using Obsidian.API.Commands;
using Obsidian.Commands.Framework;
using static Obsidian.Commands.CommandFeedback;

namespace Obsidian.Commands.Modules;

/// <summary>
/// Vanilla's <c>/scoreboard</c>: manages objectives and the scores of their holders (see <see cref="VanillaScoreboard"/>).
/// </summary>
[CommandGroup("scoreboard")]
[CommandInfo("Manages scoreboard objectives and players.", "/scoreboard <objectives|players>")]
[RequirePermissionLevel(2)]
public sealed class ScoreboardCommandModule : CommandModuleBase
{
    /// <summary>The colors vanilla names, in the order of their formatting codes.</summary>
    public enum VanillaColor
    {
        Black,
        DarkBlue,
        DarkGreen,
        DarkAqua,
        DarkRed,
        DarkPurple,
        Gold,
        Gray,
        DarkGray,
        Blue,
        Green,
        Aqua,
        Red,
        LightPurple,
        Yellow,
        White,
        Reset = 21
    }

    public enum RenderType
    {
        Integer,
        Hearts
    }

    [CommandGroup("objectives")]
    [CommandInfo("Manages objectives.", "/scoreboard objectives <add|remove|list|setdisplay|modify>")]
    public sealed class Objectives : CommandModuleBase
    {
        private VanillaScoreboard Scoreboard => ((Server)this.Server).CommandScoreboard;

        [Command("list")]
        public Task ListAsync()
        {
            var objectives = this.Scoreboard.Objectives;

            return objectives.Count == 0
                ? this.CommandContext.SuccessAsync("There are no objectives")
                : this.CommandContext.SuccessAsync($"There are {objectives.Count} objective(s): {string.Join(", ", objectives.Select(x => $"[{x.Name}]"))}");
        }

        [Command("add")]
        public Task AddAsync(string objective, [CriteriaSuggestions] string criteria) =>
            this.AddAsync(objective, criteria, objective);

        [CommandOverload]
        public async Task AddAsync(string objective, [CriteriaSuggestions] string criteria, [Remaining] string displayName)
        {
            if (objective.Length > 16)
            {
                await this.CommandContext.FailAsync("Objective names can't be longer than 16 characters");
                return;
            }

            if (!this.Scoreboard.TryAdd(objective, criteria, ChatMessage.Simple(displayName)))
            {
                await this.CommandContext.FailAsync("An objective already exists by that name");
                return;
            }

            await this.CommandContext.SuccessAsync($"Created new objective [{displayName}]");
        }

        [Command("remove")]
        public async Task RemoveAsync([ObjectiveSuggestions] string objective)
        {
            if (this.Scoreboard.Remove(objective))
                await this.CommandContext.SuccessAsync($"Removed objective [{objective}]");
            else
                await this.CommandContext.FailAsync($"Unknown scoreboard objective '{objective}'");
        }

        [Command("setdisplay")]
        public Task SetDisplayAsync([DisplaySlotSuggestions] string slot) => this.SetDisplayAsync(slot, null);

        [CommandOverload]
        public async Task SetDisplayAsync([DisplaySlotSuggestions] string slot, [ObjectiveSuggestions] string? objective)
        {
            if (ParseSlot(slot) is not DisplaySlot displaySlot)
            {
                await this.CommandContext.FailAsync($"Unknown display slot '{slot}'");
                return;
            }

            if (objective is not null && this.Scoreboard.Find(objective) is null)
            {
                await this.CommandContext.FailAsync($"Unknown scoreboard objective '{objective}'");
                return;
            }

            this.Scoreboard.SetDisplay(displaySlot, objective);
            await this.CommandContext.SuccessAsync(objective is null
                ? $"Cleared any objectives in display slot {slot}"
                : $"Set display slot {slot} to show objective {objective}");
        }

        [Command("modify")]
        public async Task ModifyAsync([ObjectiveSuggestions] string objective, [Suggestions("displayname", "rendertype")] string property,
            [Remaining] string value)
        {
            if (this.Scoreboard.Find(objective) is not { } found)
            {
                await this.CommandContext.FailAsync($"Unknown scoreboard objective '{objective}'");
                return;
            }

            switch (property)
            {
                case "displayname":
                    found.DisplayName = ChatMessage.Simple(value);
                    await this.CommandContext.SuccessAsync($"Changed the display name of {objective} to [{value}]");
                    break;
                case "rendertype" when value is "integer" or "hearts":
                    found.RenderType = value == "hearts" ? DisplayType.Hearts : DisplayType.Integer;
                    await this.CommandContext.SuccessAsync($"Changed the render type of objective [{objective}]");
                    break;
                default:
                    await this.CommandContext.FailAsync("Expected displayname <name> or rendertype <integer|hearts>");
                    return;
            }

            this.Scoreboard.Update(found);
        }
    }

    [CommandGroup("players")]
    [CommandInfo("Manages the scores of holders.", "/scoreboard players <list|get|set|add|remove|reset>")]
    public sealed class Players : CommandModuleBase
    {
        private VanillaScoreboard Scoreboard => ((Server)this.Server).CommandScoreboard;

        [Command("list")]
        public Task ListAsync()
        {
            var holders = this.Scoreboard.Holders;

            return holders.Count == 0
                ? this.CommandContext.SuccessAsync("There are no tracked entities")
                : this.CommandContext.SuccessAsync($"There are {holders.Count} tracked entity/entities: {string.Join(", ", holders)}");
        }

        [CommandOverload]
        public async Task ListAsync(ScoreHolderArgument target)
        {
            foreach (var holder in target.Resolve(this.CommandContext, this.Scoreboard.Holders))
            {
                var scores = this.Scoreboard.Objectives
                    .Where(objective => objective.Scores.ContainsKey(holder))
                    .Select(objective => $"[{objective.Name}]: {objective.Scores[holder]}")
                    .ToArray();

                await this.CommandContext.SuccessAsync(scores.Length == 0
                    ? $"{holder} has no scores to show"
                    : $"{holder} has {scores.Length} score(s): {string.Join(", ", scores)}");
            }
        }

        [Command("get")]
        public async Task GetAsync(ScoreHolderArgument target, [ObjectiveSuggestions] string objective)
        {
            if (await this.FindAsync(objective) is not { } found)
                return;

            if (target.Resolve(this.CommandContext, this.Scoreboard.Holders) is not [var holder])
            {
                await this.CommandContext.FailAsync("Only one score holder is allowed");
                return;
            }

            if (this.Scoreboard.GetScore(holder, found) is int score)
                await this.CommandContext.SuccessAsync($"{holder} has {score} [{objective}]");
            else
                await this.CommandContext.FailAsync($"Can't get value of {objective} for {holder}; none is set");
        }

        [Command("set")]
        public Task SetAsync(ScoreHolderArgument targets, [ObjectiveSuggestions] string objective, int score) =>
            this.ChangeAsync(targets, objective, _ => score, (holders, _) => $"Set [{objective}] for {holders} to {score}");

        [Command("add")]
        public Task AddAsync(ScoreHolderArgument targets, [ObjectiveSuggestions] string objective, int score) =>
            this.ChangeAsync(targets, objective, current => unchecked(current + score),
                (holders, now) => $"Added {score} to [{objective}] for {holders}{now}");

        [Command("remove")]
        public Task RemoveAsync(ScoreHolderArgument targets, [ObjectiveSuggestions] string objective, int score) =>
            this.ChangeAsync(targets, objective, current => unchecked(current - score),
                (holders, now) => $"Removed {score} from [{objective}] for {holders}{now}");

        [Command("reset")]
        public async Task ResetAsync(ScoreHolderArgument targets)
        {
            var holders = targets.Resolve(this.CommandContext, this.Scoreboard.Holders);
            foreach (var holder in holders)
                this.Scoreboard.Reset(holder, null);

            await this.CommandContext.SuccessAsync($"Reset all scores for {DescribeHolders(holders)}");
        }

        [CommandOverload]
        public async Task ResetAsync(ScoreHolderArgument targets, [ObjectiveSuggestions] string objective)
        {
            if (await this.FindAsync(objective) is not { } found)
                return;

            var holders = targets.Resolve(this.CommandContext, this.Scoreboard.Holders);
            foreach (var holder in holders)
                this.Scoreboard.Reset(holder, found);

            await this.CommandContext.SuccessAsync($"Reset [{objective}] for {DescribeHolders(holders)}");
        }

        private async Task ChangeAsync(ScoreHolderArgument targets, string objective, Func<int, int> change, Func<string, string, string> message)
        {
            if (await this.FindAsync(objective) is not { } found)
                return;

            var holders = targets.Resolve(this.CommandContext, this.Scoreboard.Holders);
            if (holders.Count == 0)
            {
                await this.CommandContext.FailAsync("No entity was found");
                return;
            }

            var last = 0;
            foreach (var holder in holders)
            {
                last = change(this.Scoreboard.GetScore(holder, found) ?? 0);
                this.Scoreboard.SetScore(holder, found, last);
            }

            await this.CommandContext.SuccessAsync(message(DescribeHolders(holders), holders.Count == 1 ? $" (now {last})" : string.Empty));
        }

        private async Task<VanillaScoreboard.Objective?> FindAsync(string objective)
        {
            var found = this.Scoreboard.Find(objective);
            if (found is null)
                await this.CommandContext.FailAsync($"Unknown scoreboard objective '{objective}'");

            return found;
        }

        private static string DescribeHolders(IReadOnlyList<string> holders) =>
            holders.Count == 1 ? holders[0] : $"{holders.Count} entities";
    }

    /// <summary>
    /// Reads a display slot as vanilla names it: <c>list</c>, <c>sidebar</c>, <c>below_name</c> or
    /// <c>sidebar.team.&lt;color&gt;</c>.
    /// </summary>
    internal static DisplaySlot? ParseSlot(string slot)
    {
        switch (slot)
        {
            case "list":
                return DisplaySlot.List;
            case "sidebar":
                return DisplaySlot.Sidebar;
            case "below_name" or "belowName":
                return DisplaySlot.BelowName;
        }

        const string TeamPrefix = "sidebar.team.";
        if (!slot.StartsWith(TeamPrefix, StringComparison.Ordinal))
            return null;

        var color = slot[TeamPrefix.Length..].Replace("_", string.Empty);
        foreach (var name in Enum.GetNames<VanillaColor>())
        {
            if (name.Equals(color, StringComparison.OrdinalIgnoreCase) && Enum.Parse<VanillaColor>(name) is var parsed && parsed != VanillaColor.Reset)
                return DisplaySlot.TeamBlack + (int)parsed;
        }

        return null;
    }

    private sealed class ObjectiveSuggestionsAttribute : BaseSuggestionProviderAttribute
    {
        public override ValueTask<IEnumerable<CommandSuggestion>> GetSuggestionsAsync(CommandContext context) =>
            ValueTask.FromResult(context.Server is Server server
                ? server.CommandScoreboard.Objectives.Select(x => new CommandSuggestion(x.Name))
                : []);
    }

    private sealed class CriteriaSuggestionsAttribute : BaseSuggestionProviderAttribute
    {
        private static readonly string[] criteria =
            ["dummy", "trigger", "deathCount", "playerKillCount", "totalKillCount", "health", "xp", "level", "food", "air", "armor"];

        public override ValueTask<IEnumerable<CommandSuggestion>> GetSuggestionsAsync(CommandContext context) =>
            ValueTask.FromResult(criteria.Select(x => new CommandSuggestion(x)));
    }

    private sealed class DisplaySlotSuggestionsAttribute : BaseSuggestionProviderAttribute
    {
        public override ValueTask<IEnumerable<CommandSuggestion>> GetSuggestionsAsync(CommandContext context) =>
            ValueTask.FromResult<IEnumerable<CommandSuggestion>>([
                new("list"), new("sidebar"), new("below_name"),
                .. Enum.GetValues<VanillaColor>().Where(x => x != VanillaColor.Reset)
                    .Select(x => new CommandSuggestion($"sidebar.team.{x.ToString().ToSnakeCase()}"))]);
    }
}

/// <summary>
/// Vanilla's <c>/team</c>: manages the teams of the server's default scoreboard.
/// </summary>
[CommandGroup("team")]
[CommandInfo("Manages teams.", "/team <add|remove|empty|join|leave|list|modify>")]
[RequirePermissionLevel(2)]
public sealed class TeamCommandModule : CommandModuleBase
{
    private IScoreboard Scoreboard => this.Server.ScoreboardManager.DefaultScoreboard;

    [Command("list")]
    public Task ListAsync()
    {
        var teams = this.Scoreboard.Teams;

        return teams.Count == 0
            ? this.CommandContext.SuccessAsync("There are no teams")
            : this.CommandContext.SuccessAsync($"There are {teams.Count} team(s): {string.Join(", ", teams.Select(x => $"[{x.Name}]"))}");
    }

    [CommandOverload]
    public async Task ListAsync([TeamSuggestions] string team)
    {
        if (await this.FindAsync(team) is not { } found)
            return;

        await this.CommandContext.SuccessAsync(found.Entities.Count == 0
            ? $"There are no members on team [{team}]"
            : $"Team [{team}] has {found.Entities.Count} member(s): {string.Join(", ", found.Entities)}");
    }

    [Command("add")]
    public Task AddAsync(string team) => this.AddAsync(team, team);

    [CommandOverload]
    public async Task AddAsync(string team, [Remaining] string displayName)
    {
        if (this.Scoreboard.Teams.Any(x => x.Name == team))
        {
            await this.CommandContext.FailAsync("A team already exists by that name");
            return;
        }

        this.Scoreboard.CreateTeam(team, ChatMessage.Simple(displayName), NameTagVisibility.Always, CollisionRule.Always, TeamColor.White,
            ChatMessage.Empty, ChatMessage.Empty);
        await this.CommandContext.SuccessAsync($"Created team [{displayName}]");
    }

    [Command("remove")]
    public async Task RemoveAsync([TeamSuggestions] string team)
    {
        if (await this.FindAsync(team) is not { } found)
            return;

        found.Delete();
        await this.CommandContext.SuccessAsync($"Removed team [{team}]");
    }

    [Command("empty")]
    public async Task EmptyAsync([TeamSuggestions] string team)
    {
        if (await this.FindAsync(team) is not { } found)
            return;

        if (found.Entities.Count == 0)
        {
            await this.CommandContext.FailAsync("Nothing changed. That team is already empty");
            return;
        }

        var removed = found.RemoveEntities([.. found.Entities]);
        await this.CommandContext.SuccessAsync($"Removed {removed} member(s) from team [{team}]");
    }

    [Command("join")]
    public Task JoinAsync([TeamSuggestions] string team) =>
        this.CommandContext.Executor is IEntity executor
            ? this.JoinAsync(team, new ScoreHolderArgument(ScoreHolderArgument.NameOf(executor), null))
            : this.CommandContext.FailAsync("Name the members to add to the team");

    [CommandOverload]
    public async Task JoinAsync([TeamSuggestions] string team, ScoreHolderArgument members)
    {
        if (await this.FindAsync(team) is not { } found)
            return;

        var names = members.Resolve(this.CommandContext, []);

        // An entity is on one team at a time.
        foreach (var other in this.Scoreboard.Teams.Where(x => x != found))
        {
            var leaving = names.Where(other.Entities.Contains).ToArray();
            if (leaving.Length > 0)
                other.RemoveEntities(leaving);
        }

        found.AddEntities([.. names]);
        await this.CommandContext.SuccessAsync($"Added {Describe(names)} to team [{team}]");
    }

    [Command("leave")]
    public async Task LeaveAsync(ScoreHolderArgument members)
    {
        var names = members.Resolve(this.CommandContext, []);

        foreach (var team in this.Scoreboard.Teams)
        {
            var leaving = names.Where(team.Entities.Contains).ToArray();
            if (leaving.Length > 0)
                team.RemoveEntities(leaving);
        }

        await this.CommandContext.SuccessAsync($"Removed {Describe(names)} from any team");
    }

    [Command("modify")]
    public async Task ModifyAsync([TeamSuggestions] string team,
        [Suggestions("color", "displayName", "prefix", "suffix", "nametagVisibility", "collisionRule")] string option,
        [Remaining] string value)
    {
        if (await this.FindAsync(team) is not { } found)
            return;

        switch (option)
        {
            case "color" when TryParseEnum<ScoreboardCommandModule.VanillaColor>(value, out var color):
                found.Color = (TeamColor)(int)color;
                break;
            case "displayName":
                found.DisplayName = ChatMessage.Simple(value);
                break;
            case "prefix":
                found.Prefix = ChatMessage.Simple(value);
                break;
            case "suffix":
                found.Suffix = ChatMessage.Simple(value);
                break;
            case "nametagVisibility" when TryParseEnum<NameTagVisibility>(value, out var visibility):
                found.NameTagVisibility = visibility;
                break;
            case "collisionRule" when TryParseEnum<CollisionRule>(value, out var rule):
                found.CollisionRule = rule;
                break;
            default:
                await this.CommandContext.FailAsync($"Invalid value '{value}' for {option}");
                return;
        }

        found.Update();
        await this.CommandContext.SuccessAsync($"Updated the {option} of team [{team}] to {value}");
    }

    private async Task<ITeam?> FindAsync(string team)
    {
        var found = this.Scoreboard.Teams.FirstOrDefault(x => x.Name == team);
        if (found is null)
            await this.CommandContext.FailAsync($"Unknown team '{team}'");

        return found;
    }

    private static string Describe(IReadOnlyList<string> names) => names.Count == 1 ? names[0] : $"{names.Count} entities";

    // Reads vanilla's camelCase or snake_case names, like hideForOtherTeams or dark_blue.
    private static bool TryParseEnum<TEnum>(string value, out TEnum result) where TEnum : struct, Enum =>
        Enum.TryParse(value.Replace("_", string.Empty), ignoreCase: true, out result) && !int.TryParse(value, out _);

    private sealed class TeamSuggestionsAttribute : BaseSuggestionProviderAttribute
    {
        public override ValueTask<IEnumerable<CommandSuggestion>> GetSuggestionsAsync(CommandContext context) =>
            ValueTask.FromResult(context.Server.ScoreboardManager.DefaultScoreboard.Teams.Select(x => new CommandSuggestion(x.Name)));
    }
}
