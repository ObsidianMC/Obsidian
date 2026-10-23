using Microsoft.Extensions.Logging;
using Obsidian.API.Commands.Exceptions;
using Obsidian.API.Utilities;
using Obsidian.Commands.Framework;
using System.Threading;

namespace Obsidian.Console;

/// <summary>
/// Runs a console line through the server's command handler, as a player's slash command runs; replies land in the log.
/// </summary>
public sealed partial class RegistryConsoleCommandHandler(IServer server, ILogger<RegistryConsoleCommandHandler> logger)
    : IConsoleCommandHandler
{
    private readonly ConsoleCommandSender sender = new(logger);

    /// <summary>
    /// Completes the text before the caret with the command handler's suggestions, as a player's tab completion does,
    /// replacing the rest of the word at the caret too.
    /// </summary>
    public async ValueTask<ConsoleCompletion?> CompleteAsync(string commandLine, int cursor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var context = new CommandContext(commandLine[..cursor], this.sender, null, server);
        var completion = await server.CommandHandler.CompleteAsync(context).ConfigureAwait(false);

        if (completion.Suggestions.Count == 0)
            return null;

        var end = CommandParser.FindWordEnd(commandLine, cursor);

        return new ConsoleCompletion(completion.Start, end - completion.Start, [.. completion.Suggestions.Select(x => x.Text)]);
    }

    public async ValueTask<bool> HandleAsync(string commandLine, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var line = commandLine.Trim();

        if (line.StartsWith(CommandHelpers.DefaultPrefix, StringComparison.Ordinal))
            line = line[CommandHelpers.DefaultPrefix.Length..];

        if (line.Length == 0)
            return false;

        var args = CommandParser.SplitQualifiedString(line.AsMemory());

        if (!server.CommandHandler.GetAllCommands().Any(command => command.CheckCommand(args, null)))
            return false;

        var context = new CommandContext(CommandHelpers.DefaultPrefix + line, this.sender, null, server);

        try
        {
            await server.CommandHandler.ProcessCommand(context).ConfigureAwait(false);
        }
        catch (DisallowedCommandIssuerException ex)
        {
            Log.DisallowedIssuer(logger, ex.Message);
        }

        return true;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "{Message}")]
        public static partial void DisallowedIssuer(ILogger logger, string message);
    }
}
