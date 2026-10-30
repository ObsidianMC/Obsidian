using Obsidian.Commands.Framework;
using Obsidian.API.Commands;
using Obsidian.Net.Packets.Play.Clientbound;
using System.Runtime.CompilerServices;

namespace Obsidian.Commands.Modules;

[CommandGroup("posteffect")]
[RequirePermissionLevel(2)]
public sealed class PostEffectCommandModule : CommandModuleBase
{
    private static readonly ConditionalWeakTable<IPlayer, List<string>> effects = new();

    [Command("add")]
    public Task<int> AddAsync(IPlayer[] targets, ResourceLocation posteffect) =>
        this.ChangeAsync(targets, "add", posteffect.ToString());

    [Command("remove")]
    public Task<int> RemoveAsync(IPlayer[] targets, ResourceLocation posteffect) =>
        this.ChangeAsync(targets, "remove", posteffect.ToString());

    [Command("clear")]
    public Task<int> ClearAsync(IPlayer[] targets) => this.ChangeAsync(targets, "clear", null);

    [Command("list")]
    public async Task<int> ListAsync(IPlayer target)
    {
        var current = effects.GetOrCreateValue(target);
        await this.CommandContext.SuccessAsync($"{target.Username}: {string.Join(", ", current)}");
        return current.Count;
    }

    private async Task<int> ChangeAsync(IPlayer[] targets, string operation, string? effect)
    {
        if (await this.CommandContext.NoneFoundAsync(targets))
            return 0;

        var count = 0;
        foreach (var target in targets)
        {
            var current = effects.GetOrCreateValue(target);
            var changed = false;
            switch (operation)
            {
                case "add" when !current.Contains(effect!):
                    current.Add(effect!);
                    changed = true;
                    break;

                case "remove":
                    changed = current.Remove(effect!);
                    break;

                case "clear":
                    changed = current.Count != 0;
                    current.Clear();
                    break;
            }

            if (!changed)
                continue;

            count++;
            await target.QueuePacketAsync(new PostEffectsPacket { PostEffects = [.. current] });
        }

        if (count == 0)
            await this.CommandContext.FailAsync("No player's post effects changed");
        else
            await this.CommandContext.SuccessAsync($"Changed post effects for {count} players");

        return count;
    }
}
