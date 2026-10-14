using Obsidian.API.Commands;

namespace Obsidian.Commands.Modules;

[CommandGroup("advancement")]
[RequirePermission(permissions: "advancement")]
public sealed class AdvancementCommandModule : CommandModuleBase
{
    [Command("grant")]
    [CommandInfo("Grants advancement criteria", "/advancement grant <player> only <advancement> [criterion]")]
    public Task GrantAsync(IPlayer target, string mode, string identifier, string? criterion = null) =>
        this.ChangeAsync(target, mode, identifier, criterion, grant: true);

    [Command("revoke")]
    [CommandInfo("Revokes advancement criteria", "/advancement revoke <player> only <advancement> [criterion]")]
    public Task RevokeAsync(IPlayer target, string mode, string identifier, string? criterion = null) =>
        this.ChangeAsync(target, mode, identifier, criterion, grant: false);

    private async Task ChangeAsync(IPlayer target, string mode, string identifier, string? criterion, bool grant)
    {
        if (mode != "only" || target is not Entities.Player player)
        {
            await this.Sender.SendMessageAsync("Use an online player and the 'only' mode.");
            return;
        }

        identifier = AdvancementsRegistry.Qualify(identifier);
        var changed = grant
            ? await player.Advancements.GrantAsync(identifier, criterion)
            : player.Advancements.Revoke(identifier, criterion);
        await player.ProgressAdvancements.FlushAsync();
        await this.Sender.SendMessageAsync(
            changed ? "Advancement progress changed." : "No advancement progress changed.");
    }
}
