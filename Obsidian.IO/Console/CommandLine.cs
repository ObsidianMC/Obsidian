using System;
using System.Collections.Generic;
using System.Threading;

namespace Obsidian.IO.Console;

public static partial class CommandLine
{
    // Reading key in a dedicated loop must check if the exitSemaphore
    // is not null, and if so, release it.

    public static string Title { set => System.Console.Title = value; }

    public static string CommandPrefix
    {
        get => _commandPrefix;
        set => ChangeCommandPrefixInternal(value);
    }

    private static readonly string defaultCommandPrefix = "> ";
    private static string _commandPrefix = defaultCommandPrefix;

    public static ConsoleColor ForegroundColor
    {
        get => _foregroundColor;
        set {
            _foregroundColor = value;
            SetForegroundColor(value);
        }
    }

    public static ConsoleColor BackgroundColor
    {
        get => _backgroundColor;
        set {
            _backgroundColor = value;
            SetBackgroundColor(value);
        }
    }

    private static ConsoleColor _foregroundColor;
    private static ConsoleColor _backgroundColor;

    public static event Func<bool>? CancelKeyPress;

    private static List<Command> commands = new();

    private static SemaphoreSlim? exitSemaphore;


    private static readonly ConsoleColor inputColor = ConsoleColor.White;

    private static bool hasControl;

    public static partial void ResetColor();
    private static partial void SetForegroundColor(ConsoleColor color);
    private static partial void SetBackgroundColor(ConsoleColor color);

    public static partial void Write(string text);
    public static partial void Write(ReadOnlySpan<char> text);
    public static partial void WriteLine();
    public static partial void WriteLine(string text);
    public static partial void WriteLine(ReadOnlySpan<char> text);

    private static partial void TakeControlInternal();
    private static partial void ChangeCommandPrefixInternal(string value);

    public static void RegisterCommand(string command, CommandCallback callback)
    {
        if (string.IsNullOrWhiteSpace(command))
            throw new FormatException("Command can't be null or empty.");

        if (callback is null)
            throw new ArgumentNullException(nameof(callback));

        commands.Add(new Command(command, callback));
    }

    public static void TakeControl()
    {
        if (hasControl)
            return;
        hasControl = true;

        TakeControlInternal();
    }

    public static void ResetCommandPrefix()
    {
        CommandPrefix = defaultCommandPrefix;
    }

    public static void WaitForExit()
    {
        exitSemaphore = new SemaphoreSlim(initialCount: 0, maxCount: 1);
        exitSemaphore.Wait();
        exitSemaphore.Dispose();
        exitSemaphore = null;
    }

    private static void ExecuteCommand(string input)
    {
        string[] words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (Command command in commands)
        {
            if (command.TryExecute(input, words))
                return;
        }

        ConsoleColor previousColor = ForegroundColor;
        ForegroundColor = ConsoleColor.Red;
        WriteLine("Command not found");
        ForegroundColor = previousColor;
    }
}
