using System;
using System.Collections.Generic;
using System.Threading;

namespace Obsidian.IO.Console
{
    public static partial class CommandLine
    {
        // Some implementation is platform specific
        // These members need to be implemented for all platforms:
        //
        // ConsoleColor ForegroundColor { get; set; }
        // ConsoleColor BackgroundColor { get; set; }
        // void ResetColor()
        // void Write(string text)
        // void Write(ReadOnlySpan<char> text)
        // void WriteLine()
        // void WriteLine(string text)
        // void WriteLine(ReadOnlySpan<char> text)
        // 
        // void TakeControlInternal()
        // void ChangeCommandPrefixInternal(string value)
        //
        // Reading key in a dedicated loop must check if the exitSemaphore
        // is not null, and if so, release it.

        public static string Title { set => System.Console.Title = value; }

        public static string CommandPrefix
        {
            get => commandPrefix;
            set => ChangeCommandPrefixInternal(value);
        }

        public static event Func<bool>? CancelKeyPress;

        private static List<Command> commands = new();

        private static SemaphoreSlim? exitSemaphore;

        private static readonly string defaultCommandPrefix = "> ";
        private static string commandPrefix = defaultCommandPrefix;
        private static readonly ConsoleColor inputColor = ConsoleColor.White;

        private static bool hasControl;

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
}
