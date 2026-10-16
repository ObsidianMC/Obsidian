using Obsidian.API.Commands;

namespace Obsidian.Commands.Framework;

/// <summary>
/// Requires a permission level, like vanilla's commands: the console may always run the command, and a player whose
/// operator level (or integrated session's level) is at least <see cref="Level"/>.
/// </summary>
/// <param name="level">The level, from 1 to 4; vanilla's game master commands, like <c>/locate</c>, need 2.</param>
internal sealed class RequirePermissionLevelAttribute(int level) : BaseExecutionCheckAttribute
{
    public int Level { get; } = level;

    public override Task<bool> RunChecksAsync(CommandContext context)
    {
        if (context.Sender.Issuer is CommandIssuers.Console or CommandIssuers.RemoteConsole)
            return Task.FromResult(true);

        var allowed = context.Player is not null
            && context.Server.Operators is OperatorList operators
            && operators.GetPermissionLevel(context.Player) >= this.Level;

        return Task.FromResult(allowed);
    }
}
