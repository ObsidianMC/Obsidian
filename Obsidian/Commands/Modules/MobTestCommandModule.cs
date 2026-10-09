using Obsidian.API.Commands;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators;

namespace Obsidian.Commands.Modules;

public sealed class MobTestCommandModule : CommandModuleBase
{
    [Command("mob_tp")]
    [CommandInfo("Teleports to a mob test exhibit", "/mob_tp <entity> [state]")]
    [IssuerScope(CommandIssuers.Client)]
    public Task TeleportAsync(string entity) => TeleportAsync(entity, "active");

    [CommandOverload]
    public async Task TeleportAsync(string entity, [Remaining] string state)
    {
        if (Player is not IPlayer player) return;
        if (player.Level is not AbstractLevel { Generator: MobTestGenerator generator })
        {
            await player.SendMessageAsync("Use mob_tp while in a mob-test world.");
            return;
        }
        var name = entity.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase) ? entity[10..] : entity;
        name = name.Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        if (!Enum.TryParse<EntityType>(name, true, out var type) || !Enum.IsDefined(type))
        {
            await player.SendMessageAsync($"Unknown mob: {entity}.");
            return;
        }
        if (!generator.TryGetExhibitDestination(type, state.Trim(), out var destination, out var availableStates))
        {
            await player.SendMessageAsync(availableStates.Length == 0 ? $"No test exhibits for {type}." : $"Available states for {type}: {availableStates}.");
            return;
        }
        await player.TeleportAsync(destination);
        await player.SendMessageAsync($"Teleported beside {type} / {state.Trim()}.");
    }
}
