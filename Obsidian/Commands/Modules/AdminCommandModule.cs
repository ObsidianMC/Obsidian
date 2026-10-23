using Obsidian.API.Commands;
using Obsidian.Commands.Framework;
using Obsidian.Utilities;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Obsidian.Commands.CommandFeedback;

namespace Obsidian.Commands.Modules;

/// <summary>
/// Vanilla's commands for running a server and talking on it: <c>/kick</c>, <c>/ban</c>, <c>/ban-ip</c>,
/// <c>/pardon</c>, <c>/pardon-ip</c>, <c>/banlist</c>, <c>/list</c>, <c>/say</c>, <c>/me</c> and <c>/msg</c>.
/// </summary>
public sealed class AdminCommandModule : CommandModuleBase
{
    [Command("kick")]
    [CommandInfo("Kicks players from the server.", "/kick <targets> [<reason>]")]
    [RequirePermissionLevel(3)]
    public Task KickAsync(IPlayer[] targets) => this.KickAsync(targets, "Kicked by an operator");

    [CommandOverload]
    public async Task KickAsync(IPlayer[] targets, [Remaining] string reason)
    {
        if (await this.CommandContext.NoneFoundAsync(targets))
            return;

        foreach (var player in targets)
        {
            await player.KickAsync(reason);
            await this.CommandContext.SuccessAsync($"Kicked {player.Username}: {reason}");
        }
    }

    [Command("ban")]
    [CommandInfo("Bans players from the server.", "/ban <targets> [<reason>]")]
    [RequirePermissionLevel(3)]
    public Task BanAsync([OnlinePlayerSuggestions] string targets) => this.BanAsync(targets, null);

    [CommandOverload]
    public async Task BanAsync([OnlinePlayerSuggestions] string targets, [Remaining] string? reason)
    {
        // Players who aren't online can be banned by name, as vanilla bans game profiles.
        var players = this.FindProfiles(targets);
        if (players is null)
        {
            await this.CommandContext.FailAsync("That player does not exist");
            return;
        }

        var bans = ((Server)this.Server).Bans;
        var banned = 0;

        foreach (var (name, player) in players)
        {
            if (!bans.Ban(name, player?.Uuid, this.SourceName, reason))
                continue;

            banned++;
            await this.CommandContext.SuccessAsync($"Banned {name}: {reason ?? "Banned by an operator."}");

            if (player is not null)
                await player.KickAsync(BanList.DisconnectMessage(reason ?? "Banned by an operator."));
        }

        if (banned == 0)
            await this.CommandContext.FailAsync("Nothing changed. The player is already banned");
    }

    [Command("ban-ip")]
    [CommandInfo("Bans an IP address from the server.", "/ban-ip <target> [<reason>]")]
    [RequirePermissionLevel(3)]
    public Task BanIpAsync([OnlinePlayerSuggestions] string target) => this.BanIpAsync(target, null);

    [CommandOverload]
    public async Task BanIpAsync([OnlinePlayerSuggestions] string target, [Remaining] string? reason)
    {
        // An address, or the address of an online player.
        var ip = IPAddress.TryParse(target, out _) ? target : this.Server.GetPlayer(target)?.ClientIP;
        if (ip is null)
        {
            await this.CommandContext.FailAsync("Invalid IP address or unknown player");
            return;
        }

        if (!((Server)this.Server).Bans.BanIp(ip, this.SourceName, reason))
        {
            await this.CommandContext.FailAsync("Nothing changed. That IP is already banned");
            return;
        }

        var affected = this.Server.OnlinePlayers.Values.Where(player => player.ClientIP == ip).ToArray();
        foreach (var player in affected)
            await player.KickAsync(BanList.DisconnectMessage(reason ?? "Banned by an operator."));

        await this.CommandContext.SuccessAsync($"Banned IP {ip}: {reason ?? "Banned by an operator."}");
        if (affected.Length > 0)
            await this.CommandContext.SuccessAsync($"This ban affects {affected.Length} player(s): {string.Join(", ", affected.Select(x => x.Username))}");
    }

    [Command("pardon")]
    [CommandInfo("Removes a player's ban.", "/pardon <targets>")]
    [RequirePermissionLevel(3)]
    public async Task PardonAsync([BannedPlayerSuggestions] string targets)
    {
        if (((Server)this.Server).Bans.Pardon(targets))
            await this.CommandContext.SuccessAsync($"Unbanned {targets}");
        else
            await this.CommandContext.FailAsync("Nothing changed. The player isn't banned");
    }

    [Command("pardon-ip")]
    [CommandInfo("Removes an IP address's ban.", "/pardon-ip <target>")]
    [RequirePermissionLevel(3)]
    public async Task PardonIpAsync([BannedIpSuggestions] string target)
    {
        if (!IPAddress.TryParse(target, out _))
        {
            await this.CommandContext.FailAsync("Invalid IP address");
            return;
        }

        if (((Server)this.Server).Bans.PardonIp(target))
            await this.CommandContext.SuccessAsync($"Unbanned IP {target}");
        else
            await this.CommandContext.FailAsync("Nothing changed. That IP isn't banned");
    }

