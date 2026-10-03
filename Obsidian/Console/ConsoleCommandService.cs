using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO;
using System.Threading;

namespace Obsidian.Console;

/// <summary>Reads ordinary terminal lines without taking over the screen or its scrollback.</summary>
public sealed partial class ConsoleCommandService(
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

        // Commands run one at a time in arrival order, off the reader loop. DispatchAsync never
        // faults, so each link can await the previous one unconditionally.
        Task pending = Task.CompletedTask;

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
                    Log.InputUnavailable(logger, ex);

                    break;
                }

                if (line is null)
                    break;

                token.ThrowIfCancellationRequested();

                string commandLine = line.Trim();

                if (commandLine.Length == 0)
                    continue;

                if (options.Value.EchoCommands)
                    Log.Echo(logger, options.Value.Prompt, commandLine);

                if (await this.TryHandleBuiltInAsync(commandLine, pending, token).ConfigureAwait(false))
                    continue;

                Task previous = pending;
                pending = Task.Run(async () =>
                {
                    await previous.ConfigureAwait(false);
                    await this.DispatchAsync(commandLine, token).ConfigureAwait(false);
                });
            }

            await pending.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private async ValueTask<bool> TryHandleBuiltInAsync(string commandLine, Task pending, CancellationToken token)
    {
        switch (commandLine.ToLowerInvariant())
        {
            case "stop":
            case "exit":
            case "quit":
                // Redirected input is read ahead of execution. Let earlier commands finish before
                // StopApplication cancels the token they run with.
                if (terminal?.IsInteractive != true)
                    await pending.WaitAsync(token).ConfigureAwait(false);

                Log.ShutdownRequested(logger);
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
                        Log.ClearUnsupported(logger, ex);
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
            Log.CompletionFailed(logger, error);
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

            Log.UnknownCommand(logger, commandLine);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Log.CommandFailed(logger, ex, commandLine);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Console input is unavailable; the server will continue running")]
        public static partial void InputUnavailable(ILogger logger, Exception exception);

        [LoggerMessage(Level = LogLevel.Information, Message = "{Prompt}{CommandLine}")]
        public static partial void Echo(ILogger logger, string prompt, string commandLine);

        [LoggerMessage(Level = LogLevel.Information, Message = "Shutdown requested from the console")]
        public static partial void ShutdownRequested(ILogger logger);

        [LoggerMessage(Level = LogLevel.Debug, Message = "The terminal does not support clearing the screen")]
        public static partial void ClearUnsupported(ILogger logger, Exception exception);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Console completion failed")]
        public static partial void CompletionFailed(ILogger logger, Exception exception);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Unknown command: {CommandLine}")]
        public static partial void UnknownCommand(ILogger logger, string commandLine);

        [LoggerMessage(Level = LogLevel.Error, Message = "Command failed: {CommandLine}")]
        public static partial void CommandFailed(ILogger logger, Exception exception, string commandLine);
    }
}
