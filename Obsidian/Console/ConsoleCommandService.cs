using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO;
using System.Threading;

namespace Obsidian.Console;

/// <summary>Reads ordinary terminal lines without taking over the screen or its scrollback.</summary>
public sealed class ConsoleCommandService(
    TextReader input,
    IOptions<ConsoleCommandOptions> options,
    IHostApplicationLifetime lifetime,
    IEnumerable<IConsoleCommandHandler> handlers,
    ILogger<ConsoleCommandService> logger,
    ConsoleTerminal? terminal = null) : BackgroundService
{
    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken, lifetime.ApplicationStopping);
        CancellationToken token = cancellation.Token;
        List<Task> commands = [];

        try
        {
            while (!token.IsCancellationRequested)
            {
                // Console.In.ReadLineAsync also blocks synchronously. Read on a worker and cancel
                // the wait so an idle terminal cannot hold up host shutdown. At most one read is
                // outstanding; its background thread does not prevent the process from exiting.
                string? line;

                try
                {
                    line = terminal?.IsInteractive == true
                        ? await terminal.ReadLineAsync(token, this.CompleteAsync).ConfigureAwait(false)
                        : await Task.Run(input.ReadLine, token).WaitAsync(token).ConfigureAwait(false);
                }
                catch (IOException ex)
                {
                    logger.LogWarning(ex, "Console input is unavailable; the server will continue running.");

                    break;
                }

                if (line is null)
                    break;

                token.ThrowIfCancellationRequested();

                string commandLine = line.Trim();

                if (commandLine.Length == 0)
                    continue;

                if (options.Value.EchoCommands)
                    logger.LogInformation("{Prompt}{CommandLine}", options.Value.Prompt, commandLine);

                if (this.TryHandleBuiltIn(commandLine))
                    continue;

                commands.RemoveAll(task => task.IsCompleted);
                commands.Add(Task.Run(() => this.DispatchAsync(commandLine, token), token));
            }

            await Task.WhenAll(commands).WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private bool TryHandleBuiltIn(string commandLine)
    {
        switch (commandLine.ToLowerInvariant())
        {
            case "stop":
            case "exit":
            case "quit":
                logger.LogInformation("Shutdown requested from the console.");
                lifetime.StopApplication();

                return true;

            case "clear":
            case "cls":
                if (terminal?.IsInteractive == true)
                {
                    terminal.Clear();

                    return true;
                }

                if (!System.Console.IsOutputRedirected)
                {
                    try
                    {
                        System.Console.Clear();
                    }
                    catch (IOException ex)
                    {
                        logger.LogDebug(ex, "The terminal does not support clearing the screen.");
                    }
                }

                return true;

            default:
                return false;
        }
    }

    private async ValueTask<ConsoleCompletion?> CompleteAsync(string line, int cursor, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var handler in handlers)
            {
                if (await handler.CompleteAsync(line, cursor, cancellationToken).ConfigureAwait(false) is { Candidates.Count: > 0 } completion)
                    return completion;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            logger.LogDebug(error, "Console completion failed");
        }

        return null;
    }

    private async Task DispatchAsync(string commandLine, CancellationToken token)
    {
        try
        {
            foreach (IConsoleCommandHandler handler in handlers)
            {
                if (await handler.HandleAsync(commandLine, token).ConfigureAwait(false))
                    return;
            }

            logger.LogWarning("Unknown command: {CommandLine}", commandLine);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Command failed: {CommandLine}", commandLine);
        }
    }
}