    [Command("banlist")]
    [CommandInfo("Lists the bans.", "/banlist [ips|players]")]
    [RequirePermissionLevel(3)]
    public async Task BanListAsync()
    {
        var bans = ((Server)this.Server).Bans;
        await this.ListBansAsync([
            .. bans.Players.Select(ban => (ban.Name, ban.Source, ban.Reason)),
            .. bans.Ips.Select(ban => (ban.Ip, ban.Source, ban.Reason))]);
    }

    [CommandOverload]
    public async Task BanListAsync([Suggestions("ips", "players")] string type)
    {
        var bans = ((Server)this.Server).Bans;

        switch (type)
        {
            case "players":
                await this.ListBansAsync([.. bans.Players.Select(ban => (ban.Name, ban.Source, ban.Reason))]);
                break;
            case "ips":
                await this.ListBansAsync([.. bans.Ips.Select(ban => (ban.Ip, ban.Source, ban.Reason))]);
                break;
            default:
                await this.CommandContext.FailAsync("Expected ips or players");
                break;
        }
    }

    private async Task ListBansAsync(IReadOnlyList<(string Target, string Source, string Reason)> bans)
    {
        if (bans.Count == 0)
        {
            await this.CommandContext.SuccessAsync("There are no bans");
            return;
        }

        await this.CommandContext.SuccessAsync($"There are {bans.Count} ban(s):");
        foreach (var (target, source, reason) in bans)
            await this.CommandContext.SuccessAsync($"{target} was banned by {source}: {reason}");
    }

    [Command("list")]
    [CommandInfo("Lists the players online.", "/list [uuids]")]
    public Task ListAsync() => this.ListPlayersAsync(uuids: false);

    [CommandOverload]
    public Task ListAsync([Suggestions("uuids")] string uuids) => this.ListPlayersAsync(uuids == "uuids");

    private Task ListPlayersAsync(bool uuids)
    {
        var players = this.Server.OnlinePlayers.Values.OrderBy(player => player.Username, StringComparer.OrdinalIgnoreCase)
            .Select(player => uuids ? $"{player.Username} ({player.Uuid})" : player.Username)
            .ToArray();

        return this.CommandContext.SuccessAsync(
            $"There are {players.Length} of a max of {this.Server.Configuration.MaxPlayers} players online: {string.Join(", ", players)}");
    }

    [Command("say")]
    [CommandInfo("Sends a message to everyone.", "/say <message>")]
    [RequirePermissionLevel(2)]
    public void Say([Remaining] string message) => this.Server.BroadcastMessage($"[{this.SourceName}] {message}");

    [Command("me")]
    [CommandInfo("Describes what you're doing.", "/me <action>")]
    public void Me([Remaining] string action) => this.Server.BroadcastMessage($"* {this.SourceName} {action}");

    [Command("msg", "tell", "w")]
    [CommandInfo("Sends a private message.", "/msg <targets> <message>")]
    public async Task MessageAsync(IPlayer[] targets, [Remaining] string message)
    {
        if (await this.CommandContext.NoneFoundAsync(targets))
            return;

        foreach (var player in targets)
        {
            await player.SendMessageAsync(new ChatMessage { Text = $"{this.SourceName} whispers to you: {message}", Color = HexColor.Gray, Italic = true });
            await this.Sender.SendMessageAsync(new ChatMessage { Text = $"You whisper to {player.Username}: {message}", Color = HexColor.Gray, Italic = true });
        }
    }

    // The name commands show for whoever ran them, as vanilla's do: the executing entity's, or Server for the console.
    private string SourceName => this.CommandContext.Executor is IEntity executor ? NameOf(executor) : "Server";

    /// <summary>
    /// The players a ban names: those a selector selects, or one player by name, online or not.
    /// </summary>
    /// <returns>The names with the online players among them, or <see langword="null"/> for a malformed name.</returns>
    private List<(string Name, IPlayer? Player)>? FindProfiles(string targets)
    {
        if (!EntitySelector.TryParse(targets, out var selector))
            return null;

        if (targets.StartsWith('@'))
            return [.. selector.SelectPlayers(this.CommandContext).Select(player => (player.Username, (IPlayer?)player))];

        var online = this.Server.GetPlayer(targets);
        return [(online?.Username ?? targets, online)];
    }

    /// <summary>Suggests the names of the players online.</summary>
    internal sealed class OnlinePlayerSuggestionsAttribute : BaseSuggestionProviderAttribute
    {
        public override ValueTask<IEnumerable<CommandSuggestion>> GetSuggestionsAsync(CommandContext context) =>
            ValueTask.FromResult(context.Server.OnlinePlayers.Values.Select(player => new CommandSuggestion(player.Username)));
    }

    private sealed class BannedPlayerSuggestionsAttribute : BaseSuggestionProviderAttribute
    {
        public override ValueTask<IEnumerable<CommandSuggestion>> GetSuggestionsAsync(CommandContext context) =>
            ValueTask.FromResult(context.Server is Server server
                ? server.Bans.Players.Select(ban => new CommandSuggestion(ban.Name))
                : []);
    }

