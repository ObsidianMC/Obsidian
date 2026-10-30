using Obsidian.API.Commands;
using Obsidian.Commands.Framework;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Utilities;
using static Obsidian.Commands.CommandFeedback;

namespace Obsidian.Commands.Modules;

/// <summary>
/// Vanilla's <c>/experience</c> (and <c>/xp</c>): adds, sets or queries players' experience points or levels.
/// </summary>
[CommandGroup("experience", "xp")]
[CommandInfo("Changes or queries players' experience.", "/experience <add|set|query>")]
[RequirePermissionLevel(2)]
public sealed class ExperienceCommandModule : CommandModuleBase
{
    public enum ExperienceUnit
    {
        Points,
        Levels
    }

    [Command("add")]
    [CommandInfo("Adds experience.", "/experience add <targets> <amount> [points|levels]")]
    public Task AddAsync(IPlayer[] targets, int amount) => this.AddAsync(targets, amount, ExperienceUnit.Points);

    [CommandOverload]
    public async Task AddAsync(IPlayer[] targets, int amount, ExperienceUnit unit)
    {
        if (await this.CommandContext.NoneFoundAsync(targets))
            return;

        foreach (var player in targets)
        {
            if (unit == ExperienceUnit.Levels)
                await SetLevelsAsync(player, Math.Max(0, player.XpLevel + amount));
            else
                await SetTotalPointsAsync(player, Math.Max(0, PointsOf(player) + amount));
        }

        await this.CommandContext.SuccessAsync(
            $"Gave {amount} experience {(unit == ExperienceUnit.Levels ? "levels" : "points")} to {Describe(targets, "players")}");
    }

    [Command("set")]
    [CommandInfo("Sets experience.", "/experience set <targets> <amount> [points|levels]")]
    public Task SetAsync(IPlayer[] targets, int amount) => this.SetAsync(targets, amount, ExperienceUnit.Points);

    [CommandOverload]
    public async Task SetAsync(IPlayer[] targets, int amount, ExperienceUnit unit)
    {
        if (await this.CommandContext.NoneFoundAsync(targets))
            return;

        if (amount < 0)
        {
            await this.CommandContext.FailAsync("Experience can't be negative");
            return;
        }

        foreach (var player in targets)
        {
            if (unit == ExperienceUnit.Levels)
            {
                await SetLevelsAsync(player, amount);
            }
            else
            {
                // Like vanilla's, setting points only changes the progress within the current level.
                var needed = XpHelper.ExperienceRequiredForNextLevel(player.XpLevel);
                if (amount > needed - 1)
                {
                    await this.CommandContext.FailAsync("Unable to set experience points above the maximum points for the player's current level");
                    return;
                }

                await SetTotalPointsAsync(player, XpHelper.TotalExperienceFromLevel(player.XpLevel) + amount);
            }
        }

        await this.CommandContext.SuccessAsync(
            $"Set {amount} experience {(unit == ExperienceUnit.Levels ? "levels" : "points")} on {Describe(targets, "players")}");
    }

    [Command("query")]
    [CommandInfo("Queries experience.", "/experience query <target> <points|levels>")]
    public async Task QueryAsync(IPlayer target, ExperienceUnit unit)
    {
        if (target is null)
        {
            await this.CommandContext.FailAsync("No player was found");
            return;
        }

        await this.CommandContext.SuccessAsync(unit == ExperienceUnit.Levels
            ? $"{target.Username} has {target.XpLevel} experience levels"
            : $"{target.Username} has {(int)(target.XpP * XpHelper.ExperienceRequiredForNextLevel(target.XpLevel))} experience points");
    }

    // The points a player has in all, from their levels and their progress towards the next.
    private static int PointsOf(IPlayer player) =>
        XpHelper.TotalExperienceFromLevel(player.XpLevel)
            + (int)(player.XpP * XpHelper.ExperienceRequiredForNextLevel(player.XpLevel));

    private static ValueTask SetLevelsAsync(IPlayer player, int level)
    {
        player.XpLevel = level;
        player.XpTotal = Math.Max(player.XpTotal, XpHelper.TotalExperienceFromLevel(level));

        return player.QueuePacketAsync(new SetExperiencePacket(player.XpP, player.XpLevel, player.XpTotal));
    }

    private static ValueTask SetTotalPointsAsync(IPlayer player, int points)
    {
        var level = 0;
        while (points >= XpHelper.ExperienceRequiredForNextLevel(level))
        {
            points -= XpHelper.ExperienceRequiredForNextLevel(level);
            level++;
        }

        player.XpLevel = level;
        player.XpP = points / (float)XpHelper.ExperienceRequiredForNextLevel(level);
        player.XpTotal = XpHelper.TotalExperienceFromLevel(level) + points;

        return player.QueuePacketAsync(new SetExperiencePacket(player.XpP, player.XpLevel, player.XpTotal));
    }
}
