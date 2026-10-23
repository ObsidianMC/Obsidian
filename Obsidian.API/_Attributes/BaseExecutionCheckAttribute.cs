namespace Obsidian.API;

/// <summary>
/// Decides whether a sender may run a command. Checks on a group also apply to its subcommands, and checks on a command
/// to all of its overloads.
/// </summary>
/// <remarks>
/// Checks also run to decide which commands help lists and which suggestions a sender gets, and may run more than once
/// for one command, so they should not have side effects.
/// </remarks>
public abstract class BaseExecutionCheckAttribute : Attribute
{
    public abstract Task<bool> RunChecksAsync(CommandContext context);
}