    private sealed class BannedIpSuggestionsAttribute : BaseSuggestionProviderAttribute
    {
        public override ValueTask<IEnumerable<CommandSuggestion>> GetSuggestionsAsync(CommandContext context) =>
            ValueTask.FromResult(context.Server is Server server
                ? server.Bans.Ips.Select(ban => new CommandSuggestion(ban.Ip))
                : []);
    }
}

/// <summary>
/// Vanilla's <c>/whitelist</c>: turns the whitelist on or off and changes who's on it, saving
/// <c>config/whitelist.json</c> and the <c>Whitelist</c> setting of <c>config/server.json</c>.
/// </summary>
[CommandGroup("whitelist")]
[CommandInfo("Manages the whitelist.", "/whitelist <on|off|add|remove|list|reload>")]
[RequirePermissionLevel(3)]
public sealed class WhitelistCommandModule : CommandModuleBase
{
    private static readonly JsonSerializerOptions indented = new() { WriteIndented = true };

    private Server HostServer => (Server)this.Server;

    [Command("on")]
    public Task OnAsync() => this.SetEnabledAsync(true);

    [Command("off")]
    public Task OffAsync() => this.SetEnabledAsync(false);

    [Command("list")]
    public Task ListAsync()
    {
        var players = this.HostServer.WhitelistConfiguration.CurrentValue.WhitelistedPlayers;

        return players.Count == 0
            ? this.CommandContext.SuccessAsync("There are no whitelisted players")
            : this.CommandContext.SuccessAsync($"There are {players.Count} whitelisted player(s): {string.Join(", ", players.Select(x => x.Name))}");
    }

    [Command("add")]
    public async Task AddAsync([AdminCommandModule.OnlinePlayerSuggestions] string targets)
    {
        var whitelist = this.HostServer.WhitelistConfiguration.CurrentValue;

        if (whitelist.WhitelistedPlayers.Any(x => string.Equals(x.Name, targets, StringComparison.OrdinalIgnoreCase)))
        {
            await this.CommandContext.FailAsync("Player is already whitelisted");
            return;
        }

        // A player who isn't online is added by name; their UUID is filled in when they're found online.
        var player = this.Server.GetPlayer(targets);
        whitelist.WhitelistedPlayers.Add(new WhitelistedPlayer { Name = player?.Username ?? targets, Id = player?.Uuid ?? Guid.Empty });

        await this.SaveAsync();
        await this.CommandContext.SuccessAsync($"Added {player?.Username ?? targets} to the whitelist");
    }

    [Command("remove")]
    public async Task RemoveAsync([WhitelistedSuggestions] string targets)
    {
        var whitelist = this.HostServer.WhitelistConfiguration.CurrentValue;

        if (whitelist.WhitelistedPlayers.RemoveAll(x => string.Equals(x.Name, targets, StringComparison.OrdinalIgnoreCase)) == 0)
        {
            await this.CommandContext.FailAsync("Player is not whitelisted");
            return;
        }

        await this.SaveAsync();
        await this.CommandContext.SuccessAsync($"Removed {targets} from the whitelist");
    }

    [Command("reload")]
    public Task ReloadAsync() =>
        // The configuration reloads whitelist.json whenever it changes, so there's nothing to read again.
        this.CommandContext.SuccessAsync("Reloaded the whitelist");

    private async Task SetEnabledAsync(bool enabled)
    {
        if (this.Server.Configuration.Whitelist == enabled)
        {
            await this.CommandContext.FailAsync(enabled ? "Whitelist is already turned on" : "Whitelist is already turned off");
            return;
        }

        this.Server.Configuration.Whitelist = enabled;

        if (this.HostServer.Integrated is null)
        {
            var file = new FileInfo(Path.Combine(ServerConstants.ConfigPath, "server.json"));
            var root = file.Exists ? JsonNode.Parse(await File.ReadAllTextAsync(file.FullName)) as JsonObject ?? [] : [];

            root["Whitelist"] = enabled;

            file.Directory?.Create();
            await File.WriteAllTextAsync(file.FullName, root.ToJsonString(indented));
        }

        await this.CommandContext.SuccessAsync(enabled ? "Whitelist is now turned on" : "Whitelist is now turned off");
    }

    private async Task SaveAsync()
    {
        if (this.HostServer.Integrated is not null)
            return;

        var whitelist = this.HostServer.WhitelistConfiguration.CurrentValue;
        var file = new FileInfo(Path.Combine(ServerConstants.ConfigPath, "whitelist.json"));
        file.Directory?.Create();

        await using var stream = file.Create();
        await JsonSerializer.SerializeAsync(stream, whitelist, indented);
    }

    private sealed class WhitelistedSuggestionsAttribute : BaseSuggestionProviderAttribute
    {
        public override ValueTask<IEnumerable<CommandSuggestion>> GetSuggestionsAsync(CommandContext context) =>
            ValueTask.FromResult(context.Server is Server server
                ? server.WhitelistConfiguration.CurrentValue.WhitelistedPlayers.Select(x => new CommandSuggestion(x.Name))
                : []);
    }
}
