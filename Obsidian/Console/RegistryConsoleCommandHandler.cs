using Microsoft.Extensions.Logging;
using Obsidian.API.Commands;
using Obsidian.API.Commands.Exceptions;
using Obsidian.API.Utilities;
using Obsidian.Commands.Framework;
using System.Threading;

namespace Obsidian.Console;

/// <summary>
/// Runs a console line through the server's command handler, as a player's slash command runs; replies land in the log.
/// </summary>
public sealed class RegistryConsoleCommandHandler(IServer server, ILogger<RegistryConsoleCommandHandler> logger)
    : IConsoleCommandHandler
{
    private readonly ConsoleCommandSender sender = new(logger);

    /// <summary>
    /// Completes command and subcommand names. Obsidian's argument parsers have no suggestion API, so arguments are
    /// not completed.
    /// </summary>
    public ValueTask<ConsoleCompletion?> CompleteAsync(string commandLine, int cursor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var start = cursor == 0 ? 0 : commandLine.LastIndexOf(' ', cursor - 1) + 1;
        var end = commandLine.IndexOf(' ', cursor);

        if (end < 0)
            end = commandLine.Length;

        var previous = commandLine[..start].Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (previous.Length == 0 && start < cursor && commandLine[start] == '/')
            start++;

        var commands = server.CommandHandler.GetAllCommands()
            .Where(command => command.AllowedIssuers.HasFlag(CommandIssuers.Console))
            .ToArray();
        Command? parent = null;

        foreach (var word in previous)
        {
            parent = commands.FirstOrDefault(command => command.CheckCommand([word.TrimStart('/')], parent));

            if (parent is null)
                return ValueTask.FromResult<ConsoleCompletion?>(null);
        }

        var partial = commandLine[start..cursor];
        var candidates = commands.Where(command => command.Parent == parent)
            .SelectMany(command => command.Aliases.Prepend(command.Name))
            .Where(name => name.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return ValueTask.FromResult(candidates.Length == 0 ? null : new ConsoleCompletion(start, end - start, candidates));
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
            logger.LogWarning("{Message}", ex.Message);
        }

        return true;
    }
}
